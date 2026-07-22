using System;
using System.Collections.Generic;

namespace Hypocycloid.Ratioscope
{
    public struct CortexCellInfo
    {
        /// <summary>Zero-based transformer block, or -1 for the token surface.</summary>
        public int TransformerBlock;
        public string Branch;
        public string Stage;
        public string Region;
        public float Heat;
        public int TokenId;
        public float Probability;
    }

    /// <summary>
    /// Structural model behind the cortex matrix. Every transformer block owns two columns:
    /// attention and MLP. Data flows top to bottom through the stage rows and lands on the
    /// token surface, which occupies the bottom rows (row 0 renders at the bottom edge).
    /// A completed forward pulses every block because these dense Qwen checkpoints execute
    /// all blocks. This deliberately does not claim hidden activation strength.
    /// </summary>
    public sealed class CortexHeatGrid
    {
        public const int ColumnsPerBlock = 2;
        public const int StageCount = 4;

        readonly float[] heat;
        readonly int[] cellBlock;
        readonly int[] cellToken;
        readonly float[] cellProbability;
        readonly string[] cellBranch;
        readonly string[] cellStage;
        readonly float forwardPulseHeat;
        readonly float heatDecayRate;
        readonly float candidateBaseHeat;
        readonly float entropySmoothing;
        readonly List<TokenCandidate> lastCandidates = new();
        float smoothedEntropy;

        public int Width { get; }
        public int Height { get; }
        public int TokenRows { get; }
        public int StructureRows { get; }
        public int TransformerBlockCount { get; }

        /// <summary>Raw heat values, row-major, for direct texture upload.</summary>
        public float[] Heat => heat;

        /// <summary>Smoothed full-vocab entropy driving the palette (nats).</summary>
        public float SmoothedEntropy => smoothedEntropy;
        public IReadOnlyList<TokenCandidate> LastCandidates => lastCandidates;

        public CortexHeatGrid(
            int stageRows,
            int transformerBlockCount,
            int tokenRows,
            float forwardPulseHeat,
            float heatDecayRate,
            float candidateBaseHeat,
            float entropySmoothing
        )
        {
            if (stageRows < StageCount)
                throw new ArgumentOutOfRangeException(nameof(stageRows));
            if (transformerBlockCount < 1)
                throw new ArgumentOutOfRangeException(nameof(transformerBlockCount));
            if (tokenRows < 1)
                throw new ArgumentOutOfRangeException(nameof(tokenRows));
            if (forwardPulseHeat < 0f || forwardPulseHeat > 1f)
                throw new ArgumentOutOfRangeException(nameof(forwardPulseHeat));
            if (heatDecayRate < 0f)
                throw new ArgumentOutOfRangeException(nameof(heatDecayRate));
            if (candidateBaseHeat < 0f || candidateBaseHeat > 1f)
                throw new ArgumentOutOfRangeException(nameof(candidateBaseHeat));
            if (entropySmoothing < 0f || entropySmoothing > 1f)
                throw new ArgumentOutOfRangeException(nameof(entropySmoothing));

            Width = transformerBlockCount * ColumnsPerBlock;
            TransformerBlockCount = transformerBlockCount;
            TokenRows = tokenRows;
            StructureRows = stageRows;
            Height = StructureRows + tokenRows;
            heat = new float[Width * Height];
            cellBlock = new int[Width * Height];
            cellToken = new int[Width * Height];
            cellProbability = new float[Width * Height];
            cellBranch = new string[Width * Height];
            cellStage = new string[Width * Height];
            this.forwardPulseHeat = forwardPulseHeat;
            this.heatDecayRate = heatDecayRate;
            this.candidateBaseHeat = candidateBaseHeat;
            this.entropySmoothing = entropySmoothing;
            Array.Fill(cellBlock, -1);
            Array.Fill(cellToken, -1);
            InitializeStructure();
        }

        void InitializeStructure()
        {
            for (int block = 0; block < TransformerBlockCount; block++)
            {
                for (int columnInBlock = 0; columnInBlock < ColumnsPerBlock; columnInBlock++)
                {
                    bool attention = columnInBlock == 0;
                    int x = block * ColumnsPerBlock + columnInBlock;
                    for (int row = TokenRows; row < Height; row++)
                    {
                        int index = row * Width + x;
                        cellBlock[index] = block;
                        cellBranch[index] = attention ? "attention" : "MLP";
                        cellStage[index] = StageForRow(attention, row);
                    }
                }
            }
        }

        string StageForRow(bool attention, int row)
        {
            // Row Height-1 renders at the top edge, so depth 0 is where data enters.
            int depth = Height - 1 - row;
            int stage = Math.Min(StageCount - 1, depth * StageCount / StructureRows);
            if (attention)
            {
                return stage switch
                {
                    0 => "input RMSNorm",
                    1 => "Q/K/V projections",
                    2 => "rotary attention",
                    _ => "output projection + residual",
                };
            }

            return stage switch
            {
                0 => "post-attention RMSNorm",
                1 => "gate/up projections",
                2 => "SiLU-gated activation",
                _ => "down projection + residual",
            };
        }

        /// <summary>
        /// Marks one completed dense-model forward. All blocks pulse uniformly: cell heat is
        /// recent structural participation, never an activation magnitude.
        /// </summary>
        public void OnForward()
        {
            for (int i = TokenRows * Width; i < heat.Length; i++)
                heat[i] = forwardPulseHeat;
        }

        /// <summary>Flares token-surface cells for each candidate; brightness follows
        /// probability. Updates the entropy that drives the palette.</summary>
        public void OnToken(TokenMetrics metrics)
        {
            // Reuse the backing storage so a full generation does not churn one array per token.
            lastCandidates.Clear();
            for (int i = 0; i < metrics.TopCandidates.Count; i++)
                lastCandidates.Add(metrics.TopCandidates[i]);

            foreach (TokenCandidate candidate in metrics.TopCandidates)
            {
                int index = TokenCellIndex(candidate.Id);
                float value = candidateBaseHeat + (1f - candidateBaseHeat) * candidate.Probability;
                if (value > heat[index])
                    heat[index] = value;
                cellToken[index] = candidate.Id;
                cellProbability[index] = candidate.Probability;
                cellBlock[index] = -1;
            }
            smoothedEntropy += (metrics.Entropy - smoothedEntropy) * entropySmoothing;
        }

        public void Decay(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return;
            float factor = (float)Math.Exp(-deltaSeconds * heatDecayRate);
            for (int i = 0; i < heat.Length; i++)
                heat[i] *= factor;
        }

        public CortexCellInfo GetCell(int x, int y)
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
            int index = y * Width + x;
            bool tokenSurface = y < TokenRows;
            return new CortexCellInfo
            {
                TransformerBlock = tokenSurface ? -1 : cellBlock[index],
                Branch = tokenSurface ? null : cellBranch[index],
                Stage = tokenSurface ? null : cellStage[index],
                Region = tokenSurface ? "token surface" : "transformer structure",
                Heat = heat[index],
                TokenId = cellToken[index],
                Probability = cellProbability[index],
            };
        }

        /// <summary>Deterministic candidate-id to token-surface cell mapping.</summary>
        public int TokenCellIndex(int tokenId)
        {
            uint hash = (uint)tokenId * 2654435761u;
            int x = (int)(hash % (uint)Width);
            int y = (int)((hash >> 16) % (uint)TokenRows);
            return y * Width + x;
        }
    }
}

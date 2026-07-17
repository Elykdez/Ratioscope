using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// GPT-2-style byte-level BPE tokenizer (Qwen, Llama 3, Phi, SmolLM families). Loads the
    /// compact binary written by Tools/LlmChat/export_llm_tokenizer.py and reproduces the
    /// Hugging Face fast-tokenizer pipeline: split text with the exported pre-tokenization
    /// regex, map each piece's UTF-8 bytes through the byte-to-unicode table, and BPE-merge
    /// within each piece. Added tokens (ChatML markers etc.) are isolated before splitting.
    /// </summary>
    public sealed class LlmTokenizer
    {
        // "LTK1" little-endian.
        const uint BinaryMagic = 0x314B544C;

        readonly string[] vocabulary;
        readonly Dictionary<string, int> tokenIds;
        readonly Dictionary<long, Merge> merges;
        readonly Regex splitRegex;
        readonly List<SpecialToken> specialTokens = new();
        readonly Dictionary<int, string> addedTokensById = new();
        readonly HashSet<int> specialTokenIds = new();
        readonly int[] charTokenIds;

        // GPT-2 byte-level tables: every byte maps to a printable unicode char and back.
        static readonly char[] ByteToChar = BuildByteToChar();
        static readonly Dictionary<char, byte> CharToByte = BuildCharToByte();

        public int VocabularySize => vocabulary.Length;
        public int PadTokenId { get; }

        /// <summary>Id of the hard end-of-text token (&lt;|endoftext|&gt;).</summary>
        public int EosTokenId { get; }

        /// <summary>Id of the ChatML end-of-turn stop token (&lt;|im_end|&gt;).</summary>
        public int EndOfTurnTokenId { get; }

        struct Merge
        {
            public int Rank;
            public int MergedId;
        }

        struct SpecialToken
        {
            public string Content;
            public int Id;
        }

        public LlmTokenizer(string binaryPath)
        {
            using FileStream file = File.OpenRead(binaryPath);
            using BufferedStream buffered = new(file, 1 << 20);
            using BinaryReader reader = new(buffered, Encoding.UTF8);

            if (reader.ReadUInt32() != BinaryMagic)
                throw new InvalidDataException("Not an LLM tokenizer binary: " + binaryPath);
            int version = reader.ReadInt32();
            if (version != 1)
                throw new InvalidDataException($"Unsupported tokenizer binary version {version}.");

            splitRegex = new Regex(ReadUtf8String(reader), RegexOptions.CultureInvariant);

            int vocabularyCount = reader.ReadInt32();
            vocabulary = new string[vocabularyCount];
            tokenIds = new Dictionary<string, int>(vocabularyCount);
            for (int id = 0; id < vocabularyCount; id++)
            {
                string token = ReadUtf8String(reader);
                vocabulary[id] = token;
                tokenIds[token] = id;
            }

            int mergeCount = reader.ReadInt32();
            merges = new Dictionary<long, Merge>(mergeCount);
            for (int rank = 0; rank < mergeCount; rank++)
            {
                int left = reader.ReadInt32();
                int right = reader.ReadInt32();
                int merged = reader.ReadInt32();
                long key = PairKey(left, right);
                if (!merges.ContainsKey(key))
                    merges.Add(key, new Merge { Rank = rank, MergedId = merged });
            }

            int addedCount = reader.ReadInt32();
            for (int i = 0; i < addedCount; i++)
            {
                int id = reader.ReadInt32();
                string content = ReadUtf8String(reader);
                bool special = reader.ReadByte() != 0;
                specialTokens.Add(new SpecialToken { Content = content, Id = id });
                addedTokensById[id] = content;
                if (special)
                    specialTokenIds.Add(id);
            }
            // Longest-first so overlapping markers (e.g. <|im_start|> vs <|im|) match greedily.
            specialTokens.Sort((a, b) => b.Content.Length.CompareTo(a.Content.Length));

            // Byte-level BPE guarantees all 256 single-byte tokens exist in the vocab.
            charTokenIds = new int[512];
            for (int b = 0; b < 256; b++)
            {
                char c = ByteToChar[b];
                if (!tokenIds.TryGetValue(c.ToString(), out int id))
                    throw new InvalidDataException($"Vocabulary is missing byte token 0x{b:X2}.");
                charTokenIds[c] = id;
            }

            PadTokenId = RequireSpecialTokenId("<|endoftext|>");
            EosTokenId = PadTokenId;
            EndOfTurnTokenId = RequireSpecialTokenId("<|im_end|>");
        }

        public bool TryGetSpecialTokenId(string content, out int id)
        {
            foreach (SpecialToken token in specialTokens)
            {
                if (token.Content == content)
                {
                    id = token.Id;
                    return true;
                }
            }
            id = -1;
            return false;
        }

        public bool IsSpecialToken(int id) => specialTokenIds.Contains(id);

        /// <summary>Encodes plain text. Added-token markers are NOT recognized here.</summary>
        public List<int> Encode(string text)
        {
            List<int> result = new((text?.Length ?? 0) + 8);
            if (string.IsNullOrEmpty(text))
                return result;

            int lastEnd = 0;
            foreach (Match match in splitRegex.Matches(text))
            {
                // The pattern covers all characters, but encode any gap defensively.
                if (match.Index > lastEnd)
                    EncodePiece(text.Substring(lastEnd, match.Index - lastEnd), result);
                EncodePiece(match.Value, result);
                lastEnd = match.Index + match.Length;
            }
            if (lastEnd < text.Length)
                EncodePiece(text.Substring(lastEnd), result);
            return result;
        }

        // Byte-level BPE of a single pre-tokenized piece; merges never cross pieces.
        void EncodePiece(string piece, List<int> result)
        {
            List<int> symbols = new(piece.Length + 4);
            foreach (byte value in Encoding.UTF8.GetBytes(piece))
                symbols.Add(charTokenIds[ByteToChar[value]]);
            MergePairs(symbols);
            result.AddRange(symbols);
        }

        /// <summary>
        /// Encodes text that may contain added-token markers such as &lt;|im_start|&gt;,
        /// matching the Hugging Face added-token splitting behavior.
        /// </summary>
        public List<int> EncodeWithSpecialTokens(string text)
        {
            List<int> result = new();
            int position = 0;
            while (position < text.Length)
            {
                int matchIndex = -1;
                int matchId = 0;
                int matchLength = 0;
                foreach (SpecialToken token in specialTokens)
                {
                    int index = text.IndexOf(token.Content, position, StringComparison.Ordinal);
                    if (index < 0)
                        continue;
                    if (matchIndex < 0 || index < matchIndex)
                    {
                        matchIndex = index;
                        matchId = token.Id;
                        matchLength = token.Content.Length;
                    }
                }

                if (matchIndex < 0)
                {
                    result.AddRange(Encode(text.Substring(position)));
                    break;
                }

                if (matchIndex > position)
                    result.AddRange(Encode(text.Substring(position, matchIndex - position)));
                result.Add(matchId);
                position = matchIndex + matchLength;
            }
            return result;
        }

        public string Decode(IReadOnlyList<int> ids, bool skipSpecialTokens = true)
        {
            using MemoryStream bytes = new(ids.Count * 4);
            foreach (int id in ids)
            {
                if (addedTokensById.TryGetValue(id, out string added))
                {
                    if (skipSpecialTokens && specialTokenIds.Contains(id))
                        continue;
                    byte[] raw = Encoding.UTF8.GetBytes(added);
                    bytes.Write(raw, 0, raw.Length);
                    continue;
                }
                if (id < 0 || id >= vocabulary.Length)
                    continue;

                foreach (char c in vocabulary[id])
                {
                    if (CharToByte.TryGetValue(c, out byte value))
                        bytes.WriteByte(value);
                }
            }
            return Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
        }

        void MergePairs(List<int> symbols)
        {
            while (symbols.Count >= 2)
            {
                int bestRank = int.MaxValue;
                int bestLeft = 0;
                int bestRight = 0;
                int bestMerged = 0;
                for (int i = 0; i < symbols.Count - 1; i++)
                {
                    if (!merges.TryGetValue(PairKey(symbols[i], symbols[i + 1]), out Merge merge))
                        continue;
                    if (merge.Rank < bestRank)
                    {
                        bestRank = merge.Rank;
                        bestLeft = symbols[i];
                        bestRight = symbols[i + 1];
                        bestMerged = merge.MergedId;
                    }
                }
                if (bestRank == int.MaxValue)
                    return;

                // Merge every occurrence left-to-right. BPE rank order guarantees pairs
                // produced by this merge always rank later, so this matches the reference
                // priority-queue implementation.
                int write = 0;
                for (int read = 0; read < symbols.Count; )
                {
                    if (
                        read < symbols.Count - 1
                        && symbols[read] == bestLeft
                        && symbols[read + 1] == bestRight
                    )
                    {
                        symbols[write++] = bestMerged;
                        read += 2;
                    }
                    else
                    {
                        symbols[write++] = symbols[read++];
                    }
                }
                symbols.RemoveRange(write, symbols.Count - write);
            }
        }

        int RequireSpecialTokenId(string content)
        {
            if (!TryGetSpecialTokenId(content, out int id))
                throw new InvalidDataException($"Tokenizer binary is missing {content}.");
            return id;
        }

        static long PairKey(int left, int right) => ((long)left << 32) | (uint)right;

        static string ReadUtf8String(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            return length == 0 ? "" : Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        // GPT-2 bytes_to_unicode: printable latin-1 bytes map to themselves, the rest to
        // U+0100 + n in ascending byte order.
        static char[] BuildByteToChar()
        {
            char[] table = new char[256];
            int next = 0;
            for (int value = 0; value < 256; value++)
            {
                bool printable =
                    (value >= 0x21 && value <= 0x7E)
                    || (value >= 0xA1 && value <= 0xAC)
                    || (value >= 0xAE && value <= 0xFF);
                table[value] = printable ? (char)value : (char)(256 + next++);
            }
            return table;
        }

        static Dictionary<char, byte> BuildCharToByte()
        {
            Dictionary<char, byte> table = new(256);
            for (int value = 0; value < 256; value++)
                table[ByteToChar[value]] = (byte)value;
            return table;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Hypocycloid.Editor.Sentis;
using Hypocycloid.Utils;
using Unity.InferenceEngine;
using UnityEditor;

namespace Hypocycloid.Editor
{
    /// <summary>
    /// Runs Sentis's graph optimizer over an already-converted `.sentis` artifact.
    ///
    /// The ONNX import path deliberately skips the optimizer (see OnnxProcessor's
    /// AddSkipOptimizationWarning) because it is memory-heavy on multi-gigabyte models, so the
    /// shipped decode graphs carry every unfused primitive the exporter emitted - 4046 layers for
    /// the 1.7B/2048 graph, of which only 263 are MatMul. This tool recovers most of that without
    /// re-exporting: it reloads the artifact, replays the optimizer's passes, and rewrites it.
    ///
    /// FuseConstantsPass is intentionally excluded. It folds any layer whose inputs are all
    /// constant, which would evaluate every DequantizeUint8 into a full float32 constant and
    /// inflate a 1.9 GB uint8 artifact back to its ~7.6 GB float32 size.
    /// </summary>
    public static class SentisGraphOptimizer
    {
        const string Optimization = "Unity.InferenceEngine.Compiler.Passes.Optimization.";
        const string Cleanup = "Unity.InferenceEngine.Compiler.Passes.Cleanup.";

        /// <summary>
        /// Cleanup only: deletes dead and no-op layers without rewriting any arithmetic. Measured
        /// safe on the 1.7B decode graph - GPU time is unchanged and only the CPU-side schedule
        /// walk gets shorter.
        /// </summary>
        public static readonly string[] SafePasses =
        {
            Cleanup + "RemoveNoOpsPass",
            Optimization + "RemoveDuplicatesPass",
            Cleanup + "RemoveNoOpsPass",
            Cleanup + "RemoveUnusedPass",
        };

        /// <summary>
        /// Adds the sub-expression contractions (Sigmoid*x -> Swish, Pow2 -> Square,
        /// 1/Sqrt -> Rsqrt, mul+add -> ScalarMad).
        /// </summary>
        public static readonly string[] FusionPasses =
        {
            Optimization + "ContractSubExpressionPass",
            Cleanup + "RemoveNoOpsPass",
            Optimization + "ContractToSimplerLayerPass",
            Optimization + "FuseActivationPass",
            Optimization + "RemoveDuplicatesPass",
            Cleanup + "RemoveNoOpsPass",
            Cleanup + "RemoveUnusedPass",
        };

        /// <summary>
        /// Mirrors ModelOptimizer.OptimizeGraph's order, minus FuseConstantsPass and the DFT pass
        /// that only matters after constant folding. Includes the layout-rewriting passes, which
        /// regressed GPU time 3.7x on the 1.7B decode graph - kept for bisection, not for shipping.
        /// </summary>
        public static readonly string[] AllPasses =
        {
            Optimization + "ContractSubExpressionPass",
            Optimization + "EinsumToMatMulPass",
            Cleanup + "RemoveNoOpsPass",
            Cleanup + "RemoveUnusedPass",
            Optimization + "ConcatenateTransposesPass",
            Optimization + "ContractToSimplerLayerPass",
            Cleanup + "RemoveNoOpsPass",
            Optimization + "SimplifyReshapeInputPass",
            Optimization + "FuseDensePass",
            Optimization + "FuseActivationPass",
            Optimization + "RemoveDuplicatesPass",
            Cleanup + "RemoveNoOpsPass",
            Cleanup + "RemoveUnusedPass",
        };

        [MenuItem(EditorCommons.CTX + "Sentis/Optimize Sentis Graph")]
        public static void OptimizeSelected()
        {
            string sentisDirectory = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                "Sentis"
            );
            string source = EditorUtility.OpenFilePanel(
                "Select .sentis model",
                sentisDirectory,
                "sentis"
            );
            if (string.IsNullOrEmpty(source))
                return;

            string output = Path.Combine(
                Path.GetDirectoryName(source) ?? sentisDirectory,
                Path.GetFileNameWithoutExtension(source) + "_opt.sentis"
            );

            try
            {
                EditorUtility.DisplayProgressBar(
                    "Optimize Sentis Graph",
                    Path.GetFileName(source),
                    0.5f
                );
                OptimizationReport report = Optimize(source, output);
                LogHelper.Log(report.ToString());
                EditorUtility.DisplayDialog("Optimize Sentis Graph", report.ToString(), "OK");
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                LogHelper.LogError(exception);
                EditorUtility.DisplayDialog(
                    "Optimize Sentis Graph failed",
                    exception.GetBaseException().Message,
                    "OK"
                );
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static OptimizationReport Optimize(
            string sentisFile,
            string outputFile,
            string[] passTypeNames = null
        )
        {
            passTypeNames ??= SafePasses;
            if (!File.Exists(sentisFile))
                throw new FileNotFoundException("Sentis model not found.", sentisFile);

            Model model = ModelLoader.Load(sentisFile);
            OptimizationReport report =
                new()
                {
                    SourcePath = sentisFile,
                    OutputPath = outputFile,
                    LayersBefore = model.layers.Count,
                    ConstantsBefore = model.constants.Count,
                    SourceBytes = new FileInfo(sentisFile).Length,
                    HistogramBefore = Histogram(model),
                };

            RunPasses(model, passTypeNames, out Model optimized, report.PassLayerCounts);

            report.LayersAfter = optimized.layers.Count;
            report.ConstantsAfter = optimized.constants.Count;
            report.HistogramAfter = Histogram(optimized);

            SentisStreamingModelWriter.Save(outputFile, optimized);
            report.OutputBytes = new FileInfo(outputFile).Length;
            return report;
        }

        static void RunPasses(
            Model model,
            string[] passTypeNames,
            out Model optimized,
            List<string> passLayerCounts
        )
        {
            Type converterType = SystemHelper.RequireType(
                "Unity.InferenceEngine.Graph.GraphConverter"
            );
            MethodInfo toGraph = RequireStatic(converterType, "ModelToGraphModule");
            MethodInfo toModel = RequireStatic(converterType, "GraphToModel");
            Type passBaseType = SystemHelper.RequireType(
                "Unity.InferenceEngine.Compiler.Passes.GraphPass"
            );
            MethodInfo run =
                passBaseType.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMethodException(passBaseType.FullName, "Run");

            object graphModule = toGraph.Invoke(null, new object[] { model });

            // Node counts are read off the GraphModule directly; converting back after every pass
            // would rebuild every Constant and risk copying the multi-gigabyte weight arrays.
            foreach (string passTypeName in passTypeNames)
            {
                Type passType = SystemHelper.RequireType(passTypeName);
                object pass = Activator.CreateInstance(passType, nonPublic: true);
                run.Invoke(pass, new[] { graphModule });
                passLayerCounts.Add($"{passType.Name}: {CountNodes(graphModule)}");
            }

            optimized = (Model)toModel.Invoke(null, new[] { graphModule });
        }

        static int CountNodes(object graphModule)
        {
            object graph = graphModule
                .GetType()
                .GetField("graph", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(graphModule);
            if (graph == null)
                return -1;

            MethodInfo nodes = graph
                .GetType()
                .GetMethod("Nodes", BindingFlags.Public | BindingFlags.Instance);
            if (nodes == null)
                return -1;

            int count = 0;
            foreach (object _ in (System.Collections.IEnumerable)nodes.Invoke(graph, null))
                count++;
            return count;
        }

        static MethodInfo RequireStatic(Type type, string methodName)
        {
            return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(type.FullName, methodName);
        }

        static Dictionary<string, int> Histogram(Model model)
        {
            Dictionary<string, int> histogram = new();
            foreach (Layer layer in model.layers)
            {
                histogram.TryGetValue(layer.opName, out int count);
                histogram[layer.opName] = count + 1;
            }
            return histogram;
        }

        public sealed class OptimizationReport
        {
            public string SourcePath;
            public string OutputPath;
            public int LayersBefore;
            public int LayersAfter;
            public int ConstantsBefore;
            public int ConstantsAfter;
            public long SourceBytes;
            public long OutputBytes;
            public Dictionary<string, int> HistogramBefore;
            public Dictionary<string, int> HistogramAfter;
            public readonly List<string> PassLayerCounts = new();

            public override string ToString()
            {
                IEnumerable<string> removed = HistogramBefore
                    .Where(entry =>
                        !HistogramAfter.TryGetValue(entry.Key, out int after) || after < entry.Value
                    )
                    .OrderByDescending(entry =>
                        entry.Value - (HistogramAfter.TryGetValue(entry.Key, out int a) ? a : 0)
                    )
                    .Select(entry =>
                        $"{entry.Key} {entry.Value}->"
                        + (HistogramAfter.TryGetValue(entry.Key, out int a) ? a : 0)
                    );

                return $"{Path.GetFileName(SourcePath)} -> {Path.GetFileName(OutputPath)}\n"
                    + $"layers {LayersBefore} -> {LayersAfter}, "
                    + $"constants {ConstantsBefore} -> {ConstantsAfter}, "
                    + $"bytes {SourceBytes} -> {OutputBytes}\n"
                    + string.Join(", ", removed);
            }
        }
    }
}

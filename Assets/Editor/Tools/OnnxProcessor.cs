using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hypocycloid.Editor.Sentis;
using Hypocycloid.Utils;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Hypocycloid.Editor
{
    public sealed class OnnxProcessor : EditorWindow
    {
        const string PrefPrefix = "Hypocycloid.OnnxProcessor.";
        const string DefaultOnnxPath = "Tools/ExportedModel/Llm_Decode_2048.onnx";

        // Above this artifact size, warn that the editor should be restarted before the
        // next conversion. Import materializes the whole float32 graph as managed byte
        // arrays (Constant.array), and while FreeConversionMemory collects them, Unity's
        // collector never returns the pages to the OS - so a second large conversion in
        // the same session starts from an already-inflated heap.
        const long RestartHintBytes = 512L * 1024 * 1024;

        static readonly HashSet<string> QuantizedLayerNames =
            new() { "Conv", "ConvTranspose", "Dense", "Gather", "MatMul", "MatMul2D", "Transpose" };

        // Lower precision reduces model size and memory use, at a possible accuracy cost.
        public enum WeightQuantization
        {
            None,
            Float16,
            Uint8,
        }

        string onnxPath;
        WeightQuantization quantization;
        bool skipOptimization;
        string status;
        MessageType statusType = MessageType.None;

        [MenuItem(EditorCommons.CTX + "Sentis/ONNX to Sentis")]
        public static void ShowWindow()
        {
            OnnxProcessor window = GetWindow<OnnxProcessor>("ONNX to Sentis");
            window.minSize = new Vector2(520, 240);
        }

        void OnEnable()
        {
            onnxPath = EditorPrefs.GetString(PrefPrefix + "OnnxPath", DefaultOnnxPath);
            quantization = (WeightQuantization)
                EditorPrefs.GetInt(PrefPrefix + "Quantization", (int)WeightQuantization.Uint8);
            skipOptimization = EditorPrefs.GetBool(PrefPrefix + "SkipOptimization", true);
        }

        void OnDisable()
        {
            SavePrefs();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("ONNX to Sentis", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Converts one ONNX model into Assets/StreamingAssets/Sentis using the same file name.",
                MessageType.Info
            );

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                onnxPath = EditorGUILayout.TextField("ONNX model", onnxPath);
                if (GUILayout.Button("Browse", GUILayout.Width(72)))
                    BrowseForOnnx();
            }

            quantization = (WeightQuantization)
                EditorGUILayout.EnumPopup("Weight quantization", quantization);
            skipOptimization = EditorGUILayout.Toggle(
                new GUIContent(
                    "Skip graph optimization",
                    "Recommended for multi-gigabyte models to reduce conversion memory. Disable it for normal models to keep Sentis optimization and validation."
                ),
                skipOptimization
            );

            string sourcePath = ResolvePath(onnxPath);
            string outputPath = string.IsNullOrWhiteSpace(sourcePath)
                ? ""
                : GetOutputPath(sourcePath);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("Sentis output", outputPath);

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(!File.Exists(sourcePath)))
            {
                if (GUILayout.Button("Convert", GUILayout.Height(28)))
                    ConvertSelected(sourcePath, outputPath);
            }

            if (!string.IsNullOrWhiteSpace(status))
                EditorGUILayout.HelpBox(status, statusType);
        }

        void BrowseForOnnx()
        {
            string currentPath = ResolvePath(onnxPath);
            string startDirectory = File.Exists(currentPath)
                ? Path.GetDirectoryName(currentPath)
                : ProjectRoot;
            string selected = EditorUtility.OpenFilePanel(
                "Select ONNX model",
                startDirectory,
                "onnx"
            );
            if (!string.IsNullOrEmpty(selected))
                onnxPath = ToProjectRelativePath(selected);
        }

        void ConvertSelected(string sourcePath, string outputPath)
        {
            SavePrefs();
            try
            {
                EditorUtility.DisplayProgressBar(
                    "ONNX to Sentis",
                    Path.GetFileName(sourcePath),
                    0.5f
                );
                ConvertOnnxToSentis(sourcePath, outputPath, quantization, skipOptimization);
                AssetDatabase.Refresh();

                string assetPath = ToAssetPath(outputPath);
                if (!string.IsNullOrEmpty(assetPath))
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                        assetPath
                    );

                long bytes = new FileInfo(outputPath).Length;
                status = $"Created {Path.GetFileName(outputPath)} ({bytes / (1024 * 1024)} MB).";
                LogHelper.Log(status);

                if (bytes >= RestartHintBytes)
                {
                    status +=
                        "\n\nRestart the editor before converting another model this large. "
                        + "Import allocates the whole float32 graph on the managed heap, and "
                        + "Unity does not return those pages to the OS once collected.";
                    statusType = MessageType.Warning;
                }
                else
                {
                    statusType = MessageType.None;
                }
            }
            catch (Exception exception)
            {
                status = exception.GetBaseException().Message;
                statusType = MessageType.Error;
                LogHelper.LogError(exception);
                EditorUtility.DisplayDialog("ONNX conversion failed", status, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                FreeConversionMemory();
            }
        }

        public static string ConvertOnnxToSentis(
            string onnxFile,
            string sentisFile,
            WeightQuantization quantization = WeightQuantization.Uint8,
            bool skipOptimization = true
        )
        {
            string sourcePath = ResolvePath(onnxFile);
            string outputPath = ResolvePath(sentisFile);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("ONNX model not found.", sourcePath);
            if (
                !string.Equals(
                    Path.GetExtension(sourcePath),
                    ".onnx",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new ArgumentException("Input must be an .onnx file.", nameof(onnxFile));
            if (
                !string.Equals(
                    Path.GetExtension(outputPath),
                    ".sentis",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                throw new ArgumentException("Output must be a .sentis file.", nameof(sentisFile));

            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(outputDirectory))
                throw new ArgumentException("Output path has no directory.", nameof(sentisFile));
            Directory.CreateDirectory(outputDirectory);

            // ONNX is the portable graph format; Sentis first converts it to Unity's in-memory model.
            Model model = ImportOnnx(sourcePath, skipOptimization);
            QuantizeWeightsInPlace(model, quantization);

            // Write weights in chunks so large models do not require one extra multi-GB output buffer.
            SentisStreamingModelWriter.Save(outputPath, model);
            return outputPath;
        }

        public static string GetOutputPath(string onnxFile)
        {
            string fileName = Path.GetFileNameWithoutExtension(onnxFile) + ".sentis";
            return Path.Combine(ProjectRoot, "Assets", "StreamingAssets", "Sentis", fileName);
        }

        static Model ImportOnnx(string onnxFile, bool skipOptimization)
        {
            // Unity's ONNX converter is editor-internal, so the tool accesses it through reflection.
            Type converterType = SystemHelper.RequireType(
                "Unity.InferenceEngine.Editor.Onnx.ONNXModelConverter"
            );
            object converter = SystemHelper.CreateRequiredInstance(
                converterType,
                new object[] { onnxFile }
            );
            IList warnings = skipOptimization
                ? AddSkipOptimizationWarning(converter, converterType)
                : null;

            try
            {
                return (Model)
                    SystemHelper
                        .RequireInstanceMethod(converterType, "Convert", 0)
                        .Invoke(converter, null);
            }
            finally
            {
                warnings?.Clear();
            }
        }

        static IList AddSkipOptimizationWarning(object converter, Type converterType)
        {
            // Sentis skips its optimizer when an import error exists. This temporary marker avoids
            // the optimizer's high memory peak for very large models and is removed after import.
            Type baseType = converterType.BaseType;
            Type warningType = baseType.GetNestedType(
                "WarningType",
                BindingFlags.Public | BindingFlags.NonPublic
            );
            MethodInfo warn = baseType.GetMethod(
                "Warn",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { warningType, typeof(string) },
                null
            );
            object errorSeverity = Enum.ToObject(warningType, 3);
            warn.Invoke(
                converter,
                new[] { errorSeverity, "Conversion skips the memory-heavy graph optimizer." }
            );

            PropertyInfo warningsProperty = baseType.GetProperty(
                "ImportWarnings",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            return (IList)warningsProperty.GetValue(converter);
        }

        static void QuantizeWeightsInPlace(Model model, WeightQuantization quantization)
        {
            if (quantization == WeightQuantization.None)
                return;

            int nextTensorIndex = FindNextTensorIndex(model);
            Dictionary<int, List<Layer>> conversionLayers = new();

            for (int constantIndex = 0; constantIndex < model.constants.Count; constantIndex++)
            {
                Constant source = model.constants[constantIndex];
                if (source.dataType != DataType.Float)
                    continue;

                int firstConsumer = FindFirstQuantizedConsumer(model, source.index);
                if (firstConsumer < 0)
                    continue;

                // Store a smaller constant, then restore it to float before its first compute layer.
                QuantizeConstant(source, quantization, out Constant converted, out Layer restore);
                converted.index = nextTensorIndex++;
                restore.inputs[0] = converted.index;
                restore.outputs[0] = source.index;
                model.constants[constantIndex] = converted;

                if (!conversionLayers.TryGetValue(firstConsumer, out List<Layer> layers))
                {
                    layers = new List<Layer>();
                    conversionLayers.Add(firstConsumer, layers);
                }
                layers.Add(restore);

                if (source.lengthBytes >= 64 * 1024 * 1024)
                {
                    source = null;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }

            List<Layer> rebuiltLayers = new(model.layers.Count + conversionLayers.Count);
            for (int layerIndex = 0; layerIndex < model.layers.Count; layerIndex++)
            {
                if (conversionLayers.TryGetValue(layerIndex, out List<Layer> layers))
                    rebuiltLayers.AddRange(layers);
                rebuiltLayers.Add(model.layers[layerIndex]);
            }
            model.layers = rebuiltLayers;
        }

        static int FindFirstQuantizedConsumer(Model model, int tensorIndex)
        {
            for (int layerIndex = 0; layerIndex < model.layers.Count; layerIndex++)
            {
                Layer layer = model.layers[layerIndex];
                if (
                    QuantizedLayerNames.Contains(layer.opName)
                    && Array.IndexOf(layer.inputs, tensorIndex) >= 0
                )
                    return layerIndex;
            }
            return -1;
        }

        static void QuantizeConstant(
            Constant source,
            WeightQuantization quantization,
            out Constant converted,
            out Layer restore
        )
        {
            int sourceIndex = source.index;
            source.index = 1;
            try
            {
                if (source.shape.rank == 0)
                    throw new InvalidOperationException("Cannot quantize a scalar weight.");

                int innerDimension =
                    source.shape.rank == 1 ? source.shape[0] : source.shape[source.shape.rank - 2];

                // Run Sentis's quantizer on one weight at a time instead of duplicating the full model.
                Model miniModel = new();
                miniModel.constants.Add(
                    new Constant(0, new TensorShape(1, innerDimension), new float[innerDimension])
                );
                miniModel.constants.Add(source);

                Type matMulType = SystemHelper.RequireType("Unity.InferenceEngine.Layers.MatMul");
                Layer matMul = (Layer)Activator.CreateInstance(matMulType, true);
                matMul.inputs = new[] { 0, 1 };
                matMul.outputs = new[] { 2 };
                miniModel.layers.Add(matMul);
                miniModel.AddOutput("output", 2);

                QuantizationType quantizationType =
                    quantization == WeightQuantization.Float16
                        ? QuantizationType.Float16
                        : QuantizationType.Uint8;
                ModelQuantizer.QuantizeWeights(quantizationType, ref miniModel);

                Layer convertedMatMul = FindLayer(miniModel, "MatMul");
                string restoreOperation =
                    quantization == WeightQuantization.Float16 ? "Cast" : "DequantizeUint8";
                restore = FindLayerProducing(
                    miniModel,
                    restoreOperation,
                    convertedMatMul.inputs[1]
                );
                converted = FindConstant(miniModel, restore.inputs[0]);

                DataType expectedType =
                    quantization == WeightQuantization.Float16 ? DataType.Short : DataType.Byte;
                if (converted.dataType != expectedType)
                    throw new InvalidOperationException(
                        $"{quantization} conversion produced {converted.dataType} weights."
                    );
                if (quantization == WeightQuantization.Uint8)
                    PackByteConstant(converted);
            }
            finally
            {
                source.index = sourceIndex;
            }
        }

        static Layer FindLayer(Model model, string operation)
        {
            for (int i = 0; i < model.layers.Count; i++)
            {
                if (model.layers[i].opName == operation)
                    return model.layers[i];
            }
            throw new InvalidOperationException($"Quantizer did not produce a {operation} layer.");
        }

        static Layer FindLayerProducing(Model model, string operation, int tensorIndex)
        {
            for (int i = 0; i < model.layers.Count; i++)
            {
                Layer layer = model.layers[i];
                if (
                    layer.opName == operation
                    && layer.outputs.Length == 1
                    && layer.outputs[0] == tensorIndex
                )
                    return layer;
            }
            throw new InvalidOperationException($"Quantizer did not produce a {operation} layer.");
        }

        static Constant FindConstant(Model model, int tensorIndex)
        {
            for (int i = 0; i < model.constants.Count; i++)
            {
                if (model.constants[i].index == tensorIndex)
                    return model.constants[i];
            }
            throw new InvalidOperationException("Quantizer did not produce a weight constant.");
        }

        static void PackByteConstant(Constant constant)
        {
            // Uint8 needs one byte per weight; Sentis's temporary buffer can still be float-sized.
            int elementCount = constant.shape.length;
            if (constant.lengthBytes == elementCount)
                return;

            FieldInfo arrayField = typeof(Constant).GetField(
                "array",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            if (arrayField == null)
                throw new InvalidOperationException("Constant.array field not found.");

            ArraySegment<byte> source = (ArraySegment<byte>)arrayField.GetValue(constant);
            if (source.Count < elementCount)
                throw new InvalidOperationException(
                    "Quantized constant is smaller than its shape."
                );

            byte[] packed = new byte[elementCount];
            Buffer.BlockCopy(source.Array, source.Offset, packed, 0, elementCount);
            arrayField.SetValue(constant, new ArraySegment<byte>(packed));
            constant.lengthBytes = elementCount;
        }

        static int FindNextTensorIndex(Model model)
        {
            int maxIndex = -1;
            for (int i = 0; i < model.inputs.Count; i++)
                maxIndex = Math.Max(maxIndex, model.inputs[i].index);
            for (int i = 0; i < model.outputs.Count; i++)
                maxIndex = Math.Max(maxIndex, model.outputs[i].index);
            for (int i = 0; i < model.constants.Count; i++)
                maxIndex = Math.Max(maxIndex, model.constants[i].index);
            for (int i = 0; i < model.layers.Count; i++)
            {
                Layer layer = model.layers[i];
                for (int j = 0; j < layer.inputs.Length; j++)
                    maxIndex = Math.Max(maxIndex, layer.inputs[j]);
                for (int j = 0; j < layer.outputs.Length; j++)
                    maxIndex = Math.Max(maxIndex, layer.outputs[j]);
            }
            return checked(maxIndex + 1);
        }

        static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";
            return Path.GetFullPath(path.Trim(), ProjectRoot);
        }

        static string ToProjectRelativePath(string path)
        {
            string fullPath = Path.GetFullPath(path);
            string relativePath = Path.GetRelativePath(ProjectRoot, fullPath);
            return relativePath.StartsWith("..", StringComparison.Ordinal)
                ? fullPath
                : relativePath.Replace('\\', '/');
        }

        static string ToAssetPath(string path)
        {
            string relativePath = Path.GetRelativePath(ProjectRoot, Path.GetFullPath(path));
            return relativePath.StartsWith("Assets" + Path.DirectorySeparatorChar)
                ? relativePath.Replace('\\', '/')
                : "";
        }

        static void FreeConversionMemory()
        {
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        void SavePrefs()
        {
            EditorPrefs.SetString(PrefPrefix + "OnnxPath", onnxPath ?? "");
            EditorPrefs.SetInt(PrefPrefix + "Quantization", (int)quantization);
            EditorPrefs.SetBool(PrefPrefix + "SkipOptimization", skipOptimization);
        }

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}

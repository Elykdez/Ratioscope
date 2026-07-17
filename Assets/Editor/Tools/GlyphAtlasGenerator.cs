using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hypocycloid.Editor
{
    public sealed class GlyphAtlasGenerator : EditorWindow
    {
        public const string DefaultGlyphs = "0123456789ABCDEF";
        public const string DefaultOutputPath = "Assets/Bundles/Texture/Atlas/CortexDigitAtlas.png";

        const string DefaultFontPath = "Assets/Bundles/Fonts/GeistPixel.ttf";
        const string LegacyOutputPath = "Assets/Bundles/Texture/CortexDigitAtlas.png";

        [SerializeField]
        Font font;

        [SerializeField]
        FontStyle fontStyle = FontStyle.Normal;

        [SerializeField]
        int fontSize = 25;

        [SerializeField]
        int cellWidth = 16;

        [SerializeField]
        int cellHeight = 32;

        [SerializeField]
        string glyphs = DefaultGlyphs;

        [SerializeField]
        string outputPath = DefaultOutputPath;

        string status;

        [MenuItem(EditorCommons.CTX + "Assets/Glyph Atlas Generator")]
        public static void ShowWindow()
        {
            GlyphAtlasGenerator window = GetWindow<GlyphAtlasGenerator>("Glyph Atlas");
            window.minSize = new Vector2(460, 260);
        }

        void OnEnable()
        {
            if (font == null)
                font = AssetDatabase.LoadAssetAtPath<Font>(DefaultFontPath);

            if (string.IsNullOrWhiteSpace(outputPath) || outputPath == LegacyOutputPath)
                outputPath = DefaultOutputPath;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Glyph Atlas", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Generates one horizontal atlas in the entered glyph order.",
                MessageType.Info
            );

            font = (Font)EditorGUILayout.ObjectField("Font", font, typeof(Font), false);
            fontStyle = (FontStyle)EditorGUILayout.EnumPopup("Font style", fontStyle);
            fontSize = Mathf.Clamp(EditorGUILayout.IntField("Font size", fontSize), 1, 512);
            cellWidth = Mathf.Clamp(EditorGUILayout.IntField("Cell width", cellWidth), 4, 1024);
            cellHeight = Mathf.Clamp(EditorGUILayout.IntField("Cell height", cellHeight), 4, 1024);
            glyphs = EditorGUILayout.TextField("Glyphs", glyphs);

            using (new EditorGUILayout.HorizontalScope())
            {
                outputPath = EditorGUILayout.TextField("Output", outputPath);
                if (GUILayout.Button("Browse", GUILayout.Width(72)))
                {
                    string selectedPath = EditorUtility.SaveFilePanelInProject(
                        "Save Glyph Atlas",
                        Path.GetFileNameWithoutExtension(outputPath),
                        "png",
                        "Choose where to save the generated atlas.",
                        Path.GetDirectoryName(outputPath)?.Replace('\\', '/')
                    );
                    if (!string.IsNullOrWhiteSpace(selectedPath))
                        outputPath = selectedPath;
                }
            }

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(font == null))
            {
                if (GUILayout.Button("Generate Atlas", GUILayout.Height(28)))
                    GenerateSelectedAtlas();
            }

            if (!string.IsNullOrWhiteSpace(status))
                EditorGUILayout.HelpBox(status, MessageType.None);
        }

        void GenerateSelectedAtlas()
        {
            try
            {
                Texture2D atlas = GenerateAtlas(
                    font,
                    outputPath,
                    fontSize,
                    cellWidth,
                    cellHeight,
                    fontStyle,
                    glyphs
                );
                Selection.activeObject = atlas;
                EditorGUIUtility.PingObject(atlas);
                status = $"Generated {atlas.width}x{atlas.height} atlas at {outputPath}.";
                Debug.Log(status, atlas);
            }
            catch (Exception exception)
            {
                status = exception.Message;
                Debug.LogException(exception);
            }
        }

        public static Texture2D GenerateAtlas(
            Font sourceFont,
            string outputAssetPath = DefaultOutputPath,
            int pointSize = 25,
            int glyphCellWidth = 16,
            int glyphCellHeight = 32,
            FontStyle style = FontStyle.Normal,
            string glyphs = DefaultGlyphs
        )
        {
            if (sourceFont == null)
                throw new ArgumentNullException(nameof(sourceFont));
            if (!sourceFont.dynamic)
                throw new ArgumentException(
                    "The selected font must be a dynamic TTF or OTF asset."
                );
            if (pointSize < 1)
                throw new ArgumentOutOfRangeException(nameof(pointSize));
            if (glyphCellWidth < 4)
                throw new ArgumentOutOfRangeException(nameof(glyphCellWidth));
            if (glyphCellHeight < 4)
                throw new ArgumentOutOfRangeException(nameof(glyphCellHeight));
            if (string.IsNullOrEmpty(glyphs))
                throw new ArgumentException("Enter at least one glyph.", nameof(glyphs));

            string assetPath = NormalizeAssetPath(outputAssetPath);
            int atlasWidth = checked(glyphCellWidth * glyphs.Length);
            if (atlasWidth > 16384 || glyphCellHeight > 16384)
                throw new ArgumentException("The requested atlas exceeds Unity's 16384px limit.");

            string projectRoot =
                Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root.");
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            string assetsRoot =
                Path.GetFullPath(Application.dataPath).TrimEnd('\\', '/')
                + Path.DirectorySeparatorChar;
            if (!absolutePath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The output path must stay inside Assets.");

            byte[] png = RenderAtlas(
                sourceFont,
                pointSize,
                glyphCellWidth,
                glyphCellHeight,
                style,
                glyphs
            );
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllBytes(absolutePath, png);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            ConfigureImporter(assetPath, Mathf.Max(atlasWidth, glyphCellHeight));
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath)
                ?? throw new InvalidOperationException(
                    $"Unity did not import the atlas at {assetPath}."
                );
        }

        static byte[] RenderAtlas(
            Font sourceFont,
            int pointSize,
            int cellWidth,
            int cellHeight,
            FontStyle style,
            string glyphs
        )
        {
            sourceFont.RequestCharactersInTexture(glyphs, pointSize, style);
            var characterInfo = new CharacterInfo[glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
            {
                char character = glyphs[i];
                if (!sourceFont.GetCharacterInfo(character, out characterInfo[i], pointSize, style))
                {
                    throw new InvalidOperationException(
                        $"The selected font does not contain '{character}'."
                    );
                }
            }

            int atlasWidth = cellWidth * glyphs.Length;
            RenderTexture target = RenderTexture.GetTemporary(
                atlasWidth,
                cellHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Linear
            );
            RenderTexture previous = RenderTexture.active;
            Texture2D readable = null;
            bool matrixPushed = false;

            try
            {
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadPixelMatrix(0, atlasWidth, 0, cellHeight);

                if (!sourceFont.material.SetPass(0))
                    throw new InvalidOperationException(
                        "The selected font material could not render."
                    );

                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                for (int i = 0; i < glyphs.Length; i++)
                    DrawGlyph(characterInfo[i], i, cellWidth, cellHeight);
                GL.End();
                GL.PopMatrix();
                matrixPushed = false;

                readable = new Texture2D(atlasWidth, cellHeight, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0, 0, atlasWidth, cellHeight), 0, 0);
                readable.Apply();
                return readable.EncodeToPNG();
            }
            finally
            {
                if (matrixPushed)
                    GL.PopMatrix();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (readable != null)
                    DestroyImmediate(readable);
            }
        }

        static void DrawGlyph(CharacterInfo info, int index, int cellWidth, int cellHeight)
        {
            float sourceWidth = Mathf.Max(1, info.maxX - info.minX);
            float sourceHeight = Mathf.Max(1, info.maxY - info.minY);
            float scale = Mathf.Min(
                1,
                (cellWidth - 2f) / sourceWidth,
                (cellHeight - 2f) / sourceHeight
            );
            float width = sourceWidth * scale;
            float height = sourceHeight * scale;
            float xMin = index * cellWidth + (cellWidth - width) * 0.5f;
            float yMin = (cellHeight - height) * 0.5f;

            GL.TexCoord(info.uvBottomLeft);
            GL.Vertex3(xMin, yMin, 0);
            GL.TexCoord(info.uvBottomRight);
            GL.Vertex3(xMin + width, yMin, 0);
            GL.TexCoord(info.uvTopRight);
            GL.Vertex3(xMin + width, yMin + height, 0);
            GL.TexCoord(info.uvTopLeft);
            GL.Vertex3(xMin, yMin + height, 0);
        }

        static string NormalizeAssetPath(string outputAssetPath)
        {
            if (string.IsNullOrWhiteSpace(outputAssetPath))
                throw new ArgumentException("Choose an output path inside Assets.");

            string path = outputAssetPath.Replace('\\', '/').Trim();
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The output path must be inside Assets.");
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The output path must use the .png extension.");
            return path;
        }

        static void ConfigureImporter(string assetPath, int largestDimension)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                throw new InvalidOperationException($"No texture importer exists for {assetPath}.");

            importer.mipmapEnabled = false;
            importer.sRGBTexture = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Clamp(
                Mathf.NextPowerOfTwo(largestDimension),
                32,
                16384
            );
            importer.SaveAndReimport();
        }
    }
}

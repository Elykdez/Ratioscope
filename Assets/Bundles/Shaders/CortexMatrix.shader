// Renders the cortex heat grid: one soft rounded cell per texel of _MainTex (RFloat heat),
// palette lerped from calm to hot by _EntropyMix, with a faint shimmer so idle cells
// still feel alive. Used on a RawImage; all animation is GPU-side.
Shader "Ratioscope/CortexMatrix"
{
    Properties
    {
        _MainTex ("Heat", 2D) = "black" { }
        // The TMP SDF font atlas the token labels use; C# supplies it along with the per-symbol
        // rect and quad arrays, which cannot be declared in a Properties block.
        _LayerGlyphAtlas ("Layer Glyph Atlas", 2D) = "black" { }
        _LayerGlyphCount ("Layer Glyph Count", Float) = 0
        _LayerGlyphGradientScale ("Layer Glyph Gradient Scale", Float) = 1
        _LayerGlyphFill ("Layer Glyph Cell Fill", Range(0.3, 1)) = 0.66
        _Cols ("Columns", Float) = 1
        _Rows ("Rows", Float) = 1
        _TokenRows ("Token Rows", Float) = 0
        _LayerGlyphSlots ("Layer Glyph Slots", Float) = 3
        _LayerFlatCellAspect ("Layer Flat Cell Aspect", Float) = 1
        _LayerFoldedCellAspect ("Layer Folded Cell Aspect", Float) = 1
        _EntropyMix ("Entropy Mix", Range(0, 1)) = 0
        _CalmColor ("Calm Color", Color) = (0.18, 0.85, 0.45, 1)
        _HotColor ("Hot Color", Color) = (0.25, 0.55, 1.0, 1)
        _BgColor ("Background", Color) = (0.02, 0.035, 0.03, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        Lighting Off
        ZWrite Off
        // Required for a Canvas RawImage: the scene's MainCanvas is Screen Space - Camera,
        // where the default ZTest LEqual makes the quad fail the depth test and vanish.
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Library/CortexUtils.cginc"

            sampler2D _MainTex;
            sampler2D _LayerGlyphAtlas;
            float4 _LayerGlyphAtlas_TexelSize;
            float4 _LayerGlyphRects[CORTEX_GLYPH_CAPACITY];
            float4 _LayerGlyphQuads[CORTEX_GLYPH_CAPACITY];
            float _LayerGlyphCount;
            float _LayerGlyphGradientScale;
            float _LayerGlyphFill;
            float _Cols;
            float _Rows;
            float _TokenRows;
            float _LayerGlyphSlots;
            float _LayerFlatCellAspect;
            float _LayerFoldedCellAspect;
            float _EntropyMix;
            fixed4 _CalmColor;
            fixed4 _HotColor;
            fixed4 _BgColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 grid = float2(_Cols, _Rows);
                float2 cellId = floor(i.uv * grid);
                float2 cellUv = frac(i.uv * grid);
                float2 heatUv = (cellId + 0.5) / grid;
                return CortexShadeCell(
                    _MainTex,
                    _LayerGlyphAtlas,
                    _LayerGlyphAtlas_TexelSize,
                    _LayerGlyphRects,
                    _LayerGlyphQuads,
                    _LayerGlyphCount,
                    _LayerGlyphGradientScale,
                    _LayerGlyphFill,
                    _Cols,
                    _Rows,
                    _TokenRows,
                    _LayerGlyphSlots,
                    _LayerFlatCellAspect,
                    _LayerFoldedCellAspect,
                    _EntropyMix,
                    _CalmColor.rgb,
                    _HotColor.rgb,
                    _BgColor.rgb,
                    _Time.y,
                    i.uv,
                    heatUv,
                    cellUv,
                    cellId,
                    0.0,
                    float3(0.0, 0.0, 1.0),
                    0.0
                ) * i.color;
            }
            ENDCG
        }
    }
}

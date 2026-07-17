// Off-screen 2D-to-3D cortex morph. The generated mesh stores the folded neural-column
// position while UV0 remains the exact flat RawImage layout.
Shader "Ratioscope/CortexMatrixVolume"
{
    Properties
    {
        _MainTex ("Heat", 2D) = "black" { }
        _Cols ("Columns", Float) = 1
        _Rows ("Rows", Float) = 1
        _TokenRows ("Token Rows", Float) = 0
        _EntropyMix ("Entropy Mix", Range(0, 1)) = 0
        _Fold ("Fold", Range(0, 1)) = 0
        _FoldStagger ("Fold Stagger", Range(0, 1)) = 0.45
        _Yaw ("Yaw", Float) = 0
        _Pitch ("Pitch", Float) = 0
        _FlatYSign ("Flat Y Sign", Float) = 1
        _GlowIntensity ("Glow Intensity", Float) = 1
        _CalmColor ("Calm Color", Color) = (0.18, 0.85, 0.45, 1)
        _HotColor ("Hot Color", Color) = (0.25, 0.55, 1.0, 1)
        _BgColor ("Background", Color) = (0.02, 0.035, 0.03, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        Lighting Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Cols;
            float _Rows;
            float _TokenRows;
            float _EntropyMix;
            float _Fold;
            float _FoldStagger;
            float _Yaw;
            float _Pitch;
            float _FlatYSign;
            float _GlowIntensity;
            fixed4 _CalmColor;
            fixed4 _HotColor;
            fixed4 _BgColor;

            #include "CortexMatrixCommon.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 sheetUv : TEXCOORD0;
                float2 heatUv : TEXCOORD1;
                float2 cellUv : TEXCOORD2;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 sheetUv : TEXCOORD0;
                float2 heatUv : TEXCOORD1;
                float2 cellUv : TEXCOORD2;
                float3 viewNormal : TEXCOORD3;
                float volumeMix : TEXCOORD4;
            };

            float3 RotateY(float3 p, float angle)
            {
                float s;
                float c;
                sincos(angle, s, c);
                return float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
            }

            float3 RotateX(float3 p, float angle)
            {
                float s;
                float c;
                sincos(angle, s, c);
                return float3(p.x, c * p.y - s * p.z, s * p.y + c * p.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                float tokenEdge = _TokenRows / _Rows;
                bool tokenCell = v.heatUv.y < tokenEdge;
                float3 localNormal = tokenCell
                ? float3(0.0, 1.0, 0.0)
                : normalize(float3(v.vertex.x, 0.0, v.vertex.z));
                float3 folded = RotateX(RotateY(v.vertex.xyz, _Yaw), _Pitch);
                float3 foldedNormal = RotateX(RotateY(localNormal, _Yaw), _Pitch);

                float cellT = saturate(
                    _Fold * (1.0 + _FoldStagger) - v.heatUv.x * _FoldStagger
                );
                float eased = cellT * cellT * (3.0 - 2.0 * cellT);
                float2 flatPosition = v.sheetUv * 2.0 - 1.0;
                // C# derives this render-texture flip from Unity's GPU projection,
                // keeping the clip-space morph consistent on D3D, OpenGL, Vulkan, and Metal.
                flatPosition.y *= _FlatYSign;
                float4 flatClip = float4(flatPosition, 0.5, 1.0);
                float4 foldedClip = UnityObjectToClipPos(float4(folded, 1.0));

                o.vertex = lerp(flatClip, foldedClip, eased);
                o.sheetUv = v.sheetUv;
                o.heatUv = v.heatUv;
                o.cellUv = v.cellUv;
                o.viewNormal = mul((float3x3)UNITY_MATRIX_V, foldedNormal);
                o.volumeMix = eased;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 cellId = floor(i.heatUv * float2(_Cols, _Rows));
                return CortexShadeCell(
                    i.sheetUv,
                    i.heatUv,
                    i.cellUv,
                    cellId,
                    i.volumeMix,
                    i.viewNormal,
                    _GlowIntensity
                );
            }
            ENDCG
        }
    }
}

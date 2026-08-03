// Renders TMP-generated glyph geometry on the same flat-to-folded surface as the Cortex.
// TMP owns glyph layout and atlas UV generation; this shader owns the Cortex morph and heat.
Shader "Ratioscope/CortexTokenLabels"
{
    Properties
    {
        _MainTex ("Font Atlas", 2D) = "white" { }
        _HeatTex ("Heat", 2D) = "black" { }
        _EntropyMix ("Entropy Mix", Range(0, 1)) = 0
        _Fold ("Fold", Range(0, 1)) = 0
        _FoldStagger ("Fold Stagger", Range(0, 1)) = 0.45
        _Yaw ("Yaw", Float) = 0
        _Pitch ("Pitch", Float) = 0
        _FlatYSign ("Flat Y Sign", Float) = 1
        _SurfaceOffset ("Surface Offset", Float) = 0.002
        _GlowIntensity ("Glow Intensity", Float) = 1
        _CalmColor ("Calm Color", Color) = (0.18, 0.85, 0.45, 1)
        _HotColor ("Hot Color", Color) = (0.25, 0.55, 1.0, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _HeatTex;
            float _EntropyMix;
            float _Fold;
            float _FoldStagger;
            float _Yaw;
            float _Pitch;
            float _FlatYSign;
            float _SurfaceOffset;
            float _GlowIntensity;
            fixed4 _CalmColor;
            fixed4 _HotColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 sheetUv : TEXCOORD0;
                float2 heatUv : TEXCOORD1;
                float2 atlasUv : TEXCOORD2;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 heatUv : TEXCOORD0;
                float2 atlasUv : TEXCOORD1;
                float volumeMix : TEXCOORD2;
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
                float3 folded = RotateX(RotateY(v.vertex.xyz, _Yaw), _Pitch);

                // Labels are coplanar with the token disk, which draws first and writes depth, so
                // they need a nudge off the surface to survive ZTest. The disk is flat and
                // horizontal in object space, so one normal serves every label vertex. Offsetting
                // toward whichever face the camera is on keeps the glyphs readable from below as
                // well as from above; a fixed +Y push would sink them into the disk from underneath.
                float3 foldedNormal = RotateX(RotateY(float3(0.0, 1.0, 0.0), _Yaw), _Pitch);
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, foldedNormal);
                // The camera sits at the view-space origin, so the negated position is the
                // direction from this vertex toward it.
                float3 towardEye = -UnityObjectToViewPos(folded);
                float facing = dot(viewNormal, towardEye) < 0.0 ? -1.0 : 1.0;
                folded += foldedNormal * _SurfaceOffset * facing;

                float cellT = saturate(
                    _Fold * (1.0 + _FoldStagger) - v.heatUv.x * _FoldStagger
                );
                float eased = cellT * cellT * (3.0 - 2.0 * cellT);
                float2 flatPosition = v.sheetUv * 2.0 - 1.0;
                flatPosition.y *= _FlatYSign;
                float4 flatClip = float4(flatPosition, 0.5, 1.0);
                float4 foldedClip = UnityObjectToClipPos(float4(folded, 1.0));

                o.vertex = lerp(flatClip, foldedClip, eased);
                o.heatUv = v.heatUv;
                o.atlasUv = v.atlasUv;
                o.volumeMix = eased;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float distance = tex2D(_MainTex, i.atlasUv).a;
                float smoothing = max(fwidth(distance) * 0.8, 0.002);
                float glyph = smoothstep(0.5 - smoothing, 0.5 + smoothing, distance);
                float heat = tex2D(_HeatTex, i.heatUv).r;
                float visibility = saturate(heat * 4.0);
                fixed3 color = lerp(_CalmColor.rgb, _HotColor.rgb, _EntropyMix);
                float intensity = (0.65 + heat * 1.35) * lerp(1.0, _GlowIntensity, i.volumeMix);
                return fixed4(color * intensity, glyph * visibility);
            }
            ENDCG
        }
    }
}

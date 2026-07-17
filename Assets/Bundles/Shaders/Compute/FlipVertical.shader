Shader "Hidden/Hypocycloid/FlipVertical"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" { }
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            Fog
            {
                Mode Off
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Flip vertically by inverting Y
                fixed4 col = tex2D(_MainTex, float2(i.uv.x, 1.0 - i.uv.y));
                #ifndef UNITY_COLORSPACE_GAMMA
                    // This blit feeds the export readback, which pipes raw bytes to
                    // FFmpeg as 8-bit sRGB video. In Linear projects targetRT holds
                    // linear-light values, so re-encode here; in Gamma projects the
                    // values are already sRGB and this path compiles out.
                    col.rgb = lerp(
                        col.rgb * 12.92,
                        1.055 * pow(max(col.rgb, 0.0), 1.0 / 2.4) - 0.055,
                        step(0.0031308, col.rgb)
                    );
                #endif
                return col;
            }
            ENDCG
        }
    }
}

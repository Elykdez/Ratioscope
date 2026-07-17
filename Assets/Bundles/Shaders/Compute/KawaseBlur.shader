Shader "Hidden/Hypocycloid/KawaseBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" { }
        _KawaseOffset ("Kawase Offset", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            ZTest Always Cull Off ZWrite Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "../Library/BlurUtils.cginc"

            UNITY_DECLARE_TEX2D(_MainTex);
            float4 _MainTex_TexelSize;
            float _KawaseOffset;

            float4 frag(v2f_img i) : SV_Target
            {
                return KawaseBlur(
                    TEXTURE2D_ARGS(_MainTex, sampler_MainTex),
                    i.uv,
                    _MainTex_TexelSize,
                    (int)max(0.0, _KawaseOffset)
                );
            }
            ENDCG
        }
    }
}

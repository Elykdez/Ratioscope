Shader "Ratioscope/UI/CortexLoadingRing"
{
    Properties
    {
        [HDR] _ArcColor ("Arc Color", Color) = (0.08, 4.5, 3.8, 1)
        [HDR] _HeadColor ("Head Color", Color) = (7.0, 1.8, 0.9, 1)
        _DimColor ("Dim Ring", Color) = (0.23, 0.25, 0.26, 0.7)
        _Speed ("Cycles Per Second", Range(0.01, 1)) = 0.5
        _ArcLength ("Arc Length", Range(0.1, 0.9)) = 0.62
        _StripHeadGap ("Strip Head Gap", Range(0, 0.3)) = 0.05
        _StripCycleLength ("Strip Cycle Length", Range(0, 0.8)) = 0.37
        [Enum(Solid, 0, Digits, 1)] _StripStyle ("Strip Style", Float) = 0
        [NoScaleOffset] _DigitAtlas ("Glyph Atlas", 2D) = "white" { }
        _GlyphCount ("Atlas Glyph Count", Float) = 16
        _Radius ("Radius", Range(0.01, 1)) = 0.8
        _Thickness ("Thickness", Range(0.001, 0.1)) = 0.005
        _Progress ("Determinate Progress", Range(-1, 1)) = -1
        [PerRendererData] _MainTex ("Texture", 2D) = "white" { }
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "CanUseSpriteAtlas" = "True" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_DigitAtlas);
            SAMPLER(sampler_DigitAtlas);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ArcColor;
                float4 _HeadColor;
                float4 _DimColor;
                float _Speed;
                float _ArcLength;
                float _StripHeadGap;
                float _StripCycleLength;
                float _StripStyle;
                float _GlyphCount;
                float4 _DigitAtlas_TexelSize;
                float _Radius;
                float _Thickness;
                float _Progress;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float Band(float value, float center, float halfWidth, float feather)
            {
                return 1.0 - smoothstep(halfWidth, halfWidth + feather, abs(value - center));
            }

            float Hash11(float value)
            {
                return frac(sin(value * 12.9898) * 43758.5453);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * 2.0;
                float radius = length(p);
                float angle = frac(atan2(p.y, p.x) / TWO_PI + 1.0);
                float phase = frac(_Time.y * _Speed);
                float trailPosition = frac(phase - angle);
                float arcWindow = 1.0 - smoothstep(_ArcLength - 0.025, _ArcLength, trailPosition);
                float trail = pow(saturate(1.0 - trailPosition / _ArcLength), 0.72) * arcWindow;
                float progressArc = trail;

                float ringDistance = abs(radius - _Radius);
                float ringCore = 1.0 - smoothstep(_Thickness, _Thickness + 0.012, ringDistance);
                float ringGlow = exp(-ringDistance * 12.0);
                float ringHalo = exp(-ringDistance * 5.0);
                float dimRing = (1.0 - smoothstep(0.014, 0.035, ringDistance)) * 0.65;

                float headAngle = phase * TWO_PI;
                if (_Progress >= 0.0)
                {
                    float progress = saturate(_Progress);
                    float clockwiseFromTop = frac(0.25 - angle + 1.0);
                    progressArc = (1.0 - smoothstep(progress, progress + 0.012, clockwiseFromTop)) * step(0.00001, progress);
                    headAngle = (0.25 - progress) * TWO_PI;
                }
                float2 headPosition = _Radius * float2(cos(headAngle), sin(headAngle));
                float headDistance = length(p - headPosition);
                float headCore = 1.0 - smoothstep(0.028, 0.052, headDistance);
                float headGlow = exp(-headDistance * 9.0);

                float tickRadius = Band(radius, _Radius + 0.15, 0.052, 0.014);
                float tickCellPosition = angle * 24.0;
                float tickCellIndex = floor(tickCellPosition);
                float tickCellOffset = frac(tickCellPosition);
                float tickCells = 1.0 - smoothstep(0.06, 0.18, abs(tickCellOffset -0.5));

                float digitLocalX = (0.5 - tickCellOffset) / 0.34 + 0.5;
                float digitLocalY = (radius - (_Radius + 0.098)) / 0.104;
                float digitBounds = step(0.0, digitLocalX) * step(digitLocalX, 1.0)
                * step(0.0, digitLocalY) * step(digitLocalY, 1.0);
                float digitCycle = _Progress < 0.0 ? floor(_Time.y * _Speed) : 0.0;
                float glyphCount = max(1.0, floor(_GlyphCount + 0.5));
                float digitIndex = floor(Hash11(tickCellIndex + digitCycle * 29.0 + 0.5) * glyphCount);
                float glyphCellWidth = rcp(glyphCount);
                float2 halfTexel = _DigitAtlas_TexelSize.xy * 0.5;
                float2 digitAtlasUV = float2(
                    lerp(
                        digitIndex * glyphCellWidth + halfTexel.x,
                        (digitIndex + 1.0) * glyphCellWidth - halfTexel.x,
                        saturate(digitLocalX)
                    ),
                    lerp(halfTexel.y, 1.0 - halfTexel.y, saturate(digitLocalY))
                    );
                    float digitCells = SAMPLE_TEXTURE2D(_DigitAtlas, sampler_DigitAtlas, digitAtlasUV).a * digitBounds;
                    float stripCells = lerp(tickCells, digitCells, step(0.5, _StripStyle));

                    float stripTrailPosition = trailPosition - _StripHeadGap;
                    float tickWindow = smoothstep(0.0, 0.06, stripTrailPosition) * (1.0 - smoothstep(_StripCycleLength - 0.08, _StripCycleLength, stripTrailPosition));
                    float tickActivity = _Progress >= 0.0 ? progressArc : tickWindow;
                    float ticks = tickRadius * stripCells * tickActivity * step(0.0001, _StripCycleLength);

                    float arcEnergy = progressArc * (ringCore * 1.3 + ringGlow * 0.38 + ringHalo * 0.08);
                    float3 color = _DimColor.rgb * dimRing;
                    color += _ArcColor.rgb * (arcEnergy + ticks * 0.72);
                    color += _HeadColor.rgb * (headCore * 1.5 + headGlow * 0.38);

                    float alpha = saturate(
                        dimRing * _DimColor.a
                        + arcEnergy
                        + ticks
                        + headCore
                        + headGlow * 0.5
                    );
                    return half4(color * input.color.rgb, alpha * input.color.a);
                }
                ENDHLSL
            }
        }
    }

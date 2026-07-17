Shader "Ratioscope/UI/MosaicEffect"
{
    Properties
    {
        _IntroTex ("Initial (RGB)", 2D) = "white" { }
        _MainTex ("Final (RGB)", 2D) = "white" { }
        _Slider ("Reveal Slider", Range(0, 1)) = 0.0
        _FadeSoftness ("Fade Softness", Range(0, 0.1)) = 0.03
        _RevealShattering ("Reveal Shattering", Range(0, 1)) = 0.125
        _GlitchStrength ("Glitch Strength", Range(0, 1)) = 0.25

        _ChromaticAberration ("Chromatic Aberration", Range(0, 5)) = 1.0
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.25
        _NoiseAnimated ("Noise Animation", Range(0, 1)) = 0.75
        _TimeSpeed ("Animation Speed", Float) = 1.0
        _JitterStrength ("Jitter Strength", Int) = 300

        _MosaicStrength ("Mosaic Strength", Range(0, 1.0)) = 0.65
        _MosaicSize ("Mosaic Size", Range(0, 256)) = 64.0
        _MosaicAspect ("Mosaic Aspect", Range(0, 1)) = 0.5 // 0 = Width, 0.5 = Screen, 1 = Height
        _MosaicStretch ("Mosaic Stretch (X Y dX dY)", Vector) = (1, 1, 0, 0)
        _MosaicDropout ("Mosaic Cell Dropout", Range(0, 1)) = 0
        _VoronoiBlend ("Voronoi Blend", Range(0, 1)) = 0
        _TwinkleStrength ("Twinkle Strength", Range(0, 1.0)) = 0
        _TwinkleSpeed ("Twinkle Speed", Float) = 10
        _TwinkleDensity ("Twinkle Density", Range(0, 1.0)) = 0.2
        _TwinkleJitter ("Twinkle Jitter", Range(0, 1.0)) = 0.28

        [Toggle(_INITIAL_MOSAIC)] _ApplyMosaicToInitial ("Apply Mosaic To Initial", Float) = 0
        [Toggle(_EXIT_ONLY)] _ExitOnly ("Exit-Only (Mosaic to Final)", Float) = 0
        [Toggle(_UNIFORM_BLEND)] _UniformBlend ("Uniform Blend (Skip Wavefront)", Float) = 0

        [KeywordEnum(MainOnly, HasLine)] _PASS_MODE ("Pass Mode", Float) = 0
        [KeywordEnum(Horizontal, Vertical, Center, Corner, Random)] _REVEAL_MODE ("Reveal Mode", Float) = 2
        _LineDirection ("Reveal Direction (Right Up Left Down)", Vector) = (1, 1, 1, 1)
        _LineThickness ("Line Thickness", Range(0, 0.1)) = 0.01
        _LineColor ("Line Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "Library/FragmentUtils.cginc"

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
        };

        float _TimeSpeed;
        float _RevealShattering;
        float4 _LineDirection;
        float _MosaicAspect;
        float4 _MosaicStretch;
        float _VoronoiBlend;

        float4 revealDirection()
        {
            float4 raw = _LineDirection;
            float4 direction = max(raw, float4(0.0, 0.0, 0.0, 0.0));
            direction.z = raw.x < 0.0 && direction.z <= 0.0001 ? - raw.x : direction.z;
            direction.w = raw.y < 0.0 && direction.w <= 0.0001 ? - raw.y : direction.w;
            return dot(direction, float4(1.0, 1.0, 1.0, 1.0)) > 0.0001
            ? direction
            : float4(1.0, 1.0, 1.0, 1.0);
        }

        // Shared cell mapping for both the mosaic pixelation and the reveal wavefront, so the
        // reveal follows the same cells as the blocks. Continuous controls, no hard modes:
        //   _MosaicAspect            0 = width-based square, 0.5 = quad-shaped, 1 = height-based square.
        //   _MosaicStretch.xy        1 = normal, higher values stretch cells on X/Y.
        //   _MosaicStretch.zw        0 = uniform, 1 = randomize X per row and Y per column.
        //   _VoronoiBlend            0 = regular grid, 1 = irregular Voronoi polygons.
        // Outputs the per-cell id, the per-axis whole-cell count (for reveal scaling) and the UV to
        // sample for the block colour. The display aspect comes from _ScreenParams (a spatially
        // constant uniform), so the cell grid never wobbles within the quad.
        void mosaicCells(float2 uv, float size, out float2 cell, out float2 cellCount, out float2 pixelUV)
        {
            // Cell counts per axis. _MosaicAspect picks the reference dimension for square cells
            // Use _ScreenParams (uniform), NOT ddx/ddy: per-pixel derivatives wobble and tear each cell
            // into thin strips once the vertical count also depends on the aspect. (texture, not uv)
            float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0); // widthPx / heightPx
            float t = saturate(_MosaicAspect);
            float2 n = max(float2(
                size * aspect / lerp(aspect, 1.0, saturate(2.0 * t - 1.0)),
                size / lerp(aspect, 1.0, saturate(2.0 * t))
            ), 1.0);

            float2 stretch = max(_MosaicStretch.xy, float2(1.0, 1.0));
            float2 variation = saturate(_MosaicStretch.zw);
            float2 variationCell = floor(uv * n);
            if (variation.x > 0.0 && stretch.x > 1.0)
            {
                float rowNoise = Rand(float2(variationCell.y, 43.17));
                stretch.x = lerp(stretch.x, lerp(1.0, stretch.x, rowNoise), variation.x);
            }
            if (variation.y > 0.0 && stretch.y > 1.0)
            {
                float columnNoise = Rand(float2(variationCell.x, 87.31));
                stretch.y = lerp(stretch.y, lerp(1.0, stretch.y, columnNoise), variation.y);
            }
            n = max(n / stretch, 1.0);

            float2 cellUV = 1.0 / n;
            cellCount = max(floor(n), 1.0);
            float2 margin = (1.0 - cellCount * cellUV) * 0.5; // centre the leftover when it doesn't divide evenly

            float jitter = saturate(_VoronoiBlend);
            if (jitter <= 0.0)
            {
                // Pure grid (rectangle or square per _MosaicAspect).
                cell = clamp(floor((uv - margin) / cellUV), 0.0, cellCount - 1.0);
                pixelUV = saturate((cell + 0.5) * cellUV + margin);
                return;
            }

            // Voronoi: nearest jittered seed in grid space (see Voronoi() in FragmentUtils).
            // jitter == 0 keeps seeds at cell centres (a regular grid); 1 scatters them into polygons.
            float2 seed;
            Voronoi((uv - margin) / cellUV, jitter, cell, seed);
            pixelUV = saturate(seed * cellUV + margin);
        }

        float horizontalRevealMetric(float2 cell, float2 cells)
        {
            float4 direction = revealDirection();
            float scale = max(cells.x - 1.0, 1.0);
            return direction.z > direction.x ? (cells.x - 1.0 - cell.x) / scale : cell.x / scale;
        }

        float verticalRevealMetric(float2 cell, float2 cells)
        {
            float4 direction = revealDirection();
            float scale = max(cells.y - 1.0, 1.0);
            return direction.w > direction.y ? (cells.y - 1.0 - cell.y) / scale : cell.y / scale;
        }

        float randomizeRevealMetric(float metric, float2 cell)
        {
            if (_RevealShattering <= 0.0 || metric > 1.0)
                return metric;

            float noise = Rand(cell * 13.13 + float2(71.17, 19.31));
            float offset = (noise - 0.5) * saturate(_RevealShattering);
            return saturate(metric + offset);
        }

        float2 regularGridRevealOffset(float2 cell)
        {
            float regularGrid = 1.0 - saturate(_VoronoiBlend);
            float rowShift = Rand(float2(cell.y, 43.17)) - 0.5;
            float columnShift = Rand(float2(87.31, cell.x)) - 0.5;
            return float2(rowShift, columnShift) * (0.45 * regularGrid);
        }

        float centerRevealMetric(float2 cell, float2 cells)
        {
            float4 direction = revealDirection();
            float2 center = (cells - 1.0) * 0.5;
            float2 offset = cell - center;
            float2 metricOffset = offset + regularGridRevealOffset(cell) * saturate(length(offset));

            float hasHorizontal = step(0.0001, max(direction.x, direction.z));
            float hasVertical = step(0.0001, max(direction.y, direction.w));
            float horizontalActive = offset.x >= 0.0 ? step(0.0001, direction.x) : step(0.0001, direction.z);
            float verticalActive = offset.y >= 0.0 ? step(0.0001, direction.y) : step(0.0001, direction.w);
            float active = lerp(1.0, horizontalActive, hasHorizontal) * lerp(1.0, verticalActive, hasVertical);

            float innerRadius = length(float2(frac(center.x), frac(center.y)));
            float outerRadius = max(length(center), innerRadius + 0.0001);
            float metric = saturate((length(metricOffset) - innerRadius) / max(outerRadius - innerRadius, 0.0001));
            return active > 0.0 ? metric : 2.0;
        }

        float cornerRevealMetric(float2 cell, float2 cells)
        {
            float4 direction = revealDirection();
            float2 cornerOffset = float2(
                direction.z > direction.x ? cells.x - 1.0 - cell.x : cell.x,
                direction.w > direction.y ? cells.y - 1.0 - cell.y : cell.y
            );
            float outerRadius = max(length(cells - 1.0), 1.0);
            float metric = length(cornerOffset) / outerRadius;
            return saturate(metric);
        }

        // Per-cell deterministic value in [0,1]. Cells with lower values reveal first;
        // cells with higher values reveal last. No spatial wavefront — purely scattered.
        float randomRevealMetric(float2 cell)
        {
            return saturate(Rand(cell * 13.13 + float2(71.17, 19.31)));
        }

        float revealMetric(float2 uv, float mosaicSize)
        {
            float2 cell, cellCount, pixelUV;
            mosaicCells(uv, mosaicSize, cell, cellCount, pixelUV);

            float metric;
            #ifdef _REVEAL_MODE_HORIZONTAL
                metric = horizontalRevealMetric(cell, cellCount);
            #elif defined(_REVEAL_MODE_VERTICAL)
                metric = verticalRevealMetric(cell, cellCount);
            #elif defined(_REVEAL_MODE_CORNER)
                metric = cornerRevealMetric(cell, cellCount);
            #elif defined(_REVEAL_MODE_RANDOM)
                // Already maximally scattered; skip the extra shattering pass.
                return randomRevealMetric(cell);
            #else
                metric = centerRevealMetric(cell, cellCount);
            #endif

            return randomizeRevealMetric(metric, cell);
        }

        float mosaicReveal(float2 uv, float slider, float softness, float mosaicSize)
        {
            float slider01 = saturate(slider);
            if (slider01 <= 0.0)
                return 0.0;
            if (slider01 >= 1.0)
                return 1.0;

            float metric = revealMetric(uv, mosaicSize);
            float soft = max(softness, 0.0001);
            return smoothstep(metric - soft, metric + soft, slider01);
        }

        v2f vert(appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            o.color = v.color;
            return o;
        }
        ENDCG

        // Main Pass
        Pass
        {
            Name "Main effect"
            Cull Off
            Lighting Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local __ _REVEAL_MODE_HORIZONTAL _REVEAL_MODE_VERTICAL _REVEAL_MODE_CENTER _REVEAL_MODE_CORNER _REVEAL_MODE_RANDOM
            #pragma multi_compile_local __ _INITIAL_MOSAIC
            #pragma multi_compile_local __ _UNIFORM_BLEND
            #pragma multi_compile_local __ _EXIT_ONLY
            #include "Library/BlurUtils.cginc"

            UNITY_DECLARE_TEX2D(_MainTex);
            UNITY_DECLARE_TEX2D(_IntroTex);
            float4 _IntroTex_TexelSize;

            float _Slider;
            float _FadeSoftness;
            float _GlitchStrength;
            float _ChromaticAberration;
            float _NoiseStrength;
            float _NoiseAnimated;
            float _JitterStrength;
            float _MosaicSize;
            float _MosaicStrength;
            float _MosaicDropout;
            float _TwinkleStrength;
            float _TwinkleSpeed;
            float _TwinkleDensity;
            float _TwinkleJitter;

            // Apply chromatic aberration, glitch scanlines, noise overlay and pixelation
            // to a sampled colour. `texel` is _IntroTex's pixel size for offset math; the
            // resampling stays on the intro texture so the effect always represents the
            // "before" image being torn apart, even when blending toward the final.
            fixed4 applyIntroEffects(float2 uv, fixed4 baseCol, float2 texel)
            {
                float2 offset = texel * _ChromaticAberration;
                float r = UNITY_SAMPLE_TEX2D(_IntroTex, uv + float2(offset.x, 0)).r;
                float g = UNITY_SAMPLE_TEX2D(_IntroTex, uv).g;
                float b = UNITY_SAMPLE_TEX2D(_IntroTex, uv - float2(offset.x, 0)).b;
                fixed4 col = fixed4(r, g, b, baseCol.a);

                float glitch = step(0.95, Rand(float2(floor(uv.y * 200), _Time.y * _TimeSpeed)));
                if (glitch > 0)
                {
                    float shift = (Rand(float2(uv.y * 50.0, _Time.y)) - 0.5) * 0.05 * _GlitchStrength;
                    col.rgb = UNITY_SAMPLE_TEX2D(_IntroTex, uv + float2(shift, 0)).rgb;
                }

                float noise;
                if (_NoiseAnimated > 0.5)
                    noise = (Rand(uv * _JitterStrength + _Time.y * _TimeSpeed) - 0.5) * 2.0;
                else
                    noise = (Rand(uv * _JitterStrength) - 0.5) * 2.0;
                col.rgb += noise * _NoiseStrength;

                if (_MosaicSize > 0)
                {
                    float2 mosaicCell, cellCount, pixelUV;
                    mosaicCells(uv, _MosaicSize, mosaicCell, cellCount, pixelUV);
                    float twinkle = 0;
                    if (_TwinkleStrength > 0 && _TwinkleDensity > 0)
                    {
                        float cellSeed = Rand(mosaicCell + 19.19);
                        float twinkleClock = _Time.y * _TwinkleSpeed + cellSeed * 37.0;
                        float timeStep = floor(twinkleClock);
                        float twinklePhase = frac(twinkleClock);
                        float twinkleGate = step(1.0 - _TwinkleDensity, Rand(mosaicCell * 2.17 + timeStep * 11.31));
                        float twinkleLife = smoothstep(0.0, 0.18, twinklePhase) * (1.0 - smoothstep(0.45, 1.0, twinklePhase));
                        twinkle = twinkleGate * twinkleLife;

                        float2 jitter = float2(
                            Rand(mosaicCell + float2(13.17, timeStep * 1.37)),
                            Rand(mosaicCell + float2(timeStep * 2.11, 31.41))
                        ) - 0.5;
                        pixelUV = saturate(pixelUV + jitter * twinkle * _TwinkleJitter / max(_MosaicSize, 1.0));
                    }

                    fixed4 mosaicCol = UNITY_SAMPLE_TEX2D(_IntroTex, pixelUV);
                    if (_MosaicDropout > 0)
                    {
                        float dropoutSeed = Rand(mosaicCell * 5.31 + float2(17.23, 91.7));
                        float dropout = step(1.0 - saturate(_MosaicDropout), dropoutSeed);
                        mosaicCol.rgb = lerp(mosaicCol.rgb, fixed3(0.0, 0.0, 0.0), dropout);
                    }
                    if (twinkle > 0)
                    {
                        float pulse = (Rand(mosaicCell * 2.37 + floor(_Time.y * _TwinkleSpeed * 2.0)) - 0.5) * 2.0;
                        mosaicCol.rgb = saturate(mosaicCol.rgb + pulse * _TwinkleStrength * twinkle);
                    }
                    col.rgb = lerp(col.rgb, mosaicCol.rgb, _MosaicStrength);
                }

                return col;
            }

            // Low precision for performance
            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                fixed4 intro = UNITY_SAMPLE_TEX2D(_IntroTex, uv);
                fixed4 final = UNITY_SAMPLE_TEX2D(_MainTex, uv);
                fixed4 effectsIntro = applyIntroEffects(uv, intro, _IntroTex_TexelSize.xy);

                #ifdef _INITIAL_MOSAIC
                    fixed4 initial = effectsIntro;
                #else
                    fixed4 initial = intro;
                #endif

                // Apply vertex color uniformly across all sample colours.
                initial.rgb *= i.color.rgb;
                initial.a *= i.color.a;
                effectsIntro.rgb *= i.color.rgb;
                effectsIntro.a *= i.color.a;
                final.rgb *= i.color.rgb;
                final.a *= i.color.a;

                // Skips the spatial cell reveal entirely and uses the slider directly as a temporal blend factor
                // Useful when the caller wants the whole image to glitch then resolve, without a moving wave
                // The default path reveals whole mosaic cells for callers like the intro screen.
                #ifdef _UNIFORM_BLEND
                    float reveal = saturate(_Slider);
                #else
                    float reveal = mosaicReveal(uv, _Slider, _FadeSoftness, _MosaicSize);
                #endif

                #ifdef _EXIT_ONLY
                    // Exit-only: slider 0 = pure mosaic (held while the caller waits for a response),
                    // slider 1 = pure final. The initial-side ramp is gone.
                    // Callers snap to 0 on request start and ease to 1 when the result arrives.
                    fixed4 result = lerp(effectsIntro, final, reveal);
                    // Force fully opaque: this mode is a full-coverage splash.
                    // Source captures from CameraScreenshotIntoRenderTexture often land with alpha = 0,
                    // which would otherwise let what's drawn behind the overlay bleed through
                    // (e.g. live 3D rendered before this pass).
                    result.a = i.color.a;
                    return result;
                #else
                    // Three-zone blend driven by reveal:
                    //   reveal ~ 0   -> initial (clean or mosaic'd per toggle)
                    //   reveal ~ 0.5 -> mosaic'd "intro" colour at peak
                    //   reveal ~ 1   -> final (always clean)
                    float aWeight = saturate(1.0 - reveal * 2.0);
                    float bWeight = saturate((reveal - 0.5) * 2.0);
                    float mWeight = saturate(1.0 - aWeight - bWeight);

                    fixed4 result;
                    result.rgb = aWeight * initial.rgb + mWeight * effectsIntro.rgb + bWeight * final.rgb;
                    result.a = aWeight * initial.a + mWeight * effectsIntro.a + bWeight * final.a;
                    return result;
                #endif
            }
            ENDCG
        }

        // Line Pass
        Pass
        {
            Name "Bright line"
            Cull Off
            Lighting Off
            ZWrite Off
            ZTest [unity_GUIZTestMode]
            Blend SrcAlpha One

            CGPROGRAM
            #pragma multi_compile _PASS_MODE_BOTH _PASS_MODE_MAINONLY
            #pragma multi_compile_local __ _REVEAL_MODE_HORIZONTAL _REVEAL_MODE_VERTICAL _REVEAL_MODE_CENTER _REVEAL_MODE_CORNER _REVEAL_MODE_RANDOM
            #pragma vertex vert
            #pragma fragment frag
            float _Slider;
            float _LineThickness;
            fixed4 _LineColor;
            float _FadeSoftness;
            float _MosaicSize;

            fixed4 frag(v2f i) : SV_Target
            {
                #ifdef _PASS_MODE_MAINONLY
                    return fixed4(0, 0, 0, 0); // Transparent, skip rendering
                #endif

                float2 uv = i.uv;
                float dist = abs(revealMetric(uv, _MosaicSize) - saturate(_Slider));
                float halfThick = _LineThickness * 0.5;
                float low = halfThick - _FadeSoftness;
                float high = halfThick + _FadeSoftness;
                float lineMask = 1.0 - smoothstep(low, high, dist);

                // Rainbow with angular shift
                float angle = atan2(uv.y - 0.5, uv.x - 0.5);
                float hue = frac(angle / (2 * PI) + _Time.y * _TimeSpeed * 0.1); // Wrap angle to [0, 1]
                float3 rgb = HSVtoRGB(float3(hue, 0.8, 1.0)); // Bright neon colors
                fixed4 lineCol = fixed4(rgb, _LineColor.a);
                lineCol.rgb *= i.color.rgb;
                lineCol.a *= i.color.a * lineMask;
                return lineCol;
            }
            ENDCG
        }
    }
}

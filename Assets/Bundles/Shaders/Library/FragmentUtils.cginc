#ifndef FRAGMENT_UTILS_INCLUDED
#define FRAGMENT_UTILS_INCLUDED
#include "_Meta.cginc"

// Simple random function based on UV coordinates for use in shaders
// that need a noise pattern without a texture.
// Good enough for visual variety in effects like the mosaic shader.
float Rand(float2 uv)
{
    return frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
}

// Jittered-grid Voronoi (cellular noise). For a point `p` in grid space (one cell = one unit),
// finds the nearest of the per-cell jittered seeds across the 3x3 neighbourhood.
//   jitter: 0 keeps every seed at its cell centre (a regular grid), 1 scatters it across the cell.
// Returns the squared distance to the nearest seed (F1); outputs that seed's integer cell id and
// its position in grid space. Seeds stay inside their own cell, so the nearest is always in the 3x3.
float Voronoi(float2 p, float jitter, out float2 cell, out float2 seed)
{
    float2 baseCell = floor(p);
    float best = 1e9;
    cell = baseCell;
    seed = baseCell + 0.5;
    [unroll]
    for (int oy = -1; oy <= 1; oy++)
    {
        [unroll]
        for (int ox = -1; ox <= 1; ox++)
        {
            float2 nb = baseCell + float2(ox, oy);
            float2 jittered = nb + 0.5 + (float2(Rand(nb + 11.71), Rand(nb + 17.13)) - 0.5) * jitter;
            float2 diff = jittered - p;
            float d = dot(diff, diff);
            if (d < best)
            {
                best = d;
                cell = nb;
                seed = jittered;
            }
        }
    }
    return best;
}

// Face normal calculation using ddx/ddy
float3 GetFaceNormal(float3 worldPos)
{
    float3 dx = ddx(worldPos);
    float3 dy = ddy(worldPos);
    return normalize(cross(dy, dx));
}

// RGB to HSV conversion
float3 RGBtoHSV(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + EPSILON)), d / (q.x + EPSILON), q.x);
}

// HSV to RGB conversion
float3 HSVtoRGB(float3 hsv)
{
    float3 rgb = clamp(abs(fmod(hsv.x * 6.0 + float3(0.0, 4.0, 2.0), 6) - 3.0) - 1.0, 0, 1);
    rgb = rgb * rgb * (3.0 - 2.0 * rgb);
    return hsv.z * lerp(float3(1, 1, 1), rgb, hsv.y);
}

// Adjust Color based on rgb - HSV
float3 AdjustColor(float3 rgb, float4 adjust)
{
    float hue = adjust.x;
    float saturation = adjust.y;
    float value = adjust.z;
    float3 hsv = RGBtoHSV(rgb);
    hsv.x = fmod(hsv.x + hue, 1.0f);
    hsv.y = clamp(hsv.y * saturation, 0.0f, 1.0f);
    hsv.z = clamp(hsv.z * value, 0.0f, 1.0f);
    return HSVtoRGB(hsv);
}

// Luminance-based color overlay
// https://discussions.unity.com/t/overlay-blend-mode-shader/504124/3
float4 Overlay(float4 src, float4 blender)
{
    // Calculate the twiceLuminance of the texture color
    float twiceLuminance = dot(src, float4(0.2126, 0.7152, 0.0722, 0)) * 2;
    // Declare the output structure
    float4 output = 0;

    // The actual overlay/high light method is based on the shader
    if (twiceLuminance < 1)
    {
        output = lerp(float4(0, 0, 0, 0), blender, twiceLuminance);
    }
    else
    {
        output = lerp(blender, float4(1, 1, 1, 1), twiceLuminance - 1);
    }

    // The alpha can actually just be a simple blend of the two-
    // makes things nicely controllable in both texture and color
    output.a = src.a * blender.a;
    return output;
}

// 双边滤波采样（减少噪声）
float3 BilateralSample(TEXTURE2D_PARAM(tex, texSampler), float2 uv, float2 offset)
{
    float2 sampleUV = uv + offset;
    float3 center = SAMPLE_TEXTURE2D(tex, texSampler, uv).rgb;
    float3 sampleColor = SAMPLE_TEXTURE2D(tex, texSampler, sampleUV).rgb;

    // 颜色差异检测
    float colorDiff = length(center - sampleColor);
    float weight = exp(-colorDiff * 20.0); // 降低差异大的区域权重

    return sampleColor * weight;
}

// Improve Quality
// Unity provides size of _MainTex as _MainTex_TexelSize.zw
float4 BicubicTextureSampling(TEXTURE2D_PARAM(tex, texSampler), float2 uv, float2 texSize)
{
    float2 texCoord = uv * texSize - 0.5;
    float2 f = frac(texCoord);
    float2 floorCoord = floor(texCoord) / texSize;

    float4 c00 = SAMPLE_TEXTURE2D(tex, texSampler, floorCoord);
    float4 c10 = SAMPLE_TEXTURE2D(tex, texSampler, floorCoord + float2(1.0 / texSize.x, 0));
    float4 c01 = SAMPLE_TEXTURE2D(tex, texSampler, floorCoord + float2(0, 1.0 / texSize.y));
    float4 c11 = SAMPLE_TEXTURE2D(tex, texSampler, floorCoord + float2(1.0 / texSize.x, 1.0 / texSize.y));

    float4 c0 = lerp(c00, c10, f.x);
    float4 c1 = lerp(c01, c11, f.x);
    return lerp(c0, c1, f.y);
}

#endif // FRAGMENT_UTILS_INCLUDED
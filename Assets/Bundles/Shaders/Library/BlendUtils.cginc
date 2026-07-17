#ifndef BLEND_UTILS_INCLUDED
#define BLEND_UTILS_INCLUDED
#include "_Meta.cginc"

// Simple n-planar sampling, does NOT consider object space.
float2 ProjectUV(float3 worldPos, float3 normalDir)
{
    // Take the major axis of the projection direction and drop it
    float3 absN = abs(normalDir);
    // project onto YZ
    if (absN.x > absN.y && absN.x > absN.z) return worldPos.zy;
    // project onto XZ
    else if (absN.y > absN.x && absN.y > absN.z) return worldPos.xz;
    // project onto XY
    else return worldPos.xy;
}

// Triplanar texture sampling in world space
float4 Triplanar_World(TEXTURE2D_PARAM(tex, texSampler), float3 worldPos, float3 worldNormal, float tiling)
{
    // World-space UVs for each plane
    float2 uvX = worldPos.zy * tiling; // Project onto YZ
    float2 uvY = worldPos.xz * tiling; // Project onto XZ
    float2 uvZ = worldPos.xy * tiling; // Project onto XY

    uvX = frac(uvX);
    uvY = frac(uvY);
    uvZ = frac(uvZ);

    // Absolute normal components as blending weights
    float3 blend = abs(worldNormal);
    blend = pow(blend, 4.0); // Sharpen blending
    blend /= (blend.x + blend.y + blend.z);

    // Sample the texture in each direction
    float4 colX = SAMPLE_TEXTURE2D(tex, texSampler, uvX);
    float4 colY = SAMPLE_TEXTURE2D(tex, texSampler, uvY);
    float4 colZ = SAMPLE_TEXTURE2D(tex, texSampler, uvZ);

    // Weighted blend
    return colX * blend.x + colY * blend.y + colZ * blend.z;
}

// 像素权重计算
// *Info: WebGL does NOT support arrays of matrices!
// Blending calculation does NOT need frustum culling, so no projection matrices.
void ProcessBlendFactor(
    float3 worldPos,
    int index,
    float4 color,
    float depthDiff,
    float4x4 matV,
    float dist,
    float offset,
    int useDepthDiff,
    inout float depths[4],
    inout float valid[4],
    inout float bestDepth,
    inout int bestIndex)
{
    // Invisible source
    if (color.a < EPSILON)
    {
        valid[index] = 0;
        depths[index] = INFINITY;
        return;
    }

    float4 viewPos4 = mul(matV, float4(worldPos, 1.0));

    // Cull points behind camera
    if (viewPos4.z >= 0.0)
    {
        valid[index] = 0;
        depths[index] = INFINITY;
        return;
    }

    // //? Visibility test (normal)
    // // Extract camera position from view matrix and compute view direction from point to camera
    // float3 camPos = -mul(transpose((float3x3)matV), matV[3].xyz);
    // float3 viewDir = normalize(camPos - worldPos);
    // float ndot = dot(worldNormal, viewDir);

    // Compute view-space depth (front is negative)
    float viewDepth = lerp(-viewPos4.z, -viewPos4.z - dist, offset);
    depths[index] = viewDepth;

    // TODO: Make it toggle-able?
    // Use depth difference for visibility
    if (useDepthDiff > 0) valid[index] = (depthDiff < 0.5) ? - 1 : 1;
    // Skip depthDiff check, always consider visible
    else valid[index] = 1;

    // Choose best depth among valid
    if (valid[index] > 0 && viewDepth < bestDepth)
    {
        bestDepth = viewDepth;
        bestIndex = index;
    }
}

// Quad projection, depth-based blending + occluding
float4 QuadUVBlend(
    float3 worldPos,
    float4 colors_0, float4 colors_1, float4 colors_2, float4 colors_3,
    float dp_0, float dp_1, float dp_2, float dp_3,
    float4x4 mat_v_0, float4x4 mat_v_1, float4x4 mat_v_2, float4x4 mat_v_3,
    float d_0, float d_1, float d_2, float d_3,
    float offset, float sharpness, float suppression,
    int useDepthDiff)
{
    float4 colorsArr[4] = {
        colors_0, colors_1, colors_2, colors_3
    };
    float dpArr[4] = {
        dp_0, dp_1, dp_2, dp_3
    };
    float dist[4] = {
        d_0, d_1, d_2, d_3
    };

    // Continue with normal depth-based blending
    // ------------------------------------------------------------
    float depths[4];
    float valid[4];
    {
        for (int i = 0; i < 4; i++)
        {
            depths[i] = INFINITY;
            valid[i] = 0.0;
        }
    }

    float bestDepth = INFINITY;
    int bestIndex = -1;

    // Compute depth-based validity for all cameras
    ProcessBlendFactor(worldPos, 0, colorsArr[0], dpArr[0], mat_v_0, dist[0], offset, useDepthDiff, depths, valid, bestDepth, bestIndex);
    ProcessBlendFactor(worldPos, 1, colorsArr[1], dpArr[1], mat_v_1, dist[1], offset, useDepthDiff, depths, valid, bestDepth, bestIndex);
    ProcessBlendFactor(worldPos, 2, colorsArr[2], dpArr[2], mat_v_2, dist[2], offset, useDepthDiff, depths, valid, bestDepth, bestIndex);
    ProcessBlendFactor(worldPos, 3, colorsArr[3], dpArr[3], mat_v_3, dist[3], offset, useDepthDiff, depths, valid, bestDepth, bestIndex);

    clip(bestIndex);

    // Adjustable suppression when multiple projectors overlap
    // ------------------------------------------------------------
    if (suppression > 0)
    {
        int visibleCount = 0;
        UNITY_UNROLL
        for (int i = 0; i < 4; i++)
        {
            if (valid[i] != 0)
                visibleCount++;
        }

        // 0 = strict occlusion, 1 = fully ignore occlusion when overlap
        if (visibleCount > 1)
        {
            UNITY_UNROLL
            for (int i = 0; i < 4; i++)
            {
                if (valid[i] != 0)
                {
                    // If originally occluded (valid[i] < 0), gradually lift suppression
                    valid[i] = lerp(valid[i], 1.0, saturate(suppression));
                }
            }
        }
    }

    // Weighted blending among valid layers
    // ------------------------------------------------------------
    float depthFailWeightScale = 0.0;
    float4 result = float4(0, 0, 0, 0);
    float totalWeight = 0.0;
    {
        UNITY_UNROLL
        for (int i = 0; i < 4; i++)
        {
            if (abs(valid[i]) < EPSILON)
                continue;

            float diff = abs(depths[i] - bestDepth);
            float w = exp(-diff * sharpness * 100.0);

            if (valid[i] < 0)
                w *= depthFailWeightScale;

            w *= saturate(colorsArr[i].a);

            result += colorsArr[i] * w;
            totalWeight += w;
        }
    }

    if (totalWeight > 0.0)
        return result / totalWeight;

    // Fallback (no valid blend)
    // ------------------------------------------------------------

    {
        UNITY_UNROLL
        for (int i = 0; i < 4; i++)
        {
            if (colorsArr[i].a > EPSILON)
                return lerp(float4(0, 0, 0, 0), colorsArr[i], colorsArr[i].a);
        }
    }

    return float4(0, 0, 0, 0);
}

#endif // BLEND_UTILS_INCLUDED
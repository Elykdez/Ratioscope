#ifndef VERTEX_UTILS_INCLUDED
#define VERTEX_UTILS_INCLUDED
#include "UnityCG.cginc"
#include "_Meta.cginc"

// Stable 2D hash
float hash21(float2 p)
{
    p = frac(p * float2(0.3183099, 0.3678123));
    p = p * p * (3.0 - 2.0 * p);
    return frac(p.x + p.y);
}

// Remap UV (0..1) to Clip Space (-1..1)
// Returns a clip space position based on UV coordinates (with platform correction)
// We use v.uv.y for Direct3D-like, but in OpenGL/WebGL we might need adjustments.
// Unity's UV starts at bottom-left (0,0). Clip space (-1,-1) is bottom-left.
// So: 0 -> -1, 1 -> 1.
float4 UVToClipPos(float2 uv)
{
    // Put clip space z to 0.5 to survive depth testing
    float4 uvBasedPos = float4(uv * 2.0 - 1.0, 0.5, 1.0);
    // OpenGL => DX
    #if UNITY_UV_STARTS_AT_TOP
        uvBasedPos.y = -uvBasedPos.y;
    #endif
    return uvBasedPos;
}

// Lerp vertex from clip position to uv based position
float4 Lerp2D23D(float4 vertex, float2 uv, float dimension)
{
    float4 uvBasedPos = UVToClipPos(uv);

    // Correct aspect ratio on screen
    #if UNITY_UV_STARTS_AT_TOP
        // _ScreenParams does not work in OpenGL. Let's assume all the others has this feature.
        float aspectRatio = _ScreenParams.x / _ScreenParams.y;
    #else
        float aspectRatio = UNITY_MATRIX_P[1][1] / UNITY_MATRIX_P[0][0];
    #endif

    uvBasedPos.x *= min(1.0, 1 / aspectRatio);
    uvBasedPos.y *= min(1.0, aspectRatio);

    float4 clipPos = UnityObjectToClipPos(vertex);
    return lerp(uvBasedPos, clipPos, saturate(dimension));
}

// Get view space unit depth bounds
float2 GetDepthBound(float4x4 boundMatrix)
{
    // Object's bounds in world space within 1 size unit square
    float3 boundsWorld[8] = {
        mul(boundMatrix, float4(-0.5, -0.5, -0.5, 1.0)).xyz,
        mul(boundMatrix, float4(0.5, -0.5, -0.5, 1.0)).xyz,
        mul(boundMatrix, float4(-0.5, 0.5, -0.5, 1.0)).xyz,
        mul(boundMatrix, float4(0.5, 0.5, -0.5, 1.0)).xyz,
        mul(boundMatrix, float4(-0.5, -0.5, 0.5, 1.0)).xyz,
        mul(boundMatrix, float4(0.5, -0.5, 0.5, 1.0)).xyz,
        mul(boundMatrix, float4(-0.5, 0.5, 0.5, 1.0)).xyz,
        mul(boundMatrix, float4(0.5, 0.5, 0.5, 1.0)).xyz,
    };

    // Transform bounds into view space
    float3 boundsView[8];
    UNITY_UNROLL
    for (int i = 0; i < 8; i++)
    {
        boundsView[i] = mul(UNITY_MATRIX_MV, float4(boundsWorld[i], 1.0)).xyz;
    }

    // Find closest and farthest depths in view space
    float closest = -boundsView[0].z;
    float farthest = -boundsView[0].z;

    UNITY_UNROLL
    for (int j = 1; j < 8; j++)
    {
        float depth = -boundsView[j].z;
        closest = min(closest, depth);
        farthest = max(farthest, depth);
    }

    return float2(closest, farthest);
}

#endif // VERTEX_UTILS_INCLUDED
#ifndef CHROMA_LUMA_KEY_INCLUDED
#define CHROMA_LUMA_KEY_INCLUDED

// Shared chroma+luma key math, used by ColorKeyComposite (turns the selection
// into the CG matte) and ColorSuppress (uses it as a despill qualifier). Pure
// functions parameterized by explicit values - no uniforms are declared here, so
// each caller supplies its own (_Key* for the keyer, _Suppress* for the despill).

float CLK_Luma(float3 c)
{
    return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float2 CLK_Chroma(float3 c)
{
    float maxChannel = max(max(c.r, c.g), c.b);
    float3 normalized = c / max(maxChannel, 0.0001);
    float y = CLK_Luma(normalized);
    return float2(normalized.b - y, normalized.r - y);
}

float CLK_Band(float edge, float width, float value)
{
    return smoothstep(edge, edge + max(width, 0.0001), value);
}

// Foreground-convention chroma alpha: 1 where the pixel is FAR from keyColor
// (kept), 0 where it matches. choke shifts the boundary (CG keyer uses it; pass
// 0 to disable).
float CLK_ChromaAlpha(float3 c, float3 keyColor, float threshold, float smoothing, float choke)
{
    float chromaDistance = distance(CLK_Chroma(c), CLK_Chroma(keyColor)) - choke;
    return CLK_Band(threshold, smoothing, chromaDistance);
}

// Foreground-convention luma alpha for the three luma modes (0 dark, 1 bright,
// 2 around target). 1 = kept, 0 = keyed.
float CLK_LumaAlpha(
    float3 c,
    float lumaMode,
    float lumaTarget,
    float lumaThreshold,
    float lumaSmoothing,
    float choke)
{
    float luma = CLK_Luma(c);
    float score;
    if (lumaMode < 0.5)
        score = luma;                 // key out dark, keep bright
    else if (lumaMode < 1.5)
        score = 1.0 - luma;           // key out bright, keep dark
    else
        score = abs(luma - lumaTarget); // key out a target brightness
    score -= choke;
    return CLK_Band(lumaThreshold, lumaSmoothing, score);
}

// Combine chroma/luma per KeyMode (0 chroma, 1 luma, 2 and = min, 3 or = max).
// Symmetric in its inputs: the keyer feeds foreground alphas, the suppressor
// feeds (1 - alpha) selection weights, and both get consistent And/Or behavior.
float CLK_Combine(float keyMode, float chromaValue, float lumaValue)
{
    if (keyMode < 0.5)
        return chromaValue;
    if (keyMode < 1.5)
        return lumaValue;
    if (keyMode < 2.5)
        return min(chromaValue, lumaValue);
    return max(chromaValue, lumaValue);
}

// Luma-preserving despill: project the key hue out of the pixel's chroma and
// remove it scaled by amount. Shared by the CG edge despill (amount from spill x
// edge weight) and the selectable suppressor (amount from strength x qualifier).
float3 CLK_DespillHue(float3 rgb, float3 keyColor, float amount)
{
    if (amount <= 0.0001)
        return rgb;

    float luma = CLK_Luma(rgb);
    float3 gray = float3(luma, luma, luma);
    float3 chroma = rgb - gray;

    float keyLuma = CLK_Luma(keyColor);
    float3 keyChroma = keyColor - float3(keyLuma, keyLuma, keyLuma);
    float keyChromaLengthSq = dot(keyChroma, keyChroma);
    if (keyChromaLengthSq <= 0.0001)
        return rgb;

    float3 keyHue = keyChroma * rsqrt(keyChromaLengthSq);
    float spill = max(0.0, dot(chroma, keyHue));
    float3 correctedChroma = chroma - keyHue * spill * amount;
    return saturate(gray + correctedChroma);
}

#endif // CHROMA_LUMA_KEY_INCLUDED

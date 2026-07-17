#ifndef RATIOSCOPE_CORTEX_MATRIX_COMMON_INCLUDED
#define RATIOSCOPE_CORTEX_MATRIX_COMMON_INCLUDED

float CortexHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

fixed4 CortexShadeCell(
    float2 sheetUv,
    float2 heatUv,
    float2 cellUv,
    float2 cellId,
    float volumeMix,
    float3 viewNormal,
    float glowIntensity
)
{
    float heat = tex2D(_MainTex, heatUv).r;

    float2 d = abs(cellUv - 0.5);
    float mask = (1.0 - smoothstep(0.32, 0.46, d.x))
    * (1.0 - smoothstep(0.32, 0.46, d.y));
    float bloomMask = (1.0 - smoothstep(0.28, 0.5, d.x))
    * (1.0 - smoothstep(0.28, 0.5, d.y));

    float seed = CortexHash21(cellId);
    float shimmer = 0.07 + 0.06 * sin(_Time.y * (1.5 + seed * 2.0) + seed * 6.2831);
    fixed3 cellColor = lerp(
        _CalmColor.rgb,
        _HotColor.rgb,
        saturate(_EntropyMix + seed * 0.15 - 0.075)
    );
    float intensity = shimmer + heat * 1.6;
    float2 centeredUv = sheetUv * 2.0 - 1.0;
    float vignette = saturate(1.0 - dot(centeredUv, centeredUv) * 0.28);
    float tokenEdge = _TokenRows / _Rows;
    float separator = 1.0 - smoothstep(0.0, 0.006, abs(sheetUv.y - tokenEdge));
    float scan = 0.015 * sin(sheetUv.y * _Rows * 3.14159 + _Time.y * 0.7);
    fixed3 rgb = _BgColor.rgb
    + cellColor * (intensity * mask * vignette + separator * 0.18 + scan);

    float fresnel = pow(1.0 - saturate(abs(normalize(viewNormal).z)), 2.2);
    // Positive time term moves the wave toward -y: downward, following the data flow.
    float crawl = 0.5 + 0.5 * sin(sheetUv.y * _Rows * 1.7 + _Time.y * 4.0);
    float flicker = 0.97 + 0.03 * sin(_Time.y * (8.0 + seed * 5.0) + seed * 31.0);
    fixed3 hologram = cellColor
    * glowIntensity
    * (fresnel * 0.32 + heat * bloomMask * 0.42 + crawl * 0.035);
    rgb = lerp(rgb, rgb * flicker + hologram, saturate(volumeMix));

    return fixed4(rgb, 1.0);
}

#endif

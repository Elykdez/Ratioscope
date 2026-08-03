#ifndef CORTEX_UTILS_INCLUDED
#define CORTEX_UTILS_INCLUDED
#include "FragmentUtils.cginc"

// Upper bound on the layer symbol set ("+", "=", "#", "@", "*", "%"). C# uploads however many
// of them the font actually carries and passes the live count alongside. The symbols are
// deliberately meaningless - a fixed label per layer reads as static text, while a drifting
// field of symbols reads as a layer doing work.
#define CORTEX_GLYPH_CAPACITY 6

// Fraction of a cell's width the symbol row occupies. The leftover margin is what keeps the
// cell boundary readable: slots tile at a constant pitch, so without it the sheet becomes one
// undifferentiated grid of characters and a layer cell stops reading as a cell. Sitting wider
// than the gap between slots is the whole point - the boundary has to beat the inner spacing.
#define CORTEX_GLYPH_ROW_INSET 0.84

// How far a symbol is widened past its true aspect. At four or five pixels tall a character
// drawn at its natural width leaves its slot half empty and the set stops being tellable apart -
// a "#" and a "@" collapse into the same narrow smudge. Stretching it to take up the slot buys
// back the horizontal detail; much beyond this and the type visibly distorts.
#define CORTEX_GLYPH_WIDEN 1.16

float CortexHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

/// Ink of the symbol row inside one cell: glyph coverage, already scaled by its flash.
///
/// The symbols come out of the same TMP SDF atlas the token labels are built from, so both
/// halves of the sheet draw the same typeface at the same weight. C# uploads one atlas UV rect
/// and one placement quad per symbol; this only picks an index and samples.
///
/// A cell is split into glyph slots - as many as fit at roughly one slot per row height, so the
/// characters land on a square grid rather than clustering - and every slot keeps its own
/// 3-11Hz clock. On each beat it re-rolls its symbol; a minority of those
/// rolls land as a flash and a few blank the slot outright. Nothing ties one slot to its
/// neighbours, so the rows scintillate rather than sliding a pattern across the sheet.
///
/// Only the glyph carries any of it. The caller draws the symbol in place of the cell block, so
/// the returned ink decides which pixels of a layer cell light up - never in what colour or how
/// brightly, which stay entirely with the heat and entropy data.
///
/// Antialiasing derives from sheetUv rather than cellUv: the flat path builds cellUv with
/// frac(), whose derivatives explode on the cell seams, while sheetUv stays continuous.
float CortexSampleLayerGlyph(
    sampler2D glyphAtlas,
    float4 glyphAtlasTexelSize,
    float4 glyphRects[CORTEX_GLYPH_CAPACITY],
    float4 glyphQuads[CORTEX_GLYPH_CAPACITY],
    float glyphCount,
    float glyphGradientScale,
    float glyphFill,
    float columns,
    float rows,
    float glyphSlots,
    float layerFlatCellAspect,
    float layerFoldedCellAspect,
    float2 sheetUv,
    float2 cellUv,
    float2 cellId,
    float timeSeconds,
    float volumeMix
)
{
    float symbols = clamp(floor(glyphCount + 0.5), 1.0, CORTEX_GLYPH_CAPACITY);
    float slots = max(1.0, floor(glyphSlots + 0.5));

    // Slots tile across the inset row, not the whole cell, so a margin survives at each cell
    // boundary. The slot index is clamped rather than wrapped: inside that margin slotUv falls
    // outside 0..1 and the glyph's own bounds test drops it, which is what leaves the gap empty.
    float rowU = (cellUv.x - 0.5) / CORTEX_GLYPH_ROW_INSET + 0.5;
    float slot = clamp(floor(rowU * slots), 0.0, slots - 1.0);
    float2 slotUv = float2(rowU * slots - slot, cellUv.y);
    float2 slotId = float2(cellId.x * slots + slot, cellId.y);
    float slotSeed = Rand(slotId);

    // Ticks are wrapped before they reach the hash: sin-based hashing loses its randomness once
    // the argument runs into the thousands, and these clocks would get there within the hour.
    float beat = timeSeconds * lerp(3.0, 11.0, slotSeed) + slotSeed * 37.0;
    float tick = fmod(floor(beat), 1024.0);
    float phase = frac(beat);

    int glyphIndex = (int)min(
        symbols - 1.0,
        floor(Rand(slotId * 0.61 + tick) * symbols)
    );

    // Roughly a quarter of the rolls land as a flash - a hard attack that decays away inside
    // its own tick, so the sheet sparkles at no fixed rhythm.
    float flash =
        1.0 + step(0.74, Rand(slotId * 1.31 + tick + 5.7)) * exp2(-phase * 6.0) * 1.6;

    // A slot is either lit or dark, never a half-faded glyph. The occasional blank tick is what
    // makes the rows blink like a character display instead of sitting there as a texture.
    float presence = step(0.07, Rand(slotId * 0.83 + tick + 11.3));

    float4 rect = glyphRects[glyphIndex];
    float4 quad = glyphQuads[glyphIndex];

    // Fit the symbol set's shared box into a slot that is rarely square. C# normalized that box
    // to span 1.0 on its longer axis, so glyph space stays isotropic on screen and each symbol
    // keeps its true size and baseline position relative to the others.
    //
    // glyphFill is how much of a row's height the symbol may take, and everything left over is
    // the gap. Layer rows are only a few pixels tall, so this is what stops the sheet from
    // closing up into one slab of text - the leading is what separates one layer from the next.
    // The horizontal allowance runs a little looser because the slot is the wider axis.
    float cellAspect = max(
        lerp(layerFlatCellAspect, layerFoldedCellAspect, saturate(volumeMix)),
        0.01
    );
    float slotAspect = max(cellAspect / slots, 0.01);
    float widthAllowance = saturate(glyphFill + 0.2);
    float2 fit = float2(widthAllowance, saturate(glyphFill));
    float fittedWidth = fit.y / slotAspect;
    if (fittedWidth <= fit.x)
        fit.x = fittedWidth;
    else
        fit.y = fit.x * slotAspect;

    // Widen last, so the symbol fills out its slot horizontally without ever growing past the
    // allowance and running into the cell margin that draws the boundary.
    fit.x = min(fit.x * CORTEX_GLYPH_WIDEN, widthAllowance);

    float2 p = (slotUv - 0.5) / fit;
    float2 g = (p - quad.xy) / max(quad.zw, 0.0001);
    float2 atlasUv = rect.xy + (g * 0.5 + 0.5) * rect.zw;
    // Explicit LOD: atlasUv jumps wherever two neighbouring pixels land on different symbols or
    // different slots, and letting the hardware derive a mip from that picks nonsense.
    float distanceField = tex2Dlod(glyphAtlas, float4(atlasUv, 0.0, 0.0)).a;

    // The SDF ramps by 1.0 over gradientScale texels, so turn the slot's pixel footprint into
    // texels and then into field units to land a one-pixel edge. Derived analytically instead
    // of with fwidth(), which would spike into a blurred band on every slot seam.
    float2 slotSize = float2(columns * slots / CORTEX_GLYPH_ROW_INSET, rows);
    float2 unitsPerPixel = max(abs(ddx(sheetUv)), abs(ddy(sheetUv))) * slotSize / fit;
    float texelsPerUnit = rect.w * glyphAtlasTexelSize.w / max(quad.w * 2.0, 0.0001);
    float smoothing = max(
        max(unitsPerPixel.x, unitsPerPixel.y)
        * texelsPerUnit
        / max(glyphGradientScale, 1.0)
        * 0.5,
        0.001
    );
    float coverage = smoothstep(0.5 - smoothing, 0.5 + smoothing, distanceField);
    float2 inside = step(abs(g), 1.0);
    return coverage * inside.x * inside.y * presence * flash;
}

fixed4 CortexShadeCell(
    sampler2D heatTexture,
    sampler2D glyphAtlas,
    float4 glyphAtlasTexelSize,
    float4 glyphRects[CORTEX_GLYPH_CAPACITY],
    float4 glyphQuads[CORTEX_GLYPH_CAPACITY],
    float glyphCount,
    float glyphGradientScale,
    float glyphFill,
    float columns,
    float rows,
    float tokenRows,
    float layerGlyphSlots,
    float layerFlatCellAspect,
    float layerFoldedCellAspect,
    float entropyMix,
    fixed3 calmColor,
    fixed3 hotColor,
    fixed3 backgroundColor,
    float timeSeconds,
    float2 sheetUv,
    float2 heatUv,
    float2 cellUv,
    float2 cellId,
    float volumeMix,
    float3 viewNormal,
    float glowIntensity
)
{
    float heat = tex2D(heatTexture, heatUv).r;

    float2 d = abs(cellUv - 0.5);
    float mask = (1.0 - smoothstep(0.32, 0.46, d.x))
    * (1.0 - smoothstep(0.32, 0.46, d.y));
    float bloomMask = (1.0 - smoothstep(0.28, 0.5, d.x))
    * (1.0 - smoothstep(0.28, 0.5, d.y));

    float seed = CortexHash21(cellId);
    float shimmer = 0.07 + 0.06 * sin(timeSeconds * (1.5 + seed * 2.0) + seed * 6.2831);
    fixed3 cellColor = lerp(
        calmColor,
        hotColor,
        saturate(entropyMix + seed * 0.15 - 0.075)
    );
    float intensity = shimmer + heat * 1.6;
    float2 centeredUv = sheetUv * 2.0 - 1.0;
    float vignette = saturate(1.0 - dot(centeredUv, centeredUv) * 0.28);
    float tokenEdge = tokenRows / rows;
    float structureMask = step(tokenEdge, sheetUv.y);
    float tokenLabelVisibility = (1.0 - structureMask) * saturate(heat * 4.0);
    float cellFillVisibility = 1.0 - tokenLabelVisibility;

    // Sampled unconditionally: the symbol field needs screen-space derivatives, and gating
    // it on per-cell heat would leave them undefined on the quads that straddle a seam.
    float layerGlyph = CortexSampleLayerGlyph(
        glyphAtlas,
        glyphAtlasTexelSize,
        glyphRects,
        glyphQuads,
        glyphCount,
        glyphGradientScale,
        glyphFill,
        columns,
        rows,
        layerGlyphSlots,
        layerFlatCellAspect,
        layerFoldedCellAspect,
        sheetUv,
        cellUv,
        cellId,
        timeSeconds,
        volumeMix
    );

    // On the layer rows the symbol *is* the cell: it replaces the solid block rather than
    // sitting on top of it. Everything downstream is untouched, so heat and entropy still own
    // the colour and the brightness exactly as they do for a block - the symbol just decides
    // which pixels of the cell receive it.
    //
    // No cell mask is applied. The slots already tile at a constant pitch straight across cell
    // boundaries, so fading them towards the cell edge would only stripe the sheet into visible
    // per-cell groups; the grid has to read as one even field of characters.
    //
    // An idle sheet is a field of blocks; a layer only breaks into symbols once it carries heat,
    // so the glyphs read as the model working rather than as wallpaper. Without a symbol set at
    // all, the layer rows keep the blocks they always had.
    float glyphRows = structureMask * step(0.5, glyphCount) * saturate(heat * 3.0);
    float contentMask = lerp(mask, layerGlyph, glyphRows);
    float contentBloom = lerp(bloomMask, layerGlyph, glyphRows);
    float separator = 1.0 - smoothstep(0.0, 0.006, abs(sheetUv.y - tokenEdge));
    float scan = 0.015 * sin(sheetUv.y * rows * 3.14159 + timeSeconds * 0.7);
    fixed3 rgb = backgroundColor
    + cellColor
        * (intensity * contentMask * vignette * cellFillVisibility + separator * 0.18 + scan);

    float fresnel = pow(1.0 - saturate(abs(normalize(viewNormal).z)), 2.2);
    // Positive time term moves the wave toward -y: downward, following the data flow.
    float crawl = 0.5 + 0.5 * sin(sheetUv.y * rows * 1.7 + timeSeconds * 4.0);
    float flicker = 0.97 + 0.03 * sin(timeSeconds * (8.0 + seed * 5.0) + seed * 31.0);
    fixed3 hologram = cellColor
    * glowIntensity
    * (
        fresnel * 0.32
        + heat * contentBloom * cellFillVisibility * 0.42
        + crawl * 0.035
    );
    rgb = lerp(rgb, rgb * flicker + hologram, saturate(volumeMix));

    return fixed4(rgb, 1.0);
}

#endif

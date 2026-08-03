# Cortex glyph sources

The Cortex view draws characters in three places, from two unrelated sources. Only one of
them is a texture asset you can open in the project, which is why searching `Assets/Bundles/Texture`
for the layer symbols finds nothing.

| Where | Characters | Source |
| --- | --- | --- |
| Layer rows (the scrambling symbol field) | (Random Glyphs like `#@$`) | TMP font asset SDF atlas, at runtime |
| Token labels (the strip at the bottom) | sampled token text | Same TMP font asset SDF atlas |
| Loading ring | `0123456789ABCDEF` | `Assets/Bundles/Texture/Atlas/CortexDigitAtlas.png` |

## Layer symbols and token labels

Both come out of `tokenTextSource.font.atlasTexture` - the TMP font asset assigned to the
`Token Text Source` field on `CortexMatrix.prefab`. Nothing is baked; there is no glyph
image on disk for either.

The symbol set is a code constant, which is deliberately meaningless: a fixed label per layer reads as
static text, a drifting field of symbols reads as a layer doing work.

`CortexMatrixVolume.UploadLayerGlyphs()` runs once during material setup and:

- asks a dynamic TMP atlas to add the symbols (`TryAddCharacters`); a static atlas is left alone,
- reads each character's `glyph.glyphRect` and `glyph.metrics` out of `font.characterLookupTable`,
- packs an atlas UV rect per symbol into `_LayerGlyphRects` (inset half a texel so bilinear
  taps cannot reach the glyph TMP packed next door),
- packs a placement box per symbol into `_LayerGlyphQuads`, normalised so the set's shared
  ink box spans 1 on its longer axis, which preserves relative sizes and baselines,
- binds `font.atlasTexture` as `_LayerGlyphAtlas`, the resolved symbol count as
  `_LayerGlyphCount`, and TMP's distance ramp width (`atlasPadding + 1`) as
  `_LayerGlyphGradientScale`.

Token labels bind the same texture as `_MainTex` on the label material instance and build a
single batched mesh from TMP's own layout and atlas UVs.

Shader side, `CortexSampleLayerGlyph` in `Assets/Bundles/Shaders/Library/CortexUtils.cginc`
splits each cell into up to `MaxLayerGlyphSlots` (4) slots, gives every slot its own 3-11 Hz
clock that re-rolls which symbol it shows, samples the SDF alpha with an explicit-LOD tap, and
returns coverage. `CortexShadeCell` then substitutes that coverage for the solid cell block on
layer rows, gated on cell heat, so an idle sheet stays blocks and a working layer breaks into
symbols. Both `CortexMatrix.shader` (flat) and `CortexMatrixVolume.shader` (folded) include
the same file and pass the same properties.

Failure modes are soft. Symbols the font cannot supply are skipped; symbols TMP packed onto
atlas page 1 or later are skipped too, because only page 0 is ever bound. If none resolve,
`_LayerGlyphCount` goes to 0, a warning is logged, and the layer rows keep plain blocks.

`CORTEX_GLYPH_CAPACITY` in `CortexUtils.cginc` (6) is the hard ceiling on the symbol set;
`LayerGlyphSymbols` must not exceed it.

To change the symbols, edit `LayerGlyphSymbols` and make sure the assigned TMP font asset
carries the new characters. To change the typeface, swap the font on `tokenTextSource` -
that moves the token labels with it, by design.

## Loading ring

`CortexDigitAtlas.png` is the only baked glyph sheet, and it is not part of the matrix at all.
`Assets/Editor/Tools/GlyphAtlasGenerator.cs` renders `0123456789ABCDEF` from
`Assets/Bundles/Fonts/GeistPixel.ttf` into one horizontal strip of 16x32 cells
(**Tools > Hypocycloid > Assets > Glyph Atlas Generator**). `CortexLoadingRing.shader`
samples it as `_DigitAtlas` with `_GlyphCount = 16` when `_StripStyle` is set to `Digits`.

Regenerating this atlas has no effect on the layer symbols.

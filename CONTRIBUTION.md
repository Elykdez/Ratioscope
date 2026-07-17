# Ratioscope - Core Module Guide

This document introduces the **core, reusable infrastructure** of this Unity project. It is meant for developers joining this project, and for anyone who wants to lift these modules into another Unity project of a similar shape (desktop tool / media editor with Addressables, Unity Localization, and a TMP-based UI).

**Scope:** Everything below is foundational plumbing that is decoupled from any specific feature.

## Development Notes

- ONNX is only used for editor-side export and conversion. At runtime, use the already converted `.sentis` model assets and configuration. Conversion entry points are available from `Tools/Hypocycloid/ONNX Processor` and `Tools/Hypocycloid/Sentis/Convert ONNX to Sentis`.
- See [Docs/BiRefNet-Sentis-Pipeline.md](Docs/BiRefNet-Sentis-Pipeline.md) for AI inference pipeline details and quality considerations.
- See [Docs/Model-Hosting.md](Docs/Model-Hosting.md) for model hosting and lite package download instructions.

Two namespaces hold the core:

- `Hypocycloid.Utils` ([Assets/Scripts/Utils/](Assets/Scripts/Utils/)) - engine-agnostic-ish helpers, base classes, collections, math. Zero dependency on Hypocycloid feature code; drop-in portable.
- `Hypocycloid.UI` ([Assets/Scripts/Modules/UI/](Assets/Scripts/Modules/UI/)) - reusable TMP/UI widgets with no app coupling (tooltips, markdown text + hyperlinks, and the shared UI components in [UI/Shared/](Assets/Scripts/Modules/UI/Shared/)). Reference `Hypocycloid.Utils`.
- `Hypocycloid.Ratioscope` ([Assets/Scripts/Modules/](Assets/Scripts/Modules/)) - app-level modules (lifecycle/state, loading, config panel, localization). Reference `Hypocycloid.Utils` and `Hypocycloid.UI`.

---

## Module Map

| Module | Namespace | Entry point | Portable? |
| --- | --- | --- | --- |
| Module lifecycle & state | `Hypocycloid.Ratioscope` | [ModuleBase.cs](Assets/Scripts/Modules/ModuleBase.cs) | Yes |
| Global loading effect | `Hypocycloid.Ratioscope` | [LoadingRegistry.cs](Assets/Scripts/Modules/LoadingRegistry.cs), [UILoading.cs](Assets/Scripts/Modules/UI/UILoading.cs) | Yes |
| Reflection config panel | `Hypocycloid.Ratioscope` | [UIConfigPanel.cs](Assets/Scripts/Modules/UI/UIConfigPanel.cs) | Yes |
| Tooltip / tips | `Hypocycloid.UI` | [TipSystem.cs](Assets/Scripts/Modules/UI/Tips/TipSystem.cs), [TipsTrigger.cs](Assets/Scripts/Modules/UI/Tips/TipsTrigger.cs) | Yes |
| Localization / language switch | `Hypocycloid.Ratioscope` | [UILanguage.cs](Assets/Scripts/Modules/UI/UILanguage.cs) | Yes (needs `com.unity.localization`) |
| Markdown text & hyperlinks | `Hypocycloid.UI` | [UIMarkdownRenderer.cs](Assets/Scripts/Modules/UI/Text/UIMarkdownRenderer.cs), [Markdown.cs](Assets/Scripts/Modules/UI/Text/Markdown/Markdown.cs) | Yes |
| Shared UI components | `Hypocycloid.UI` | [UI/Shared/](Assets/Scripts/Modules/UI/Shared/) | Yes |
| Addressable asset loading | `Hypocycloid.Utils` | [AssetLoadBase.cs](Assets/Scripts/Utils/Asset/AssetLoadBase.cs) | Yes (needs `com.unity.addressables`) |
| Atlas / spritesheet packing | `Hypocycloid.Utils` | [SpriteSheetPacker.cs](Assets/Scripts/Utils/SpriteSheetPacker.cs) | Yes |
| Singletons & main-thread dispatch | `Hypocycloid.Utils` | [MonoSingleton.cs](Assets/Scripts/Utils/Mono/MonoSingleton.cs), [Dispatcher.cs](Assets/Scripts/Utils/Mono/Dispatcher.cs) | Yes |
| Logging | `Hypocycloid.Utils` | [LogHelper.cs](Assets/Scripts/Utils/LogHelper.cs) | Yes |
| UI / collections / math / extensions | `Hypocycloid.Utils` | [Utils/](Assets/Scripts/Utils/) | Yes |

---

## 1. Module Lifecycle & State

Files: [ModuleBase.cs](Assets/Scripts/Modules/ModuleBase.cs), [Defs.cs](Assets/Scripts/Modules/Defs.cs)

`ModuleBase` is the base class every feature module derives from. It gives a module a single observable `State` (`Loading` / `Ready` / `Occupied`, see `ModuleState` in `Defs.cs`) plus a token-based way to declare what it is doing. You never set the state directly; you open and close requests and the state is recomputed:

- `BeginModuleLoading()` / `EndModuleLoading(token)` - the work is async I/O the user should wait on. This also opens a request on the global `LoadingRegistry`, so the loading overlay shows automatically.
- `BeginModuleWork()` / `EndModuleWork(token)` - the module is busy but no global spinner is needed (`Occupied`).
- Subscribe to `StateChanged` to react (disable buttons, etc.).

Tokens are integers handed back from each `Begin*`; pass the same token to the matching `End*`. Multiple overlapping requests are reference-counted, and `OnDisable`/`OnDestroy` auto-release everything so a destroyed module can never leave the app stuck in `Loading`.

Reuse: copy `ModuleBase`, `IModule`, and `ModuleState`. The only external coupling is `LoadingRegistry` (next section); drop that one call if you do not want the global overlay.

`Defs.cs` is the project's shared enum/POCO bucket (`ModuleState`, `PreviewMode`, `ExportType`, `LanguageOption`, etc.). When porting, take only the enums a module actually references.

---

## 2. Global Loading Effect

Files: [LoadingRegistry.cs](Assets/Scripts/Modules/LoadingRegistry.cs), [UILoading.cs](Assets/Scripts/Modules/UI/UILoading.cs)

A decoupled "something is loading" system. Producers and the visual are wired through an interface, so no module references the loading UI directly.

- `LoadingRegistry` is a static reference-counted registry of in-flight requests. Anyone calls `LoadingRegistry.BeginLoadingRequest()` / `EndLoadingRequest(token)`. `ModuleBase` already does this for you.
- `ILoadingEffectReceiver` is implemented by whatever renders the effect. `UILoading` is the concrete receiver: a full-screen overlay (`RawImage` + animated material) plus an optional progress `Image`. It self-registers in `Awake`/`OnEnable`.
- The registry picks the most recently registered live receiver, so the topmost active screen owns the spinner; dead receivers are pruned automatically.

`UILoading` also exposes `BeginLoading()/EndLoading()` directly (for non-registry callers) and `SetProgress(0..1)` for determinate progress. `Bind(RawImage)` lets the overlay sample the current preview texture so the spinner blurs the live content instead of a blank screen.

Reuse: take both files. Implement `ILoadingEffectReceiver` on your own overlay if you do not want `UILoading`'s material-driven look. The static registry has no other dependencies.

---

## 3. Reflection-Driven Config Panel

Files: [UIConfigPanel.cs](Assets/Scripts/Modules/UI/UIConfigPanel.cs), [ConfigSettingsAttribute.cs](Assets/Scripts/Generics/ConfigSettingsAttribute.cs)

One of the most reusable pieces here. It builds a settings UI **at runtime by reflection**, so you never hand-wire a settings screen. Annotate a `MonoBehaviour`:

- Put `[ConfigSettings("CategoryI18nKey", priority)]` on the class.
- Put `[ConfigSetting("LabelI18nKey", tipKey, priority, resync)]` on any field, property, or zero-arg method.

`UIConfigPanel.Rebuild()` scans all loaded types for `[ConfigSettings]`, finds the live scene instances, and generates a control per member by type: `bool` -> toggle, `enum` -> dropdown, `int/float` with `[Range]` -> slider, other `int/float/string` -> input field, zero-arg method -> button, getter-only property -> read-only status label. Values are parsed/clamped (honors `[Range]`/`[Min]`), written back to the instance, and `resync: true` members poll-refresh their control four times a second so external changes show up.

Extra hooks:

- `IConfigEnumOptionProvider` lets a target relabel or disable individual enum dropdown options (Hypocycloid uses it to gray out AI models that are not downloaded yet).
- Every label/tooltip key is run through Unity Localization (`StringTable`), and tooltips reuse the Tips module (section 4).
- Each row is cloned from a template prefab assigned on the panel (one per control kind). The clone's caption and control are located by the authored `{{Label}}` / `{{Value}}` child anchors; a missing prefab logs an error and skips that member rather than building anything at runtime.

Reuse: highly portable - copy `UIConfigPanel.cs` and `ConfigSettingsAttribute.cs`. It needs TMP and (for localized labels) Unity Localization; strip the `SetLocalizedText` localization calls if you only want raw keys.

---

## 4. Tooltips / Tips

Files: [TipSystem.cs](Assets/Scripts/Modules/UI/Tips/TipSystem.cs), [TipsTrigger.cs](Assets/Scripts/Modules/UI/Tips/TipsTrigger.cs), [UITip.cs](Assets/Scripts/Modules/UI/Tips/UITip.cs)

A hover-tooltip system split into trigger and renderer:

- `TipsTrigger` sits on any UI element. It implements pointer enter/exit, an optional hover delay, target-graphic highlight, and a positioning anchor (an auto-created `{{TooltipAnchor}}` child). It extends `LocalizeStringEvent`, so tooltip text is localized out of the box, and a `ContentProvider` callback allows live text (e.g. the timeline pushes the current time).
- `TipSystem` is the single per-scene renderer that listens to all triggers and drives one shared `UITip` (the floating label). Triggers self-register statically, so a `TipSystem` automatically picks up every trigger in its scene - no manual wiring per element.
- Positioning converts the anchor's world position into the tip canvas's local space, honoring a pivot preset and offset.

Reuse: copy the `Tips/` folder. `TipsTrigger`'s localization base class requires Unity Localization; if you remove that dependency, change the base to `MonoBehaviour` and feed `tipString` directly.

---

## 5. Markdown Text & Hyperlinks

Files: [UIMarkdownRenderer.cs](Assets/Scripts/Modules/UI/Text/UIMarkdownRenderer.cs), [Markdown.cs](Assets/Scripts/Modules/UI/Text/Markdown/Markdown.cs), [MarkdownRenderingSettings.cs](Assets/Scripts/Modules/UI/Text/MarkdownRenderingSettings.cs), [UITextHyperlink.cs](Assets/Scripts/Modules/UI/Text/Links/UITextHyperlink.cs), [SimpleLinkBehavior.cs](Assets/Scripts/Modules/UI/Text/Links/SimpleLinkBehavior.cs)

A lightweight markdown-to-TMP-rich-text stack (namespace `Hypocycloid.UI`). Two layers:

- `UIMarkdownRenderer` is the drop-on component: add it to a `TMP_Text`, set its `Source` string (in code or the inspector), and it rebuilds the rendered text. It re-renders live on `OnValidate`, so authored markdown previews in edit mode.
- `Markdown` is the static engine: `Markdown.RenderToTextMesh(source, tmp, settings)` converts markdown into TMP rich-text tags through an ordered pipeline of line processors (auto-links, ordered/unordered lists, bold, italics, strikethrough, superscript, monospace, headers, links). `MarkdownRenderingSettings` is a serializable object that toggles and styles each feature (header sizes, list bullets, link color, etc.). The pipeline is extensible via `ICustomTextPreProcessor`.

Clickable links are a separate, composable pair:

- `UITextHyperlink` (on a `TMP_Text`) makes TMP `<link>` segments hover- and click-aware: it recolors the link glyphs on hover/press and raises `OnLinkClicked(linkID)`. `Markdown.UpdateTextMesh` notifies it after each render so link info stays in sync.
- `SimpleLinkBehavior` is the default click handler: it opens the link id as a URL via `Application.OpenURL` (standalone), or runs a custom action registered with `SetCustomLink`.

The help panel ([UIHelpPanel.cs](Assets/Scripts/Modules/UI/UIHelpPanel.cs)) is the reference consumer: it fetches a locale-specific `desc_<locale>.md` from StreamingAssets and feeds the raw markdown to a wired `UIMarkdownRenderer` (it does no rich-text conversion of its own).

Reuse: portable - copy the `Text/` folder. Depends only on TMP, the Input System (`UITextHyperlink` reads the mouse), and `Hypocycloid.Utils` (`CoroutineHelper`). `Markdown` is intentionally experimental/incomplete per its own header comment; it covers the common subset, not the full spec.

---

## 6. Localization & Language Switching

Files: [UILanguage.cs](Assets/Scripts/Modules/UI/UILanguage.cs), `LanguageOption` in [Defs.cs](Assets/Scripts/Modules/Defs.cs)

Wraps Unity's `com.unity.localization` package with a ready-made language dropdown widget. `UILanguage` builds a toggle-group of available locales from a serialized `List<LanguageOption>` (flag icon + language image per locale), syncs the collapsed "current language" view to `LocalizationSettings.SelectedLocale`, and writes the user's choice back. It is `[ExecuteAlways]` so the editor previews the selected locale without instantiating/serializing option toggles.

Note that the whole project's localized text flows through Unity Localization's `StringTable`; the config panel (section 3) and tips (section 4) both resolve their keys against it. If you adopt this stack, set up a `StringTable` named `StringTable` (or change the constant) and your localized modules work together.

Reuse: `UILanguage` is a self-contained widget. Requires the Localization package and an `AvailableLocales` setup.

---

## 7. Addressable Asset Loading

Files: [AssetLoadBase.cs](Assets/Scripts/Utils/Asset/AssetLoadBase.cs) and the typed loaders [AssetLoadTexture2D.cs](Assets/Scripts/Utils/Asset/AssetLoadTexture2D.cs), [AssetLoadCSV.cs](Assets/Scripts/Utils/Asset/AssetLoadCSV.cs), [AssetLoadMaterial.cs](Assets/Scripts/Utils/Asset/AssetLoadMaterial.cs), [AssetLoadModel.cs](Assets/Scripts/Utils/Asset/AssetLoadModel.cs), [AssetLoadShader.cs](Assets/Scripts/Utils/Asset/AssetLoadShader.cs), [AssetLoadGameObject.cs](Assets/Scripts/Utils/Asset/AssetLoadGameObject.cs)

`AssetLoadBase<T>` is a generic `MonoBehaviour` that owns one `AssetReference` and the full Addressables lifecycle: load on `Awake` (or lazily via `EnsureLoaded()`), optional `Instantiate`, automatic handle release on `OnDestroy`/`Unload()`, and success/failure surfaced both as events (`OnAssetLoad`, `OnLoadingBegin/End`) and as pull-style state (`LoadedAsset`, `LoadFailed`, `IsLoading`, `HasAsset`). It optionally raises the loading effect (section 2) while a request is pending.

Each concrete subclass just binds `T` and adds a typed convenience. `AssetLoadCSV`, for example, parses the loaded `TextAsset` into `List<string[]>` via `StringHelper.CSV2List`. Pattern to add a new type: subclass `AssetLoadBase<YourType>`, override `OnLoadedAsset` for post-processing.

Reuse: very portable; the only dependency is `com.unity.addressables`. This is the recommended way to reference any heavy/optional asset in this codebase rather than direct references or `Resources`.

---

## 8. Atlas / Spritesheet Packing

File: [SpriteSheetPacker.cs](Assets/Scripts/Utils/SpriteSheetPacker.cs)

A pure (no `UnityEditor`) runtime atlas packer. Given a list of frame textures and a serializable `SpriteSheetSettings`, it searches column counts for the smallest (optionally power-of-two) sheet, auto-shrinks cells to fit a max size, centers/scales each frame into its cell, and emits the RGBA32 sheet plus optional JSON metadata (`SpriteSheetMetadata` with per-frame rects).

Two entry points:

- `Pack(frames, settings, ...)` - batch: all frames in memory at once. Used by the editor image-sequencer tool.
- `StreamPacker` - streaming: resolves layout up front from a uniform frame size, then `WriteFrame(i, tex)` composites one frame at a time so you never hold all frames in memory (it even clears the sheet row-by-row to avoid a single ~256 MB allocation for an 8192 sheet). Used by runtime sequence export.

Reuse: fully standalone (only `UnityEngine`). Good for any sprite-sequence, GIF/atlas, or texture-array authoring need. The caller owns and must `Destroy` the returned `Texture2D`.

---

## 9. Singletons & Main-Thread Dispatch

Files: [MonoSingleton.cs](Assets/Scripts/Utils/Mono/MonoSingleton.cs), [SingletonOneScene.cs](Assets/Scripts/Utils/Mono/SingletonOneScene.cs), [Dispatcher.cs](Assets/Scripts/Utils/Mono/Dispatcher.cs)

- `MonoSingleton<T>` - persistent (`DontDestroyOnLoad`) singleton with lazy `Ins` access, duplicate destruction, and a virtual `Init()` hook. `SingletonOneScene<T>` is the non-persistent, per-scene variant.
- `Dispatcher` - marshals work from background threads onto the Unity main thread. Call `Dispatcher.EnsureInitialized()` once at startup (it self-bootstraps via `[RuntimeInitializeOnLoadMethod]`), then `Dispatcher.InvokeAsync(action)` (fire-and-forget) or `Dispatcher.Invoke(action)` (blocks until run). Essential for the AI/FFmpeg work that runs off-thread.

Reuse: all three are dependency-free (`MonoSingleton`/`SingletonOneScene` use `LogHelper`). Drop in as-is.

---

## 10. Logging

File: [LogHelper.cs](Assets/Scripts/Utils/LogHelper.cs)

A `[Conditional("ENABLE_LOG")]` / `[Conditional("UNITY_EDITOR")]` logging facade - all calls are **compiled out of release builds** unless `ENABLE_LOG` is defined. Adds scene-tagged messages, colored logs (with a WebGL `console` bridge), a base64/large-array JSON truncator so logs do not explode on image payloads, and a simple named timer (`LogStart`/`LogEnd`). Prefer `LogHelper` over raw `Debug.Log` throughout this codebase.

---

## 11. Other Reusable Utilities

Under [Assets/Scripts/Utils/](Assets/Scripts/Utils/), mostly stateless helpers:

- UI: the self-contained UI components moved to [Modules/UI/Shared/](Assets/Scripts/Modules/UI/Shared/) under `Hypocycloid.UI` (`UIGradient`, `UIFadeAnim`, `UIButtonDebouncer`/`UIButtonThrottler`, `UIAspectResizer`, `UIRingLayoutGroup`, `UIStateSwitcher`, `UIDraggable`, `UIAutoResize`, `UIMatPropAnim`, `UIEmptyRaycast`, `UITimeCounter`). The static `UIHelper` event/binding/RectTransform helpers live in [Modules/UI/UIHelper.cs](Assets/Scripts/Modules/UI/UIHelper.cs).
- Collections: [Utils/Collections/](Assets/Scripts/Utils/Collections/) - `BiMap`/`TwoWayDictionary`, `HashList`, `ListedDictionary`, thread-safe `LockedList`/`LockedHashSet`.
- Math: [Utils/Math/](Assets/Scripts/Utils/Math/) - `BezierHelper`, `BitHelper`, `MathUtils`, `NumericHelper`, `VectorHelper`.
- Extensions: [Utils/Extensions/](Assets/Scripts/Utils/Extensions/) - typed extension methods for `Vector*`, `Color`, `GameObject`, `Material`, `Quaternion`, collections, reflection, and more.
- Mono helpers: [Utils/Mono/](Assets/Scripts/Utils/Mono/) - `CoroutineHelper`, `FunctionTimer`/`FunctionUpdater`, `FPSCounter`, `BlendFollow`, object pooling (`ObjectPool`/`ObjectPoolItem`), stepped animation.
- System/IO: [SystemHelper.cs](Assets/Scripts/Utils/SystemHelper.cs), [FFMpegHelper.cs](Assets/Scripts/Utils/FFMpegHelper.cs) (FFmpeg process wrapper for video/GIF), [PngCompressor.cs](Assets/Scripts/Utils/PngCompressor.cs), [StringHelper.cs](Assets/Scripts/Utils/StringHelper.cs).

---

## Porting Checklist

When lifting a module into another Unity project:

1. Copy the file(s), keep the `Hypocycloid.Utils` / `Hypocycloid.Ratioscope` namespace or rename consistently.
2. Add the package the module needs: Addressables (section 7), Localization (sections 3, 4, 6 for localized text), TextMeshPro + Input System (any UI module; the Markdown stack in section 5 needs both).
3. For UI modules, follow the project rule: wire serialized references on the prefab/scene rather than discovering them at runtime. The default-builder fallbacks (e.g. in `UIConfigPanel`) exist for bootstrapping, not as the intended wiring path.

## IL2CPP Build Troubleshooting

IL2CPP player builds invoke MSVC directly. Unity 6000.3 targets the v143 (14.4x) toolset; install it via the Visual Studio Installer under Individual Components if only a newer toolset is present.

If a build fails with `fatal error C1001: Internal compiler error` or exit code `-1073741819` (`0xC0000005`) in generated files such as `Generics__*.cpp`, `mscorlib.cpp`, or `Unity.TextMeshPro.cpp`, check whether the failing file changes between runs. A genuine compiler bug is deterministic and fails in the same place every time. Failure sites that move between builds indicate the machine is corrupting data under load, not a problem in this project.

This was diagnosed on 2026-07-18: an i7-13700K (Raptor Lake K-series) affected by Intel's Vmin Shift instability issue, running BIOS 1001 (2023-04-11) with microcode `0x113` -- predating all of Intel's mitigations (`0x125`, `0x129`, `0x12B`). Symptoms were random C1001/access-violation crashes 30-60s into every build plus hard system hangs (Kernel-Power 41). Updating to BIOS (microcode `0x12F`) and selecting **Intel Default Settings** resolved it. Parallel C++ compilation is among the heaviest sustained multi-core loads a desktop sees, so it surfaces this class of hardware fault before anything else does.

Verify the applied microcode with:

```powershell
(Get-ItemProperty "HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0").'Update Revision'
```

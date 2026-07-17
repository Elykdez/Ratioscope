using System;
using UnityEngine;

namespace Hypocycloid.Ratioscope
{
    public enum ModuleState
    {
        Loading,
        Ready,
        Occupied,
    }

    public enum TransformKind
    {
        Move,
        Scale,
        Rotate,
    }

    public enum PreviewMode
    {
        Front,
        Back,
        Combined,
    }

    /// <summary>
    /// Selectable download format. Image = current composited frame (PNG);
    /// Gif/Video iterate every frame of the foreground (video frames or the image sequence);
    /// Sequence packs every frame into a spritesheet PNG.
    /// </summary>
    public enum ExportType
    {
        Image,
        Gif,
        Video,
        Sequence,
    }

    /// <summary>
    /// Matches Sentis SupportsedBackends.
    /// </summary>
    public enum BackendPreference
    {
        GPUCompute = 0,
        GPUPixel = 1,
        CPU = 2,
    }

    public enum KeyMode
    {
        Chroma = 0,
        Luma = 1,
        CAndL = 2,
        COrL = 3,
    }

    public enum LumaKeyMode
    {
        Dark = 0,
        Bright = 1,
        Around = 2,
    }

    // GPU sampler used when the centered front is aspect-fitted or placed at the
    // background's physical pixel scale. Maps directly to Texture.filterMode.
    public enum ScaleFilter
    {
        Nearest = 0,
        Bilinear = 1,
    }

    public enum ShadowEffectMode
    {
        Shadow,
        Reflection,
        ShadowAndReflection,
    }

    /// <summary>
    /// Where the fake shadow sits in the layer stack.
    /// </summary>
    public enum ShadowOrder
    {
        BehindFront, // between the background and the foreground subject (normal contact shadow)
        InFront, // on top of everything
    }

    public interface IModule
    {
        ModuleState State { get; }
    }

    [Serializable]
    public sealed class LanguageOption
    {
        public LanguageOption() { }

        public LanguageOption(string localeCode)
        {
            LocaleCode = localeCode;
        }

        [field: SerializeField]
        public string LocaleCode { get; private set; }

        [field: SerializeField]
        public Sprite FlagIcon { get; private set; }

        [field: SerializeField]
        public Sprite LanguageImage { get; private set; }
    }
}

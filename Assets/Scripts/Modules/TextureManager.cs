using System.Collections.Generic;
using Hypocycloid.Utils;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Central owner for runtime RenderTextures created by processors and exporters.
    /// Scene-authored RenderTextures can remain as descriptor templates without being released here.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TextureManager : MonoSingleton<TextureManager>
    {
        static TextureManager activeInstance;
        readonly HashSet<RenderTexture> activeRenderTextures = new();

        public int ActiveRenderTextureCount => activeRenderTextures.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            activeInstance = null;
        }

        public override void Init()
        {
            activeInstance = this;
        }

        public RenderTexture GetRenderTexture(
            string name,
            int width,
            int height,
            RenderTextureFormat format = RenderTextureFormat.ARGB32,
            int depthBuffer = 0,
            FilterMode filterMode = FilterMode.Bilinear,
            bool sRGB = true,
            bool useMipMap = false,
            int anisoLevel = 1
        )
        {
            RenderTextureDescriptor descriptor =
                new(Mathf.Max(1, width), Mathf.Max(1, height), format, depthBuffer)
                {
                    dimension = TextureDimension.Tex2D,
                    msaaSamples = 1,
                    useMipMap = useMipMap,
                    autoGenerateMips = false,
                    sRGB = sRGB,
                };

            return GetRenderTexture(name, descriptor, filterMode, anisoLevel);
        }

        public RenderTexture GetRenderTexture(
            string name,
            RenderTextureDescriptor descriptor,
            FilterMode filterMode = FilterMode.Bilinear,
            int anisoLevel = 1
        )
        {
            activeInstance = this;

            descriptor.width = Mathf.Max(1, descriptor.width);
            descriptor.height = Mathf.Max(1, descriptor.height);
            descriptor.msaaSamples = Mathf.Max(1, descriptor.msaaSamples);

            RenderTexture rt = RenderTexture.GetTemporary(descriptor);
            rt.name = string.IsNullOrWhiteSpace(name) ? "Managed RenderTexture" : name;
            rt.filterMode = filterMode;
            rt.anisoLevel = Mathf.Max(1, anisoLevel);
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.hideFlags = HideFlags.HideAndDontSave;

            activeRenderTextures.Add(rt);
            return rt;
        }

        public RenderTexture GetRenderTexture(string name, RenderTexture template)
        {
            if (template == null)
                return null;

            RenderTexture rt = GetRenderTexture(
                name,
                template.descriptor,
                template.filterMode,
                template.anisoLevel
            );
            rt.wrapModeU = template.wrapModeU;
            rt.wrapModeV = template.wrapModeV;
            rt.wrapModeW = template.wrapModeW;
            return rt;
        }

        public bool Release(RenderTexture rt)
        {
            if (rt == null || !activeRenderTextures.Remove(rt))
                return false;

            rt.DiscardContents();
            RenderTexture.ReleaseTemporary(rt);
            return true;
        }

        public static void ReleaseManaged(ref RenderTexture rt)
        {
            if (rt == null)
                return;

            TextureManager manager = activeInstance;
            if (manager == null)
            {
                manager = FindFirstObjectByType<TextureManager>();
                activeInstance = manager;
            }

            manager?.Release(rt);

            rt = null;
        }

        public void CleanupAll()
        {
            int count = activeRenderTextures.Count;
            if (count > 0)
                LogHelper.LogWarning($"[TextureManager] Releasing {count} tracked RenderTextures.");

            foreach (RenderTexture rt in activeRenderTextures)
            {
                if (rt == null)
                    continue;

                rt.DiscardContents();
                RenderTexture.ReleaseTemporary(rt);
            }
            activeRenderTextures.Clear();
        }

        protected override void OnDestroy()
        {
            CleanupAll();
            if (activeInstance == this)
                activeInstance = null;
            base.OnDestroy();
        }
    }
}

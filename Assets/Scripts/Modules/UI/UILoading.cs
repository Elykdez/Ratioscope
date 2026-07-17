using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hypocycloid.Ratioscope
{
    [DisallowMultipleComponent]
    public sealed class UILoading : MonoBehaviour, ILoadingEffectReceiver
    {
        const float IndeterminateFill = 0.22f;
        const float IndeterminateDegreesPerSecond = 180f;
        const string DefaultMessage = "Loading...";

        [field: SerializeField]
        public RawImage LoadingOverlay { get; private set; }

        [field: SerializeField]
        public Image ProgressFillImage { get; private set; }

        [field: SerializeField]
        public RawImage PreviewDisplay { get; private set; }

        [field: SerializeField]
        public TMP_Text StatusLabel { get; private set; }

        [SerializeField]
        [Tooltip(
            "Root object activated while either the loading overlay or progress indicator is active. "
                + "Defaults to this GameObject."
        )]
        GameObject activeRoot;

        [SerializeField]
        [Tooltip(
            "Full-screen invisible raycast blocker, enabled while loading so the UI underneath "
                + "cannot be clicked. Link the scene's 'Masking' object here."
        )]
        GameObject inputBlocker;

        [SerializeField]
        [Tooltip("Enable the legacy preview-mosaic surface while loading.")]
        bool showLoadingOverlay = true;

        [SerializeField]
        [Tooltip("Optional authored visual used instead of the fallback rotating progress image.")]
        GameObject indeterminateVisual;

        public GameObject IndeterminateVisual => indeterminateVisual;

        readonly HashSet<int> loadingTokens = new();
        int nextLoadingToken = 1;
        float progress;
        Material loadingMaterialInstance;
        bool registered;

        static int LoadingIntroTexId { get; } = Shader.PropertyToID("_IntroTex");
        static int LoadingMainTexId { get; } = Shader.PropertyToID("_MainTex");
        static int LoadingSliderId { get; } = Shader.PropertyToID("_Slider");

        GameObject ActiveRoot => activeRoot != null ? activeRoot : gameObject;
        bool IsLoading => loadingTokens.Count > 0;
        bool IsProgressing => progress > 0f;
        bool ShouldShowRoot => IsLoading || IsProgressing;

        void Awake()
        {
            ResolveReferences();
            RegisterReceiver();
            EnsureLoadingMaterialInstance();
            ApplyVisualState();
        }

        void OnEnable()
        {
            RegisterReceiver();
            ApplyVisualState();
        }

        void OnDestroy()
        {
            LoadingRegistry.Unregister(this);
            registered = false;

            if (loadingMaterialInstance != null)
            {
                Destroy(loadingMaterialInstance);
                loadingMaterialInstance = null;
            }
        }

        void Update()
        {
            DriveProgressIndicator();
            DriveLoadingOverlay();
        }

        public void Bind(RawImage previewDisplay)
        {
            RegisterReceiver();

            if (previewDisplay != null)
                PreviewDisplay = previewDisplay;

            ResolveReferences();
            EnsureLoadingMaterialInstance();
            ApplyVisualState();
        }

        public void SetProgress(float value)
        {
            progress = Mathf.Clamp01(value);
            ApplyVisualState();
        }

        public void SetMessage(string message)
        {
            if (StatusLabel != null)
                StatusLabel.text = string.IsNullOrEmpty(message) ? DefaultMessage : message;
        }

        public void ResetMessage() => SetMessage(DefaultMessage);

        public int BeginLoading()
        {
            RegisterReceiver();

            if (nextLoadingToken == int.MaxValue)
                nextLoadingToken = 1;

            int token = nextLoadingToken++;
            loadingTokens.Add(token);
            ApplyVisualState();
            return token;
        }

        public void EndLoading(int token)
        {
            if (token <= 0)
                return;

            if (loadingTokens.Remove(token))
                ApplyVisualState();
        }

        public void EndLoading()
        {
            if (loadingTokens.Count > 0)
            {
                int tokenToRemove = 0;
                foreach (int token in loadingTokens)
                {
                    tokenToRemove = token;
                    break;
                }

                if (tokenToRemove > 0)
                    loadingTokens.Remove(tokenToRemove);
            }

            ApplyVisualState();
        }

        public void ClearLoading()
        {
            if (loadingTokens.Count == 0)
                return;

            loadingTokens.Clear();
            ApplyVisualState();
        }

        void RegisterReceiver()
        {
            if (registered)
                return;

            LoadingRegistry.Register(this);
            registered = true;
        }

        void ApplyVisualState()
        {
            ResolveReferences();

            GameObject root = ActiveRoot;
            if (root != null && root.activeSelf != ShouldShowRoot)
                root.SetActive(ShouldShowRoot);

            bool showOverlay = IsLoading && showLoadingOverlay;
            SetGraphicObjectActive(LoadingOverlay, showOverlay);
            if (LoadingOverlay != null)
            {
                LoadingOverlay.enabled = showOverlay;
                LoadingOverlay.raycastTarget = showOverlay;
            }

            bool showIndeterminate = IsLoading && !IsProgressing && indeterminateVisual != null;
            if (indeterminateVisual != null && indeterminateVisual.activeSelf != showIndeterminate)
                indeterminateVisual.SetActive(showIndeterminate);

            bool showProgress = IsProgressing || (IsLoading && indeterminateVisual == null);
            SetGraphicObjectActive(ProgressFillImage, showProgress);
            if (ProgressFillImage != null)
            {
                ProgressFillImage.fillAmount = IsProgressing ? progress : IndeterminateFill;
                ProgressFillImage.enabled = showProgress;
                ProgressFillImage.raycastTarget = false;
                if (!IsLoading || IsProgressing)
                    ProgressFillImage.rectTransform.localRotation = Quaternion.identity;
            }

            // Block clicks to the UI underneath while the loading overlay is up.
            if (inputBlocker != null && inputBlocker.activeSelf != IsLoading)
                inputBlocker.SetActive(IsLoading);
        }

        void DriveProgressIndicator()
        {
            if (
                ProgressFillImage == null
                || indeterminateVisual != null
                || !IsLoading
                || IsProgressing
            )
                return;

            ProgressFillImage.fillAmount = IndeterminateFill;
            ProgressFillImage.rectTransform.localRotation = Quaternion.Euler(
                0f,
                0f,
                -Time.unscaledTime * IndeterminateDegreesPerSecond
            );
        }

        void SetGraphicObjectActive(Graphic graphic, bool active)
        {
            if (graphic == null)
                return;

            GameObject graphicObject = graphic.gameObject;
            GameObject root = ActiveRoot;
            if (graphicObject == root)
                return;

            if (graphicObject.activeSelf != active)
                graphicObject.SetActive(active);
        }

        void DriveLoadingOverlay()
        {
            ResolveReferences();

            if (!IsLoading || !showLoadingOverlay || LoadingOverlay == null)
                return;

            Material mat =
                loadingMaterialInstance != null ? loadingMaterialInstance : LoadingOverlay.material;
            if (mat == null)
                return;

            Texture content =
                PreviewDisplay != null && PreviewDisplay.texture != null
                    ? PreviewDisplay.texture
                    : Texture2D.whiteTexture;
            LoadingOverlay.texture = content;
            mat.SetTexture(LoadingMainTexId, content);
            mat.SetTexture(LoadingIntroTexId, content);
            mat.SetFloat(LoadingSliderId, 0.5f + 0.08f * Mathf.Sin(Time.unscaledTime * 3f));
        }

        void EnsureLoadingMaterialInstance()
        {
            ResolveReferences();

            if (LoadingOverlay == null || LoadingOverlay.material == null)
                return;

            if (
                loadingMaterialInstance == null
                || LoadingOverlay.material != loadingMaterialInstance
            )
            {
                if (loadingMaterialInstance != null)
                    Destroy(loadingMaterialInstance);

                loadingMaterialInstance = new Material(LoadingOverlay.material);
                LoadingOverlay.material = loadingMaterialInstance;
            }
        }

        void ResolveReferences()
        {
            if (LoadingOverlay == null)
                LoadingOverlay = GetComponent<RawImage>();
            if (ProgressFillImage == null)
                ProgressFillImage = GetComponentInChildren<Image>(true);
        }
    }
}

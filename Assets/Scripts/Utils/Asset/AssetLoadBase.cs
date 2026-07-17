using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace Hypocycloid.Utils
{
    public abstract class AssetLoadBase<T> : MonoBehaviour
        where T : Object
    {
        [SerializeField]
        protected AssetReference asset;

        // !WARN: Instantiating a non-readable texture is not allowed, mark it readable in the inspector if so.
        [SerializeField]
        bool useInstance;

        [SerializeField]
        [Tooltip(
            "Invokes the loading begin/end events while this Addressables request is pending."
        )]
        bool triggerLoadingEffect = true;

        [SerializeField]
        [Tooltip(
            "Load the asset automatically on Awake. Turn off for on-demand (lazy) loading "
                + "driven by a controller via EnsureLoaded/Unload, so only the asset in use "
                + "stays resident."
        )]
        bool loadOnAwake = true;

        protected virtual bool UseInstance => useInstance;
        protected virtual bool TriggerLoadingEffect => triggerLoadingEffect;

        protected AsyncOperationHandle<T> loadHandle;
        bool subscribed;
        T instance;
        bool loadingEffectActive;

        // Pull-style accessors for consumers that read the eager-loaded asset directly
        // (e.g. BirefNetController) instead of subscribing to OnAssetLoad and racing the
        // Awake order. LoadedAsset is the source asset, not the optional instance.
        public T LoadedAsset { get; private set; }
        public bool LoadFailed { get; private set; }
        public bool IsLoading => loadHandle.IsValid() && !loadHandle.IsDone;

        // True when an Addressable asset reference is actually assigned (e.g. a bundled model
        // is wired in this build). Lets callers pick the bundle as a source vs. a downloaded
        // file fallback without attempting a load first.
        public bool HasAsset => asset != null && asset.RuntimeKeyIsValid();

        [SerializeField]
        protected UnityEvent<T> onAssetLoad;
        public UnityEvent<T> OnAssetLoad
        {
            get
            {
                onAssetLoad ??= new UnityEvent<T>();
                return onAssetLoad;
            }
        }

        [SerializeField]
        UnityEvent onLoadingBegin;
        public UnityEvent OnLoadingBegin
        {
            get
            {
                onLoadingBegin ??= new UnityEvent();
                return onLoadingBegin;
            }
        }

        [SerializeField]
        UnityEvent onLoadingEnd;
        public UnityEvent OnLoadingEnd
        {
            get
            {
                onLoadingEnd ??= new UnityEvent();
                return onLoadingEnd;
            }
        }

        protected virtual void Awake()
        {
            if (loadOnAwake)
                TryLoadAsset();
        }

        // Lazy-load entry point. Starts the Addressables request if it is not already
        // loading or loaded; safe to call repeatedly. Consumers poll LoadedAsset/LoadFailed.
        public void EnsureLoaded()
        {
            if (LoadedAsset != null || loadHandle.IsValid())
                return;

            LoadFailed = false;
            TryLoadAsset();
        }

        // Releases the Addressables handle and clears LoadedAsset so the asset can unload.
        // The caller must dispose anything built from the asset (e.g. a Sentis Worker whose
        // Model holds segments into the ModelAsset) BEFORE calling this.
        public void Unload()
        {
            CleanupHandle();
            loadHandle = default;
            LoadFailed = false;
        }

        protected virtual void OnDestroy()
        {
            CleanupHandle();
            OnAssetLoad.RemoveAllListeners();
        }

        protected virtual void TryLoadAsset()
        {
            if (loadHandle.IsValid())
                return;

            BeginLoadingEffect();
            try
            {
                loadHandle = asset.LoadAssetAsync<T>();
                loadHandle.Completed += OnLoadCompleted;
                subscribed = true;
            }
            catch
            {
                EndLoadingEffect();
                throw;
            }
        }

        void OnLoadCompleted(AsyncOperationHandle<T> handle)
        {
            subscribed = false;

            try
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    LogHelper.Log($"Successfully loaded asset <{typeof(T)}:{asset.RuntimeKey}>.");
                    LoadedAsset = handle.Result;
                    var result = UseInstance
                        ? (instance = Instantiate(handle.Result))
                        : handle.Result;
                    OnAssetLoad.Invoke(result);
                    OnLoadedAsset(result);
                }
                else
                {
                    LoadFailed = true;
                    LogHelper.LogError($"Failed to load asset <{typeof(T)}:{asset.RuntimeKey}>.");
                    OnAssetLoadFailed();
                }
            }
            finally
            {
                EndLoadingEffect();
            }
        }

        protected virtual void OnLoadedAsset(T loadedAsset) { }

        protected virtual void OnAssetLoadFailed() { }

        void CleanupHandle()
        {
            EndLoadingEffect();
            LoadedAsset = null;
            if (instance != null)
            {
                Destroy(instance);
                instance = null;
            }
            if (loadHandle.IsValid())
            {
                if (subscribed)
                {
                    loadHandle.Completed -= OnLoadCompleted;
                    subscribed = false;
                }
                Addressables.Release(loadHandle);
            }
        }

        protected virtual void BeginLoadingEffect()
        {
            if (!TriggerLoadingEffect || loadingEffectActive)
                return;

            loadingEffectActive = true;
            OnLoadingBegin.Invoke();
        }

        protected virtual void EndLoadingEffect()
        {
            if (!loadingEffectActive)
                return;

            loadingEffectActive = false;
            OnLoadingEnd.Invoke();
        }
    }
}

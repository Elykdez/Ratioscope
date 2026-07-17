using UnityEngine;

namespace Hypocycloid.Ratioscope
{
    /// <summary>
    /// Connects the scene AICortex to the HUD loading overlay without coupling the
    /// model runtime to HUD implementation details.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CortexHudLoadingBinder : MonoBehaviour
    {
        [SerializeField]
        CortexCore source;

        [SerializeField]
        UILoading loadingView;

        int loadingToken;

        void OnEnable()
        {
            if (source == null || loadingView == null)
                return;

            source.ModelLoadingStarted += BeginLoading;
            source.ModelLoadingStatusChanged += UpdateLoadingStatus;
            source.ModelLoadingFinished += EndLoading;
            if (source.IsModelLoading)
                BeginLoading();
        }

        void OnDisable()
        {
            if (source != null)
            {
                source.ModelLoadingStarted -= BeginLoading;
                source.ModelLoadingStatusChanged -= UpdateLoadingStatus;
                source.ModelLoadingFinished -= EndLoading;
            }
            EndLoading();
        }

        void BeginLoading()
        {
            if (loadingView == null)
                return;

            if (loadingToken == 0)
                loadingToken = loadingView.BeginLoading();
            UpdateLoadingStatus(source != null ? source.ModelLoadingStatus : "Loading...");
        }

        void UpdateLoadingStatus(string message) => loadingView?.SetMessage(message);

        void EndLoading()
        {
            if (loadingView != null && loadingToken != 0)
                loadingView.EndLoading(loadingToken);
            loadingView?.ResetMessage();
            loadingToken = 0;
        }
    }
}

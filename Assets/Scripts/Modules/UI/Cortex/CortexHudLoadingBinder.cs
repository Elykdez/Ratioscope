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

        LlmModelDownloader downloader;
        int modelLoadingToken;
        int downloadLoadingToken;

        void OnEnable()
        {
            if (source == null || loadingView == null)
                return;

            source.ModelLoadingStarted += BeginModelLoading;
            source.ModelLoadingStatusChanged += UpdateModelLoadingStatus;
            source.ModelLoadingFinished += EndModelLoading;

            downloader = source.ModelDownloader;
            if (downloader != null)
            {
                downloader.DownloadStarted += BeginDownload;
                downloader.DownloadStatusChanged += UpdateDownloadStatus;
                downloader.DownloadFinished += EndDownload;
            }

            if (source.IsModelLoading)
                BeginModelLoading();
            if (downloader != null && downloader.IsDownloading)
                BeginDownload();
        }

        void OnDisable()
        {
            if (source != null)
            {
                source.ModelLoadingStarted -= BeginModelLoading;
                source.ModelLoadingStatusChanged -= UpdateModelLoadingStatus;
                source.ModelLoadingFinished -= EndModelLoading;
            }

            if (downloader != null)
            {
                downloader.DownloadStarted -= BeginDownload;
                downloader.DownloadStatusChanged -= UpdateDownloadStatus;
                downloader.DownloadFinished -= EndDownload;
            }

            EndDownload();
            EndModelLoading();
            downloader = null;
        }

        void BeginModelLoading()
        {
            if (loadingView == null)
                return;

            if (modelLoadingToken == 0)
                modelLoadingToken = loadingView.BeginLoading();
            loadingView.SetProgress(0f);
            UpdateModelLoadingStatus(source != null ? source.ModelLoadingStatus : "Loading...");
        }

        void UpdateModelLoadingStatus(string message)
        {
            if (downloadLoadingToken == 0)
                loadingView?.SetMessage(message);
        }

        void EndModelLoading()
        {
            if (loadingView != null && modelLoadingToken != 0)
                loadingView.EndLoading(modelLoadingToken);
            modelLoadingToken = 0;

            if (downloadLoadingToken == 0)
            {
                loadingView?.SetProgress(0f);
                loadingView?.ResetMessage();
            }
        }

        void BeginDownload()
        {
            if (loadingView == null)
                return;

            if (downloadLoadingToken == 0)
                downloadLoadingToken = loadingView.BeginLoading();
            if (downloader != null)
                UpdateDownloadStatus(downloader.StatusText, downloader.Progress);
        }

        void UpdateDownloadStatus(string message, float progress)
        {
            if (loadingView == null)
                return;

            loadingView.SetProgress(progress);
            loadingView.SetMessage(message);
        }

        void EndDownload()
        {
            if (loadingView != null && downloadLoadingToken != 0)
                loadingView.EndLoading(downloadLoadingToken);
            downloadLoadingToken = 0;
            loadingView?.SetProgress(0f);

            if (modelLoadingToken != 0 && source != null)
                loadingView?.SetMessage(source.ModelLoadingStatus);
            else
                loadingView?.ResetMessage();
        }
    }
}

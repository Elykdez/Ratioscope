using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace Hypocycloid.Utils
{
    /// <summary>
    /// Loads a CSV file from StreamingAssets at runtime and invokes a callback with the parsed rows.
    /// Works on WebGL (HTTP fetch) and standalone (local file via UnityWebRequest) without extra configuration.
    /// Set <see cref="relativePath"/> to the path relative to StreamingAssets, e.g. "res/Configs/texer_comfy_settings.csv".
    /// </summary>
    public sealed class StreamingAssetLoadCSV : MonoBehaviour
    {
        [SerializeField]
        string relativePath;

        [SerializeField]
        UnityEvent<List<string[]>> onContentLoad;
        public UnityEvent<List<string[]>> OnContentLoad
        {
            get
            {
                onContentLoad ??= new();
                return onContentLoad;
            }
        }

        void Start() => StartCoroutine(LoadCoroutine());

        /// <summary>Re-fetch and re-parse the CSV from StreamingAssets.</summary>
        public void Reload() => StartCoroutine(LoadCoroutine());

        IEnumerator LoadCoroutine()
        {
            string url = Application.streamingAssetsPath + "/" + relativePath;
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LogHelper.LogError($"[StreamingAssets] Failed to load CSV at '{url}': {req.error}");
                yield break;
            }

            List<string[]> data = StringHelper.CSV2List(req.downloadHandler.text);
            if (data.Count > 0)
                OnContentLoad.Invoke(data);
        }
    }
}

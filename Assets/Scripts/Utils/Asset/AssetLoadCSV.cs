using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Hypocycloid.Utils
{
    public sealed class AssetLoadCSV : AssetLoadBase<TextAsset>
    {
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

        protected override void OnLoadedAsset(TextAsset loadedAsset)
        {
            if (loadedAsset == null)
                return;

            List<string[]> data = StringHelper.CSV2List(loadedAsset.text);
            if (data.Count > 0)
                OnContentLoad.Invoke(data);
        }
    }
}

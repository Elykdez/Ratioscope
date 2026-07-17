#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SFB
{
    public class StandaloneFileBrowserAndroidListener : AndroidJavaProxy
    {
        private Action<IList<ItemWithStream>> _multipleFilesCallback;

        public StandaloneFileBrowserAndroidListener(Action<IList<ItemWithStream>> onMultipleFilesSelected) : base("com.sfb.standalonefilebrowser.StandaloneFileBrowserAndroidListener")
        {
            _multipleFilesCallback = onMultipleFilesSelected;
        }

        public void onFilesSelected(string filenames)
        {
            if (_multipleFilesCallback != null) {
                var files = string.IsNullOrEmpty(filenames)
                    ? Array.Empty<string>()
                    : filenames.Split('|');
                var itemsWithStream = new ItemWithStream[files.Length];
                for (var i = 0; i < itemsWithStream.Length; i++) {
                    itemsWithStream[i] = new ItemWithStream() {
                        Name = files[i]
                    };
                }
                _multipleFilesCallback(itemsWithStream);
            }
        }
    }

    public class StandaloneFileBrowserAndroidSaveListener : AndroidJavaProxy
    {
        private readonly Action<ItemWithStream> _callback;
        private readonly string _defaultName;

        public StandaloneFileBrowserAndroidSaveListener(Action<ItemWithStream> callback, string defaultName)
            : base("com.sfb.standalonefilebrowser.StandaloneFileBrowserAndroidSaveListener")
        {
            _callback = callback;
            _defaultName = defaultName;
        }

        public void onFileSaved(bool saved)
        {
            _callback?.Invoke(saved ? new ItemWithStream { Name = _defaultName } : null);
        }
    }

    public class StandaloneFileBrowserAndroid : IStandaloneFileBrowser<ItemWithStream>
    {
        private AndroidJavaClass _standaloneFileBrowser;
        private AndroidJavaObject _activity;

        public byte[] Data { private get; set; }

        public StandaloneFileBrowserAndroid()
        {
            using (AndroidJavaObject unityClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                _activity = unityClass.GetStatic<AndroidJavaObject>("currentActivity");
                _standaloneFileBrowser = new AndroidJavaClass("com.sfb.standalonefilebrowser.StandaloneFileBrowser");
            }
        }

        public IList<ItemWithStream> OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect)
        {
            throw new NotSupportedException();
        }

        public void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            var listener = new StandaloneFileBrowserAndroidListener(cb);
            var args = new object[] {_activity, title, multiselect, listener};
            _standaloneFileBrowser.CallStatic("showOpenFileDialog", args);
        }

        public IList<ItemWithStream> OpenFolderPanel(string title, string directory, bool multiselect)
        {
            throw new NotSupportedException();
        }

        public void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            throw new NotSupportedException();
        }

        public ItemWithStream SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions)
        {
            throw new NotSupportedException();
        }

        public void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<ItemWithStream> cb)
        {
            var listener = new StandaloneFileBrowserAndroidSaveListener(cb, defaultName);
            var args = new object[] {_activity, title, defaultName, Data ?? Array.Empty<byte>(), listener};
            Data = null;
            _standaloneFileBrowser.CallStatic("showSaveFileDialog", args);
        }
    }
}
#endif

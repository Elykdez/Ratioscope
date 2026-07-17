#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace SFB
{
    public class StandaloneFileBrowserLinux : IStandaloneFileBrowser<ItemWithStream>
    {
        private static Action<IList<ItemWithStream>> _openFileCb;
        private static Action<IList<ItemWithStream>> _openFolderCb;
        private static Action<ItemWithStream> _saveFileCb;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void AsyncCallback(string path);

        [AOT.MonoPInvokeCallback(typeof(AsyncCallback))]
        private static void openFileCb(string paths)
        {
            var filenames = ParseResults(paths);
            var results = StandaloneFileBrowser.BuildItemsFromFilenames(filenames);
            _openFileCb.Invoke(results);
        }

        [AOT.MonoPInvokeCallback(typeof(AsyncCallback))]
        private static void openFolderCb(string paths)
        {
            IList<ItemWithStream> results = null;
            var filenames = ParseResults(paths);
            if (filenames?.Count > 0)
            {
                var filename = filenames[0];
                results = StandaloneFileBrowser.BuildItemsFromFolderContents(filename);
            }
            _openFolderCb.Invoke(results);
        }

        [AOT.MonoPInvokeCallback(typeof(AsyncCallback))]
        private static void saveFileCb(string path)
        {
            ItemWithStream result = null;
            if (path != null)
            {
                result = new ItemWithStream
                {
                    Name = path
                };
            }
            _saveFileCb(result);
        }

        [DllImport("StandaloneFileBrowser")]
        private static extern void DialogInit();
        [DllImport("StandaloneFileBrowser")]
        private static extern IntPtr DialogOpenFilePanel(string title, string directory, string extension, bool multiselect);
        [DllImport("StandaloneFileBrowser")]
        private static extern void DialogOpenFilePanelAsync(string title, string directory, string extension, bool multiselect, AsyncCallback callback);
        [DllImport("StandaloneFileBrowser")]
        private static extern IntPtr DialogOpenFolderPanel(string title, string directory, bool multiselect);
        [DllImport("StandaloneFileBrowser")]
        private static extern void DialogOpenFolderPanelAsync(string title, string directory, bool multiselect, AsyncCallback callback);
        [DllImport("StandaloneFileBrowser")]
        private static extern IntPtr DialogSaveFilePanel(string title, string directory, string defaultName, string extension);
        [DllImport("StandaloneFileBrowser")]
        private static extern void DialogSaveFilePanelAsync(string title, string directory, string defaultName, string extension, AsyncCallback callback);

        public StandaloneFileBrowserLinux()
        {
            DialogInit();
        }

        public IList<ItemWithStream> OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect)
        {
            var paths = Marshal.PtrToStringAnsi(DialogOpenFilePanel(
                title,
                directory,
                GetFilterFromFileExtensionList(extensions),
                multiselect));
            var filenames = ParseResults(paths);
            var results = StandaloneFileBrowser.BuildItemsFromFilenames(filenames);
            return results;
        }

        public void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            _openFileCb = cb;
            DialogOpenFilePanelAsync(
                title,
                directory,
                GetFilterFromFileExtensionList(extensions),
                multiselect,
                openFileCb);
        }

        public IList<ItemWithStream> OpenFolderPanel(string title, string directory, bool multiselect)
        {
            var paths = Marshal.PtrToStringAnsi(DialogOpenFolderPanel(
                title,
                directory,
                multiselect));
            var filenames = ParseResults(paths);
            if (filenames?.Count > 0)
            {
                var filename = filenames[0];
                return StandaloneFileBrowser.BuildItemsFromFolderContents(filename);
            }
            return null;
        }

        public void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            _openFolderCb = cb;
            DialogOpenFolderPanelAsync(
                title,
                directory,
                multiselect,
                openFolderCb);
        }

        public ItemWithStream SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions)
        {
            var filename = Marshal.PtrToStringAnsi(DialogSaveFilePanel(
                title,
                directory,
                defaultName,
                GetFilterFromFileExtensionList(extensions)));
            if (filename != null)
            {
                return new ItemWithStream
                {
                    Name = filename
                };
            }
            return null;
        }

        public void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<ItemWithStream> cb)
        {
            _saveFileCb = cb;
            DialogSaveFilePanelAsync(
                title,
                directory,
                defaultName,
                GetFilterFromFileExtensionList(extensions),
                saveFileCb);
        }

        private static string GetFilterFromFileExtensionList(ExtensionFilter[] extensions)
        {
            if (extensions == null)
            {
                return "";
            }
            var filterString = "";
            foreach (var filter in extensions)
            {
                filterString += filter.Name + ";";
                foreach (var ext in filter.Extensions)
                {
                    filterString += ext + ",";
                }
                filterString = filterString.Remove(filterString.Length - 1);
                filterString += "|";
            }
            filterString = filterString.Remove(filterString.Length - 1);
            return filterString;
        }

        private static IList<string> ParseResults(string paths)
        {
            if (paths != null)
            {
                var filenames = paths.Split((char)28);
                return filenames;
            }
            return null;
        }
    }
}
#endif
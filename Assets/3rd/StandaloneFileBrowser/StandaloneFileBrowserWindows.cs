#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SFB
{
    public class StandaloneFileBrowserWindows : IStandaloneFileBrowser<ItemWithStream>
    {
        private const int BufferSize = 2048;

        [DllImport("StandaloneFileBrowser", CharSet = CharSet.Unicode)]
        private static extern bool DialogOpenFilePanel(IntPtr buffer, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] string title, [MarshalAs(UnmanagedType.LPWStr)] string directory, [MarshalAs(UnmanagedType.LPWStr)] string extension, bool multiselect);
        [DllImport("StandaloneFileBrowser", CharSet = CharSet.Unicode)]
        private static extern bool DialogOpenFilePanelAsync(IntPtr buffer, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] string title, [MarshalAs(UnmanagedType.LPWStr)] string directory, [MarshalAs(UnmanagedType.LPWStr)] string extension, bool multiselect, AsyncCallback callback);
        [DllImport("StandaloneFileBrowser", CharSet = CharSet.Unicode)]
        private static extern bool DialogOpenFolderPanel(IntPtr buffer, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] string title, [MarshalAs(UnmanagedType.LPWStr)] string directory, bool multiselect);
        [DllImport("StandaloneFileBrowser", CharSet = CharSet.Unicode)]
        private static extern void DialogOpenFolderPanelAsync(IntPtr buffer, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] string title, [MarshalAs(UnmanagedType.LPWStr)] string directory, bool multiselect, AsyncCallback callback);
        [DllImport("StandaloneFileBrowser", CharSet = CharSet.Unicode)]
        private static extern bool DialogSaveFilePanel(IntPtr buffer, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] string title, [MarshalAs(UnmanagedType.LPWStr)] string directory, [MarshalAs(UnmanagedType.LPWStr)] string defaultName, [MarshalAs(UnmanagedType.LPWStr)] string extension);
        [DllImport("StandaloneFileBrowser", CharSet = CharSet.Unicode)]
        private static extern void DialogSaveFilePanelAsync(IntPtr buffer, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] string title, [MarshalAs(UnmanagedType.LPWStr)] string directory, [MarshalAs(UnmanagedType.LPWStr)] string defaultName, [MarshalAs(UnmanagedType.LPWStr)] string extension, AsyncCallback callback);

        public IList<ItemWithStream> OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect)
        {
            IList<ItemWithStream> results = null;
            var buffer = new char[BufferSize];
            var bufferLock = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            if (DialogOpenFilePanel(bufferLock.AddrOfPinnedObject(), BufferSize, title, directory, GetFilterFromFileExtensionList(extensions), multiselect))
            {
                var filenames = new List<string>();
                ParseResults(buffer, filenames, multiselect);
                results = StandaloneFileBrowser.BuildItemsFromFilenames(filenames);
            }
            bufferLock.Free();
            return results;
        }

        public void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            //todo: async
            cb(OpenFilePanel(title, directory, extensions, multiselect));
        }

        public IList<ItemWithStream> OpenFolderPanel(string title, string directory, bool multiselect)
        {
            IList<ItemWithStream> results = null;
            var buffer = new char[BufferSize];
            var bufferLock = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            if (DialogOpenFolderPanel(bufferLock.AddrOfPinnedObject(), BufferSize, title, directory, multiselect))
            {
                var filenames = new List<string>();
                ParseResults(buffer, filenames, multiselect);
                if (filenames?.Count > 0)
                {
                    var filename = filenames[0];
                    results = StandaloneFileBrowser.BuildItemsFromFolderContents(filename);
                }
            }
            bufferLock.Free();
            return results;
        }

        public void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            //todo: async
            cb(OpenFolderPanel(title, directory, multiselect));
        }

        public ItemWithStream SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions)
        {
            ItemWithStream result = null;
            var buffer = new char[BufferSize];
            var bufferLock = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            if (DialogSaveFilePanel(bufferLock.AddrOfPinnedObject(), BufferSize, title, directory, defaultName, GetFilterFromFileExtensionList(extensions)))
            {
                var filenames = new List<string>();
                ParseResults(buffer, filenames, false);
                result = StandaloneFileBrowser.BuildItemFromSingleFilename(filenames);
            }
            bufferLock.Free();
            return result;
        }

        public void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<ItemWithStream> cb)
        {
            //todo: async
            cb(SaveFilePanel(title, directory, defaultName, extensions));
        }

        private static string GetFilterFromFileExtensionList(ExtensionFilter[] extensions)
        {
            var filterString = "";
            if (extensions != null)
            {
                foreach (var extension in extensions)
                {
                    if (!string.IsNullOrWhiteSpace(extension.Name))
                    {
                        filterString += extension.Name;
                    }
                    else
                    {
                        var descriptionString = "";
                        foreach (var format in extension.Extensions)
                        {
                            if (descriptionString != "")
                            {
                                descriptionString += ",";
                            }
                            descriptionString += "(*" + (format[0] == '.' ? format.Substring(1) : format) + ")";
                        }
                        filterString += descriptionString;
                    }
                    filterString += "\0";
                    foreach (var format in extension.Extensions)
                    {
                        filterString += "*" + (format[0] == '.' ? format.Substring(1) : format) + ";";
                    }
                    filterString += "\0";
                }
            }
            filterString += "\0";
            return filterString;
        }

        private static void ParseResults(char[] buffer, List<String> filenames, bool multiselect)
        {
            var currentStringBytes = new List<char>();
            foreach (var c in buffer)
            {
                if (c == 0)
                {
                    var currentString = new string(currentStringBytes.ToArray());
                    if (!string.IsNullOrWhiteSpace(currentString) && currentString != "\0")
                    {
                        var filename = multiselect && filenames.Count > 0 ? $"{filenames[0]}\\{currentString}" : currentString;
                        filenames.Add(filename);
                    }
                    currentStringBytes.Clear();
                    continue;
                }
                currentStringBytes.Add(c);
            }
            if (multiselect && filenames.Count > 1)
            {
                filenames.RemoveAt(0);
            }
        }
    }
}
#endif

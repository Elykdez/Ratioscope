using System;
using System.Collections.Generic;
using System.IO;
namespace SFB
{
    /// <summary>
    /// Provides a platform-specific file browser interface for opening and saving files and folders
    /// using native dialogs. This static class delegates file browsing operations to an underlying platform‐
    /// specific implementation of <see cref="IStandaloneFileBrowser{ItemWithStream}"/>.
    /// </summary>
    /// <remarks>
    /// The <c>StandaloneFileBrowser</c> class wraps various native file dialog implementations based on the
    /// target platform (e.g., Unity Editor, Windows, Mac, Linux, Android, iOS, WebGL, UWP). At compile time,
    /// the appropriate platform-specific implementation is assigned to the internal <c>_platformWrapper</c> field.
    /// If no implementation is available for the target platform, the file browsing operations will not function.
    ///
    /// The class exposes both synchronous and asynchronous methods for opening files, opening folders, and saving files,
    /// with support for extension filters to limit file types. Asynchronous methods use callbacks to return user selections.
    /// </remarks>
    public class StandaloneFileBrowser
    {
#if UNITY_EDITOR
        private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserEditor();
#else
#if UNITY_WSA
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserWinRT();
#elif UNITY_ANDROID
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserAndroid();
#elif UNITY_WEBGL
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserWebGL();
#elif UNITY_STANDALONE_OSX
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserMac();
#elif UNITY_IOS
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserIOS();
#elif UNITY_STANDALONE_WIN
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserWindows();
#elif UNITY_STANDALONE_LINUX
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = new StandaloneFileBrowserLinux();
#else
    private static readonly IStandaloneFileBrowser<ItemWithStream> _platformWrapper = null;
#endif
#endif

        /// <summary>
        /// Opens a native file dialog for selecting files.
        /// </summary>
        /// <param name="title">The title of the dialog window.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="extension">A string representing the allowed file extension (e.g., "png").</param>
        /// <param name="multiselect">If <c>true</c>, allows multiple files to be selected; otherwise, only one file.</param>
        /// <returns>
        /// An <see cref="IList{ItemWithStream}"/> containing the selected items, or an empty list if the dialog is cancelled.
        /// </returns>
        public static IList<ItemWithStream> OpenFilePanel(string title, string directory, string extension, bool multiselect)
        {
            var extensions = string.IsNullOrEmpty(extension) ? null : new[] { new ExtensionFilter("", extension) };
            return OpenFilePanel(title, directory, extensions, multiselect);
        }

        /// <summary>
        /// Opens a native file dialog for selecting files.
        /// </summary>
        /// <param name="title">The title of the dialog window.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="extensions">
        /// An array of <see cref="ExtensionFilter"/> objects specifying allowed file types (e.g.,
        /// <c>new ExtensionFilter("Image Files", "jpg", "png")</c>).
        /// </param>
        /// <param name="multiselect">If <c>true</c>, allows multiple files to be selected; otherwise, only one file.</param>
        /// <returns>
        /// An <see cref="IList{ItemWithStream}"/> containing the selected items, or an empty list if the dialog is cancelled.
        /// </returns>
        public static IList<ItemWithStream> OpenFilePanel(string title, string directory, ExtensionFilter[] extensions, bool multiselect)
        {
            return _platformWrapper.OpenFilePanel(title, directory, extensions, multiselect);
        }

        /// <summary>
        /// Opens a native file dialog asynchronously for selecting files.
        /// </summary>
        /// <param name="title">The title of the dialog window.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="extension">A string representing the allowed file extension (e.g., "png").</param>
        /// <param name="multiselect">If <c>true</c>, allows multiple files to be selected.</param>
        /// <param name="cb">
        /// A callback action to be invoked with the list of selected items (or an empty list if cancelled).
        /// </param>
        public static void OpenFilePanelAsync(string title, string directory, string extension, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            var extensions = string.IsNullOrEmpty(extension) ? null : new[] { new ExtensionFilter("", extension) };
            OpenFilePanelAsync(title, directory, extensions, multiselect, cb);
        }

        /// <summary>
        /// Opens a native file dialog asynchronously for selecting files.
        /// </summary>
        /// <param name="title">The title of the dialog window.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="extensions">
        /// An array of <see cref="ExtensionFilter"/> objects specifying allowed file types.
        /// </param>
        /// <param name="multiselect">If <c>true</c>, allows multiple files to be selected.</param>
        /// <param name="cb">
        /// A callback action to be invoked with the list of selected items (or an empty list if cancelled).
        /// </param>
        public static void OpenFilePanelAsync(string title, string directory, ExtensionFilter[] extensions, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            _platformWrapper.OpenFilePanelAsync(title, directory, extensions, multiselect, cb);
        }

        /// <summary>
        /// Opens a native folder dialog for selecting a folder.
        /// </summary>
        /// <param name="title">The title of the folder dialog.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="multiselect">If <c>true</c>, allows multiple folder selections; otherwise, only one folder can be selected.</param>
        /// <returns>
        /// An <see cref="IList{ItemWithStream}"/> containing the selected folder(s), or an empty list if cancelled.
        /// </returns>
        public static IList<ItemWithStream> OpenFolderPanel(string title, string directory, bool multiselect)
        {
            return _platformWrapper.OpenFolderPanel(title, directory, multiselect);
        }

        /// <summary>
        /// Opens a native folder dialog asynchronously for selecting a folder.
        /// </summary>
        /// <param name="title">The title of the folder dialog.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="multiselect">If <c>true</c>, allows multiple folder selections; otherwise, only one folder can be selected.</param>
        /// <param name="cb">
        /// A callback action to be invoked with the list of selected folder items (or an empty list if cancelled).
        /// </param>
        public static void OpenFolderPanelAsync(string title, string directory, bool multiselect, Action<IList<ItemWithStream>> cb)
        {
            _platformWrapper.OpenFolderPanelAsync(title, directory, multiselect, cb);
        }

        /// <summary>
        /// Opens a native save file dialog.
        /// </summary>
        /// <param name="title">The title of the save dialog.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="defaultName">The default file name to pre-populate in the dialog.</param>
        /// <param name="extension">A string representing the allowed file extension (e.g., "txt").</param>
        /// <returns>
        /// An <see cref="ItemWithStream"/> representing the chosen file, or <c>null</c> if the dialog was cancelled.
        /// </returns>
        public static ItemWithStream SaveFilePanel(string title, string directory, string defaultName, string extension)
        {
            var extensions = string.IsNullOrEmpty(extension) ? null : new[] { new ExtensionFilter("", extension) };
            return SaveFilePanel(title, directory, defaultName, extensions);
        }

        /// <summary>
        /// Opens a native save file dialog.
        /// </summary>
        /// <param name="title">The title of the save dialog.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="defaultName">The default file name to pre-populate in the dialog.</param>
        /// <param name="extensions">
        /// An array of <see cref="ExtensionFilter"/> objects specifying allowed file types.
        /// </param>
        /// <returns>
        /// An <see cref="ItemWithStream"/> representing the chosen file, or <c>null</c> if cancelled.
        /// </returns>
        public static ItemWithStream SaveFilePanel(string title, string directory, string defaultName, ExtensionFilter[] extensions)
        {
            return _platformWrapper.SaveFilePanel(title, directory, defaultName, extensions);
        }

        /// <summary>
        /// Opens a native save file dialog asynchronously.
        /// </summary>
        /// <param name="title">The title of the save file dialog.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="defaultName">The default file name to pre-populate in the dialog.</param>
        /// <param name="extension">A string representing the allowed file extension.</param>
        /// <param name="cb">
        /// A callback action that is invoked with the selected file item, or <c>null</c> if cancelled.
        /// </param>
        /// <param name="data">
        /// Optional data to be saved (used on WebGL platform).
        /// </param>
        public static void SaveFilePanelAsync(string title, string directory, string defaultName, string extension, Action<ItemWithStream> cb, byte[] data = null)
        {
            var extensions = string.IsNullOrEmpty(extension) ? null : new[] { new ExtensionFilter("", extension) };
            SaveFilePanelAsync(title, directory, defaultName, extensions, cb, data);
        }

        /// <summary>
        /// Opens a native save file dialog asynchronously.
        /// </summary>
        /// <param name="title">The title of the save file dialog.</param>
        /// <param name="directory">The initial directory to display.</param>
        /// <param name="defaultName">The default file name to pre-populate in the dialog.</param>
        /// <param name="extensions">
        /// An array of <see cref="ExtensionFilter"/> objects specifying allowed file types.
        /// </param>
        /// <param name="cb">
        /// A callback action that is invoked with the selected file item, or <c>null</c> if cancelled.
        /// </param>
        /// <param name="data">
        /// Optional data to be saved (used on WebGL platform).
        /// </param>
        public static void SaveFilePanelAsync(string title, string directory, string defaultName, ExtensionFilter[] extensions, Action<ItemWithStream> cb, byte[] data = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_platformWrapper is StandaloneFileBrowserWebGL standaloneFileBrowserWebGL)
        {
            standaloneFileBrowserWebGL.Data = data;
        }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_platformWrapper is StandaloneFileBrowserAndroid standaloneFileBrowserAndroid)
            {
                standaloneFileBrowserAndroid.Data = data;
            }
#endif
            _platformWrapper.SaveFilePanelAsync(title, directory, defaultName, extensions, cb);
        }

        internal static ItemWithStream BuildItemFromSingleFilename(IList<string> filenames)
        {
            if (filenames?.Count > 0)
            {
                return new ItemWithStream()
                {
                    Name = filenames[0]
                };
            }
            return null;
        }

        internal static IList<ItemWithStream> BuildItemsFromFilenames(IList<string> filenames)
        {
            if (filenames?.Count > 0)
            {
                var results = new ItemWithStream[filenames.Count];
                for (var i = 0; i < filenames.Count; i++)
                {
                    results[i] = new ItemWithStream()
                    {
                        Name = filenames[i]
                    };
                }
                return results;
            }
            return null;
        }

        internal static IList<ItemWithStream> BuildItemsFromFolderContents(string filename)
        {
            if (Directory.Exists(filename))
            {
                var directoryFilenames = Directory.GetFiles(filename);
                return BuildItemsFromFilenames(directoryFilenames);
            }
            return null;
        }
    }
}

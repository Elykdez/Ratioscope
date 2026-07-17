using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hypocycloid.Utils;
using SFB;
using UnityEngine;

namespace Hypocycloid.Ratioscope
{
    [DefaultExecutionOrder(-100)]
    public sealed class LoadManager : MonoSingleton<LoadManager>
    {
        static readonly ExtensionFilter[] TextFilters = { new("Dialogue text", "txt", "json") };

        Action<string, string> openCompleted;
        Action<string> saveCompleted;
        Action<string> failed;
        string pendingFileName;
        string pendingText;

        public bool Busy { get; private set; }

        public void OpenTextFile(Action<string, string> completed, Action<string> onFailed = null)
        {
            if (!BeginOperation(onFailed))
                return;
            openCompleted = completed;

            try
            {
                StandaloneFileBrowser.OpenFilePanelAsync(
                    "Import dialogue",
                    "",
                    TextFilters,
                    false,
                    OnFileSelected
                );
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        public void SaveTextFile(
            string defaultFileName,
            string text,
            Action<string> completed,
            Action<string> onFailed = null
        )
        {
            if (!BeginOperation(onFailed))
                return;
            saveCompleted = completed;
            pendingFileName = defaultFileName;
            pendingText = text;

            try
            {
                StandaloneFileBrowser.SaveFilePanelAsync(
                    "Export dialogue",
                    "",
                    defaultFileName,
                    "txt",
                    OnSaveSelected,
                    new UTF8Encoding(false).GetBytes(text)
                );
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        void OnFileSelected(IList<ItemWithStream> items)
        {
            if (items == null || items.Count == 0 || items[0] == null || !items[0].HasData)
            {
                Finish();
                return;
            }

            ItemWithStream item = items[0];
            try
            {
                string text;
                using (Stream stream = item.OpenStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    text = reader.ReadToEnd();

                string fileName = Path.GetFileName(item.Name);
                Action<string, string> callback = openCompleted;
                Finish();
                callback?.Invoke(text, fileName);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
            finally
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!string.IsNullOrEmpty(item.Name) && File.Exists(item.Name))
                    File.Delete(item.Name);
#endif
            }
        }

        void OnSaveSelected(ItemWithStream item)
        {
            if (item == null || !item.HasData)
            {
                Finish();
                return;
            }

            try
            {
#if !UNITY_ANDROID || UNITY_EDITOR
                File.WriteAllText(item.Name, pendingText, new UTF8Encoding(false));
#endif
                string fileName = string.IsNullOrEmpty(item.Name)
                    ? pendingFileName
                    : Path.GetFileName(item.Name);
                Action<string> callback = saveCompleted;
                Finish();
                callback?.Invoke(fileName);
            }
            catch (Exception exception)
            {
                Fail(exception.Message);
            }
        }

        bool BeginOperation(Action<string> onFailed)
        {
            if (Busy)
            {
                onFailed?.Invoke("Another file operation is already open.");
                return false;
            }

            Busy = true;
            failed = onFailed;
            return true;
        }

        void Fail(string error)
        {
            Action<string> callback = failed;
            Finish();
            callback?.Invoke(error);
        }

        void Finish()
        {
            Busy = false;
            openCompleted = null;
            saveCompleted = null;
            failed = null;
            pendingFileName = null;
            pendingText = null;
        }
    }
}

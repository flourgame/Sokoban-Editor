using System;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine;

namespace Kuluobishi.Sokoban.Editor
{
    /// <summary>Uses the Editor file picker or the Windows system picker in the standalone player.</summary>
    public static class SokobanExchangeFileDialog
    {
        public static string Choose(bool save, string initialPath)
        {
#if UNITY_EDITOR
            var directory = System.IO.Path.GetDirectoryName(initialPath);
            return save ? UnityEditor.EditorUtility.SaveFilePanel("导出关卡 XLSX", directory, "Sokoban-Levels.xlsx", "xlsx")
                : UnityEditor.EditorUtility.OpenFilePanel("导入关卡 XLSX", directory, "xlsx");
#elif UNITY_STANDALONE_WIN
            string result = "";
            ExceptionDispatchInfo failure = null;
            // Shell dialogs require STA. Give them their own stack rather than running
            // COM/Shell code on Unity's main thread; don't call Unity APIs on this thread.
            var thread = new Thread(() =>
            {
                try { result = ChooseWindows(save, initialPath); }
                catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
            }, 4 * 1024 * 1024) { IsBackground = true, Name = "Sokoban XLSX file dialog" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            failure?.Throw();
            return result;
#else
            throw new NotSupportedException("此平台请在路径输入框中填写 XLSX 完整路径。");
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private static string ChooseWindows(bool save, string initialPath)
        {
            const int capacity = 32768;
            // Mono's StringBuilder P/Invoke marshaler can allocate the wide-char
            // buffer on the stack. A 64 KB buffer overflowed in the Windows player.
            // Use explicit native heap storage and release it for every outcome.
            var buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
            try
            {
                var name = save ? System.IO.Path.GetFileName(initialPath) : "";
                if (string.IsNullOrEmpty(name) && save) name = "Sokoban-Levels.xlsx";
                var initialName = (name + "\0").ToCharArray();
                if (initialName.Length > capacity) throw new ArgumentException("文件名过长。");
                Marshal.Copy(initialName, 0, buffer, initialName.Length);
                var options = new OpenFileName
                {
                    size = Marshal.SizeOf(typeof(OpenFileName)),
                    // The caller waits synchronously; a cross-thread owner can
                    // deadlock while Windows sends messages to Unity's waiting thread.
                    owner = IntPtr.Zero,
                    filter = "Excel 工作簿 (*.xlsx)\0*.xlsx\0\0", filterIndex = 1,
                    file = buffer, maxFile = capacity, initialDirectory = System.IO.Path.GetDirectoryName(initialPath),
                    title = save ? "导出关卡 XLSX" : "导入关卡 XLSX", defaultExtension = "xlsx",
                    flags = 0x80000 | 0x800000 | 0x8 | 0x800 | (save ? 0x2 : 0x1000)
                };
                var selected = save ? GetSaveFileName(ref options) : GetOpenFileName(ref options);
                if (selected) return Marshal.PtrToStringUni(buffer) ?? "";
                var error = CommDlgExtendedError();
                if (error != 0) throw new InvalidOperationException("文件选择窗口打开失败（" + error + "），可手动填写路径。");
                return "";
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int size;
            public IntPtr owner, instance;
            public string filter, customFilter;
            public int maxCustomFilter, filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public string initialDirectory, title;
            public int flags;
            public short fileOffset, fileExtension;
            public string defaultExtension;
            public IntPtr customData, hook;
            public string templateName;
            public IntPtr reserved;
            public int reservedCount, flagsEx;
        }
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetOpenFileName(ref OpenFileName options);
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetSaveFileNameW")]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSaveFileName(ref OpenFileName options);
        [DllImport("comdlg32.dll")] private static extern int CommDlgExtendedError();
#endif
    }
}

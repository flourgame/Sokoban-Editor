using System;
using System.Runtime.InteropServices;
using System.Text;
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
            var buffer = new StringBuilder(32768);
            if (save) buffer.Append("Sokoban-Levels.xlsx");
            var options = new OpenFileName
            {
                size = Marshal.SizeOf(typeof(OpenFileName)), owner = GetActiveWindow(),
                filter = "Excel 工作簿 (*.xlsx)\0*.xlsx\0\0", filterIndex = 1,
                file = buffer, maxFile = buffer.Capacity, initialDirectory = System.IO.Path.GetDirectoryName(initialPath),
                title = save ? "导出关卡 XLSX" : "导入关卡 XLSX", defaultExtension = "xlsx",
                flags = 0x80000 | 0x800000 | 0x8 | 0x800 | (save ? 0x2 : 0x1000)
            };
            var selected = save ? GetSaveFileName(ref options) : GetOpenFileName(ref options);
            if (selected) return buffer.ToString();
            var error = CommDlgExtendedError();
            if (error != 0) throw new InvalidOperationException("文件选择窗口打开失败（" + error + "），可手动填写路径。");
            return "";
#else
            throw new NotSupportedException("此平台请在路径输入框中填写 XLSX 完整路径。");
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int size;
            public IntPtr owner, instance;
            public string filter, customFilter;
            public int maxCustomFilter, filterIndex;
            public StringBuilder file;
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
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
#endif
    }
}

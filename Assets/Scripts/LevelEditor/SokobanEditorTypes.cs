using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;
using Kuluobishi.Sokoban;

namespace Kuluobishi.Sokoban.Editor
{
    /// <summary>编辑器单个页签（多文档）。</summary>
    public sealed class SokobanEditorTab
    {
        public SokobanLevelDescriptor Descriptor;
        public SokobanJsonLevel Data;
        public bool Dirty;
        public bool IsStamp, StampSaved;
        public readonly HashSet<SokobanGridPoint> StampMask = new HashSet<SokobanGridPoint>();
        public bool IsSaved => IsStamp ? StampSaved : Descriptor != null;
        public readonly Stack<string> UndoStack = new Stack<string>();

        [Serializable] private sealed class StampUndo { public SokobanJsonLevel data; public SokobanJsonPoint[] mask; }
        public string CaptureUndo() => IsStamp ? JsonUtility.ToJson(new StampUndo { data = Data,
            mask = StampMask.Select(p => new SokobanJsonPoint(p.x, p.y)).ToArray() }) : JsonUtility.ToJson(Data);
        public bool RestoreUndo(string json)
        {
            if (!IsStamp) { var data = SokobanLevelRepository.Parse(json); if (data == null) return false; Data = data; return true; }
            var snapshot = JsonUtility.FromJson<StampUndo>(json);
            if (snapshot?.data == null || snapshot.mask == null) return false;
            Data = snapshot.data; StampMask.Clear();
            foreach (var p in snapshot.mask) StampMask.Add(new SokobanGridPoint(p.x, p.y));
            return true;
        }
        public void Save()
        {
            if (IsStamp) { SokobanStampRepository.Save(SokobanStamp.FromDocument(this)); StampSaved = true; }
            else
            {
                var descriptor = Descriptor ?? new SokobanLevelDescriptor { LevelId = Data.levelId, Folder = Data.levelId, Title = DisplayName, Source = "Generated" };
                SokobanLevelRepository.SaveJson(Data, descriptor); descriptor.Title = Data.name; Descriptor = descriptor;
                SokobanBatchGenerationService.Existing?.MarkSaved(Data);
            }
            Dirty = false;
        }

        public void AddUndo(string json, int limit)
        {
            UndoStack.Push(json);
            if (UndoStack.Count <= limit) return;
            var latest = UndoStack.ToArray(); UndoStack.Clear();
            for (var i = limit - 1; i >= 0; i--) UndoStack.Push(latest[i]);
        }

        public string DisplayName => (IsStamp ? "印章 · " : "") + (Data == null ? "未命名" : (string.IsNullOrWhiteSpace(Data.name) ? Data.levelId : Data.name));
    }

    /// <summary>一个格子的完整内容（地形/目标/箱子/玩家），用于剪贴板与虚影。</summary>
    public struct SokobanEditorCell
    {
        public bool Wall;
        public bool Goal;
        public bool Box;
        public bool Player;
        public bool Transparent;
    }

    /// <summary>剪贴板条目：相对偏移 + 格子内容。支持不相邻多选（分开复制）。</summary>
    public struct SokobanClipboardCell
    {
        public int Dx;
        public int Dy;
        public SokobanEditorCell Cell;
        /// <summary>true=整格内容（复制/整格剪切），粘贴时完全覆盖；false=仅最上层（长按/单层剪切），粘贴时与目标格合并、不抹掉下层。</summary>
        public bool WholeCell;
    }
}

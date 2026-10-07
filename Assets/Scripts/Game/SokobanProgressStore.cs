using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    [Serializable] public sealed class SokobanProgressEntry
    {
        public string levelId, layoutKey;
        public int bestMoves = -1, bestPushes = -1;
        public bool completed;
    }
    [Serializable] public sealed class SokobanProgressData
    {
        public int schemaVersion = 1;
        public SokobanProgressEntry[] entries = Array.Empty<SokobanProgressEntry>();
        public string[] unlockedLevelIds = Array.Empty<string>();
    }

    /// <summary>成绩属于具体布局；改名不清成绩，改布局不沿用旧成绩。GM 和编辑试玩不记录。</summary>
    public sealed class SokobanProgressStore
    {
        private static SokobanProgressStore current;
        public static SokobanProgressStore Current => current ?? (current = new SokobanProgressStore(Path.Combine(Application.persistentDataPath, "progress.json")));
        private readonly string path;
        private SokobanProgressData data;
        public string LastError { get; private set; }
        public event Action Changed;
        public SokobanProgressStore(string filePath) { path = filePath; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { current = null; }

        public static string LayoutKey(SokobanJsonLevel source)
        {
            var level = new SokobanLevelRuntime(source);
            var text = new StringBuilder().Append(level.Width).Append(':').Append(level.Height).Append(':').Append(level.PlayerStart);
            for (var y = 0; y < level.Height; y++) for (var x = 0; x < level.Width; x++) text.Append(level.Walls[x, y] ? '#' : '.');
            foreach (var p in level.BoxesStart.OrderBy(p => p.y).ThenBy(p => p.x)) text.Append("b").Append(p);
            foreach (var p in level.Goals.OrderBy(p => p.y).ThenBy(p => p.x)) text.Append("g").Append(p);
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private void Load()
        {
            if (data != null) return;
            data = new SokobanProgressData();
            if (!File.Exists(path)) return;
            try
            {
                var loaded = new SokobanProgressData();
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), loaded);
                if (loaded == null || loaded.schemaVersion != 1 || loaded.entries == null || loaded.entries.Any(e =>
                    e == null || string.IsNullOrWhiteSpace(e.levelId) || string.IsNullOrWhiteSpace(e.layoutKey) || e.bestMoves < 0 || e.bestPushes < 0) ||
                    loaded.unlockedLevelIds == null || loaded.unlockedLevelIds.Any(string.IsNullOrWhiteSpace))
                    throw new InvalidDataException("进度内容不完整");
                data = loaded;
            }
            catch (Exception error)
            {
                LastError = "本地进度无法读取，已使用空进度；原文件保留。";
                try { File.Copy(path, path + ".corrupt-" + Guid.NewGuid().ToString("N")); } catch { }
                Debug.LogWarning(LastError + " " + error.Message);
            }
        }

        public SokobanProgressEntry Find(SokobanJsonLevel level)
        {
            Load(); var key = LayoutKey(level);
            return data.entries.FirstOrDefault(e => e.levelId == level.levelId && e.layoutKey == key);
        }

        public bool IsUnlocked(string levelId, IReadOnlyList<string> campaign)
        {
            Load();
            if (string.IsNullOrWhiteSpace(levelId)) return false;
            if (data.unlockedLevelIds.Contains(levelId)) return true;
            var index = campaign == null ? -1 : campaign.ToList().IndexOf(levelId);
            if (index < 0) return false;
            // Existing v1 scores also grant access to completed levels and their successors.
            return index == 0 || data.entries.Any(e => e.completed && (e.levelId == levelId || e.levelId == campaign[index - 1]));
        }

        private SokobanProgressData CopyData()
        {
            Load(); return JsonUtility.FromJson<SokobanProgressData>(JsonUtility.ToJson(data));
        }

        public bool UnlockAll(IEnumerable<string> levelIds)
        {
            var next = CopyData();
            next.unlockedLevelIds = next.unlockedLevelIds.Concat(levelIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
            return Save(next);
        }

        public bool Clear() => Save(new SokobanProgressData());

        public bool RecordWin(SokobanJsonLevel level, int moves, int pushes, bool isGm = false, bool isPreview = false,
            IReadOnlyList<string> campaign = null)
        {
            if (isGm || isPreview || moves < 0 || pushes < 0) return false;
            var next = CopyData(); var key = LayoutKey(level);
            var entry = next.entries.FirstOrDefault(e => e.levelId == level.levelId && e.layoutKey == key);
            if (entry == null)
            {
                entry = new SokobanProgressEntry { levelId = level.levelId, layoutKey = key };
                next.entries = next.entries.Where(e => e.levelId != level.levelId).Concat(new[] { entry }).ToArray();
            }
            entry.completed = true;
            if (entry.bestMoves < 0 || moves < entry.bestMoves || moves == entry.bestMoves && pushes < entry.bestPushes)
            { entry.bestMoves = moves; entry.bestPushes = pushes; }
            var ids = campaign?.ToList(); var index = ids?.IndexOf(level.levelId) ?? -1;
            if (index >= 0)
            {
                var unlocked = new[] { ids[0], level.levelId }.Concat(index + 1 < ids.Count ? new[] { ids[index + 1] } : Array.Empty<string>());
                next.unlockedLevelIds = next.unlockedLevelIds.Concat(unlocked).Distinct().ToArray();
            }
            return Save(next);
        }

        private bool Save(SokobanProgressData next)
        {
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllText(temporary, JsonUtility.ToJson(next, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
                data = next; LastError = null;
            }
            catch (Exception error) { LastError = "玩家数据暂未保存：" + error.Message; return false; }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Changed?.Invoke(); return true;
        }
    }

    public static class SokobanCampaign
    {
        /// <summary>
        /// 玩家战役链：关卡管理里勾选"对玩家公开"的类别下的全部关卡（按索引顺序）。
        /// 可见性只看关卡分类，不看关卡来自哪个资源目录；解锁、选关可见性与"下一关"接续都基于这条链。
        /// 类别名不对玩家显示。
        /// </summary>
        public static List<SokobanLevelDescriptor> Levels()
        {
            var published = new HashSet<string>(
                SokobanLevelRepository.Library.PublishedCategories(), StringComparer.OrdinalIgnoreCase);
            return SokobanLevelRepository.ListAll()
                .Where(d => published.Contains(d.Category ?? d.Source ?? ""))
                .ToList();
        }

        public static List<string> Ids() => Levels().Select(d => d.LevelId).ToList();
    }
}

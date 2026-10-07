using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Kuluobishi.Sokoban.Editor
{
    [Serializable]
    public sealed class SokobanStampCell
    {
        public int x, y;
        public bool wall, goal, box, player;
    }

    /// <summary>Only painted cells are stored; missing coordinates are transparent.</summary>
    [Serializable]
    public sealed class SokobanStamp
    {
        public int schemaVersion = 1;
        public string stampId, name;
        public int width = 8, height = 8;
        public SokobanStampCell[] cells = Array.Empty<SokobanStampCell>();

        public static string NewId() => "Stamp_" + Guid.NewGuid().ToString("N");
        public static List<string> Validate(SokobanStamp stamp)
        {
            var errors = new List<string>();
            if (stamp == null) { errors.Add("印章内容为空"); return errors; }
            if (stamp.schemaVersion != 1) errors.Add("不支持的印章版本");
            if (!Regex.IsMatch(stamp.stampId ?? "", @"\A[A-Za-z0-9_-]{1,96}\z")) errors.Add("印章 ID 无效");
            if (string.IsNullOrWhiteSpace(stamp.name) || stamp.name.Length > 80 || stamp.name.IndexOfAny(new[] { '\r', '\n' }) >= 0) errors.Add("印章名称应为 1–80 个字符，不能换行");
            if (stamp.width < 1 || stamp.height < 1 || stamp.width > 40 || stamp.height > 40) errors.Add("印章尺寸应为 1–40");
            if (stamp.cells == null || stamp.cells.Length == 0) errors.Add("请至少绘制一个印章格子");
            if (stamp.cells == null) return errors;
            var coordinates = new HashSet<SokobanGridPoint>(); var players = 0;
            foreach (var cell in stamp.cells)
            {
                if (cell == null) { errors.Add("印章包含空格子记录"); continue; }
                if (cell.x < 0 || cell.y < 0 || cell.x >= stamp.width || cell.y >= stamp.height) errors.Add("印章格子越界");
                if (!coordinates.Add(new SokobanGridPoint(cell.x, cell.y))) errors.Add("印章格子重复");
                if (cell.wall && (cell.goal || cell.box || cell.player) || cell.box && cell.player) errors.Add("印章格子层叠无效");
                if (cell.player) players++;
            }
            if (players > 1) errors.Add("印章最多包含一个玩家");
            return errors.Distinct().ToList();
        }

        public static SokobanStamp Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 2 * 1024 * 1024) throw new InvalidDataException("印章 JSON 为空或过大");
            var stamp = JsonUtility.FromJson<SokobanStamp>(json);
            var errors = Validate(stamp);
            if (errors.Count > 0) throw new InvalidDataException(string.Join("；", errors));
            return stamp;
        }

        // Use a painted cell as the cursor anchor, even when the canvas has transparent margins.
        // Row-first ordering also keeps the anchor on a real cell for irregular patterns.
        public SokobanGridPoint PlacementAnchor
        {
            get
            {
                SokobanStampCell first = null;
                if (cells != null) foreach (var cell in cells)
                    if (cell != null && (first == null || cell.y < first.y || cell.y == first.y && cell.x < first.x)) first = cell;
                return first == null ? new SokobanGridPoint(0, 0) : new SokobanGridPoint(first.x, first.y);
            }
        }

        public List<SokobanClipboardCell> AsBrush()
        {
            var anchor = PlacementAnchor;
            return cells.Select(c => new SokobanClipboardCell
            {
                Dx = c.x - anchor.x, Dy = c.y - anchor.y, WholeCell = true,
                Cell = new SokobanEditorCell { Wall = c.wall, Goal = c.goal, Box = c.box, Player = c.player }
            }).ToList();
        }

        public bool Fits(int width, int height, int x, int y)
        {
            if (cells == null || cells.Length == 0) return false;
            var anchor = PlacementAnchor;
            return cells.All(c => c != null && x + c.x - anchor.x >= 0 && y + c.y - anchor.y >= 0 &&
                x + c.x - anchor.x < width && y + c.y - anchor.y < height);
        }

        public static SokobanStamp FromDocument(SokobanEditorTab tab)
        {
            if (tab == null || !tab.IsStamp || tab.Data == null) throw new InvalidOperationException("当前文档不是印章");
            var data = tab.Data;
            return new SokobanStamp { stampId = data.levelId, name = data.name, width = data.size.width, height = data.size.height,
                cells = tab.StampMask.OrderBy(p => p.y).ThenBy(p => p.x).Select(p => new SokobanStampCell
                {
                    x = p.x, y = p.y, wall = data.terrain[p.y][p.x] == '#',
                    goal = data.goals.Any(g => g.x == p.x && g.y == p.y),
                    box = data.boxes.Any(b => b.x == p.x && b.y == p.y),
                    player = data.player != null && data.player.x == p.x && data.player.y == p.y
                }).ToArray() };
        }

        public SokobanEditorTab CreateDocument(bool saved = true)
        {
            var tab = new SokobanEditorTab { IsStamp = true, StampSaved = saved, Dirty = !saved,
                Data = new SokobanJsonLevel { levelId = stampId, name = name, size = new SokobanJsonSize { width = width, height = height },
                    terrain = Enumerable.Repeat(new string('.', width), height).ToArray(), player = new SokobanJsonPoint(-1, -1),
                    boxes = Array.Empty<SokobanJsonPoint>(), goals = Array.Empty<SokobanJsonPoint>() } };
            foreach (var c in cells)
            {
                tab.StampMask.Add(new SokobanGridPoint(c.x, c.y));
                if (c.wall) { var row = tab.Data.terrain[c.y].ToCharArray(); row[c.x] = '#'; tab.Data.terrain[c.y] = new string(row); }
                if (c.box) tab.Data.boxes = tab.Data.boxes.Concat(new[] { new SokobanJsonPoint(c.x, c.y) }).ToArray();
                if (c.goal) tab.Data.goals = tab.Data.goals.Concat(new[] { new SokobanJsonPoint(c.x, c.y) }).ToArray();
                if (c.player) tab.Data.player = new SokobanJsonPoint(c.x, c.y);
            }
            return tab;
        }
    }

    /// <summary>Filesystem store with read-only bundled fallback; local IDs override bundled IDs.</summary>
    public sealed class SokobanStampStore
    {
        public string Root { get; }
        private readonly Func<IEnumerable<string>> bundled;
        public SokobanStampStore(string root, Func<IEnumerable<string>> bundledJson = null)
        { Root = Path.GetFullPath(root); bundled = bundledJson; }

        public List<SokobanStamp> ReadAll(out List<string> problems)
        {
            problems = new List<string>(); var issues = problems;
            var found = new Dictionary<string, SokobanStamp>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> read = (json, source) =>
            {
                try { var stamp = SokobanStamp.Parse(json); found[stamp.stampId] = stamp; }
                catch (Exception error) { issues.Add(source + "：" + error.Message); }
            };
            if (bundled != null) foreach (var json in bundled()) read(json, "预设印章");
            if (Directory.Exists(Root)) foreach (var path in Directory.GetFiles(Root, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p))
            {
                try
                {
                    if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("文件过大");
                    read(File.ReadAllText(path), Path.GetFileName(path));
                }
                catch (Exception error) { issues.Add(Path.GetFileName(path) + "：" + error.Message); }
            }
            return found.Values.OrderBy(s => s.name, StringComparer.CurrentCulture).ThenBy(s => s.stampId).ToList();
        }

        public string Save(SokobanStamp stamp)
        {
            var errors = SokobanStamp.Validate(stamp);
            if (errors.Count > 0) throw new InvalidDataException(string.Join("；", errors));
            Directory.CreateDirectory(Root);
            var path = Path.Combine(Root, stamp.stampId + ".json");
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(stamp, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
    }

    public static class SokobanStampRepository
    {
        internal static string RootOverride;
        public const string ResourcePath = "data/Stamps";
        public static string Root => RootOverride ?? (Application.isEditor ? Path.Combine(Application.dataPath, "Resources", "data", "Stamps") : Path.Combine(Application.persistentDataPath, "Stamps"));
        public static SokobanStampStore Store => new SokobanStampStore(Root, () => Resources.LoadAll<TextAsset>(ResourcePath).Select(a => a.text));
        public static void Save(SokobanStamp stamp)
        {
            var path = Store.Save(stamp);
#if UNITY_EDITOR
            var assetRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (path.StartsWith(assetRoot, StringComparison.OrdinalIgnoreCase))
                UnityEditor.AssetDatabase.ImportAsset("Assets/" + path.Substring(assetRoot.Length).Replace('\\', '/'));
#endif
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { RootOverride = null; }
    }
}

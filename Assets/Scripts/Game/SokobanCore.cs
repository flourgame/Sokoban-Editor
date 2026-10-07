using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    [Serializable]
    public class SokobanJsonPoint
    {
        public int x;
        public int y;

        public SokobanJsonPoint() { }
        public SokobanJsonPoint(int x, int y) { this.x = x; this.y = y; }
    }

    [Serializable]
    public class SokobanJsonSize
    {
        public int width = 8;
        public int height = 8;
    }

    [Serializable]
    public class SokobanJsonMetadata
    {
        public string author = "";
        public string[] tags = Array.Empty<string>();
        public int difficulty = 1;
        public int parPushes;
        public int parMoves;
        public string notes = "";
    }

    [Serializable]
    public class SokobanJsonGeneration
    {
        public bool isGenerated;
        public int seed;
        public string generatorVersion = "";
        public SokobanGenerationSettings parameters;
        public SokobanDifficultyReport complexity;
        public SokobanGenerationQualityReport quality;
    }

    [Serializable]
    public class SokobanJsonSolution
    {
        public string status = "Unknown";
        public string moves = "";
        public int pushes;
        public int moveCount;
    }

    [Serializable]
    public class SokobanJsonLevel
    {
        public int schemaVersion = 1;
        public string levelId = "level";
        public string name = "未命名关卡";
        // 当前版本布局的可信求解总移动步数；-1 表示没有可信解答。
        public int verifiedMoves = -1;
        public SokobanJsonSize size = new SokobanJsonSize();
        public string[] terrain = Array.Empty<string>();
        public SokobanJsonPoint player = new SokobanJsonPoint(1, 1);
        public SokobanJsonPoint[] boxes = Array.Empty<SokobanJsonPoint>();
        public SokobanJsonPoint[] goals = Array.Empty<SokobanJsonPoint>();
        public SokobanJsonMetadata metadata = new SokobanJsonMetadata();
        public SokobanJsonGeneration generation = new SokobanJsonGeneration();
        public SokobanJsonSolution solution = new SokobanJsonSolution();
    }

    [Serializable]
    public class SokobanCatalogEntry
    {
        public string folder = "1";
        public string levelId = "L001";
        public string title = "第一关";
        public int order;
    }

    [Serializable]
    public class SokobanCatalogFile
    {
        public int schemaVersion = 1;
        public SokobanCatalogEntry[] entries = Array.Empty<SokobanCatalogEntry>();
    }

    public struct SokobanGridPoint : IEquatable<SokobanGridPoint>
    {
        public int x;
        public int y;

        public SokobanGridPoint(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(SokobanGridPoint other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is SokobanGridPoint other && Equals(other);
        public override int GetHashCode() => (x * 397) ^ y;
        public static bool operator ==(SokobanGridPoint left, SokobanGridPoint right) => left.Equals(right);
        public static bool operator !=(SokobanGridPoint left, SokobanGridPoint right) => !left.Equals(right);
        public override string ToString() => $"({x},{y})";
    }

    public enum SokobanDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    public enum SokobanBrush
    {
        Floor,
        Wall,
        Goal,
        Box,
        Player
    }

    public sealed class SokobanLevelRuntime
    {
        public string LevelId { get; }
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public bool[,] Walls { get; }
        public HashSet<SokobanGridPoint> Goals { get; }
        public SokobanGridPoint PlayerStart { get; }
        public HashSet<SokobanGridPoint> BoxesStart { get; }
        public SokobanJsonLevel Source { get; }

        public SokobanLevelRuntime(SokobanJsonLevel source)
        {
            Source = source;
            LevelId = source.levelId ?? "level";
            Name = string.IsNullOrWhiteSpace(source.name) ? LevelId : source.name;
            Width = Mathf.Max(1, source.size == null ? 8 : source.size.width);
            Height = Mathf.Max(1, source.size == null ? 8 : source.size.height);
            Walls = new bool[Width, Height];
            Goals = new HashSet<SokobanGridPoint>();
            BoxesStart = new HashSet<SokobanGridPoint>();

            for (var y = 0; y < Height; y++)
            {
                var row = source.terrain != null && y < source.terrain.Length ? source.terrain[y] ?? "" : "";
                for (var x = 0; x < Width; x++)
                {
                    Walls[x, y] = x >= row.Length || row[x] == '#';
                }
            }

            if (source.goals != null)
            {
                foreach (var goal in source.goals)
                {
                    if (goal != null) Goals.Add(new SokobanGridPoint(goal.x, goal.y));
                }
            }

            if (source.boxes != null)
            {
                foreach (var box in source.boxes)
                {
                    if (box != null) BoxesStart.Add(new SokobanGridPoint(box.x, box.y));
                }
            }

            PlayerStart = source.player == null ? new SokobanGridPoint(1, 1) : new SokobanGridPoint(source.player.x, source.player.y);
        }

        public bool InBounds(SokobanGridPoint point) => point.x >= 0 && point.x < Width && point.y >= 0 && point.y < Height;
        public bool IsWall(SokobanGridPoint point) => !InBounds(point) || Walls[point.x, point.y];
        public bool IsFloor(SokobanGridPoint point) => InBounds(point) && !Walls[point.x, point.y];

        public SokobanState CreateInitialState()
        {
            return new SokobanState(PlayerStart, BoxesStart);
        }
    }

    public sealed class SokobanState
    {
        public SokobanGridPoint Player { get; private set; }
        public HashSet<SokobanGridPoint> Boxes { get; private set; }
        public int MoveCount { get; private set; }
        public int PushCount { get; private set; }
        public bool IsWon { get; private set; }

        public SokobanState(SokobanGridPoint player, IEnumerable<SokobanGridPoint> boxes)
        {
            Player = player;
            Boxes = new HashSet<SokobanGridPoint>(boxes ?? Array.Empty<SokobanGridPoint>());
        }

        private SokobanState(SokobanState other)
        {
            Player = other.Player;
            Boxes = new HashSet<SokobanGridPoint>(other.Boxes);
            MoveCount = other.MoveCount;
            PushCount = other.PushCount;
            IsWon = other.IsWon;
        }

        public SokobanState Clone() => new SokobanState(this);

        internal void ApplyMove(SokobanGridPoint player, bool pushed, SokobanGridPoint pushedBox, SokobanGridPoint destination)
        {
            Player = player;
            MoveCount++;
            if (pushed)
            {
                Boxes.Remove(pushedBox);
                Boxes.Add(destination);
                PushCount++;
            }
        }

        internal void SetWon(bool won) => IsWon = won;

        /// <summary>按"所有箱子都在目标点上"重新判定胜利（开局检测与强制胜利共用）。</summary>
        public void RefreshWin(SokobanLevelRuntime level)
        {
            if (level == null) return;
            SetWon(Boxes.Count > 0 && Boxes.Count == level.Goals.Count && Boxes.All(level.Goals.Contains));
        }

        public void ForceWin(SokobanLevelRuntime level)
        {
            if (level == null) return;
            Boxes = new HashSet<SokobanGridPoint>(level.Goals);
            IsWon = Boxes.Count > 0 && Boxes.Count == level.Goals.Count;
        }
    }

    public sealed class SokobanSimulationResult
    {
        public bool Accepted { get; }
        public bool Pushed { get; }
        public bool Won { get; }
        public string Reason { get; }

        public SokobanSimulationResult(bool accepted, bool pushed, bool won, string reason = "")
        {
            Accepted = accepted;
            Pushed = pushed;
            Won = won;
            Reason = reason ?? "";
        }
    }

    public static class SokobanSimulation
    {
        public static SokobanSimulationResult TryMove(SokobanLevelRuntime level, SokobanState state, SokobanDirection direction)
        {
            var delta = DirectionDelta(direction);
            var next = new SokobanGridPoint(state.Player.x + delta.x, state.Player.y + delta.y);
            if (level.IsWall(next)) return new SokobanSimulationResult(false, false, state.IsWon, "前方是墙或越界");

            var pushed = false;
            if (state.Boxes.Contains(next))
            {
                var destination = new SokobanGridPoint(next.x + delta.x, next.y + delta.y);
                if (level.IsWall(destination) || state.Boxes.Contains(destination))
                {
                    return new SokobanSimulationResult(false, false, state.IsWon, "箱子无法推动");
                }

                state.ApplyMove(next, true, next, destination);
                pushed = true;
            }
            else
            {
                state.ApplyMove(next, false, default, default);
            }

            var won = state.Boxes.Count > 0 && state.Boxes.All(level.Goals.Contains) && state.Boxes.Count == level.Goals.Count;
            state.SetWon(won);
            return new SokobanSimulationResult(true, pushed, won);
        }

        public static SokobanGridPoint DirectionDelta(SokobanDirection direction)
        {
            switch (direction)
            {
                case SokobanDirection.Up: return new SokobanGridPoint(0, -1);
                case SokobanDirection.Down: return new SokobanGridPoint(0, 1);
                case SokobanDirection.Left: return new SokobanGridPoint(-1, 0);
                default: return new SokobanGridPoint(1, 0);
            }
        }
    }

    public sealed class SokobanLevelDescriptor
    {
        public string LevelId;
        public string Title;
        public string Folder;
        public string Source;
        public string Category;
        public string ResourcePath;
        public string FilePath;
        public string BundledJson;
        public int Order;

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? LevelId : Title;
    }

    public static class SokobanRuntimeContext
    {
        public static string SelectedLevelId = "L001";
        public static bool IsGmEnabled;
        public static bool IsEditorPreview;
        public static bool IsDebugPlay;
        public static bool ShowAllLevels;
        public static bool IsQuitConfirmationOpen;
    }

    public static class SokobanLevelRepository
    {
        // 关卡数据根目录：Assets/Resources/data/level。
        // 读取走 Resources.Load（编辑器与玩家构建都可用、可打包）；
        // 编辑器内额外走文件读写以支持保存与目录枚举。
        public const string DataFolderName = "data";
        public const string LevelFolderName = "level";
        public const string BuiltInFolderName = "BuiltIn";
        public const string GeneratedFolderName = "Generated";

        /// <summary>Resources 相对根（不含扩展名），用于 Resources.Load。</summary>
        public const string ResourceLevelRoot = DataFolderName + "/" + LevelFolderName;

        internal static string PlayerDataRootOverride;
        public static string DataRoot => Application.isEditor ? Path.Combine(Application.dataPath, "Resources", DataFolderName) : PlayerDataRootOverride ?? Application.persistentDataPath;
        public static string LevelRoot => Path.Combine(DataRoot, LevelFolderName);
        public static string BuiltInRoot => Path.Combine(LevelRoot, BuiltInFolderName);
        public static string GeneratedRoot => Path.Combine(LevelRoot, GeneratedFolderName);

        private static readonly Dictionary<string, SokobanLevelDescriptor> DescriptorCache = new Dictionary<string, SokobanLevelDescriptor>();

        public static SokobanLibraryStore Library => new SokobanLibraryStore(LevelRoot,
            Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/SokobanDeletedLevels")) : Path.Combine(Application.persistentDataPath, "DeletedLevels"),
            Application.isEditor ? null : Resources.Load<TextAsset>(ResourceLevelRoot + "/library")?.text);

        public static IReadOnlyList<SokobanLevelDescriptor> ListAll()
        {
            DescriptorCache.Clear();
            var result = new List<SokobanLevelDescriptor>();
            AddBuiltIn(result);
            AddGenerated(result);
            var ordered = result.OrderBy(d => d.Order).ThenBy(d => d.LevelId).ToList();
            try { return Library.Apply(ordered); }
            catch (Exception error)
            {
                Debug.LogWarning("关卡分类索引读取失败，使用原始列表：" + error.Message);
                foreach (var level in ordered) level.Category = level.Source;
                return ordered;
            }
        }

        private static void AddBuiltIn(List<SokobanLevelDescriptor> result)
        {
            var catalogText = Resources.Load<TextAsset>(ResourceLevelRoot + "/" + BuiltInFolderName + "/level")?.text;
            if (string.IsNullOrWhiteSpace(catalogText))
            {
                var catalogPath = Path.Combine(BuiltInRoot, "level.json");
                if (File.Exists(catalogPath)) catalogText = File.ReadAllText(catalogPath);
            }
            if (!string.IsNullOrWhiteSpace(catalogText))
            {
                var catalog = ParseCatalog(catalogText);
                if (catalog != null && catalog.entries != null)
                {
                    foreach (var entry in catalog.entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.levelId)) continue;
                        var folder = string.IsNullOrWhiteSpace(entry.folder) ? entry.levelId : entry.folder;
                        if (Application.isEditor && !File.Exists(Path.Combine(BuiltInRoot, folder, "level.json"))) continue;
                        var descriptor = new SokobanLevelDescriptor
                        {
                            LevelId = entry.levelId,
                            Title = entry.title,
                            Folder = folder,
                            Source = "BuiltIn",
                            Order = entry.order,
                            ResourcePath = $"{ResourceLevelRoot}/{BuiltInFolderName}/{folder}/level",
                            FilePath = Path.Combine(BuiltInRoot, folder, "level.json")
                        };
                        if (!Application.isEditor) descriptor.BundledJson = Resources.Load<TextAsset>(descriptor.ResourcePath)?.text;
                        var data = LoadJson(descriptor);
                        if (data != null && !string.IsNullOrWhiteSpace(data.name)) descriptor.Title = data.name;
                        AddDescriptor(result, descriptor);
                    }
                }
            }

            // 若目录索引还未建立，仍允许直接发现内置 JSON。
            if (result.Count == 0)
            {
                if (Application.isEditor && Directory.Exists(BuiltInRoot))
                {
                    foreach (var file in Directory.GetFiles(BuiltInRoot, "level.json", SearchOption.AllDirectories))
                    {
                        if (Path.GetFullPath(file) == Path.GetFullPath(Path.Combine(BuiltInRoot, "level.json"))) continue;
                        var data = Parse(File.ReadAllText(file));
                        if (data == null || string.IsNullOrWhiteSpace(data.levelId)) continue;
                        var relativeFolder = Path.GetDirectoryName(file)?.Substring(BuiltInRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? data.levelId;
                        AddDescriptor(result, new SokobanLevelDescriptor
                        {
                            LevelId = data.levelId,
                            Title = data.name,
                            Folder = relativeFolder.Replace('\\', '/'),
                            Source = "BuiltIn",
                            Order = result.Count,
                            ResourcePath = $"{ResourceLevelRoot}/{BuiltInFolderName}/{relativeFolder.Replace('\\', '/')}/level",
                            FilePath = file
                        });
                    }
                }
                else
                {
                    foreach (var asset in Resources.LoadAll<TextAsset>(ResourceLevelRoot + "/" + BuiltInFolderName))
                    {
                        if (asset == null || asset.name != "level") continue;
                        if (asset == Resources.Load<TextAsset>(ResourceLevelRoot + "/" + BuiltInFolderName + "/level")) continue;
                        var data = Parse(asset.text);
                        if (data == null || string.IsNullOrWhiteSpace(data.levelId)) continue;
                        AddDescriptor(result, new SokobanLevelDescriptor
                        {
                            LevelId = data.levelId,
                            Title = data.name,
                            Folder = data.levelId,
                            Source = "BuiltIn",
                            Order = result.Count,
                            ResourcePath = $"{ResourceLevelRoot}/{BuiltInFolderName}/{data.levelId}/level"
                        });
                    }
                }
            }
        }

        private static SokobanCatalogFile ParseCatalog(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return JsonUtility.FromJson<SokobanCatalogFile>(text);
        }

        private static void AddGenerated(List<SokobanLevelDescriptor> result)
        {
            var generatedRoot = GeneratedRoot;
            if (Directory.Exists(generatedRoot))
            {
                foreach (var file in Directory.GetFiles(generatedRoot, "level.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
                {
                    try
                    {
                        var data = Parse(File.ReadAllText(file));
                        if (data == null || string.IsNullOrWhiteSpace(data.levelId)) continue;
                        var relativeFolder = Path.GetDirectoryName(file)?.Substring(generatedRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? data.levelId;
                        AddDescriptor(result, new SokobanLevelDescriptor
                        {
                            LevelId = data.levelId,
                            Title = data.name,
                            Folder = relativeFolder.Replace('\\', '/'),
                            Source = "Generated",
                            Order = 10000 + result.Count,
                            ResourcePath = $"{ResourceLevelRoot}/{GeneratedFolderName}/{relativeFolder.Replace('\\', '/')}/level",
                            FilePath = file
                        });
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"无法读取 Generated 关卡 {file}: {exception.Message}");
                    }
                }
                if (Application.isEditor) return;
            }

            // 玩家构建：无文件系统枚举能力，用 Resources 全量加载。
            foreach (var asset in Resources.LoadAll<TextAsset>(ResourceLevelRoot + "/" + GeneratedFolderName))
            {
                if (asset == null || asset.name != "level") continue;
                var data = Parse(asset.text);
                if (data == null || string.IsNullOrWhiteSpace(data.levelId)) continue;
                AddDescriptor(result, new SokobanLevelDescriptor
                {
                    LevelId = data.levelId,
                    Title = data.name,
                    Folder = data.levelId,
                    Source = "Generated",
                    Order = 10000 + result.Count,
                    ResourcePath = $"{ResourceLevelRoot}/{GeneratedFolderName}/{data.levelId}/level",
                    FilePath = Path.Combine(GeneratedRoot, data.levelId, "level.json"),
                    BundledJson = asset.text
                });
            }
        }

        private static void AddDescriptor(List<SokobanLevelDescriptor> result, SokobanLevelDescriptor descriptor)
        {
            if (DescriptorCache.ContainsKey(descriptor.LevelId)) return;
            DescriptorCache[descriptor.LevelId] = descriptor;
            result.Add(descriptor);
        }

        public static SokobanJsonLevel LoadJson(string levelId)
        {
            var descriptor = ListAll().FirstOrDefault(item => item.LevelId == levelId);
            if (descriptor == null) return null;
            return LoadJson(descriptor);
        }

        public static SokobanJsonLevel LoadJson(SokobanLevelDescriptor descriptor)
        {
            try
            {
                return Parse(ReadSavedText(descriptor));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"读取关卡失败 {descriptor.LevelId}: {exception.Message}");
                return null;
            }
        }

        public static string ReadSavedText(SokobanLevelDescriptor descriptor)
        {
            if (descriptor == null) return null;
            if (!string.IsNullOrWhiteSpace(descriptor.FilePath) && File.Exists(descriptor.FilePath)) return File.ReadAllText(descriptor.FilePath);
            if (!string.IsNullOrWhiteSpace(descriptor.BundledJson)) return descriptor.BundledJson;
            return !string.IsNullOrWhiteSpace(descriptor.ResourcePath) ? Resources.Load<TextAsset>(descriptor.ResourcePath)?.text : null;
        }

        public static void PrepareWritableCopy(SokobanLevelDescriptor descriptor)
        {
            if (Application.isEditor || string.IsNullOrWhiteSpace(descriptor.FilePath) || File.Exists(descriptor.FilePath)) return;
            var data = LoadJson(descriptor);
            if (data != null) SaveJson(data, descriptor, false, false);
        }

        public static void SaveJson(SokobanJsonLevel data, SokobanLevelDescriptor descriptor = null,
            bool refreshAssets = true, bool overwrite = true)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.size == null) data.size = new SokobanJsonSize();
            if (data.terrain == null)
            {
                data.terrain = BuildEmptyTerrain(data.size.width, data.size.height);
            }

            var path = descriptor?.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var folder = string.IsNullOrWhiteSpace(descriptor?.Folder) ? data.levelId : descriptor.Folder;
                path = Path.Combine(GeneratedRoot, folder, "level.json");
                if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(GeneratedRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("关卡目录无效，不能保存到关卡根目录外。");
            }

            var directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            var json = JsonUtility.ToJson(data, true);
            var pendingPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(pendingPath, json);
                if (overwrite && File.Exists(path)) File.Replace(pendingPath, path, null); else File.Move(pendingPath, path);
            }
            finally { if (File.Exists(pendingPath)) File.Delete(pendingPath); }
            if (descriptor != null)
            {
                descriptor.FilePath = path;
                descriptor.ResourcePath = $"{ResourceLevelRoot}/{(descriptor.Source == "BuiltIn" ? BuiltInFolderName : GeneratedFolderName)}/{descriptor.Folder}/level";
            }
#if UNITY_EDITOR
            if (refreshAssets) UnityEditor.AssetDatabase.Refresh();
#endif
        }

        public static SokobanJsonLevel Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Trim() == "null") return null;
            var data = new SokobanJsonLevel();
            JsonUtility.FromJsonOverwrite(text, data);
            if (data.size == null) data.size = new SokobanJsonSize();
            if (data.terrain == null || data.terrain.Length == 0) data.terrain = BuildEmptyTerrain(data.size.width, data.size.height);
            if (data.player == null) data.player = new SokobanJsonPoint(1, 1);
            if (data.boxes == null) data.boxes = Array.Empty<SokobanJsonPoint>();
            if (data.goals == null) data.goals = Array.Empty<SokobanJsonPoint>();
            if (data.metadata == null) data.metadata = new SokobanJsonMetadata();
            if (data.generation == null) data.generation = new SokobanJsonGeneration();
            if (data.solution == null) data.solution = new SokobanJsonSolution();
            return data;
        }

        public static string[] BuildEmptyTerrain(int width, int height)
        {
            width = Mathf.Clamp(width, 3, 40);
            height = Mathf.Clamp(height, 3, 40);
            var result = new string[height];
            for (var y = 0; y < height; y++)
            {
                var chars = new char[width];
                for (var x = 0; x < width; x++) chars[x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? '#' : '.';
                result[y] = new string(chars);
            }
            return result;
        }
    }

    public static class SokobanValidation
    {
        public static List<string> Validate(SokobanJsonLevel data)
        {
            var errors = new List<string>();
            if (data == null) return new List<string> { "关卡为空" };
            if (data.schemaVersion != 1) errors.Add("不支持的关卡 schemaVersion，当前仅支持版本 1");
            if (data.size == null || data.size.width < 3 || data.size.height < 3 || data.size.width > 40 || data.size.height > 40)
            {
                errors.Add("网格尺寸必须在 3 x 3 至 40 x 40 之间");
                return errors;
            }
            if (data.terrain == null || data.terrain.Length != data.size.height) errors.Add("terrain 行数与高度不一致");
            else
            {
                for (var i = 0; i < data.terrain.Length; i++)
                {
                    if (data.terrain[i] == null || data.terrain[i].Length != data.size.width) errors.Add($"terrain 第 {i + 1} 行长度不一致");
                    else if (data.terrain[i].Any(c => c != '#' && c != '.')) errors.Add($"terrain 第 {i + 1} 行包含未知地形字符");
                }
            }

            var level = new SokobanLevelRuntime(data);
            if (data.player == null || !level.IsFloor(new SokobanGridPoint(data.player.x, data.player.y))) errors.Add("玩家必须位于可走地面");
            if (data.boxes == null || data.goals == null || data.boxes.Length != data.goals.Length) errors.Add("箱子数量必须等于目标数量");
            if (data.boxes == null || data.boxes.Length == 0) errors.Add("至少需要一个箱子");
            if (data.boxes != null)
            {
                var set = new HashSet<SokobanGridPoint>();
                foreach (var box in data.boxes)
                {
                    if (box == null) { errors.Add("箱子数据包含空项"); continue; }
                    var point = new SokobanGridPoint(box.x, box.y);
                    if (!level.IsFloor(point)) errors.Add($"箱子 {point} 不在可走地面");
                    if (!set.Add(point)) errors.Add($"箱子 {point} 重叠");
                    if (data.player != null && box.x == data.player.x && box.y == data.player.y) errors.Add($"玩家与箱子 {point} 重叠");
                }
            }
            if (data.goals != null)
            {
                var set = new HashSet<SokobanGridPoint>();
                foreach (var goal in data.goals)
                {
                    if (goal == null) { errors.Add("目标数据包含空项"); continue; }
                    var point = new SokobanGridPoint(goal.x, goal.y);
                    if (!level.IsFloor(point)) errors.Add($"目标 {point} 不在可走地面");
                    if (!set.Add(point)) errors.Add($"目标 {point} 重叠");
                }
            }
            return errors;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    [Serializable]
    public sealed class SokobanLibraryEntry
    {
        public string levelId;
        public string category;
        public int order;
        public bool deleted;
    }

    [Serializable]
    public sealed class SokobanLibraryCatalog
    {
        public int schemaVersion = 1;
        public string[] categories = { "BuiltIn", "Generated" };
        public SokobanLibraryEntry[] entries = Array.Empty<SokobanLibraryEntry>();
        /// <summary>
        /// 在关卡管理里勾选"对玩家公开"的类别。这些类别的全部关卡会并入玩家战役链
        ///（沿用同一套索引顺序解锁）。玩家可见关卡完全由这里决定，与关卡来自哪个资源目录无关。
        /// 默认公开 BuiltIn；可以取消勾选，也可以只公开自定义类别。
        /// 类别本身是编辑器内部概念，玩家界面不显示类别名。
        /// </summary>
        public string[] publishedCategories = { "BuiltIn" };
    }

    /// <summary>分类与顺序独立于来源目录和关卡 ID，避免迁移分类改变资源路径或通关记录。</summary>
    public sealed class SokobanLibraryStore
    {
        private readonly string root, backupRoot, resourceCatalog;
        public string CatalogPath => Path.Combine(root, "library.json");

        public SokobanLibraryStore(string levelRoot, string deletionBackupRoot, string bundledCatalog = null)
        {
            root = Path.GetFullPath(levelRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            backupRoot = Path.GetFullPath(deletionBackupRoot);
            resourceCatalog = bundledCatalog;
        }

        /// <summary>索引文件缺少 publishedCategories 字段（null）时使用的默认公开分类。</summary>
        private static readonly string[] DefaultPublishedCategories = { "BuiltIn" };

        public SokobanLibraryCatalog Read()
        {
            var text = File.Exists(CatalogPath) ? File.ReadAllText(CatalogPath) : resourceCatalog;
            var catalog = string.IsNullOrWhiteSpace(text) ? new SokobanLibraryCatalog() : JsonUtility.FromJson<SokobanLibraryCatalog>(text);
            if (catalog == null || catalog.schemaVersion != 1) throw new InvalidDataException("关卡分类索引版本无效。");
            var categories = new List<string> { "BuiltIn", "Generated" };
            foreach (var category in catalog.categories ?? Array.Empty<string>())
            {
                ValidateCategory(category);
                if (!categories.Contains(category, StringComparer.OrdinalIgnoreCase)) categories.Add(category);
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in catalog.entries ?? Array.Empty<SokobanLibraryEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.levelId) || !ids.Add(entry.levelId))
                    throw new InvalidDataException("关卡分类索引包含空 ID 或重复 ID。");
                ValidateCategory(entry.category);
                if (!categories.Contains(entry.category, StringComparer.OrdinalIgnoreCase)) categories.Add(entry.category);
            }
            catalog.categories = categories.ToArray();
            catalog.entries = catalog.entries ?? Array.Empty<SokobanLibraryEntry>();
            catalog.publishedCategories = NormalizePublished(
                catalog.publishedCategories ?? DefaultPublishedCategories, catalog.categories);
            return catalog;
        }

        private static string[] NormalizePublished(string[] published, string[] categories)
        {
            var result = new List<string>();
            foreach (var name in published ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var match = categories.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
                if (match != null && !result.Contains(match, StringComparer.OrdinalIgnoreCase)) result.Add(match);
            }
            return result.ToArray();
        }

        /// <summary>当前对玩家公开的类别；玩家可见关卡完全由这些类别决定，BuiltIn 也可被取消勾选。</summary>
        public IReadOnlyList<string> PublishedCategories()
        {
            return Read().publishedCategories ?? Array.Empty<string>();
        }

        /// <summary>设置对玩家公开的类别；未知类别直接报错，避免静默丢失勾选。</summary>
        public void SetPublishedCategories(IEnumerable<string> names)
        {
            var catalog = Read();
            var requested = (names ?? Array.Empty<string>()).ToList();
            foreach (var name in requested)
            {
                if (!catalog.categories.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("分类不存在：" + name);
            }
            catalog.publishedCategories = NormalizePublished(requested.ToArray(), catalog.categories);
            Save(catalog);
        }

        public IReadOnlyList<SokobanLevelDescriptor> Apply(IReadOnlyList<SokobanLevelDescriptor> levels)
        {
            var catalog = Read();
            var entries = catalog.entries.ToDictionary(e => e.levelId, StringComparer.Ordinal);
            var nextOrder = catalog.entries.Length == 0 ? 0 : catalog.entries.Max(e => e.order) + 1;
            var result = new List<SokobanLevelDescriptor>();
            foreach (var level in levels)
            {
                if (entries.TryGetValue(level.LevelId, out var entry) && entry.deleted) continue;
                level.Category = entry?.category ?? level.Source;
                level.Order = entry?.order ?? nextOrder++;
                result.Add(level);
            }
            return result.OrderBy(l => l.Order).ThenBy(l => l.LevelId, StringComparer.Ordinal).ToList();
        }

        public void CreateCategory(string name)
        {
            name = (name ?? "").Trim(); ValidateCategory(name);
            var catalog = Read();
            if (catalog.categories.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("分类已存在：" + name);
            catalog.categories = catalog.categories.Concat(new[] { name }).ToArray();
            Save(catalog);
        }

        public void Migrate(IReadOnlyList<SokobanLevelDescriptor> levels, IEnumerable<string> selectedIds, string category)
        {
            var catalog = Merge(Read(), levels);
            category = catalog.categories.FirstOrDefault(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("目标分类不存在。");
            var ids = ValidateSelection(levels, selectedIds);
            foreach (var entry in catalog.entries) if (ids.Contains(entry.levelId)) entry.category = category;
            Save(catalog);
        }

        /// <summary>只替换当前分类可见项的全局顺序槽位，其他分类的相对顺序不变。</summary>
        public void Reorder(IReadOnlyList<SokobanLevelDescriptor> levels, IReadOnlyList<string> visibleIds, IReadOnlyList<string> newVisibleOrder)
        {
            var catalog = Merge(Read(), levels);
            var visible = ValidateSelection(levels, visibleIds);
            if (visible.Count != visibleIds.Count || newVisibleOrder.Count != visible.Count ||
                new HashSet<string>(newVisibleOrder, StringComparer.Ordinal).Count != visible.Count || !visible.SetEquals(newVisibleOrder))
                throw new InvalidOperationException("重排序必须包含当前列表中的每个关卡且不能重复。");
            var entries = catalog.entries.ToDictionary(e => e.levelId, StringComparer.Ordinal);
            var replacementIndex = 0;
            for (var i = 0; i < levels.Count; i++)
            {
                var id = visible.Contains(levels[i].LevelId) ? newVisibleOrder[replacementIndex++] : levels[i].LevelId;
                entries[id].order = i;
            }
            Save(catalog);
        }

        public void Delete(IReadOnlyList<SokobanLevelDescriptor> levels, IEnumerable<string> selectedIds)
        {
            var catalog = Merge(Read(), levels);
            var ids = ValidateSelection(levels, selectedIds);
            var selected = levels.Where(l => ids.Contains(l.LevelId)).ToList();
            // 整批预检后才移动文件：只处理选中的 level.json 与其 .meta，不删除所在目录中的其他资源。
            foreach (var level in selected)
            {
                var path = CheckedLevelPath(level.FilePath);
                if (!File.Exists(path) && string.IsNullOrEmpty(level.BundledJson)) throw new FileNotFoundException("关卡文件已不存在：" + level.DisplayTitle, path);
                var data = SokobanLevelRepository.Parse(File.Exists(path) ? File.ReadAllText(path) : level.BundledJson);
                if (data == null || data.levelId != level.LevelId) throw new InvalidDataException("关卡文件与所选 ID 不一致：" + level.LevelId);
            }
            var transactionRoot = Path.Combine(backupRoot, DateTime.Now.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N"));
            var moved = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var level in selected)
                {
                    var path = CheckedLevelPath(level.FilePath);
                    if (!File.Exists(path)) continue;
                    var relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    MoveToBackup(path, Path.Combine(transactionRoot, relative), moved);
                    if (File.Exists(path + ".meta")) MoveToBackup(path + ".meta", Path.Combine(transactionRoot, relative) + ".meta", moved);
                }
                foreach (var entry in catalog.entries) if (ids.Contains(entry.levelId)) entry.deleted = true;
                Save(catalog);
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                for (var i = moved.Count - 1; i >= 0; i--)
                    try { File.Move(moved[i].Value, moved[i].Key); }
                    catch (Exception rollback) { failures.Add(rollback); }
                if (failures.Count > 1) throw new AggregateException("删除失败且部分文件恢复失败，删除备份仍保留。", failures);
                throw;
            }
        }

        private static void MoveToBackup(string source, string destination, List<KeyValuePair<string, string>> moved)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Move(source, destination);
            moved.Add(new KeyValuePair<string, string>(source, destination));
        }

        private string CheckedLevelPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("此关卡没有可管理的本地文件。");
            var resolved = Path.GetFullPath(path);
            if (!resolved.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(resolved), "level.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("关卡文件必须位于关卡根目录内。");
            return resolved;
        }

        private static HashSet<string> ValidateSelection(IReadOnlyList<SokobanLevelDescriptor> levels, IEnumerable<string> selectedIds)
        {
            var selected = new HashSet<string>(selectedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var available = new HashSet<string>(levels.Select(l => l.LevelId), StringComparer.Ordinal);
            if (selected.Count == 0 || !selected.IsSubsetOf(available)) throw new InvalidOperationException("请选择仍存在的关卡。");
            return selected;
        }

        private static SokobanLibraryCatalog Merge(SokobanLibraryCatalog catalog, IReadOnlyList<SokobanLevelDescriptor> levels)
        {
            var entries = catalog.entries.ToDictionary(e => e.levelId, StringComparer.Ordinal);
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                if (!entries.TryGetValue(level.LevelId, out var entry))
                    entries[level.LevelId] = entry = new SokobanLibraryEntry { levelId = level.LevelId, category = level.Category ?? level.Source, order = i };
                if (entry.deleted) throw new InvalidOperationException("关卡已经删除，请刷新列表。");
            }
            catalog.entries = entries.Values.ToArray();
            return catalog;
        }

        private static void ValidateCategory(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 40 || name.Any(char.IsControl) ||
                string.Equals(name, "All", StringComparison.OrdinalIgnoreCase) || name == "全部")
                throw new InvalidOperationException("分类名称须为 1–40 个字符，不能使用“全部”或换行。");
        }

        public void AppendImported(IReadOnlyList<SokobanLevelDescriptor> existing, IReadOnlyList<SokobanLevelDescriptor> imported)
        {
            var catalog = Merge(Read(), existing);
            var ids = new HashSet<string>(catalog.entries.Select(e => e.levelId), StringComparer.OrdinalIgnoreCase);
            var additions = new List<SokobanLibraryEntry>();
            var order = catalog.entries.Length == 0 ? 0 : catalog.entries.Max(e => e.order) + 1;
            foreach (var level in imported)
            {
                if (string.IsNullOrWhiteSpace(level.LevelId) || !ids.Add(level.LevelId)) throw new InvalidDataException("导入关卡 ID 重复。");
                ValidateCategory(level.Category);
                CheckedLevelPath(level.FilePath);
                var category = catalog.categories.FirstOrDefault(c => string.Equals(c, level.Category, StringComparison.OrdinalIgnoreCase));
                if (category == null) { category = level.Category; catalog.categories = catalog.categories.Concat(new[] { category }).ToArray(); }
                additions.Add(new SokobanLibraryEntry { levelId = level.LevelId, category = category, order = order++ });
            }
            catalog.entries = catalog.entries.Concat(additions).ToArray();
            // Existing publication preferences are preserved; newly created categories stay hidden.
            Save(catalog);
        }

        private void Save(SokobanLibraryCatalog catalog)
        {
            Directory.CreateDirectory(root);
            var temporary = CatalogPath + ".tmp_" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(catalog, true));
                if (File.Exists(CatalogPath)) File.Replace(temporary, CatalogPath, null);
                else File.Move(temporary, CatalogPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public sealed class SokobanLibrarySelection
    {
        public readonly HashSet<string> Selected = new HashSet<string>(StringComparer.Ordinal);
        public string Anchor { get; private set; }

        public void Click(IReadOnlyList<string> visible, string id, bool control, bool shift)
        {
            var index = IndexOf(visible, id);
            if (index < 0) return;
            var anchor = IndexOf(visible, Anchor);
            if (shift && anchor >= 0)
            {
                if (!control) Selected.Clear();
                for (var i = Math.Min(index, anchor); i <= Math.Max(index, anchor); i++) Selected.Add(visible[i]);
                return;
            }
            if (!control) Selected.Clear();
            if (control && !Selected.Add(id)) Selected.Remove(id);
            else Selected.Add(id);
            Anchor = id;
        }

        public void Clear() { Selected.Clear(); Anchor = null; }
        public void SelectAll(IReadOnlyList<string> visible) { Selected.Clear(); Selected.UnionWith(visible); Anchor = visible.FirstOrDefault(); }
        public void Retain(IReadOnlyList<string> visible)
        {
            Selected.IntersectWith(visible);
            if (IndexOf(visible, Anchor) < 0) Anchor = null;
        }
        private static int IndexOf(IReadOnlyList<string> values, string value)
        {
            for (var i = 0; i < values.Count; i++) if (values[i] == value) return i;
            return -1;
        }
    }

    public static class SokobanLibraryOrdering
    {
        public static List<string> MoveGroup(IReadOnlyList<string> visible, IEnumerable<string> selected, int insertionBoundary)
        {
            var ids = new HashSet<string>(selected, StringComparer.Ordinal);
            insertionBoundary = Math.Max(0, Math.Min(insertionBoundary, visible.Count));
            var group = visible.Where(ids.Contains).ToList();
            var result = visible.Where(id => !ids.Contains(id)).ToList();
            var offset = visible.Take(insertionBoundary).Count(ids.Contains);
            result.InsertRange(insertionBoundary - offset, group);
            return result;
        }
    }
}

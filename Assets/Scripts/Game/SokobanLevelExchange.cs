using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    public sealed class SokobanExchangeRow
    {
        public int RowNumber;
        public int Order;
        public string Category;
        public SokobanJsonLevel Level;
    }

    public static class SokobanLevelExchange
    {
        public static readonly string[] Headers = { "ID", "名称", "分类", "顺序", "宽", "高", "箱子数", "推箱次数", "验证步数", "配置JSON" };

        // Unlike the repository's tolerant legacy reader, imports require an explicit layout.
        [Serializable] private sealed class ImportShape
        {
            public SokobanJsonSize size;
            public string[] terrain;
            public SokobanJsonPoint player;
            public SokobanJsonPoint[] boxes;
            public SokobanJsonPoint[] goals;
        }

        public static SokobanJsonLevel ParseImport(string text)
        {
            text = (text ?? "").Trim().TrimStart('\uFEFF').Trim();
            if (text.Length == 0 || text.Length > 1024 * 1024 || !text.StartsWith("{") || !text.EndsWith("}"))
                throw new InvalidDataException("请粘贴一个完整的关卡 JSON 对象（最多 1 MB）。");
            var shape = JsonUtility.FromJson<ImportShape>(text);
            if (shape?.size == null || shape.terrain == null || shape.player == null || shape.boxes == null || shape.goals == null)
                throw new InvalidDataException("JSON 须包含 size、terrain、player、boxes、goals，不能使用印章或其他配置。");
            // Check raw dimensions before any runtime allocates a grid.
            if (shape.size.width < 3 || shape.size.width > 40 || shape.size.height < 3 || shape.size.height > 40)
                throw new InvalidDataException("关卡尺寸须在 3×3 至 40×40 之间。");
            var level = SokobanLevelRepository.Parse(text);
            var errors = SokobanValidation.Validate(level);
            if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors.Take(12)));
            if (string.IsNullOrWhiteSpace(level.name) || level.name.Length > 200 || level.name.Any(char.IsControl))
                throw new InvalidDataException("关卡名称须为 1–200 个字符，不能含换行。");
            return level;
        }

        public static List<string[]> ExportRows(IReadOnlyList<SokobanLevelDescriptor> levels)
        {
            var rows = new List<string[]> { Headers.ToArray() };
            foreach (var descriptor in levels)
            {
                var level = SokobanLevelRepository.LoadJson(descriptor) ?? throw new InvalidDataException("读取失败：" + descriptor.LevelId);
                rows.Add(new[] { level.levelId, level.name, descriptor.Category, (rows.Count).ToString(CultureInfo.InvariantCulture),
                    level.size.width.ToString(), level.size.height.ToString(), (level.boxes?.Length ?? 0).ToString(),
                    level.solution?.pushes.ToString() ?? "0", level.verifiedMoves.ToString(), JsonUtility.ToJson(level) });
            }
            return rows;
        }

        public static List<SokobanExchangeRow> ParseRows(IReadOnlyList<string[]> table)
        {
            if (table == null || table.Count < 2 || table.Count > SokobanXlsx.MaxRows + 1) throw new InvalidDataException("表格须包含表头和关卡数据，最多 5000 个关卡。");
            var header = table[0].Select(h => (h ?? "").Trim()).ToArray();
            var jsonColumn = FindColumn(header, "配置JSON", "配置 JSON", "configJson", "json");
            if (jsonColumn < 0) throw new InvalidDataException("第一行缺少“配置JSON”列。");
            var nameColumn = FindColumn(header, "名称", "名字", "关卡名称", "name");
            var categoryColumn = FindColumn(header, "分类", "category");
            var orderColumn = FindColumn(header, "顺序", "order");
            var result = new List<SokobanExchangeRow>();
            var errors = new List<string>();
            for (var i = 1; i < table.Count; i++)
            {
                var values = table[i];
                if (values == null || values.All(string.IsNullOrWhiteSpace)) continue;
                try
                {
                    var level = ParseImport(Value(values, jsonColumn));
                    var name = Value(values, nameColumn).Trim();
                    if (name.Length > 0) level.name = name;
                    if (level.name.Length > 200 || level.name.Any(char.IsControl)) throw new InvalidDataException("名称须为 1–200 个字符，不能含换行。");
                    var category = Value(values, categoryColumn).Trim();
                    if (category.Length == 0) category = "Generated";
                    ValidateCategory(category);
                    var orderText = Value(values, orderColumn).Trim();
                    var order = i;
                    if (orderText.Length > 0 && (!int.TryParse(orderText, NumberStyles.None, CultureInfo.InvariantCulture, out order) || order < 1))
                        throw new InvalidDataException("顺序须为正整数。");
                    result.Add(new SokobanExchangeRow { RowNumber = i + 1, Order = order, Category = category, Level = level });
                }
                catch (Exception error) { errors.Add("第 " + (i + 1) + " 行：" + error.Message); }
            }
            if (errors.Count > 0) throw new InvalidDataException("整表未导入；请修正以下内容：\n" + string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? "\n另有 " + (errors.Count - 12) + " 行错误。" : ""));
            if (result.Count == 0) throw new InvalidDataException("表格中没有关卡数据。");
            return result.OrderBy(r => r.Order).ThenBy(r => r.RowNumber).ToList();
        }

        public static string NewId() => "Imported_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        /// <summary>Import only new copies. Catalog is committed once after all files have been written.</summary>
        public static List<SokobanLevelDescriptor> SaveNew(IReadOnlyList<SokobanExchangeRow> rows,
            string generatedRoot, SokobanLibraryStore library, IReadOnlyList<SokobanLevelDescriptor> existing)
        {
            if (rows == null || rows.Count == 0 || rows.Count > SokobanXlsx.MaxRows) throw new InvalidDataException("没有可导入的关卡。");
            var copies = rows.Select(row => new SokobanExchangeRow { Category = row.Category,
                Level = ParseImport(JsonUtility.ToJson(row.Level)) }).ToList();
            foreach (var row in copies) ValidateCategory(row.Category);
            library.Read();
            var createdFiles = new List<string>();
            var createdFolders = new List<string>();
            var imported = new List<SokobanLevelDescriptor>();
            try
            {
                Directory.CreateDirectory(generatedRoot);
                foreach (var row in copies)
                {
                    row.Level.levelId = NewId();
                    var folder = Path.Combine(generatedRoot, row.Level.levelId);
                    if (Directory.Exists(folder)) throw new IOException("新关卡目录已存在，请重新导入。");
                    Directory.CreateDirectory(folder); createdFolders.Add(folder);
                    var path = Path.Combine(folder, "level.json");
                    // CreateNew also guards against a concurrent file appearing.
                    using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write)))
                    { createdFiles.Add(path); writer.Write(JsonUtility.ToJson(row.Level, true)); }
                    imported.Add(new SokobanLevelDescriptor { LevelId = row.Level.levelId, Title = row.Level.name,
                        Folder = row.Level.levelId, Source = "Generated", Category = row.Category, FilePath = path });
                }
                library.AppendImported(existing, imported);
                return imported;
            }
            catch (Exception error)
            {
                var failures = new List<Exception> { error };
                foreach (var path in createdFiles)
                    try { if (File.Exists(path)) File.Delete(path); } catch (Exception rollback) { failures.Add(rollback); }
                foreach (var folder in createdFolders)
                    try { if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder); }
                    catch (Exception rollback) { failures.Add(rollback); }
                if (failures.Count > 1) throw new AggregateException("导入失败，部分新建文件清理失败，请查看日志。", failures);
                throw;
            }
        }

        private static int FindColumn(string[] header, params string[] aliases)
        {
            var found = header.Select((h, i) => new { h, i }).Where(x => aliases.Contains(x.h, StringComparer.OrdinalIgnoreCase)).ToList();
            if (found.Count > 1) throw new InvalidDataException("表头重复：" + aliases[0]);
            return found.Count == 0 ? -1 : found[0].i;
        }
        private static string Value(string[] row, int column) => column >= 0 && column < row.Length ? row[column] ?? "" : "";
        public static void ValidateCategory(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 40 || name.Any(char.IsControl) ||
                string.Equals(name, "All", StringComparison.OrdinalIgnoreCase) || name == "全部")
                throw new InvalidDataException("分类须为 1–40 个字符，不能使用“全部”或换行。");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Kuluobishi.Sokoban
{
    /// <summary>Small Open XML table adapter. No Office installation or runtime spreadsheet package required.</summary>
    public static class SokobanXlsx
    {
        public const int MaxRows = 5000;
        public const int MaxCellCharacters = 32767;
        private const long MaxXmlBytes = 48 * 1024 * 1024;
        private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static void Write(string path, IReadOnlyList<string[]> rows)
        {
            if (rows == null || rows.Count < 1 || rows.Count > MaxRows + 1)
                throw new InvalidDataException("表格须包含表头，最多导出 " + MaxRows + " 个关卡。");
            // Validate before opening the destination; a failure never truncates an existing workbook.
            foreach (var row in rows)
            {
                if (row == null || row.Length > 64) throw new InvalidDataException("表格列数无效。");
                foreach (var value in row)
                {
                    if ((value?.Length ?? 0) > MaxCellCharacters)
                        throw new InvalidDataException("单元格超过 Excel 的 32767 字符限制，请减少关卡备注等内容后导出。");
                    XmlConvert.VerifyXmlChars(value ?? "");
                }
            }
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    XNamespace content = "http://schemas.openxmlformats.org/package/2006/content-types";
                    Put(archive, "[Content_Types].xml", new XElement(content + "Types",
                        new XElement(content + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                        new XElement(content + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                        new XElement(content + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                        new XElement(content + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
                    Put(archive, "_rels/.rels", new XElement(PackageRel + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml")));
                    Put(archive, "xl/_rels/workbook.xml.rels", new XElement(PackageRel + "Relationships", Relationship("rId1", "worksheet", "worksheets/sheet1.xml")));
                    Put(archive, "xl/workbook.xml", new XElement(Sheet + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel),
                        new XElement(Sheet + "sheets", new XElement(Sheet + "sheet", new XAttribute("name", "关卡"), new XAttribute("sheetId", "1"), new XAttribute(Rel + "id", "rId1")))));
                    var data = new XElement(Sheet + "sheetData");
                    for (var r = 0; r < rows.Count; r++)
                    {
                        var row = new XElement(Sheet + "row", new XAttribute("r", r + 1));
                        for (var c = 0; c < rows[r].Length; c++)
                            row.Add(new XElement(Sheet + "c", new XAttribute("r", ColumnName(c) + (r + 1)), new XAttribute("t", "inlineStr"),
                                new XElement(Sheet + "is", new XElement(Sheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), rows[r][c] ?? ""))));
                        data.Add(row);
                    }
                    Put(archive, "xl/worksheets/sheet1.xml", new XElement(Sheet + "worksheet",
                        new XElement(Sheet + "sheetViews", new XElement(Sheet + "sheetView", new XAttribute("workbookViewId", "0"),
                            new XElement(Sheet + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                        new XElement(Sheet + "cols", new XElement(Sheet + "col", new XAttribute("min", "1"), new XAttribute("max", "64"), new XAttribute("width", "22"), new XAttribute("customWidth", "1"))),
                        data, new XElement(Sheet + "autoFilter", new XAttribute("ref", "A1:" + ColumnName(rows[0].Length - 1) + rows.Count))));
                }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public static List<string[]> Read(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("找不到 XLSX 文件。", path);
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("XLSX 文件过大，最多支持 32 MB。");
            using (var stream = File.OpenRead(path))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                if (archive.Entries.Count > 2048) throw new InvalidDataException("工作簿内容过多。");
                if (archive.Entries.Sum(e => e.Length) > 96 * 1024 * 1024) throw new InvalidDataException("工作簿解压后过大。");
                var workbook = Load(archive, "xl/workbook.xml");
                var first = workbook.Descendants(Sheet + "sheet").FirstOrDefault();
                if (first == null) throw new InvalidDataException("工作簿没有工作表。");
                var id = (string)first.Attribute(Rel + "id");
                var relations = Load(archive, "xl/_rels/workbook.xml.rels");
                var relation = relations.Root?.Elements(PackageRel + "Relationship").FirstOrDefault(e => (string)e.Attribute("Id") == id);
                if (relation == null || (string)relation.Attribute("TargetMode") == "External") throw new InvalidDataException("工作表引用无效。");
                var target = (string)relation.Attribute("Target") ?? "";
                var uri = new Uri(new Uri("http://xlsx.local/xl/workbook.xml"), target);
                if (uri.Host != "xlsx.local") throw new InvalidDataException("工作表路径无效。");
                var sheetPath = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
                var strings = new List<string>();
                if (archive.GetEntry("xl/sharedStrings.xml") != null)
                {
                    foreach (var item in Load(archive, "xl/sharedStrings.xml").Descendants(Sheet + "si"))
                        strings.Add(TextRuns(item));
                }
                var document = Load(archive, sheetPath);
                var result = new List<string[]>();
                var previous = 0;
                foreach (var row in document.Descendants(Sheet + "sheetData").Elements(Sheet + "row"))
                {
                    var numberText = (string)row.Attribute("r");
                    var number = numberText == null ? previous + 1 : ParseNumber(numberText, "行号");
                    if (number <= previous || number > MaxRows + 1) throw new InvalidDataException("行号无效或超过 " + MaxRows + " 个关卡。");
                    while (result.Count < number - 1) result.Add(Array.Empty<string>());
                    var cells = new Dictionary<int, string>();
                    var nextColumn = 0;
                    foreach (var cell in row.Elements(Sheet + "c"))
                    {
                        if (cell.Element(Sheet + "f") != null) throw new InvalidDataException("第 " + number + " 行包含公式，请粘贴为值后导入。");
                        var address = (string)cell.Attribute("r");
                        var column = address == null ? nextColumn : ColumnIndex(address, number);
                        if (column >= 64 || cells.ContainsKey(column)) throw new InvalidDataException("第 " + number + " 行列索引无效。");
                        var type = (string)cell.Attribute("t");
                        var value = (string)cell.Element(Sheet + "v") ?? "";
                        if (type == "inlineStr") value = TextRuns(cell.Element(Sheet + "is"));
                        else if (type == "s")
                        {
                            var index = ParseNumber(value, "共享字符串索引");
                            if (index < 0 || index >= strings.Count) throw new InvalidDataException("共享字符串索引无效。");
                            value = strings[index];
                        }
                        else if (type == "e") throw new InvalidDataException("第 " + number + " 行包含 Excel 错误值。");
                        if (value.Length > MaxCellCharacters) throw new InvalidDataException("单元格超过 Excel 字符限制。");
                        cells[column] = value; nextColumn = column + 1;
                    }
                    var values = new string[cells.Count == 0 ? 0 : cells.Keys.Max() + 1];
                    foreach (var cell in cells) values[cell.Key] = cell.Value;
                    result.Add(values); previous = number;
                }
                return result;
            }
        }

        private static XDocument Load(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException("工作簿缺少 " + name);
            if (entry.Length > MaxXmlBytes) throw new InvalidDataException("工作表过大。");
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null, MaxCharactersInDocument = MaxXmlBytes })) return XDocument.Load(reader);
        }

        private static void Put(ZipArchive archive, string name, XElement root)
        {
            using (var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open())
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) })) root.Save(writer);
        }

        private static XElement Relationship(string id, string type, string target) => new XElement(PackageRel + "Relationship",
            new XAttribute("Id", id), new XAttribute("Type", Rel.NamespaceName + "/" + type), new XAttribute("Target", target));
        private static string TextRuns(XElement parent) => parent == null ? "" : string.Concat(parent.Elements()
            .Where(e => e.Name == Sheet + "t" || e.Name == Sheet + "r").Select(e => e.Name == Sheet + "t" ? e.Value : string.Concat(e.Elements(Sheet + "t").Select(t => t.Value))));
        private static int ParseNumber(string value, string label)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) throw new InvalidDataException(label + "无效。");
            return number;
        }
        private static string ColumnName(int index)
        {
            var name = "";
            for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
            return name;
        }
        private static int ColumnIndex(string address, int row)
        {
            var index = 0; var i = 0;
            while (i < address.Length && address[i] >= 'A' && address[i] <= 'Z')
            { index = index * 26 + address[i++] - 'A' + 1; if (index > 64) throw new InvalidDataException("工作表最多支持 64 列。"); }
            if (i == 0 || ParseNumber(address.Substring(i), "单元格行号") != row) throw new InvalidDataException("单元格坐标无效。");
            return index - 1;
        }
    }
}

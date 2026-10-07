using System;
using System.IO;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    /// <summary>在主线程更新验证信息。版本比较排除验证产物，保留布局/名称等编辑内容。</summary>
    public static class SokobanVerificationMetadata
    {
        public static void Invalidate(SokobanJsonLevel data)
        {
            if (data == null) return;
            data.verifiedMoves = -1; data.solution = new SokobanJsonSolution();
            if (data.generation != null) { data.generation.complexity = null; data.generation.quality = null; }
            if (data.metadata != null) { data.metadata.parMoves = 0; data.metadata.parPushes = 0; }
        }

        public static void Apply(SokobanJsonLevel data, SokobanSolveResult result)
        {
            data.verifiedMoves = result.IsSolved ? result.Moves.Length : -1;
            data.solution = new SokobanJsonSolution { status = result.Status.ToString(),
                moves = result.IsSolved ? result.Moves : "", pushes = result.IsSolved ? result.Pushes : 0,
                moveCount = result.IsSolved ? result.Moves.Length : 0 };
            if (data.metadata != null)
            { data.metadata.parMoves = result.IsSolved ? result.Moves.Length : 0; data.metadata.parPushes = result.IsSolved ? result.Pushes : 0; }
        }

        public static string Revision(SokobanJsonLevel data)
        {
            var copy = SokobanLevelRepository.Parse(JsonUtility.ToJson(data));
            copy.verifiedMoves = -1; copy.solution = new SokobanJsonSolution();
            if (copy.metadata != null) { copy.metadata.parMoves = 0; copy.metadata.parPushes = 0; }
            return JsonUtility.ToJson(copy);
        }

        public static bool TryPersist(SokobanLevelDescriptor descriptor, string originalJson, SokobanSolveResult result,
            out SokobanJsonLevel updated, out string message)
        {
            updated = null;
            try
            {
                if (string.IsNullOrWhiteSpace(descriptor.FilePath)) throw new IOException("关卡没有可写入的本地文件，结果已保留。");
                var path = Path.GetFullPath(descriptor.FilePath);
                if (!File.Exists(path)) { message = "关卡已删除，结果未写入"; return false; }
                if (File.ReadAllText(path) != originalJson) { message = "关卡已改动，结果未写入，请重新验证"; return false; }
                var data = SokobanLevelRepository.Parse(originalJson);
                if (data.levelId != descriptor.LevelId) throw new IOException("文件 ID 与所选关卡不一致，结果未写入。");
                Apply(data, result);
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                    if (File.ReadAllText(path) != originalJson) { message = "关卡已改动，结果未写入，请重新验证"; return false; }
                    File.Replace(temporary, path, null);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                updated = data; message = "验证结果已保存"; return true;
            }
            catch (Exception error) { message = "写入失败：" + error.Message; return false; }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>无需 MCP 的可复现检查/构建入口，支持菜单和 Unity 批处理。</summary>
public static class SokobanProjectChecks
{
    [MenuItem("Sokoban/Run All Checks")]
    public static void RunMenu() { Debug.Log(RunAll()); }
    public static string RunAll()
    {
        var report = new StringBuilder();
        report.AppendLine(SokobanSolverChecks.Run());
        report.AppendLine(SokobanBoardViewChecks.Run());
        report.AppendLine(SokobanLibraryChecks.Run());
        report.AppendLine(SokobanExchangeChecks.Run());
        report.AppendLine(SokobanGenerationChecks.Run());
        report.AppendLine(SokobanIncrementalGenerationChecks.Run());
        report.AppendLine(SokobanAutomaticGenerationChecks.Run());
        report.AppendLine(SokobanBatchGenerationChecks.Run());
        report.AppendLine(SokobanBatchVerificationChecks.Run());
        report.AppendLine(SokobanProductChecks.Run());
        report.AppendLine(SokobanStampChecks.Run());
        report.AppendLine(SokobanUnlockChecks.Run());
        return report.ToString();
    }
    public static void RunBatch()
    {
        try { Debug.Log(RunAll()); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    public static void BuildWindows()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "-sokobanBuildPath");
        var destination = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : "Builds/Sokoban/Sokoban.exe";
        var development = arguments.Contains("-sokobanDevelopment");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length != 4 || !scenes[0].EndsWith("start.unity")) throw new Exception("Expected start, level, game and editor build scenes.");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = destination,
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode | (development ? BuildOptions.Development : BuildOptions.None) });
        Debug.Log($"SOKOBAN_BUILD: {report.summary.result}; errors={report.summary.totalErrors}; warnings={report.summary.totalWarnings}; bytes={report.summary.totalSize}");
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Windows build failed: " + report.summary.result);
    }
}

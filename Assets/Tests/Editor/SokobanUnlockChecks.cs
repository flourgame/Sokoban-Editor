using System;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using UnityEngine;

public static class SokobanUnlockChecks
{
    private static int checks;
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception("Unlock regression: " + reason); checks++; }
    public static string Run()
    {
        checks = 0;
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Codex/unlock-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "progress.json");
        var ids = new[] { "A", "B", "C" }; var store = new SokobanProgressStore(path);
        var a = SokobanSolverChecks.Fixture("#######", "#@ $ .#", "#     #", "#######"); a.levelId = "A";
        var b = SokobanLevelRepository.Parse(JsonUtility.ToJson(a)); b.levelId = "B";
        Check(store.IsUnlocked("A", ids) && !store.IsUnlocked("B", ids) && !store.IsUnlocked("C", ids), "fresh campaign starts with only the first level");
        Check(!store.IsUnlocked("Generated", ids) && !store.IsUnlocked(null, ids) && !store.IsUnlocked("A", Array.Empty<string>()), "unknown levels and empty campaigns do not bypass locks");
        Check(store.RecordWin(a, 5, 2, campaign: ids) && store.IsUnlocked("B", ids) && !store.IsUnlocked("C", ids), "normal victory unlocks only the successor");
        Check(File.ReadAllText(path).Contains("unlockedLevelIds") && new SokobanProgressStore(path).IsUnlocked("B", ids), "unlocked IDs persist atomically");
        Check(store.Find(b) == null, "unlock does not manufacture a completed score");
        var before = File.ReadAllText(path);
        Check(!store.RecordWin(b, 0, 0, true, false, ids) && !store.RecordWin(b, 0, 0, false, true, ids) && !store.IsUnlocked("C", ids) && File.ReadAllText(path) == before, "GM and preview victories cannot advance the campaign");
        Check(store.RecordWin(b, 8, 3, campaign: ids) && new SokobanProgressStore(path).IsUnlocked("C", ids), "second victory unlocks the final level");
        Check(store.UnlockAll(ids.Concat(new[] { "Generated", "A" })) && store.IsUnlocked("Generated", ids), "GM unlock all includes custom levels without duplicate IDs");
        Check(store.Find(a).bestMoves == 5 && store.Find(b).bestMoves == 8, "unlock all preserves actual scores");
        Check(store.Clear() && new SokobanProgressStore(path).IsUnlocked("A", ids) && !store.IsUnlocked("B", ids) && store.Find(a) == null && !store.IsUnlocked("Generated", ids), "clear resets scores and unlocks together across reload");
        Check(store.RecordWin(a, 7, 2, campaign: ids) && store.IsUnlocked("B", ids), "campaign works again after clearing");
        var legacyPath = Path.Combine(root, "legacy.json");
        File.WriteAllText(legacyPath, "{\"schemaVersion\":1,\"entries\":[" + JsonUtility.ToJson(store.Find(a)) + "]}");
        var legacy = new SokobanProgressStore(legacyPath);
        Check(legacy.IsUnlocked("A", ids) && legacy.IsUnlocked("B", ids) && !legacy.IsUnlocked("C", ids) && legacy.Find(a).bestMoves == 7 && legacy.LastError == null, "legacy scores keep completed levels and next-level access");
        Check(legacy.UnlockAll(ids) && new SokobanProgressStore(legacyPath).IsUnlocked("C", ids), "legacy file gains explicit unlock data on the next save");
        var failed = new SokobanProgressStore(root);
        Check(!failed.UnlockAll(ids) && !failed.IsUnlocked("B", ids) && failed.LastError != null, "failed unlock save does not publish in-memory unlocks");
        Check(!failed.RecordWin(a, 5, 2, campaign: ids) && failed.Find(a) == null, "failed score save does not advance memory state");
        var clearPath = Path.Combine(root, "clear-failure.json"); var clearStore = new SokobanProgressStore(clearPath);
        Check(clearStore.UnlockAll(ids), "clear-failure fixture is unlocked");
        File.Delete(clearPath); Directory.CreateDirectory(clearPath);
        Check(!clearStore.Clear() && clearStore.IsUnlocked("C", ids), "failed clear preserves previously loaded progress");
        return "PASS: " + checks + " unlock assertions (progression, persistence, legacy migration, GM isolation, full unlock, clear and failed-save rollback).";
    }
}

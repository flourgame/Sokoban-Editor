using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;
using UnityEngine.UI;

public static class SokobanAutomaticGenerationChecks
{
    private static int checks;
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Require(bool condition, string message)
    { checks++; if (!condition) throw new Exception("Automatic generation: " + message); }
    private static string Key(SokobanSolverLevel level) => level.Width + ":" + level.Height + ":" + level.Player + ":" +
        string.Join(",", level.Walls) + ":" + string.Join(",", level.Boxes) + ":" + string.Join(",", level.Goals);
    private static int Walls(SokobanSolverLevel level)
    {
        var count = 0;
        for (var y = 1; y < level.Height - 1; y++)
        for (var x = 1; x < level.Width - 1; x++) if (level.Walls[y * level.Width + x]) count++;
        return count;
    }
    private static SokobanJsonLevel Json(SokobanGenerationResult result) => (SokobanJsonLevel)typeof(SokobanEditorSceneController)
        .GetMethod("CreateGeneratedJson", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { result });
    private static void CheckResult(SokobanGenerationResult result, SokobanGenerationSettings request)
    {
        Require(result.Status == SokobanGenerationStatus.Success, $"mode succeeds ({request.width}x{request.height}, seed {request.seed}, difficulty {request.difficulty}, boxes {request.boxCount}, walls {request.wallPercent}): " + result.Message);
        var actualBoxes = result.Level.Boxes.Length; var actualWalls = Walls(result.Level);
        Require(actualBoxes >= 1 && actualBoxes <= request.MaximumBoxes && actualBoxes == result.Level.Goals.Length, "box/goal range");
        Require(actualWalls >= 0 && actualWalls <= request.MaximumWalls && request.InteriorCells - actualWalls >= actualBoxes * 2 + 1,
            "wall cap and sufficient floor");
        Require(request.AutomaticBoxes || actualBoxes == request.boxCount, "numeric box count is exact");
        Require(request.AutomaticWalls || actualWalls == request.WallCount, "numeric wall ratio is exact");
        Require(result.Complexity.valid && (request.AnyDifficulty || result.Complexity.difficulty == request.difficulty) &&
            result.Solution.Pushes >= request.minPushes && result.Solution.Pushes <= request.maxPushes, "difficulty and pushes stay strict");
        var json = Json(result);
        Require(SokobanValidation.Validate(json).Count == 0, "saved board is legal");
        var roundtrip = SokobanLevelRepository.Parse(JsonUtility.ToJson(json));
        Require(roundtrip.generation.parameters.boxCount == request.boxCount && roundtrip.generation.parameters.wallPercent == request.wallPercent &&
            roundtrip.boxes.Length == actualBoxes, "JSON retains requested automatic/exact policy and actual board");
        Require(roundtrip.generation.generatorVersion == SokobanGenerationResult.Version &&
            !JsonUtility.ToJson(json).Contains("referenceRepository"), "version retained without reference fields");
        var replay = new SokobanSolutionPlayback(roundtrip, result.Solution);
        while (replay.Next()) { }
        Require(replay.State.IsWon, "complete solution wins");
    }

    public static string Run()
    {
        checks = 0;
        var defaults = new SokobanGenerationSettings();
        Require(defaults.AutomaticBoxes && defaults.AutomaticWalls && defaults.Validate() == "", "defaults are automatic");
        Require(SokobanGenerationSettings.TryParseStructure("", "  ", out var boxes, out var walls, out var error) && boxes == 0 && walls == -1,
            "blank and whitespace mean automatic");
        Require(SokobanGenerationSettings.TryParseStructure("2", "0", out boxes, out walls, out error) && boxes == 2 && walls == 0,
            "explicit zero means zero walls");
        Require(SokobanGenerationSettings.TryParseStructure("8", "45", out boxes, out walls, out error) && boxes == 8 && walls == 45, "numeric limits parse");
        foreach (var value in new[] { "0", "-1", "9", "2.5", "bad", "999999999999999" })
            Require(!SokobanGenerationSettings.TryParseStructure(value, "", out boxes, out walls, out error), "bad box input is rejected");
        foreach (var value in new[] { "-1", "46", "0.5", "bad", "999999999999999" })
            Require(!SokobanGenerationSettings.TryParseStructure("", value, out boxes, out walls, out error), "bad wall input is rejected");
        var legacy = JsonUtility.FromJson<SokobanGenerationSettings>("{\"width\":8,\"height\":8,\"boxCount\":2,\"wallPercent\":20}");
        Require(!legacy.AutomaticBoxes && !legacy.AutomaticWalls && legacy.WallCount == 7, "legacy numeric JSON stays exact");
        var oldRange = JsonUtility.FromJson<SokobanGenerationSettings>("{\"minMoves\":20,\"maxMoves\":60,\"boxCount\":2,\"wallPercent\":20}");
        Require(oldRange.minMoves == 20 && oldRange.maxMoves == 60 && oldRange.Validate().Contains("旧参数"),
            "old movement bounds retain their meaning and cannot silently become push bounds");
        var oldRoundtrip = JsonUtility.FromJson<SokobanGenerationSettings>(JsonUtility.ToJson(oldRange));
        Require(oldRoundtrip.minMoves == 20 && oldRoundtrip.maxMoves == 60, "legacy range provenance survives a save");
        Require(new SokobanGenerationSettings { boxCount = -1 }.Validate() != "" &&
            new SokobanGenerationSettings { wallPercent = -2 }.Validate() != "", "invalid internal sentinels rejected");
        Require(new SokobanGenerationSettings { width = 5, height = 5, boxCount = 8 }.Validate() != "", "fixed boxes must fit automatic walls");
        foreach (var width in new[] { 5, 6, 7, 8 })
        foreach (var height in new[] { 5, 6, 7, 8 })
        {
            var range = new SokobanGenerationSettings { width = width, height = height };
            Require(range.MaximumBoxes >= 1 && range.MaximumBoxes <= 8 && range.MaximumWalls >= 0 &&
                range.MaximumWalls <= range.InteriorCells - 3 && range.Validate() == "", "supported rectangular sizes retain bounded spatial caps");
        }
        foreach (var size in new[] { 4, 9, 20 })
        {
            Require(SokobanGenerator.Generate(new SokobanGenerationSettings { width = size }).Status == SokobanGenerationStatus.Invalid,
                "unsupported width is rejected by the shared generator");
            Require(SokobanBatchGenerator.Validate(new SokobanGenerationSettings { height = size }, 1) != "",
                "unsupported height is rejected by batch validation");
        }
        Require(new SokobanGenerationSettings { difficulty = 0 }.Validate() == "" &&
            new SokobanGenerationSettings { difficulty = 0 }.DifficultyLabel == "不限", "unlimited complexity has explicit value and label");
        Require(new SokobanGenerationSettings { difficulty = -1 }.Validate() != "" &&
            new SokobanGenerationSettings { difficulty = 4 }.Validate() != "", "invalid complexity targets remain rejected");
        var output = new StringBuilder(); var normal = new List<SokobanGenerationResult>();
        foreach (var difficulty in new[] { 1, 2, 3 })
        for (var mode = 0; mode < 4; mode++)
        {
            var request = new SokobanGenerationSettings { width = 6, height = 6,
                seed = mode == 3 ? (difficulty == 1 ? 42 : difficulty == 2 ? 4 : 20261005) : mode == 2 ? (difficulty == 1 ? 42 : difficulty == 2 ? 17 : 20261005) : mode == 1 && difficulty == 1 ? 42 : 20261005,
                difficulty = difficulty, budgetSeconds = 10, maxCandidates = 140,
                boxCount = mode == 1 || mode == 3 ? 2 : 0, wallPercent = mode == 2 || mode == 3 ? 20 : -1 };
            var original = JsonUtility.ToJson(request); var result = SokobanGenerator.Generate(request);
            CheckResult(result, request);
            Require(JsonUtility.ToJson(request) == original && JsonUtility.ToJson(result.Settings) == original, "caller request preserved");
            output.AppendLine($"{SokobanDifficultyEvaluator.Name(difficulty)} / mode {mode}: {result.Level.Boxes.Length} boxes, {Walls(result.Level)} walls, {result.Solution.Moves.Length} moves, score {result.Complexity.score:0.0}");
            if (difficulty == 2) normal.Add(result);
        }
        for (var mode = 0; mode < normal.Count; mode++)
        {
            var single = normal[mode]; var batch = SokobanBatchGenerator.Generate(single.Settings, 1);
            Require(batch.Items.Count == 1 && batch.Items[0].Status == SokobanGenerationStatus.Success &&
                Key(single.Level) == Key(batch.Items[0].Level) && single.Solution.Moves == batch.Items[0].Solution.Moves &&
                single.Complexity.score == batch.Items[0].Complexity.score && single.Quality.score == batch.Items[0].Quality.score,
                "single and batch first slot agree in every optional mode");
        }
        var automaticBatch = SokobanBatchGenerator.Generate(normal[0].Settings, 3); var layouts = new HashSet<string>();
        Require(automaticBatch.Items.Count == 3 && !automaticBatch.Cancelled, "automatic batch completes");
        foreach (var result in automaticBatch.Items)
        {
            if (result.Status == SokobanGenerationStatus.Success)
            { CheckResult(result, result.Settings); Require(layouts.Add(Key(result.Level)), "automatic batch deduplicates"); }
            else Require(result.Level == null && (result.Status == SokobanGenerationStatus.Exhausted || result.Status == SokobanGenerationStatus.TimedOut),
                "strict source generation reports a failed slot without an invalid layout");
        }
        Require(layouts.Count > 0, "automatic batch includes verified results");
        var repeated = SokobanGenerator.Generate(normal[0].Settings);
        Require(Key(repeated.Level) == Key(normal[0].Level) && repeated.Solution.Moves == normal[0].Solution.Moves, "automatic seed is reproducible");
        for (var mode = 0; mode < 4; mode++)
        {
            var unlimited = normal[mode].Settings.Copy(); unlimited.difficulty = 0;
            var single = SokobanGenerator.Generate(unlimited); CheckResult(single, unlimited);
            Require(single.Statistics.difficultyRejected == 0 && single.Complexity.difficulty >= 1 && single.Complexity.difficulty <= 3,
                "unlimited skips the target filter but still evaluates the actual complexity");
            var batch = SokobanBatchGenerator.Generate(unlimited, 1);
            Require(batch.Items.Count == 1 && batch.Items[0].Status == SokobanGenerationStatus.Success &&
                Key(batch.Items[0].Level) == Key(single.Level) && batch.Items[0].Solution.Moves == single.Solution.Moves,
                "unlimited single and batch first slot agree");
            var roundtrip = SokobanLevelRepository.Parse(JsonUtility.ToJson(Json(single)));
            Require(roundtrip.generation.parameters.AnyDifficulty && roundtrip.metadata.difficulty == single.Complexity.difficulty,
                "JSON records unrestricted request separately from the real generated difficulty");
        }
        CheckForms();
        output.AppendLine("PASS: " + checks + " automatic checks (all optional modes, bounds, exact zero, legacy JSON, replay, form defaults/restoration and single/batch agreement).");
        return output.ToString();
    }

    private static void CheckForms()
    {
        // Inactive, unsaved objects avoid controller Awake, session loading and visible UI changes.
        var root = new GameObject("AutomaticGenerationFormChecks", typeof(RectTransform));
        root.hideFlags = HideFlags.HideAndDontSave; root.SetActive(false);
        var serviceRoot = new GameObject("AutomaticGenerationServiceChecks");
        serviceRoot.hideFlags = HideFlags.HideAndDontSave; serviceRoot.SetActive(false);
        var controller = root.AddComponent<SokobanEditorSceneController>(); var type = controller.GetType();
        typeof(SokobanSceneController).GetField("Root", PrivateInstance).SetValue(controller, root.GetComponent<RectTransform>());
        root.GetComponent<RectTransform>().sizeDelta = new Vector2(1920, 1080);
        var instance = typeof(SokobanBatchGenerationService).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static);
        var originalService = instance.GetValue(null); var service = serviceRoot.AddComponent<SokobanBatchGenerationService>();
        instance.SetValue(null, service);
        var history = (List<string>)type.GetField("operationHistory", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var originalHistory = history.ToArray();
        var open = type.GetMethod("OpenGenerationPanel", PrivateInstance);
        var read = type.GetMethod("TryReadGenerationSettings", PrivateInstance);
        try
        {
            foreach (var batch in new[] { false, true })
            {
                open.Invoke(controller, new object[] { batch });
                var fields = (Dictionary<string, InputField>)type.GetField("generationFields", PrivateInstance).GetValue(controller);
                Require(fields["MinPushes"].text == "1" && fields["MaxPushes"].text == "60" && fields["Candidates"].text == "400", "both forms use new push defaults");
                Require(fields["Boxes"].text == "" && fields["Walls"].text == "" &&
                    ((Text)fields["Boxes"].placeholder).text == "自动决定" && ((Text)fields["Walls"].placeholder).text == "自动决定", "both forms default to blank automatic fields");
                fields["Seed"].text = "42";
                var args = new object[] { null };
                Require((bool)read.Invoke(controller, args) && ((SokobanGenerationSettings)args[0]).AutomaticBoxes &&
                    ((SokobanGenerationSettings)args[0]).AutomaticWalls, "both forms read automatic settings");
                var dropdown = (Dropdown)type.GetField("generationDifficulty", PrivateInstance).GetValue(controller);
                Require(dropdown.options.Count == 4 && dropdown.options[0].text == "不限" && dropdown.options[1].text == "简单" &&
                    dropdown.options[2].text == "普通" && dropdown.options[3].text == "困难", "real dropdown offers all four choices");
                foreach (var difficulty in new[] { 0, 1, 2, 3 })
                {
                    dropdown.value = difficulty;
                    Require((bool)read.Invoke(controller, args) && ((SokobanGenerationSettings)args[0]).difficulty == difficulty &&
                        dropdown.captionText.text == dropdown.options[difficulty].text, "selection updates the shared settings and visible caption");
                }
                fields["Width"].text = "5"; fields["Height"].text = "8";
                Require((bool)read.Invoke(controller, args), "form accepts both size endpoints on a rectangle");
                fields["Width"].text = "9";
                Require(!(bool)read.Invoke(controller, args), "form rejects a width above eight");
                fields["Width"].text = "8"; fields["Height"].text = "4";
                Require(!(bool)read.Invoke(controller, args), "form rejects a height below five");
                fields["Height"].text = "8";
                var help = Array.Find(root.GetComponentsInChildren<Text>(true), t => t.name == "GenerationHelp");
                Require(help.text.Contains("5–8") && !help.text.Contains("单关与批量共用生成逻辑，失败不放宽条件。"),
                    "both forms show new size limits and omit the removed sentence");
                fields["Boxes"].text = "2"; fields["Walls"].text = "0";
                Require((bool)read.Invoke(controller, args) && ((SokobanGenerationSettings)args[0]).boxCount == 2 &&
                    ((SokobanGenerationSettings)args[0]).wallPercent == 0, "both forms read exact zero walls");
                fields["Boxes"].text = "0";
                Require(!(bool)read.Invoke(controller, args), "typed zero boxes is rejected by the form");
                foreach (var name in new[] { "GenerationLabelBoxes", "GenerationLabelWalls", "GenerationLabelMinPushes", "GenerationLabelMaxPushes", "GenerationHelp" })
                {
                    var label = Array.Find(root.GetComponentsInChildren<Text>(true), t => t.name == name);
                    var height = label.cachedTextGeneratorForLayout.GetPreferredHeight(label.text, label.GetGenerationSettings(label.rectTransform.rect.size));
                    Require(height <= label.rectTransform.rect.height + 1, "new form text fits: " + name);
                }
                CheckDropdownTemplate(dropdown);
                var overlay = (RectTransform)type.GetField("generationOverlay", PrivateInstance).GetValue(controller);
                UnityEngine.Object.DestroyImmediate(overlay.gameObject);
                type.GetField("generationOverlay", PrivateInstance).SetValue(controller, null);
            }
            foreach (var policy in new[] { new SokobanGenerationSettings { difficulty = 0 }, new SokobanGenerationSettings { boxCount = 2 },
                new SokobanGenerationSettings { wallPercent = 0 }, new SokobanGenerationSettings { boxCount = 2, wallPercent = 20, difficulty = 3 } })
            {
                typeof(SokobanBatchGenerationService).GetProperty("Settings").SetValue(service, policy);
                open.Invoke(controller, new object[] { true });
                var restored = (Dictionary<string, InputField>)type.GetField("generationFields", PrivateInstance).GetValue(controller);
                Require(restored["Boxes"].text == (policy.AutomaticBoxes ? "" : policy.boxCount.ToString()) &&
                    restored["Walls"].text == (policy.AutomaticWalls ? "" : policy.wallPercent.ToString()), "reopened batch restores automatic, mixed and exact policies");
                var dropdown = (Dropdown)type.GetField("generationDifficulty", PrivateInstance).GetValue(controller);
                Require(dropdown.value == policy.difficulty && dropdown.captionText.text == policy.DifficultyLabel,
                    "batch reopens with the saved complexity choice, including unlimited");
                var overlay = (RectTransform)type.GetField("generationOverlay", PrivateInstance).GetValue(controller);
                UnityEngine.Object.DestroyImmediate(overlay.gameObject);
                type.GetField("generationOverlay", PrivateInstance).SetValue(controller, null);
            }
        }
        finally
        {
            instance.SetValue(null, originalService); history.Clear(); history.AddRange(originalHistory);
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(serviceRoot);
        }
    }

    private static void CheckDropdownTemplate(Dropdown dropdown)
    {
        var previousParent = dropdown.transform.parent;
        var canvasRoot = new GameObject("GenerationDropdownTemplateChecks", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        canvasRoot.hideFlags = HideFlags.HideAndDontSave;
        var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        try
        {
            // Activate only the isolated control, leaving the editor controller and session inactive.
            dropdown.transform.SetParent(canvasRoot.transform, false);
            typeof(Dropdown).GetMethod("SetupTemplate", PrivateInstance).Invoke(dropdown, new object[] { canvas });
            Require((bool)typeof(Dropdown).GetField("validTemplate", PrivateInstance).GetValue(dropdown),
                "Unity accepts the real popup template");
            Require(!dropdown.template.gameObject.activeSelf && dropdown.template.GetComponent<Canvas>().overrideSorting &&
                dropdown.template.GetComponent<GraphicRaycaster>() != null, "popup opens above the form and accepts pointer events");
            var toggle = dropdown.template.GetComponentInChildren<Toggle>(true);
            Require(toggle != null && dropdown.itemText.transform.IsChildOf(toggle.transform) && toggle.graphic != null,
                "popup item has its real label, toggle and selection indicator");
        }
        finally
        {
            dropdown.transform.SetParent(previousParent, false);
            UnityEngine.Object.DestroyImmediate(canvasRoot);
        }
    }
}

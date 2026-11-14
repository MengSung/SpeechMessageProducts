/*
檔案整體說明

目的與定位：提供 P5-02b 可獨立呼叫的只編譯矩陣 API，將核定舊根的三十六個第一層專案與二十九個實際入口明確列名；Dynamics365Line 零來源方案殼另列排除，不冒充入口或編譯成功。

主要功能與流程：先驗證根路徑、第一層專案集合與方案殼，再逐入口呼叫 LegacyProjectCompiler.Compile。每次 using 結束立即清零編譯位元組，只留下固定診斷、核定型別名與狀態；七個程式庫只列去向，不編譯成虛構入口。回傳 JSON 由呼叫端掃描及發布，本檔不寫檔、不載入產物、不建立 AppDomain，也不執行任何舊入口。

使用邊界與維護：只接受核定外部舊根，不接受任意來源位置；來源讀取與網路 API 拒絕沿用正式 compiler。沒有跨呼叫可變結果、憑證、背景工作或快取；所有候選屬單次同步操作，來源及 metadata 生命週期由 compiler 管理。集合拒絕時仍回完整失敗矩陣，不發布部分成功；Generated 僅表示編譯成功。入口型別只核對 compiler 的第一個入口，不能證明每個來源沒有其他入口。維護須核對精確名單、三態、殼排除及失敗時不回顯路徑或來源。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LegacyGolden.PluginHarness;

/// <summary>
/// 核定三十六專案／二十九入口的獨立只編譯入口；回傳完全分離的 JSON 候選，
/// 呼叫端負責安全掃描、序列化與發布，不能把 Generated 當作執行或實機證據。
/// </summary>
public static class CompileMatrix
{
    private const string ApprovedRoot = @"D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.其他解決方案\Dynamics365Line";
    private static readonly string[] Libraries =
    {
        "AppointmentSigningLibrary", "Line.Messaging", "LineNotifyLibrary", "LineUtility",
        "StorLessonSigningLibrary", "ToolUtility", "Trace"
    };

    // 每列是專案、實際完整型別、實際基底種類；刻意保留不同專案中的同名入口。
    private static readonly string[][] Entries =
    {
        new[] { "AllotPlugIn", "AllotPlugIn.AllotPlugIn", "IPlugin" },
        new[] { "AllotTlsWorkflow", "AllotTlsWorkflow.AllotTlsWorkflowClass", "CodeActivity" },
        new[] { "AllotWorkflow", "AllotWorkflow.AllotWorkflowClass", "CodeActivity" },
        new[] { "AppointmentPlugIn", "AppointmentPlugIn.AppointmentPlugIn", "IPlugin" },
        new[] { "AppointmentSigning", "AppointmentSigning.AppointmentSigningWorkflow", "CodeActivity" },
        new[] { "AppointmentWorkflow", "AppointmentWorkflow.AppointmentWorkflowPlugin", "CodeActivity" },
        new[] { "CalculateQrCodeScanNumberWorkflow", "CalculateQrCodeScanNumberWorkflow.CalculateQrCodeScanNumberWorkflow", "CodeActivity" },
        new[] { "DiscipleLessonsPluIn", "DiscipleLessonsPluIn.DiscipleLessonsPluIn", "IPlugin" },
        new[] { "EquipmentWorkflow", "EquipmentWorkflow.EquipmentWorkflowClass", "CodeActivity" },
        new[] { "ExampleWorkflow", "ExampleWorkflow.ExampleWorkflowClass", "CodeActivity" },
        new[] { "FeePushLineWorkflow", "FeePushLineWorkflow.FeePushLineWorkflowClass", "CodeActivity" },
        new[] { "HappyBirthdayWorkflow", "HappyBirthdayWorkflowNamespace.HappyBirthdayWorkflowPlugIn", "CodeActivity" },
        new[] { "LineMessagePlugIn", "LineMessagePlugIn.LineMessagePlugIn", "IPlugin" },
        new[] { "NewPersonCareWorkflow", "NewPersonCareWorkflow.NewPersonCareWorkflowClass", "CodeActivity" },
        new[] { "NewPersonCareWorkflow-ForDebug", "NewPersonCareWorkflow.NewPersonCareWorkflowClass", "CodeActivity" },
        new[] { "PersonalQrCodeWorkflow", "PersonalQrCodeWorkflow.PersonalQrCodeWorkflowClass", "CodeActivity" },
        new[] { "PresentRecordLinePlugIn", "PresentRecordLinePlugIn.PresentRecordLinePlugIn", "IPlugin" },
        new[] { "QrCodeWorkflow", "QrCodeWorkflow.QrCodeWorkflow", "CodeActivity" },
        new[] { "RecursiveWorkflow", "RecursiveWorkflowNameSpace.RecursiveWorkflowPlugIn", "CodeActivity" },
        new[] { "StorLessonSigningWorkflow", "StorLessonSigningWorkflow.StorLessonWorkflowPlugin", "CodeActivity" },
        new[] { "StorLessonSignningPlugIn", "StorLessonSignningPlugIn.StorLessonSignningPlugIn", "IPlugin" },
        new[] { "SundayQrCodeWorkflow", "SundayQrCodeWorkflow.QrCodeWorkflow", "CodeActivity" },
        new[] { "TemplatePlugIn", "TemplatePlugIn.TemplatePlugIn", "IPlugin" },
        new[] { "TwoWingsPlugIn", "TwoWingsPlugIn.TwoWingsPlugIn", "IPlugin" },
        new[] { "WeeklyReportLinePlugIn", "WeeklyReportLinePlugIn.WeeklyReportLinePlugIn", "IPlugin" },
        new[] { "WeeklyReportPlugIn", "WeeklyReportPlugIn.WeeklyReportPlugIn", "IPlugin" },
        new[] { "WeeklyReportWorkflow", "WeeklyReportWorkflowNameSpace.WeeklyReportWorkflow", "CodeActivity" },
        new[] { "WorkflowTemplate", "WorkflowTemplate.WorkflowTemplatePlugin", "CodeActivity" },
        new[] { "YangMeillcStorLessonsPlugIn", "YangMeillcStorLessonsPlugIn.YangMeillcStorLessonsPlugIn", "IPlugin" }
    };

    /// <summary>
    /// 取得新的核定清單候選；不用舊磁碟、保留專案與型別的複合身份。
    /// 呼叫端可自由修改回傳值，不能改動後續呼叫或內部清單。
    /// </summary>
    public static JObject Manifest()
    {
        var projects = Entries.Select(entry => entry[0]).Concat(Libraries).OrderBy(name => name, StringComparer.Ordinal);
        return new JObject
        {
            ["projects"] = new JArray(projects),
            ["entrypoints"] = new JArray(Entries.Select(entry => new JObject
            {
                ["project"] = entry[0], ["entrypointExpected"] = entry[1], ["kind"] = entry[2]
            })),
            ["excludedShell"] = new JObject
            {
                ["project"] = "Dynamics365Line", ["reason"] = "ExcludedShellNoSource"
            }
        };
    }

    /// <summary>
    /// 驗證核定根的完整集合後同步逐專案編譯；每份產物於本列完成時 Dispose，
    /// 不載入 DLL、不呼叫 Execute、不回傳組件位元組或未核定的型別字串。
    /// 集合／路徑失敗回二十九筆 CompileFailed 與固定診斷，呼叫端應檢查 inventoryValid。
    /// </summary>
    public static JObject Create(string legacyRoot)
    {
        var inventoryIssue = ValidateInventory(legacyRoot);
        var rows = new JArray();
        foreach (var entry in Entries)
        {
            var row = new JObject
            {
                ["project"] = entry[0],
                ["projectFile"] = entry[0] + "/" + entry[0] + ".csproj",
                ["entrypointExpected"] = entry[1],
                ["kind"] = entry[2],
                ["entryTypeActual"] = null,
                ["status"] = "CompileFailed",
                ["diagnostics"] = new JArray()
            };
            if (inventoryIssue != null)
            {
                row["diagnostics"] = new JArray(inventoryIssue);
            }
            else
            {
                using var compiled = LegacyProjectCompiler.Compile(legacyRoot, entry[0]);
                row["status"] = compiled.Status;
                row["diagnostics"] = new JArray(compiled.Diagnostics);
                if (compiled.Status == "Generated")
                {
                    if (string.Equals(compiled.EntryType, entry[1], StringComparison.Ordinal))
                    {
                        row["entryTypeActual"] = entry[1];
                    }
                    else
                    {
                        row["status"] = "CompileFailed";
                        row["diagnostics"] = new JArray("EntryTypeMismatch");
                    }
                }
            }

            rows.Add(row);
        }

        var manifest = Manifest();
        return new JObject
        {
            ["schemaVersion"] = 1,
            ["mode"] = "compile-only",
            ["generatedMeaning"] = "CompilationSucceededOnly",
            ["sourceRootId"] = "ApprovedDynamics365Line",
            ["runUtc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["inventoryValid"] = inventoryIssue == null,
            ["diagnostics"] = inventoryIssue == null ? new JArray() : new JArray(inventoryIssue),
            ["expectedCounts"] = new JObject { ["projects"] = 36, ["entrypoints"] = 29, ["plugins"] = 11, ["activities"] = 18 },
            ["manifestCounts"] = new JObject
            {
                ["projects"] = ((JArray)manifest["projects"]!).Count,
                ["entrypoints"] = Entries.Length,
                ["plugins"] = Entries.Count(entry => entry[2] == "IPlugin"),
                ["activities"] = Entries.Count(entry => entry[2] == "CodeActivity")
            },
            ["projects"] = new JArray(((JArray)manifest["projects"]!).Select(name => new JObject
            {
                ["project"] = name.Value<string>(),
                ["disposition"] = Libraries.Contains(name.Value<string>(), StringComparer.Ordinal) ? "NonEntryLibrary" : "EntryProject"
            })),
            ["excludedShell"] = manifest["excludedShell"],
            ["entrypoints"] = rows,
            ["statusCounts"] = new JObject
            {
                ["Generated"] = rows.Count(row => (string?)row["status"] == "Generated"),
                ["CompileFailed"] = rows.Count(row => (string?)row["status"] == "CompileFailed"),
                ["BlockedByNetworkApi"] = rows.Count(row => (string?)row["status"] == "BlockedByNetworkApi")
            }
        };
    }

    /// <summary>
    /// 只枚舉有限第一層專案，不讀來源本文；殼必須存在、唯一專案且無任何 C#。
    /// 路徑／集合／存取失敗僅回固定代碼，不回顯未知目錄名或例外內容。
    /// </summary>
    private static string? ValidateInventory(string legacyRoot)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(legacyRoot)
                || !string.Equals(Path.GetFullPath(legacyRoot).TrimEnd('\\', '/'), ApprovedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return "MatrixRootNotApproved";
            }

            var root = BoundedSourceReader.ValidateRoot(legacyRoot);
            var directories = Directory.EnumerateDirectories(root).Take(257).ToArray();
            if (directories.Length > 256)
            {
                return "MatrixInventoryCapacityExceeded";
            }

            var projects = new List<string>();
            foreach (var directory in directories)
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    return "MatrixInventoryReparsePoint";
                }

                var projectFiles = Directory.EnumerateFiles(directory, "*.csproj").Take(2).ToArray();
                if (projectFiles.Length > 0)
                {
                    var name = Path.GetFileName(directory);
                    var expectedFile = name == "LineNotifyLibrary" ? "Line.NotifyBot.csproj" : name + ".csproj";
                    if (projectFiles.Length != 1 || Path.GetFileName(projectFiles[0]) != expectedFile)
                    {
                        return "MatrixProjectFileMismatch";
                    }

                    projects.Add(name);
                }
            }

            var expected = new HashSet<string>(Entries.Select(entry => entry[0]).Concat(Libraries), StringComparer.Ordinal);
            expected.Add("Dynamics365Line");
            if (projects.Count != expected.Count || !expected.SetEquals(projects))
            {
                return "MatrixProjectSetMismatch";
            }

            var shell = Path.Combine(root, "Dynamics365Line");
            if (Directory.EnumerateDirectories(shell).Any(path =>
                    !string.Equals(Path.GetFileName(path), "bin", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetFileName(path), "obj", StringComparison.OrdinalIgnoreCase))
                || Directory.EnumerateFiles(shell, "*.cs").Any())
            {
                return "MatrixShellNotEmpty";
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
            || exception is ArgumentException || exception is NotSupportedException)
        {
            return "MatrixInventoryReadFailed";
        }
    }
}

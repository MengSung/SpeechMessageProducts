/*
檔案整體說明

目的與定位：保護 P5-02b 矩陣的核定三十六專案、二十九入口、十一個 Plugin 與十八個活動契約；方案殼不能被成功數或入口數吸收，合法不同專案同名型別不能被去重。

主要功能與流程：使用公開 Manifest 與不核定根的 Create 呼叫，驗證精確清單、複合身份、三態與完整失敗候選。透過修改回傳 JSON 注入呼叫端污染，斷言下次取得的清單未被更改；錯誤根只回固定診斷，不暴露輸入或執行舊程式。

使用邊界與維護：測試不依賴舊磁碟、網路、D365、LINE 或動態載入產物，只有操作區域 JSON，沒有檔案、背景 task 或待排空資源。這些是離線集合與拒絕契約，不代表舊專案成功編譯、黃金資料實跑或實機驗收；真實編譯結果另由 research 矩陣記錄。
*/
using System;
using System.Linq;
using LegacyGolden.PluginHarness;
using Newtonsoft.Json.Linq;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Harness;

/// <summary>驗證公開只編譯 API 的精確覆蓋、方案殼排除及失敗隔離。</summary>
public sealed class CompileMatrixTests
{
    /// <summary>硬性列出所有核定專案，任何刪除、替換或新增均使精確比對失敗。</summary>
    [Fact]
    public void Matrix_Covers36ProjectsExactlyOnce_ExcludesShell()
    {
        var expected = ("AllotPlugIn AllotTlsWorkflow AllotWorkflow AppointmentPlugIn AppointmentSigning " +
            "AppointmentSigningLibrary AppointmentWorkflow CalculateQrCodeScanNumberWorkflow DiscipleLessonsPluIn " +
            "EquipmentWorkflow ExampleWorkflow FeePushLineWorkflow HappyBirthdayWorkflow Line.Messaging " +
            "LineMessagePlugIn LineNotifyLibrary LineUtility NewPersonCareWorkflow NewPersonCareWorkflow-ForDebug " +
            "PersonalQrCodeWorkflow PresentRecordLinePlugIn QrCodeWorkflow RecursiveWorkflow StorLessonSigningLibrary " +
            "StorLessonSigningWorkflow StorLessonSignningPlugIn SundayQrCodeWorkflow TemplatePlugIn ToolUtility Trace " +
            "TwoWingsPlugIn WeeklyReportLinePlugIn WeeklyReportPlugIn WeeklyReportWorkflow WorkflowTemplate " +
            "YangMeillcStorLessonsPlugIn").Split(' ').OrderBy(value => value, StringComparer.Ordinal);
        var manifest = CompileMatrix.Manifest();
        var projects = ((JArray)manifest["projects"]!).Values<string>().ToArray();
        Assert.Equal(36, projects.Length);
        Assert.Equal(expected, projects);
        Assert.Equal(projects.Length, projects.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("Dynamics365Line", projects);
        Assert.Equal("Dynamics365Line", (string?)manifest["excludedShell"]!["project"]);
        Assert.Equal("ExcludedShellNoSource", (string?)manifest["excludedShell"]!["reason"]);
    }

    /// <summary>驗證入口與專案複合身份唯一，保留正式與 Debug 兩個同名活動，不按型別名字刪除。</summary>
    [Fact]
    public void Matrix_Covers29EntriesExactlyOnce_With11PluginsAnd18Activities()
    {
        var manifest = CompileMatrix.Manifest();
        var entries = (JArray)manifest["entrypoints"]!;
        var projects = ((JArray)manifest["projects"]!).Values<string>().ToArray();
        Assert.Equal(29, entries.Count);
        Assert.Equal(11, entries.Count(entry => (string?)entry["kind"] == "IPlugin"));
        Assert.Equal(18, entries.Count(entry => (string?)entry["kind"] == "CodeActivity"));
        Assert.Equal(29, entries.Select(entry => (string?)entry["project"]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(29, entries.Select(entry => (string?)entry["project"] + "|" + (string?)entry["entrypointExpected"])
            .Distinct(StringComparer.Ordinal).Count());
        var expectedProjects = ((JArray)manifest["projects"]!).Values<string>().Except(
            "AppointmentSigningLibrary Line.Messaging LineNotifyLibrary LineUtility StorLessonSigningLibrary ToolUtility Trace".Split(' '),
            StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
        Assert.Equal(expectedProjects, entries.Select(entry => (string?)entry["project"]).OrderBy(value => value, StringComparer.Ordinal));
        Assert.All(entries, entry => Assert.Contains((string?)entry["project"], projects));
        Assert.Equal(2, entries.Count(entry => (string?)entry["entrypointExpected"] == "NewPersonCareWorkflow.NewPersonCareWorkflowClass"));
        Assert.Contains(entries, entry => (string?)entry["entrypointExpected"] == "HappyBirthdayWorkflowNamespace.HappyBirthdayWorkflowPlugIn");
        Assert.Contains(entries, entry => (string?)entry["entrypointExpected"] == "RecursiveWorkflowNameSpace.RecursiveWorkflowPlugIn");
        Assert.Contains(entries, entry => (string?)entry["entrypointExpected"] == "WeeklyReportWorkflowNameSpace.WeeklyReportWorkflow");
    }

    /// <summary>注入非核定路徑，斷言完整失敗列與固定診斷，不讀磁碟或回顯輸入。</summary>
    [Fact]
    public void Matrix_UnapprovedRoot_FailsClosedWithoutPartialSuccess()
    {
        const string input = "unapproved-private-root";
        var result = CompileMatrix.Create(input);
        Assert.False((bool)result["inventoryValid"]!);
        Assert.Equal("compile-only", (string?)result["mode"]);
        Assert.Equal("CompilationSucceededOnly", (string?)result["generatedMeaning"]);
        Assert.Equal(36, ((JArray)result["projects"]!).Count);
        var rows = (JArray)result["entrypoints"]!;
        Assert.Equal(29, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal("CompileFailed", (string?)row["status"]);
            Assert.Null(row["entryTypeActual"]!.Value<string>());
            Assert.Equal("MatrixRootNotApproved", Assert.Single(((JArray)row["diagnostics"]!).Values<string>()));
            Assert.Null(row["assemblyBytes"]);
        });
        Assert.DoesNotContain(input, result.ToString(), StringComparison.Ordinal);
        Assert.Equal(29, (int)result["statusCounts"]!["CompileFailed"]!);
        Assert.Equal(0, (int)result["statusCounts"]!["Generated"]!);
        Assert.Equal(0, (int)result["statusCounts"]!["BlockedByNetworkApi"]!);
    }

    /// <summary>修改呼叫端持有的清單及結果，後續呼叫必須保持核定內容，避免跨操作可變狀態洩漏。</summary>
    [Fact]
    public void Matrix_CallerMutation_DoesNotChangeNextSnapshot()
    {
        var first = CompileMatrix.Manifest();
        ((JArray)first["projects"]!).Clear();
        first["entrypoints"]![0]!["project"] = "polluted";
        first["excludedShell"]!["project"] = "polluted";
        var second = CompileMatrix.Manifest();
        Assert.Equal(36, ((JArray)second["projects"]!).Count);
        Assert.Equal("AllotPlugIn", (string?)second["entrypoints"]![0]!["project"]);
        Assert.Equal("Dynamics365Line", (string?)second["excludedShell"]!["project"]);
        var result = CompileMatrix.Create("invalid");
        result["entrypoints"]![0]!["diagnostics"] = new JArray("polluted");
        Assert.Equal("MatrixRootNotApproved", (string?)CompileMatrix.Create("invalid")["entrypoints"]![0]!["diagnostics"]![0]);
    }
}

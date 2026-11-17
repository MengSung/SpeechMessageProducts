/*
檔案整體說明
目的與定位：P7驗收情境來源的離線安全契約，經產品strict reader讀取固定新目錄，不改原Golden期望。
主要流程：逐案驗證情境宣告的教會與更新／觀察alias一致，並依entrypoint對應manifest新版型別；
這避免R4連線後才發現來源格式錯誤、選錯同事件step，或將共享測試列誤作本輪建立資料清除。
資源與隔離：只讀版控合成JSON及manifest，reader自行關閉文件；沒有SDK、秘密、程序或背景工作。
決定性斷言：案例可完整解析、週報同事件step唯一對應、舊entrypoint映射到新版step、無推播且只觀察核定alias；
資料契約測試不代表R4實機證據。
*/
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>驗收來源先離線通過安全reader，實機證據由後續R4/R6產生。</summary>
public sealed class P7AcceptanceScenarioTests
{
    /// <summary>固定驗收來源能通過strict reader；每筆期望只允許更新情境明列的教會。</summary>
    [Fact]
    public void P7_LocalizationCases_ParseAndOnlyWriteOwnedChurch()
    {
        var cases = ShadowScenarioFiles.LoadLive(Root(), "localization", true);
        Assert.NotEmpty(cases);
        Assert.Equal(cases.Count, cases.Select(item => item.Trigger.GetProperty("message").GetString()).Distinct().Count());
        foreach (var item in cases)
        {
            Assert.True(item.LiveShadow); Assert.False(item.IsActivity);
            Assert.Empty(item.ExpectedLineCalls.EnumerateArray());
            var churchAlias = item.Church.GetProperty("alias").GetString();
            Assert.Equal(churchAlias, Assert.Single(item.ExpectedWrites.EnumerateArray()).GetProperty("alias").GetString());
            foreach (var shared in item.Seed.EnumerateArray().Where(row =>
                row.GetProperty("alias").GetString() == churchAlias || row.GetProperty("entity").GetString() == "contact"))
                Assert.Empty(shared.GetProperty("fields").EnumerateObject());
            Assert.Equal(churchAlias, Assert.Single(item.Observe.EnumerateArray()).GetProperty("alias").GetString());
        }
    }
    /// <summary>
    /// 保護R4四個固定模組以entrypoint精確配對manifest新版step；appointments／attendance
    /// 用舊entrypoint對照Legacy，localization／weeklyreports用新版型別。週報兩步共用Update/entity，
    /// fault setup保留這個歧義，決定性斷言為正式事件候選中恰選一個且Step.Type正確。
    /// 此測試只讀合成／版控清單，不連D365或執行Plugin，無外部資源需要清理。
    /// </summary>
    [Fact]
    public void P7_ShadowEntrypointSelectsFormalStepTypeForEveryAcceptanceModule()
    {
        var document = RegistrationManifestLoader.LoadDocument(Root());
        foreach (var module in new[] { "appointments", "attendance", "localization", "weeklyreports" })
        foreach (var file in ShadowScenarioFiles.LoadLive(Root(), module, true).Where(item => item.LiveShadow))
        {
            var eventSteps = document.Assemblies.SelectMany(assembly => assembly.Steps)
                .Where(step => step.Module == module &&
                    step.Message == file.Trigger.GetProperty("message").GetString() &&
                    step.Entity == file.Trigger.GetProperty("entity").GetString())
                .ToArray();
            var selected = Assert.Single(eventSteps.Where(step => DeploymentCli.MatchesShadowEntrypoint(step, file)));
            Assert.NotEmpty(selected.Type);
            if (file.Basis == "new" || selected.Type == file.Entrypoint)
                Assert.Equal(file.Entrypoint, selected.Type);
            else
                Assert.Contains(selected.Legacy, reference => reference.Type == file.Entrypoint);
            if (module == "weeklyreports") Assert.Equal(2, eventSteps.Length);
        }

        var weekly = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), "weeklyreports", true));
        var weeklyStep = document.Assemblies.SelectMany(assembly => assembly.Steps)
            .Single(step => step.Module == "weeklyreports" && step.Type == weekly.Entrypoint);
        var oldEntryPoint = weekly with { Entrypoint = Assert.Single(weeklyStep.Legacy).Type };
        var mapped = Assert.Single(document.Assemblies.SelectMany(assembly => assembly.Steps)
            .Where(step => step.Module == "weeklyreports" && DeploymentCli.MatchesShadowEntrypoint(step, oldEntryPoint)));
        Assert.Equal(weeklyStep.Type, mapped.Type);
    }

    /// <summary>由本原始碼位置取得本checkout，不採程序CWD或其他worktree。</summary>
    private static string Root([CallerFilePath] string path = "") => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
}

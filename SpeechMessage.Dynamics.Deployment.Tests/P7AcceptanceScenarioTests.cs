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
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System;
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

    /// <summary>四個 P7 情境排序後每筆 owned lookup 都只指向已建立或 shared alias。</summary>
    [Fact]
    public void P7Scenarios_AllSeeds_HaveNoUnresolvedOrderAfterSort()
    {
        foreach (var module in new[] { "appointments", "attendance", "localization", "weeklyreports" })
        {
            var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), module, true));
            var rows = file.Seed.EnumerateArray().ToArray();
            var shared = new HashSet<string>(StringComparer.Ordinal)
            {
                file.Church.GetProperty("alias").GetString()!, "p7-test-contact"
            };
            if (module == "weeklyreports") shared.Add("owner");
            var aliases = rows.Select(row => row.GetProperty("alias").GetString()!)
                .Where(alias => !shared.Contains(alias)).ToArray();
            var owned = aliases.ToHashSet(StringComparer.Ordinal);
            var dependencies = rows.Where(row => owned.Contains(row.GetProperty("alias").GetString()!))
                .ToDictionary(row => row.GetProperty("alias").GetString()!,
                    row => (IReadOnlyCollection<string>)References(row.GetProperty("fields"))
                        .Where(owned.Contains).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            var ordered = ShadowSdkScenarioFactory.OrderSeedAliases(aliases, dependencies);
            var created = new HashSet<string>(shared, StringComparer.Ordinal);
            foreach (var alias in ordered)
            {
                Assert.All(dependencies[alias], dependency => Assert.Contains(dependency, created));
                created.Add(alias);
            }
        }
    }

    /// <summary>
    /// appointments 的 activity seed 必須包含當下 metadata 要求的 subject、開始及結束時間；
    /// 這是 Create 前的離線資料契約，不宣稱實機 Create 已通過。
    /// </summary>
    [Fact]
    public void P7Appointments_ActivitySeedContainsApplicationRequiredFields()
    {
        var file = Assert.Single(ShadowScenarioFiles.LoadLive(Root(), "appointments", true));
        var record = Assert.Single(file.Seed.EnumerateArray(), row =>
            row.GetProperty("alias").GetString() == "record");
        var fields = record.GetProperty("fields");
        Assert.Equal("SMV-TEST-appointment", fields.GetProperty("subject").GetString());
        Assert.Equal("2026-11-15T04:00:00Z", fields.GetProperty("scheduledstart").GetString());
        Assert.Equal("2026-11-15T05:00:00Z", fields.GetProperty("scheduledend").GetString());
    }

    private static IEnumerable<string> References(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("ref", out var reference) && reference.ValueKind == JsonValueKind.String)
                yield return reference.GetString()!;
            foreach (var property in value.EnumerateObject())
                foreach (var nested in References(property.Value)) yield return nested;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                foreach (var nested in References(item)) yield return nested;
        }
    }

    /// <summary>由本原始碼位置取得本checkout，不採程序CWD或其他worktree。</summary>
    private static string Root([CallerFilePath] string path = "") => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
}

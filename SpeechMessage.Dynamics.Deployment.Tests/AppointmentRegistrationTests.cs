/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-08 預約簽核的正式註冊清單，確保兩個 appointment Plugin
 * 步驟與一個 Workflow 活動都沿用新版 Shadow 外殼、goodshepherdback 停用及
 * reading 基準；舊型別只作盤點對照，不被誤設成可執行的正式步驟。
 *
 * 主要功能與流程：透過共用 RegistrationManifestLoader 讀取實際 JSON，逐欄檢查
 * Create／Update 的事件、階段、同步模式、篩選欄位、唯一 legacy stepId、活動
 * 顯示名稱與 shadowReport。任何新增、刪除、啟用或基準誤標都會在離線測試中失敗。
 *
 * 使用邊界與維護：本檔只讀工作樹公開清單與合成 Golden，不建立 D365 lease、不讀
 * User Secrets、不啟用步驟、不推播；測試資料在單次案例內存活，loader 的 JSON 資源
 * 由呼叫完成後釋放。通過僅代表註冊結構與停用安全邊界正確，不代表實機影子送達。
 */
using System;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護預約 Plugin／Workflow 註冊與 05A reading／Shadow 契約。</summary>
public sealed class AppointmentRegistrationTests
{
    /// <summary>檢查兩個事件步驟及一個活動均停用、影子化且保留退役入口對照。</summary>
    [Fact]
    public void Appointment_Registration_DisabledShadowWithReadingBasis()
    {
        var root = Root();
        var scenarios = ShadowScenarioFiles.Load(root, "appointments");
        Assert.Equal(6, scenarios.Count);
        Assert.All(scenarios, item => { Assert.Equal("reading", item.Basis); Assert.False(item.LiveShadow); });
        var document = RegistrationManifestLoader.LoadDocument(root);
        var plugins = document.Assemblies.SelectMany(item => item.Steps)
            .Where(item => item.Module == "appointments").ToArray();

        Assert.Equal(2, plugins.Length);
        var create = Assert.Single(plugins, item => item.Message == "Create");
        var update = Assert.Single(plugins, item => item.Message == "Update");
        Assert.Equal("appointment", update.Entity);
        Assert.Equal("PostOperation", update.Stage);
        Assert.Equal("Synchronous", update.Mode);
        Assert.Equal("new_signing_contact,new_signing_content,new_meeting_kind,new_applier_appointment",
            string.Join(",", update.FilteringAttributes));
        Assert.Equal("{\"mode\":\"Shadow\"}", update.UnsecureConfig);
        Assert.Empty(update.SecureConfig);
        Assert.False(update.EnabledByOrganization["goodshepherdback"]);
        Assert.Equal("reading", update.ShadowReport.Basis);
        Assert.Null(update.ShadowReport.Differences);
        Assert.Single(update.Legacy);
        Assert.All(plugins, item => Assert.All(item.Legacy, legacy => Assert.False(string.IsNullOrWhiteSpace(legacy.StepId))));
        Assert.Equal(2, plugins.SelectMany(item => item.Legacy).Select(item => item.StepId).Distinct().Count());
        // 身分來自 P5-08-inventory 所列的唯讀 export-legacy；合成 GUID 無法證明退役步驟的對照。
        Assert.Equal("cd6d656f-aae1-e911-810d-00155d006601", Assert.Single(create.Legacy).StepId);
        Assert.Equal("76f8d786-71df-e911-810d-00155d006601", Assert.Single(update.Legacy).StepId);

        var workflows = Assert.Single(document.Assemblies, item => item.AssemblyName == "SpeechMessage.Dynamics.Workflows");
        var activity = Assert.Single(workflows.Activities, item => item.Module == "appointments");
        Assert.EndsWith("AppointmentSigningActivity", activity.Type, StringComparison.Ordinal);
        Assert.Equal("AppointmentSigningActivity", activity.DisplayName);
        Assert.Equal("reading", activity.ShadowReport.Basis);
        Assert.Null(activity.ShadowReport.Differences);
        Assert.Equal("AppointmentWorkflow.AppointmentWorkflowPlugin", Assert.Single(activity.Legacy).Type);
    }

    /// <summary>只沿測試輸出向上找目前方案，避免跨入父工作樹的另一份清單。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Test repository root unavailable.");
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-10 關懷活動在正式註冊清單中的兩個活動入口，保護
 * NewPersonCareActivity 與 ContactAnniversaryActivity 的型別、退役來源、reading
 * 基準、Shadow 設定及組織停用狀態。測試不執行退役 DLL，也不把離線 reading
 * 證據誤寫成實機影子結果。
 *
 * 主要流程：由目前工作樹向上尋找包含方案檔的根目錄，透過共用 manifest loader
 * 與 care scenarios 讀取公開 JSON，逐欄檢查兩個活動及四個離線情境。任何活動
 * 被改成 Live、填入 secure 設定、缺少 legacy 對照或改用 legacy-live 基準時，
 * 測試應決定性失敗。
 *
 * 使用邊界與維護：本檔只讀版控清單與合成 Golden，不建立 D365 連線、不讀 User
 * Secrets、不啟用步驟、不發送通知。測試資料只在單次案例中存活；loader 與檔案
 * 讀取的資源由測試框架及區域變數持有，測試不建立背景工作、timer、cache 或
 * 跨案例可變狀態。通過僅代表離線註冊契約正確，不代表實機 Shadow 差異為零。
 */
using System;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護 P5-10 關懷活動的停用 Shadow 註冊與 reading 基準。</summary>
public sealed class CareRegistrationTests
{
    /// <summary>兩個活動均須停用、不可帶 secure 設定，且 legacy 對照必須完整。</summary>
    [Fact]
    public void Care_Registration_DisabledShadowWithReadingBasis()
    {
        var root = Root();
        var scenarios = ShadowScenarioFiles.Load(root, "care");
        Assert.Equal(4, scenarios.Count);
        Assert.All(scenarios, item =>
        {
            Assert.Equal("reading", item.Basis);
            Assert.False(item.LiveShadow);
        });

        var document = RegistrationManifestLoader.LoadDocument(root);
        var workflows = Assert.Single(document.Assemblies,
            item => item.AssemblyName == "SpeechMessage.Dynamics.Workflows");
        var activities = workflows.Activities.Where(item => item.Module == "care").ToArray();
        Assert.Equal(2, activities.Length);
        Assert.All(activities, item =>
        {
            Assert.Equal("reading", item.ShadowReport.Basis);
            Assert.Null(item.ShadowReport.Differences);
        });

        var newPerson = Assert.Single(activities, item => item.DisplayName == "NewPersonCareActivity");
        Assert.EndsWith("NewPersonCareActivity", newPerson.Type, StringComparison.Ordinal);
        Assert.Single(newPerson.Legacy);
        Assert.Equal("NewPersonCareWorkflow.NewPersonCareWorkflowClass",
            Assert.Single(newPerson.Legacy).Type);

        var anniversary = Assert.Single(activities, item => item.DisplayName == "ContactAnniversaryActivity");
        Assert.EndsWith("ContactAnniversaryActivity", anniversary.Type, StringComparison.Ordinal);
        Assert.Equal(2, anniversary.Legacy.Count);
        Assert.Contains(anniversary.Legacy, item => item.Type.EndsWith("HappyBirthdayWorkflowPlugIn",
            StringComparison.Ordinal));
        Assert.Contains(anniversary.Legacy, item => item.Type.EndsWith("RecursiveWorkflowPlugIn",
            StringComparison.Ordinal));
    }

    /// <summary>只沿測試輸出尋找本工作樹方案，避免讀到父工作樹的另一份清單。</summary>
    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
                return directory.FullName;

        throw new DirectoryNotFoundException("Test repository root unavailable.");
    }
}

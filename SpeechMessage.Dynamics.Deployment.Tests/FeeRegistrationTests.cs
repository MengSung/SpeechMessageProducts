/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-11 收費 Plugin／Workflow 的離線註冊契約。測試只讀目前
 * 工作樹的 registration manifest，不執行舊 DLL、不建立 D365 連線、不讀秘密，也
 * 不把 reading 基準誤報成實機 Shadow 結果。
 *
 * 主要功能與流程：載入 registration manifest，逐一檢查收費 Plugin／Workflow 的
 * 型別、事件、停用狀態、reading 基準及退役來源清單，遇到不符即讓測試失敗。
 *
 * 主要保護契約：FeeAllotPlugin 只有 Create／Update PostOperation 同步步驟，兩步
 * 均停用且無 secure 設定；FeeAllotActivity 與 FeeNotifyActivity 均使用 reading
 * 基準並保留退役來源。測試失敗時不修改 manifest 或任何外部狀態。
 *
 * 使用邊界與維護：測試只讀本工作樹的 JSON，測試 owner 不建立連線、不持有秘密，
 * 也不在案例間保留可變資料；文件與編碼守門負責驗證本檔格式。
 */
using System;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>保護收費活動與外掛的型別、來源、基準及停用狀態。</summary>
public sealed class FeeRegistrationTests
{
    /// <summary>收費步驟與活動必須完整註冊但保持 reading／停用邊界。</summary>
    [Fact]
    public void Fees_Registration_DisabledShadowWithReadingBasis()
    {
        var root = Root();
        var manifest = RegistrationManifestLoader.LoadDocument(root);
        var plugins = Assert.Single(manifest.Assemblies,
            item => item.AssemblyName == "SpeechMessage.Dynamics.Plugins");
        var steps = plugins.Steps.Where(item => item.Module == "fees").ToArray();
        Assert.Equal(2, steps.Length);
        Assert.All(steps, item =>
        {
            Assert.Equal("SpeechMessage.Dynamics.Plugins.Fees.FeeAllotPlugin", item.Type);
            Assert.Equal("PostOperation", item.Stage);
            Assert.Equal("Synchronous", item.Mode);
            Assert.False(item.EnabledByOrganization["goodshepherdback"]);
            Assert.Equal("reading", item.ShadowReport.Basis);
            Assert.Null(item.ShadowReport.Differences);
            Assert.Empty(item.SecureConfig);
        });

        var workflows = Assert.Single(manifest.Assemblies,
            item => item.AssemblyName == "SpeechMessage.Dynamics.Workflows");
        var activities = workflows.Activities.Where(item => item.Module == "fees").ToArray();
        Assert.Equal(2, activities.Length);
        Assert.All(activities, item =>
        {
            Assert.Equal("reading", item.ShadowReport.Basis);
            Assert.Null(item.ShadowReport.Differences);
            Assert.NotEmpty(item.Legacy);
        });
        Assert.EndsWith("FeeAllotActivity", Assert.Single(activities,
            item => item.DisplayName == "FeeAllotActivity").Type, StringComparison.Ordinal);
        Assert.EndsWith("FeeNotifyActivity", Assert.Single(activities,
            item => item.DisplayName == "FeeNotifyActivity").Type, StringComparison.Ordinal);
    }

    /// <summary>只沿測試輸出尋找本工作樹方案，避免讀到父工作樹清單。</summary>
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

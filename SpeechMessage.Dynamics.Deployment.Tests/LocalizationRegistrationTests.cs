/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-04 版控中的國際化註冊清單確實能由正式嚴格 loader 消費，
 * 防止合成清單測試通過，但實際清單含未知模組、錯誤大小寫或多餘映像屬性。
 * 主要功能與流程：由測試組件位置尋找本工作樹，讀取正式清單，核對三個外殼的
 * 六個註冊、映像與篩選欄位，以及全部 Shadow、空 secure、停用及 new 基準。
 * 使用邊界與維護：只讀公開註冊結構，不建立 SDK 連線、不讀 Secrets、不載入
 * Plugin DLL、不變更工作目錄或遠端狀態。loader 擁有並同步釋放檔案串流；
 * 測試只保留當案純值，沒有共用可變狀態、背景工作或清理外部資源的責任。
 */
using System;
using System.IO;
using System.Linq;
using SpeechMessage.Dynamics.Deployment.Shadow;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以實際產品清單保護國際化部署邊界，不以重建合成資料取代整合檢查。</summary>
public sealed class LocalizationRegistrationTests
{
    /// <summary>
    /// 正式 loader 必須接受整份清單；各外殼精確六步驟且維持停用 Shadow，
    /// 前像及同步／非同步階段均依 05A 核對，防止錯誤清單遮蔽後續 CLI 故障。
    /// </summary>
    [Fact]
    public void Localization_Registration_LoadsWithDisabledShadowAndRequiredImages()
    {
        var root = RepositoryRoot();
        var scenarios = ShadowScenarioFiles.Load(root, "localization");
        Assert.Equal(6, scenarios.Count);
        Assert.All(scenarios, scenario => Assert.Equal("new", scenario.Basis));
        var document = RegistrationManifestLoader.LoadDocument(root);
        var steps = document.Assemblies.SelectMany(assembly => assembly.Steps)
            .Where(step => step.Module == "localization").ToArray();
        Assert.Equal(6, steps.Length);
        Assert.All(steps, step =>
        {
            Assert.Equal("{\"mode\":\"Shadow\"}", step.UnsecureConfig);
            Assert.Empty(step.SecureConfig);
            Assert.False(step.EnabledByOrganization["goodshepherdback"]);
            Assert.Empty(step.Legacy);
            Assert.Equal("new", step.ShadowReport.Basis);
        });
        var text = steps.Where(step => step.Type.EndsWith(".LocalizedTextChangedPlugin", StringComparison.Ordinal)).ToArray();
        Assert.Equal("Create,Delete,Update", string.Join(",", text.Select(step => step.Message).OrderBy(value => value, StringComparer.Ordinal)));
        foreach (var step in text)
        {
            Assert.Equal("new_localizedtext", step.Entity);
            Assert.Equal("PostOperation", step.Stage);
            Assert.Equal("Asynchronous", step.Mode);
            if (step.Message == "Create") Assert.Empty(step.Images);
            else
            {
                var image = Assert.Single(step.Images);
                Assert.Equal("PreImage", image.Type);
                Assert.Equal("new_accountid", Assert.Single(image.Attributes));
            }
        }
        var member = Assert.Single(steps.Where(step => step.Type.EndsWith(".MemberLanguageChangedPlugin", StringComparison.Ordinal)));
        Assert.Equal("new_preferredlanguage", Assert.Single(member.FilteringAttributes));
        var church = steps.Where(step => step.Type.EndsWith(".ChurchLocaleChangedPlugin", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, church.Length);
        Assert.Contains(church, step => step.Stage == "PreOperation" && step.Mode == "Synchronous");
        Assert.Contains(church, step => step.Stage == "PostOperation" && step.Mode == "Asynchronous");
        Assert.All(church, step =>
        {
            var image = Assert.Single(step.Images);
            Assert.Equal("PreImage", image.Type);
            Assert.Equal(step.FilteringAttributes, image.Attributes);
        });
    }

    /// <summary>只沿當前測試組件的父目錄尋找方案，不掃描其他工作樹或依程序 cwd 取檔。</summary>
    /// <returns>包含本案方案的絕對路徑；不存在則明確失敗。</returns>
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Test repository root is unavailable.");
    }
}

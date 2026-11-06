/*
 * 檔案整體說明
 *
 * 目的與定位：真實舊入口的終端故障契約；全域告警綁定獨占，測試只寫自有暫存檔、不建立 LINE sender。
 *
 * 主要功能與流程：
 * 本檔宣告 TenancyFailureBoundaryTests、ThrowingSource、ThrowingProvider。
 * - 缺設定維持空結果；已設定 CRM 組織優先，未設定組織才讀部署預設，不在產品重複解析 token key。
 * - 有注入 resolver 時須使用實際接口；以另一份合成設定的真實 resolver 區分委派與偷偷回到靜態讀取。
 * - 設定來源注入原始例外，舊空結果不能吞掉告警；外層再報同一實例只有一筆事件且不包含秘密訊息。
 * - 建立純記憶體 provider，不保存 builder。
 * 測試案例入口包含 LegacyBoundary_ConfiguredOrganization_PreservesOptionalBehavior、
 * LegacyBoundary_InjectedResolver_UsesContainerOwner、LegacyBoundary_ProviderFailure_ReportsOriginalException、
 * Build、TryGet。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ChurchReport.Services;
using Microsoft.Extensions.Configuration;
using ToolUtilityNameSpace.Diagnostics;
using SpeechMessage.Tenancy;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

/// <summary>真實舊入口的終端故障契約；全域告警綁定獨占，測試只寫自有暫存檔、不建立 LINE sender。</summary>
[Collection("ExceptionReporting")]
public sealed class TenancyFailureBoundaryTests
{
    /// <summary>缺設定維持空結果；已設定 CRM 組織優先，未設定組織才讀部署預設，不在產品重複解析 token key。</summary>
    [Fact(DisplayName = "產品舊入口保留CRM組織與缺設定相容")]
    public void LegacyBoundary_ConfiguredOrganization_PreservesOptionalBehavior()
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:Organization"] = "alpha",
            ["LineMessaging:Alpha:ChannelAccessToken"] = "synthetic-alpha",
            ["LineMessaging:Jesus:ChannelAccessToken"] = "synthetic-default"
        }).Build();
        Assert.Equal("synthetic-alpha", ChurchReportLineChannelAccess.GetOptionalToken(config));
        config["CrmConnection:Organization"] = null;
        Assert.Equal("synthetic-default", ChurchReportLineChannelAccess.GetOptionalToken(config));
        Assert.Equal(string.Empty, ChurchReportLineChannelAccess.GetOptionalToken(null));
    }

    /// <summary>有注入 resolver 時須使用實際接口；以另一份合成設定的真實 resolver 區分委派與偷偷回到靜態讀取。</summary>
    [Fact(DisplayName = "產品DI入口確實使用注入的共用resolver")]
    public void LegacyBoundary_InjectedResolver_UsesContainerOwner()
    {
        using var hostConfig = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CrmConnection:Organization"] = "Alpha",
            ["LineMessaging:Alpha:ChannelAccessToken"] = "synthetic-host"
        }).Build();
        using var injectedConfig = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LineMessaging:Alpha:ChannelAccessToken"] = "synthetic-injected"
        }).Build();
        Assert.Equal("synthetic-injected", ChurchReportLineChannelAccess.GetOptionalToken(hostConfig,
            new ConfigurationLineChannelResolver(injectedConfig, "ChurchReport")));
    }

    /// <summary>設定來源注入原始例外，舊空結果不能吞掉告警；外層再報同一實例只有一筆事件且不包含秘密訊息。</summary>
    /// <param name="factoryFailure">將同一原例外注入 Lazy 取得階段或 provider 讀取階段。</param>
    [Theory(DisplayName = "產品設定故障回空仍上報原例外且不外洩")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyBoundary_ProviderFailure_ReportsOriginalException(bool factoryFailure)
    {
        var directory = Path.Combine(Path.GetTempPath(), "p110-" + Guid.NewGuid().ToString("N"));
        var source = new ThrowingSource();
        using var config = (ConfigurationRoot)new ConfigurationBuilder().Add(source).Build();
        try
        {
            await using (var diagnostics = new ExceptionDiagnostics(directory, outputOptions: new ExceptionOutputOptions(true, false)))
            {
                using var registration = ExceptionReporting.Attach(diagnostics);
                Assert.Equal(string.Empty, factoryFailure
                    ? ChurchReportLineChannelAccess.GetOptionalTokenFromFactory(() => throw source.Failure)
                    : ChurchReportLineChannelAccess.GetOptionalToken(config));
                ExceptionReporting.Report(source.Failure, "TenancyTest.Outer");
            }
            var record = Assert.Single(File.ReadAllLines(Path.Combine(directory, "Exception.log")));
            Assert.Contains("InvalidOperationException", record, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-private-token", record, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    /// <summary>只有單一故障 provider，沒有外部資源或監聽，生命週期由案例設定根管理。</summary>
    private sealed class ThrowingSource : IConfigurationSource
    {
        /// <summary>本例唯一原例外，用於去重而非比對替身呼叫次數。</summary>
        internal InvalidOperationException Failure { get; } = new("synthetic-private-token");
        /// <summary>建立純記憶體 provider，不保存 builder。</summary>
        /// <param name="builder">本例 builder，不保留。</param>
        /// <returns>故障 provider。</returns>
        public IConfigurationProvider Build(IConfigurationBuilder builder) => new ThrowingProvider(Failure);
    }

    /// <summary>在真實設定讀取邊界拋原例外，保證故障不是測試自行提前拋出。</summary>
    private sealed class ThrowingProvider : ConfigurationProvider
    {
        /// <summary>本案例唯一失敗，不包含真實機密或 request。</summary>
        private readonly Exception failure;
        /// <summary>借用案例失敗值，不建立資源。</summary>
        /// <param name="failure">待由設定讀取拋出的例外。</param>
        internal ThrowingProvider(Exception failure) => this.failure = failure;
        /// <summary>模擬設定 provider 真正故障；由產品失敗邊界上報。</summary>
        /// <param name="key">本次鍵，不保存。</param>
        /// <param name="value">不發布部分結果。</param>
        /// <returns>此故障案例不回傳。</returns>
        public override bool TryGet(string key, out string? value) => throw failure;
    }
}

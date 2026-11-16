/*
 * 檔案整體說明
 *
 * 目的與定位：P5-11 收費外掛的正式平台入口回歸測試，確保防遞迴與例外處理
 * 依退役來源及 05A D5-6／D5-8，而非沿用其他模組的深度設定。
 * 主要功能與流程：透過真 FeeAllotPlugin.Execute 與合成平台 context，在第一次
 * SDK 讀取注入原始故障；應執行的訊息必須傳回同一例外，應跳過的訊息零讀取。
 * 資源與隔離：每案 using 擁有 FakeOrganizationService，成功或失敗均同步排空；
 * 工廠只借用本案使用者服務。無網路、秘密、背景工作、跨案快取或正式資料。
 * 驗證範圍：只證明離線入口的政策與原例外；打包、CAS 與實機停用部署另行驗證。
 */
using System;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Plugins.Fees;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fees;

/// <summary>驗證真 Plugin 入口的深度與未知例外契約，不以獨立政策函式代替外殼。</summary>
public sealed class FeesShellTests
{
    /// <summary>Create 深度不限，Update 深度二執行、三跳過；錯誤若被吞掉亦會失敗。</summary>
    [Theory]
    [InlineData("Create", 1, true)]
    [InlineData("Create", 99, true)]
    [InlineData("Update", 2, true)]
    [InlineData("Update", 3, false)]
    public void Fees_DepthPolicy_MatchesLegacy(string message, int depth, bool executes)
    {
        using var sdk = new FakeOrganizationService();
        var failure = new InvalidOperationException("Synthetic fee read failure.");
        sdk.RetrieveHandler = (_, _, _) => throw failure;
        var context = new RemoteExecutionContext
        {
            MessageName = message, Depth = depth, Stage = 40, Mode = 0,
            PrimaryEntityName = "new_fee", PrimaryEntityId = Guid.NewGuid(),
            UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), CorrelationId = Guid.NewGuid()
        };
        context.InputParameters["Target"] = new Entity("new_fee", context.PrimaryEntityId);
        var provider = new FakeServiceProvider().Add<IPluginExecutionContext>(context)
            .Add<ITracingService>(new FakeTracingService())
            .Add<IOrganizationServiceFactory>(new Factory(sdk, context.UserId));
        var plugin = new FeeAllotPlugin("{\"mode\":\"Shadow\"}", "");
        if (executes)
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => plugin.Execute(provider)));
            Assert.Equal(1, sdk.Calls[nameof(IOrganizationService.Retrieve)]);
        }
        else
        {
            plugin.Execute(provider);
            Assert.Empty(sdk.Calls);
        }
        Assert.Empty(sdk.WriteRecords);
    }

    /// <summary>只接受平台 UserId 的單案服務；外殼不擁有、不 Dispose 此服務。</summary>
    private sealed class Factory(IOrganizationService service, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>錯誤使用者立即拒絕，不能以 null 或 initiating user 提升權限。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId) =>
            userId == user ? service : throw new InvalidOperationException("Unexpected synthetic service user.");
    }
}

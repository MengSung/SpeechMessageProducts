/*
 * 檔案整體說明
 *
 * 目的與定位：以合成 SDK context 保護 P5-04 文字覆寫變更的快取失效契約，
 * 真正執行 LocalizedTextChangedPlugin，不把離線意圖當作 D365 實機結果。
 * 主要功能與流程：Create、Update、Delete 都必須碰觸所屬教會的 UTC 整秒時間；
 * 搬移時兩間教會各寫一次，缺映像或非法 lookup 在任何寫入前拒絕。
 * 同一外殼交錯執行不同教會，檢查 Shadow 沒有真寫入，沒有上一案的識別殘留。
 * 使用邊界與維護：每案擁有 Mock、追蹤及輸入，沒有連線、秘密、計時器或背景工作；
 * 注入的 SDK 失敗必須原樣拋出。所有操作同步排空，案例結束不保存 provider。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Localization;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;
namespace SpeechMessage.Dynamics.Plugins.Tests.Localization;
/// <summary>文字覆寫外殼的完整訊息、前像及隔離契約。</summary>
public sealed class LocalizedTextChangedPluginTests
{
    /// <summary>固定時鐘包含次秒，驗證產品寫入 UTC 並向下量化，不使用主機當地時間。</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 1, 2, 3, 456, TimeSpan.Zero);
    /// <summary>三種訊息各寫一次正式 account，並確定沒有把文字內容寫入 account。</summary>
    [Theory]
    [InlineData("Create")]
    [InlineData("Update")]
    [InlineData("Delete")]
    public void LocalizedText_CreateUpdateDelete_TouchesChurchTextChangedOn(string message)
    {
        var church = Guid.NewGuid();
        var writes = new List<Entity>();
        var service = Service(writes);
        var context = Context(message, church, church);
        new LocalizedTextChangedPlugin("{\"mode\":\"Live\"}", "", () => Now)
            .Execute(Provider(context, service.Object, new FakeTracingService()));
        var row = Assert.Single(writes);
        Assert.Equal("account", row.LogicalName);
        Assert.Equal(church, row.Id);
        Assert.Single(row.Attributes);
        var time = row.GetAttributeValue<DateTime>("new_textchangedon");
        Assert.Equal(DateTimeKind.Utc, time.Kind);
        Assert.Equal(Now.UtcDateTime.Ticks - Now.Ticks % TimeSpan.TicksPerSecond, time.Ticks);
    }
    /// <summary>同一次搬移必須先驗證兩個 lookup，再各自使快取失效，不能只處理新教會。</summary>
    [Fact]
    public void LocalizedText_ChurchReassigned_TouchesBothChurches()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var writes = new List<Entity>();
        var service = Service(writes);
        new LocalizedTextChangedPlugin("{\"mode\":\"Live\"}", "", () => Now)
            .Execute(Provider(Context("Update", second, first), service.Object, new FakeTracingService()));
        Assert.Equal(2, writes.Count);
        Assert.Equal(new[] { first, second }, writes.Select(row => row.Id));
        Assert.Equal(writes[0]["new_textchangedon"], writes[1]["new_textchangedon"]);
    }
    /// <summary>更新未包含 lookup 時使用已註冊前像；缺前像則零寫入拒絕。</summary>
    [Fact]
    public void LocalizedText_MissingRequiredImage_RejectsBeforeWrite()
    {
        var writes = new List<Entity>();
        var service = Service(writes);
        var context = Context("Update", Guid.NewGuid(), Guid.NewGuid());
        context.Object.PreEntityImages.Clear();
        Assert.Throws<InvalidPluginExecutionException>(() =>
            new LocalizedTextChangedPlugin("{\"mode\":\"Live\"}", "", () => Now)
                .Execute(Provider(context, service.Object, new FakeTracingService())));
        Assert.Empty(writes);
    }
    /// <summary>以相同外殼 A→B→A 執行，Shadow 只有當案 account 意圖且真服務零寫入。</summary>
    [Fact]
    public void LocalizedText_ShadowMode_MatchesExpected()
    {
        var writes = new List<Entity>();
        var service = Service(writes);
        var shell = new LocalizedTextChangedPlugin("{\"mode\":\"Shadow\"}", "", () => Now);
        var first = Guid.NewGuid();
        foreach (var church in new[] { first, Guid.NewGuid(), first })
        {
            var trace = new FakeTracingService();
            shell.Execute(Provider(Context("Create", church, church), service.Object, trace));
            var intents = trace.Entries.Where(text => text.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(text => JObject.Parse(text.Substring("SMV-SHADOW|1|".Length))).ToArray();
            Assert.Equal(2, intents.Length);
            Assert.Equal("Update", (string?)intents[0]["operation"]);
            Assert.Equal(church.ToString("D"), (string?)intents[0]["id"]);
            Assert.True((bool)intents[1]["end"]!);
        }
        Assert.Empty(writes);
    }
    /// <summary>新模組採深度二；超過門檻時不得解析輸入或取得服務。</summary>
    [Fact]
    public void LocalizedText_DepthPolicy_MatchesLegacy()
    {
        var context = new FakePluginExecutionContext(depth: 3, primaryEntityName: "new_localizedtext");
        var provider = new FakeServiceProvider().Add<IPluginExecutionContext>(context.Object)
            .Add<ITracingService>(new FakeTracingService());
        new LocalizedTextChangedPlugin("{\"mode\":\"Shadow\"}", "", () => Now).Execute(provider);
    }
    /// <summary>門檻二仍須執行真寫入，避免只測超量跳過而容許錯誤的門檻一。</summary>
    [Fact]
    public void LocalizedText_DepthTwo_StillExecutes()
    {
        var writes = new List<Entity>();
        var service = Service(writes);
        var context = Context("Create", Guid.NewGuid(), Guid.NewGuid());
        context.Mock.SetupGet(value => value.Depth).Returns(2);
        new LocalizedTextChangedPlugin("{\"mode\":\"Live\"}", "", () => Now)
            .Execute(Provider(context, service.Object, new FakeTracingService()));
        Assert.Single(writes);
    }
    /// <summary>注入真服務寫入失敗，驗證外殼不包裝、不吞掉原始未知例外。</summary>
    [Fact]
    public void LocalizedText_UnexpectedError_Rethrown()
    {
        var failure = new InvalidOperationException("Synthetic failure.");
        var service = new Mock<IOrganizationService>();
        service.Setup(x => x.Update(It.IsAny<Entity>())).Throws(failure);
        var actual = Assert.Throws<InvalidOperationException>(() =>
            new LocalizedTextChangedPlugin("{\"mode\":\"Live\"}", "", () => Now)
                .Execute(Provider(Context("Create", Guid.NewGuid(), Guid.NewGuid()), service.Object, new FakeTracingService())));
        Assert.Same(failure, actual);
    }
    /// <summary>建立只保存本案例寫入快照的 SDK 替身，不查詢或連線。</summary>
    private static Mock<IOrganizationService> Service(List<Entity> writes)
    {
        var service = new Mock<IOrganizationService>(MockBehavior.Strict);
        service.Setup(x => x.Update(It.IsAny<Entity>())).Callback<Entity>(row => writes.Add(row));
        return service;
    }
    /// <summary>合成正式目標與必要前像，Delete 使用平台的 EntityReference 形狀。</summary>
    private static FakePluginExecutionContext Context(string message, Guid current, Guid previous)
    {
        var id = Guid.NewGuid();
        var context = new FakePluginExecutionContext(messageName: message,
            primaryEntityName: "new_localizedtext", primaryEntityId: id);
        context.Mock.SetupGet(x => x.Stage).Returns(40);
        context.Mock.SetupGet(x => x.Mode).Returns(1);
        context.Object.InputParameters["Target"] = message == "Delete"
            ? new EntityReference("new_localizedtext", id)
            : new Entity("new_localizedtext", id) { ["new_accountid"] = new EntityReference("account", current) };
        if (message != "Create")
            context.Object.PreEntityImages["PreImage"] = new Entity("new_localizedtext", id)
            { ["new_accountid"] = new EntityReference("account", previous) };
        return context;
    }
    /// <summary>所有服務只借給本次 Execute，factory 不保存下一個使用者或組織。</summary>
    private static FakeServiceProvider Provider(FakePluginExecutionContext context, IOrganizationService service, FakeTracingService trace)
    {
        var factory = new Mock<IOrganizationServiceFactory>();
        factory.Setup(x => x.CreateOrganizationService(It.IsAny<Guid>())).Returns(service);
        return new FakeServiceProvider().Add<IPluginExecutionContext>(context.Object)
            .Add<ITracingService>(trace).Add<IOrganizationServiceFactory>(factory.Object);
    }
}

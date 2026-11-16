/*
 * 檔案整體說明
 * 目的與定位：P5-12 真 LineWebhook 組合根與失敗彙總 SDK 查詢契約，不建立新
 * LINE 管道；只有 IOrganizationService 底層使用 DispatchProxy 合成來源。
 * 主要流程：從正式 DI 註冊取得 scoped source，驗 WhoAmI／頻道關聯後的 FetchXML
 * 僅聚合正式活動 ID，方向、失敗類別、時間窗、sender 與來源教會條件皆不得缺漏。
 * 組織不符須在資料查詢前拒絕，缺省主機不解析來源。沒有活動本文、姓名、token。
 * 資源與隔離：每案容器及 scope 唯一擁有替身，using 在所有 Task 完成後清理；
 * proxy 不保留來源 Entity、憑證或跨案狀態。這是離線查詢形狀與接線證據，不宣稱
 * FetchXML 已在 D365 執行或真正失敗通知已送達，實機部署仍保持全停用。
 */
using System.Reflection;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Messaging;
using Xunit;

namespace SpeechMessageProducts.LineWebhook.Tests;

/// <summary>驗真正組合及傳給 SDK 的聚合查詢，替身不重作產品查詢演算法。</summary>
public sealed class FailureSummaryHostTests
{
    /// <summary>完整主機缺 Messaging 區段仍可建立，且不發布任何彙總來源。</summary>
    [Fact]
    public async Task FailureSummary_HostWithoutSection_NoSource()
    {
        await using var host = new WebhookTestHost();
        Assert.Null(host.App.Services.GetService<IFailureSummarySource>());
    }

    /// <summary>單一受信任教會查詢精確檢查 UTC 半開窗、固定失敗條件及正式主鍵聚合。</summary>
    [Fact]
    public async Task FailureSummary_Adapter_QueriesOnlyCountsForAuthorizedSenderChurch()
    {
        var service = DispatchProxy.Create<IOrganizationService, SummarySdk>();
        var sdk = (SummarySdk)(object)service;
        var configuration = Settings(sdk.Organization, sdk.Church);
        var services = new ServiceCollection();
        services.AddScoped<IOrganizationService>(_ => service);
        services.AddLineWebhookFailureSummary(configuration);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IFailureSummarySource>();
        var end = new DateTimeOffset(2026, 11, 16, 6, 0, 0, TimeSpan.Zero);
        var row = Assert.Single(await source.ReadAsync(end.AddMinutes(-5), end, CancellationToken.None));
        Assert.Equal(sdk.Church, row.ChurchId);
        Assert.Equal(11, row.Count);
        var query = XDocument.Parse(Assert.Single(sdk.Queries));
        var attribute = Assert.Single(query.Descendants("attribute"));
        Assert.Equal("activityid", (string?)attribute.Attribute("name"));
        Assert.Equal("countcolumn", (string?)attribute.Attribute("aggregate"));
        Assert.Equal("true", (string?)attribute.Attribute("distinct"));
        // D365 9.1 拒絕 link 內引用其他 link alias；跨表來源判斷必須在根 filter。
        Assert.Empty(query.Descendants("link-entity").Descendants("condition").Where(x => x.Attribute("entityname") is not null));
        var conditions = query.Descendants("condition").ToArray();
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "directioncode" && (string?)x.Attribute("value") == "1");
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "statecode" && (string?)x.Attribute("value") == "2");
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "statuscode" && (string?)x.Attribute("value") == "5");
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "modifiedon" && (string?)x.Attribute("operator") == "ge" &&
            DateTimeOffset.Parse((string)x.Attribute("value")!, System.Globalization.CultureInfo.InvariantCulture) == end.AddMinutes(-5));
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "modifiedon" && (string?)x.Attribute("operator") == "lt" &&
            DateTimeOffset.Parse((string)x.Attribute("value")!, System.Globalization.CultureInfo.InvariantCulture) == end);
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "participationtypemask" && (string?)x.Attribute("value") == "1");
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "partyid" && (string?)x.Attribute("value") == sdk.Church.ToString("D"));
        Assert.Contains(conditions, x => (string?)x.Attribute("attribute") == "parentcustomerid" && (string?)x.Attribute("value") == sdk.Church.ToString("D"));
        var reasons = new[] { "SMV:Messaging.Failure.NotBound", "SMV:Messaging.Failure.ChurchMismatch", "SMV:Messaging.Failure.Blocked" };
        Assert.Equal(reasons,
            Assert.Single(conditions, x => (string?)x.Attribute("attribute") == "category").Elements("value").Select(x => x.Value));
    }

    /// <summary>受信任映射與 WhoAmI 不同須零聚合讀取，不從收件者或任意設定猜組織。</summary>
    [Fact]
    public async Task FailureSummary_Adapter_OrganizationMismatch_RefusesBeforeQuery()
    {
        var service = DispatchProxy.Create<IOrganizationService, SummarySdk>();
        var sdk = (SummarySdk)(object)service;
        var services = new ServiceCollection();
        services.AddScoped<IOrganizationService>(_ => service);
        services.AddLineWebhookFailureSummary(Settings(Guid.NewGuid(), sdk.Church));
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IFailureSummarySource>();
        var end = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ReadAsync(end.AddMinutes(-5), end, CancellationToken.None));
        Assert.Empty(sdk.Queries);
    }

    /// <summary>只有合成正式 ID 的受信任映射，不含密碼或 token，也不訂閱組態變更。</summary>
    private static IConfiguration Settings(Guid organization, Guid church) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:FailureSummary:Enabled"] = "true",
            ["LineWebhook:Channels:1234567890:OrganizationId"] = organization.ToString("D"),
            ["LineWebhook:Channels:1234567890:ChurchId"] = church.ToString("D"),
            ["LineWebhook:Channels:1234567890:Organization"] = "synthetic"
        }).Build();

    /// <summary>公開僅因 DispatchProxy 需要；只提供明列三種唯讀 SDK 操作，其餘拒絕。</summary>
    public class SummarySdk : DispatchProxy
    {
        internal readonly Guid Organization = Guid.NewGuid(), Church = Guid.NewGuid(), Channel = Guid.NewGuid();
        internal readonly List<string> Queries = new();

        /// <summary>回傳合成組織、頻道及單一聚合 alias；不模擬產品查詢或通知結果。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == "Execute" && args![0] is WhoAmIRequest)
            {
                var response = new WhoAmIResponse(); response.Results["OrganizationId"] = Organization; return response;
            }
            if (targetMethod.Name == "Retrieve" && (string)args![0]! == "account")
                return new Entity("account", Church) { ["new_linechannelid"] = new EntityReference("new_linechannel", Channel) };
            if (targetMethod.Name == "Retrieve" && (string)args![0]! == "new_linechannel")
                return new Entity("new_linechannel", Channel) { ["new_channelid"] = "1234567890" };
            if (targetMethod.Name == "RetrieveMultiple" && args![0] is FetchExpression fetch)
            {
                Queries.Add(fetch.Query);
                return new EntityCollection(new[] { new Entity("letter") { ["failure_count"] = new AliasedValue("letter", "activityid", 11) } });
            }
            throw new InvalidOperationException("Unexpected summary SDK operation.");
        }
    }
}

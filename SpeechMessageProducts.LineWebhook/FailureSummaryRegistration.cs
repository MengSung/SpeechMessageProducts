/*
 * 檔案整體說明
 * 目的與定位：P5-12 LineWebhook 組合根的真實彙總來源，以受信任 Channels 啟動
 * 快照及每輪 scoped SDK 查外送失敗筆數；Hosting 不反向參考產品或 Dataverse。
 * 主要流程：啟用才驗最多64間、單組織、各教會單一頻道的正式映射；每輪先 WhoAmI，
 * 再驗教會與頻道關聯。FetchXML 只回 activityid distinct count，不投影本文或個資。
 * 時窗為 modifiedon >= start 且 < end；僅方向外送、取消2/5及三個明列 SMV 失敗
 * 類別納入，人工取消不能被當 LINE 失敗。來源由 sender party 的 account 或 contact
 * 正式 parent account 判斷，不從收件者反推。查詢失敗或超平台聚合限制直接失敗，
 * 不截斷或把未知 count 當零；沒有讀到全部活動明細。
 * 資源與隔離：來源僅在當輪 DI scope 借 SDK，所有同步操作前後檢查取消，無
 * Task.Run／WaitAsync 遺棄 I/O。同步 SDK 不能被 token 中斷，硬期限由既有 Gateway
 * 連線傳輸設定擁有；worker 必須等待返回後才清 scope。最多64間、每間四個唯讀
 * 操作，30秒合作預算屆滿不再開下一操作。無 token、Session、歷史快取或背景資源。
 * category 是本輪核定回寫契約；部署仍停用，這個接線不等於 Live 已可推播。
 */
using System.Globalization;
using System.Xml.Linq;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Hosting.Messaging;

namespace SpeechMessageProducts.LineWebhook;

/// <summary>只在產品組合根讀部署映射；不接受 HTTP 租戶路由，也沒有 no-op 來源。</summary>
public static class LineWebhookFailureSummaryRegistration
{
    /// <summary>缺省停用不解析 SDK；啟用時先完整驗快照再發布來源及排程服務。</summary>
    public static IServiceCollection AddLineWebhookFailureSummary(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.GetValue("Messaging:FailureSummary:Enabled", false)) return services;
        var mappings = new List<FailureSummaryMapping>();
        var churches = new HashSet<Guid>();
        foreach (var section in configuration.GetSection("LineWebhook:Channels").GetChildren())
        {
            if (mappings.Count == 64 || !WebhookOptions.IsValidChannelId(section.Key) ||
                !Guid.TryParse(section["OrganizationId"], out var organization) || organization == Guid.Empty ||
                !Guid.TryParse(section["ChurchId"], out var church) || church == Guid.Empty ||
                !churches.Add(church) || (mappings.Count > 0 && mappings[0].OrganizationId != organization))
                throw new InvalidOperationException("Failure summary requires bounded unique single-organization channel mappings.");
            mappings.Add(new FailureSummaryMapping(organization, church, section.Key));
        }
        if (mappings.Count == 0) throw new InvalidOperationException("Failure summary channel mappings are missing.");
        // 驗 options 與重複註冊在 source 發布前完成，避免無效設定留下半套服務。
        services.AddMessageFailureSummary(configuration);
        var snapshot = mappings.ToArray();
        services.AddScoped<IFailureSummarySource>(provider =>
            new LineWebhookFailureSummarySource(provider.GetRequiredService<IOrganizationService>(), snapshot));
        return services;
    }
}

/// <summary>只含受信任部署 ID，陣列由組合根私有保存，不對 request 發布。</summary>
internal sealed record FailureSummaryMapping(Guid OrganizationId, Guid ChurchId, string ChannelId);

/// <summary>當輪唯一 SDK 借用者，無 Dispose 責任；DI scope 擁有 SDK 與 gateway。</summary>
internal sealed class LineWebhookFailureSummarySource(
    IOrganizationService sdk, IReadOnlyList<FailureSummaryMapping> mappings) : IFailureSummarySource
{
    /// <summary>完整驗組織與每間教會後只回純筆數；失敗不發布半套結果或任意來源資料。</summary>
    public Task<IReadOnlyList<FailureSummaryCount>> ReadAsync(
        DateTimeOffset windowStartUtc, DateTimeOffset windowEndUtc, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (windowStartUtc.Offset != TimeSpan.Zero || windowEndUtc.Offset != TimeSpan.Zero ||
                windowEndUtc <= windowStartUtc || windowEndUtc - windowStartUtc > TimeSpan.FromHours(1))
                throw new ArgumentException("Failure summary requires a closed UTC window.");
            var who = sdk.Execute(new WhoAmIRequest()) as WhoAmIResponse;
            ct.ThrowIfCancellationRequested();
            if (who is null || mappings.Any(x => x.OrganizationId != who.OrganizationId))
                throw new InvalidOperationException("Failure summary organization does not match the deployment.");
            var result = new List<FailureSummaryCount>(mappings.Count);
            foreach (var mapping in mappings)
            {
                ct.ThrowIfCancellationRequested();
                var church = sdk.Retrieve("account", mapping.ChurchId, new ColumnSet("new_linechannelid"));
                ct.ThrowIfCancellationRequested();
                var channel = church.GetAttributeValue<EntityReference>("new_linechannelid");
                if (church.Id != mapping.ChurchId || church.LogicalName != "account" ||
                    channel is null || channel.LogicalName != "new_linechannel" || channel.Id == Guid.Empty || channel.KeyAttributes.Count != 0)
                    throw new InvalidOperationException("Failure summary church channel relationship is invalid.");
                var record = sdk.Retrieve("new_linechannel", channel.Id, new ColumnSet("new_channelid"));
                ct.ThrowIfCancellationRequested();
                if (record.Id != channel.Id || record.LogicalName != "new_linechannel" ||
                    record.GetAttributeValue<string>("new_channelid") != mapping.ChannelId)
                    throw new InvalidOperationException("Failure summary channel is not assigned to this church.");
                var rows = sdk.RetrieveMultiple(new FetchExpression(Query(mapping.ChurchId, windowStartUtc, windowEndUtc)));
                ct.ThrowIfCancellationRequested();
                if (rows.MoreRecords || rows.Entities.Count != 1 || rows.Entities[0].Attributes.Count != 1 ||
                    rows.Entities[0].GetAttributeValue<AliasedValue>("failure_count") is not { } alias ||
                    alias.EntityLogicalName != "letter" || alias.AttributeLogicalName != "activityid")
                    throw new InvalidOperationException("Failure summary aggregate response is incomplete.");
                var count = alias.Value switch { int value => (long)value, long value => value,
                    _ => throw new InvalidOperationException("Failure summary aggregate type is invalid.") };
                if (count is < 0 or > 1000000000) throw new InvalidOperationException("Failure summary aggregate count is invalid.");
                result.Add(new FailureSummaryCount(mapping.ChurchId, count));
            }
            return Task.FromResult<IReadOnlyList<FailureSummaryCount>>(result.ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        { return Task.FromCanceled<IReadOnlyList<FailureSummaryCount>>(ct); }
        catch (Exception failure) { return Task.FromException<IReadOnlyList<FailureSummaryCount>>(failure); }
    }

    /// <summary>
    /// 固定查詢骨架只插入已驗 GUID／UTC；countcolumn distinct 僅依正式活動主鍵，
    /// 不依姓名或內容去重。sender 角色1，partyobjecttypecode標準 account=1／contact=2，
    /// 避免不同多型表 GUID 偶合而錯配。跨 link alias 條件放根 filter，因 D365 9.1
    /// 拒絕 link 內引用其他 link。未知寄件者來源不計入，不從 to 反推。
    /// </summary>
    private static string Query(Guid church, DateTimeOffset start, DateTimeOffset finish)
    {
        var id = church.ToString("D");
        var source = new XElement("filter", new XAttribute("type", "or"),
            new XElement("filter", new XAttribute("type", "and"),
                Condition("partyobjecttypecode", "eq", "1", "sender_party"), Condition("partyid", "eq", id, "sender_party")),
            new XElement("filter", new XAttribute("type", "and"),
                Condition("partyobjecttypecode", "eq", "2", "sender_party"),
                new XElement("condition", new XAttribute("entityname", "sender_contact"),
                    new XAttribute("attribute", "parentcustomerid"), new XAttribute("operator", "eq"), new XAttribute("value", id))));
        var party = new XElement("link-entity", new XAttribute("name", "activityparty"),
            new XAttribute("from", "activityid"), new XAttribute("to", "activityid"), new XAttribute("alias", "sender_party"),
            new XElement("filter", Condition("participationtypemask", "eq", "1")),
            new XElement("link-entity", new XAttribute("name", "contact"), new XAttribute("from", "contactid"),
                new XAttribute("to", "partyid"), new XAttribute("alias", "sender_contact"), new XAttribute("link-type", "outer")));
        var entity = new XElement("entity", new XAttribute("name", "letter"),
            new XElement("attribute", new XAttribute("name", "activityid"), new XAttribute("alias", "failure_count"),
                new XAttribute("aggregate", "countcolumn"), new XAttribute("distinct", "true")),
            new XElement("filter", Condition("directioncode", "eq", "1"), Condition("statecode", "eq", "2"),
                Condition("statuscode", "eq", "5"),
                Condition("modifiedon", "ge", start.ToString("O", CultureInfo.InvariantCulture)),
                Condition("modifiedon", "lt", finish.ToString("O", CultureInfo.InvariantCulture)),
                new XElement("condition", new XAttribute("attribute", "category"), new XAttribute("operator", "in"),
                    new XElement("value", "SMV:Messaging.Failure.NotBound"),
                    new XElement("value", "SMV:Messaging.Failure.ChurchMismatch"),
                    new XElement("value", "SMV:Messaging.Failure.Blocked")), source), party);
        return new XElement("fetch", new XAttribute("aggregate", "true"), entity).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>以 XML API 逸出固定欄位及純值，不用未驗證字串拼接 FetchXML。</summary>
    private static XElement Condition(string attribute, string operation, string value, string? entityName = null) =>
        new("condition", new XAttribute("attribute", attribute), new XAttribute("operator", operation),
            new XAttribute("value", value), entityName is null ? null : new XAttribute("entityname", entityName));
}

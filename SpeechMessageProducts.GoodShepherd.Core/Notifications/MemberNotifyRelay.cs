/*
 * 檔案整體說明
 * 目的與定位：階段7沒有安全設定的活動及外送Plugin之Host代送邊界；沙箱只建立
 * 第四類待辦，不讀金鑰或呼叫LINE。Host用相同領域規則重讀來源並送出通知。
 * 主要流程：以正式來源關聯推導教會，固定四欄kind/recordId/message/route不收文字；
 * 活動依流程執行個體產穩定事件ID，外送沿activityId。資料庫唯一鍵提供跨程序防重。
 * Host嚴格解析封閉payload，先重驗來源教會，再以server WhoAmI的組織ID呼叫共用規則；
 * 各規則解密前再次核對來源。成功／永久拒絕完成，429／5xx／傳輸故障交typed例外。
 * 資源與隔離：SDK、push及key只借用至單筆await排空；Host擁有transport期限及key清零。
 * store使用using清借用參照，payload、Entity與結果皆操作局部，不保存跨教會static
 * 狀態、快取、timer或背景工作。錯誤不回顯本文或秘密；未知分類明確失敗不吞錯。
 * 驗證方式：雙目標合成SDK與HTTP底層替身驗防重、來源、訊息等價、拒絕及A/B/A。
 * 這些離線契約不代表D365沙箱部署或LINE實機送達。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LineMessagingProcessor.Core;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Appointments;
using SpeechMessageProducts.GoodShepherd.Core.Fees;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Outbound;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.Outbound;

namespace SpeechMessageProducts.GoodShepherd.Core.Notifications;

/// <summary>固定四種通知來源，僅供封閉數字payload使用，不能接受任意型別名稱。</summary>
public enum MemberNotifyRelayKind
{
    /// <summary>從學員來源推導教會的課程簽核通知。</summary>
    LessonSigning = 1,
    /// <summary>從申請人來源推導教會的預約簽核通知。</summary>
    AppointmentSigning = 2,
    /// <summary>從繳費會友來源推導教會的繳費通知。</summary>
    FeeNotify = 3,
    /// <summary>從寄件者來源推導教會的單人文字外送。</summary>
    OutboundLetter = 4
}

/// <summary>代送可重試傳輸故障；保留HTTP狀態及真原始inner，Host將其轉接成worker重試契約。不輸出例外本文。</summary>
public sealed class MemberNotifyRelayDeliveryException : HttpRequestException
{
    /// <summary>僅由已分類傳輸結果建立，不捏造供應商狀態；沒有回應時狀態為null。</summary>
    internal MemberNotifyRelayDeliveryException(HttpStatusCode? status, Exception? original)
        : base("Member notification delivery requires retry.", original) => ResponseStatus = status;
    /// <summary>真實HTTP回應狀態；傳輸無回應時為null，與timeout不能混為一談。</summary>
    public HttpStatusCode? ResponseStatus { get; }
}

/// <summary>無操作欄位的雙目標代送組合；每筆資料只屬本次入列或執行。</summary>
public static class MemberNotifyRelay
{
    /// <summary>以來源正式關聯入列；不查收件者頻道、不解密或推播。入站／已終止外送回null；其餘回資料庫正式待辦ID。</summary>
    /// <param name="sdk">平台或Host已授權固定組織的借用服務。</param>
    /// <param name="kind">封閉通知種類。</param>
    /// <param name="recordId">來源正式主鍵，不接受顯示文字或替代鍵。</param>
    /// <param name="message">1 ExecuteWorkflow、2 Create、3 Update。</param>
    /// <param name="route">已按原Contains優先序解析的數字。</param>
    /// <param name="instance">OperationId，空時由外殼改用CorrelationId；仍空即拒絕。</param>
    public static Guid? Enqueue(IOrganizationService sdk, MemberNotifyRelayKind kind, Guid recordId, int message, int route, Guid instance)
    {
        Validate(kind, recordId, message, route);
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#else
        ArgumentNullException.ThrowIfNull(sdk);
#endif
        if (kind != MemberNotifyRelayKind.OutboundLetter && instance == Guid.Empty)
            throw new ArgumentException("A workflow execution identity is required.", nameof(instance));
        var church = SourceChurch(sdk, kind, recordId, CancellationToken.None);
        if (!church.HasValue) return null;
        var sourceId = kind == MemberNotifyRelayKind.OutboundLetter ? recordId :
            DeterministicGuid.Create("notify.relay.v1", (int)kind, recordId, message, route, instance);
        using var store = new D365OutboundEnqueueStore(sdk);
        return new OutboundTaskWriter(store).Enqueue(OutboundTaskType.MemberNotifyRelay, church.Value, null, sourceId,
            new Dictionary<string, object> { ["kind"] = (int)kind, ["recordId"] = recordId, ["message"] = message, ["route"] = route });
    }

    /// <summary>解析封閉四欄並重讀來源，教會不符不解密；借用key至通知排空，不清理caller所有的key或SDK。</summary>
    public static async Task ExecuteAsync(IOrganizationService sdk, ILineMemberPushClient push, Guid churchId,
        string payloadJson, byte[] tokenKey, CancellationToken ct, ITextCatalog? catalog = null,
        string textProduct = "GoodShepherd")
    {
#if NETFRAMEWORK
        if (sdk is null) throw new ArgumentNullException(nameof(sdk));
#else
        ArgumentNullException.ThrowIfNull(sdk);
#endif
#if NETFRAMEWORK
        if (push is null) throw new ArgumentNullException(nameof(push));
#else
        ArgumentNullException.ThrowIfNull(push);
#endif
        if (churchId == Guid.Empty) throw new ArgumentException("A church identity is required.", nameof(churchId));
        ct.ThrowIfCancellationRequested();
        var payload = Parse(payloadJson);
        var kind = (MemberNotifyRelayKind)Integer(payload, "kind");
        if (payload["recordId"]?.Type != JTokenType.String || !Guid.TryParseExact((string?)payload["recordId"], "D", out var recordId))
            throw new ArgumentException("Relay record identity is invalid.", nameof(payloadJson));
        var message = Integer(payload, "message"); var route = Integer(payload, "route");
        Validate(kind, recordId, message, route);
        var sourceChurch = SourceChurch(sdk, kind, recordId, ct);
        if (!sourceChurch.HasValue || sourceChurch.Value != churchId) return;
        if (tokenKey is null || tokenKey.Length != 64) throw new ArgumentException("A token key is required.", nameof(tokenKey));
        ct.ThrowIfCancellationRequested();
        if (sdk.Execute(new WhoAmIRequest()) is not WhoAmIResponse who || who.OrganizationId == Guid.Empty)
            throw new InvalidOperationException("Server organization identity is unavailable.");
        ct.ThrowIfCancellationRequested();
        LinePushResult? result;
        switch (kind)
        {
            case MemberNotifyRelayKind.LessonSigning:
                result = await LessonSigningDispatch.NotifyRouteAsync(sdk, push, recordId, who.OrganizationId, route, null, tokenKey, churchId, ct).ConfigureAwait(false); break;
            case MemberNotifyRelayKind.AppointmentSigning:
                result = await AppointmentSigningRule.NotifyRouteAsync(sdk, push, recordId, who.OrganizationId, route, null, tokenKey, churchId, ct).ConfigureAwait(false); break;
            case MemberNotifyRelayKind.FeeNotify:
                result = await FeeNotificationService.NotifyRouteAsync(sdk, push, recordId, who.OrganizationId,
                    route, churchId, tokenKey, catalog, textProduct, ct).ConfigureAwait(false); break;
            default:
                await OutboundLetterRecords.SendAsync(sdk, push, recordId, who.OrganizationId, churchId, tokenKey, ct).ConfigureAwait(false); return;
        }
        if (result is null) return;
        ThrowIfRetryable(result);
        if (result.Success || result.Reason is "NotBound" or "ChurchMismatch" ||
            result.Outcome == LinePushOutcome.Permanent && result.StatusCode is { } code && (int)code is >= 400 and <= 499) return;
        throw new InvalidOperationException("Unexpected relay notification classification.");
    }

    /// <summary>只把真可重試結果交Host，保留原始傳輸inner與實際狀態，沒有把拒絕偽造為200。</summary>
    internal static void ThrowIfRetryable(LinePushResult result)
    {
        if (result.Outcome == LinePushOutcome.Retryable)
            throw new MemberNotifyRelayDeliveryException(result.StatusCode, result.TransportException);
    }

    /// <summary>嚴格四欄、無重複鍵與尾隨JSON；最大4000字／深度4，未知輸入在任何SDK之前拒絕。</summary>
    private static JObject Parse(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > 4000) throw new ArgumentException("Relay payload is invalid.", nameof(json));
        try
        {
            using var text = new StringReader(json);
            using var reader = new JsonTextReader(text) { MaxDepth = 4, DateParseHandling = DateParseHandling.None };
            var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
            if (reader.Read() || value.Count != 4 || value.Properties().Any(p => p.Name is not ("kind" or "recordId" or "message" or "route")))
                throw new ArgumentException("Relay payload schema is invalid.", nameof(json));
            return value;
        }
        catch (JsonException) { throw new ArgumentException("Relay payload JSON is invalid.", nameof(json)); }
    }

    /// <summary>只讀真正整數token，拒絕字串、浮點及超出Int32的數字。</summary>
    private static int Integer(JObject value, string field)
    {
        var token = value[field];
        if (token?.Type != JTokenType.Integer || !int.TryParse(token.ToString(), System.Globalization.NumberStyles.AllowLeadingSign,
                System.Globalization.CultureInfo.InvariantCulture, out var number)) throw new ArgumentException("Relay number is invalid.");
        return number;
    }

    /// <summary>種類與分派必須相容；課程沒有付款route，Fee與外送固定0。</summary>
    private static void Validate(MemberNotifyRelayKind kind, Guid id, int message, int route)
    {
        if (id == Guid.Empty || message is < 1 or > 3 || kind is < MemberNotifyRelayKind.LessonSigning or > MemberNotifyRelayKind.OutboundLetter ||
            (kind == MemberNotifyRelayKind.LessonSigning ? route is not (1 or 2) : kind == MemberNotifyRelayKind.AppointmentSigning ? route is < 1 or > 3 : route != 0))
            throw new ArgumentException("Relay dispatch is invalid.");
    }

    /// <summary>從學員／申請人／繳費會友／寄件者推導教會；收件者不是權威，入站或已終止letter略過。</summary>
    private static Guid? SourceChurch(IOrganizationService sdk, MemberNotifyRelayKind kind, Guid id, CancellationToken ct)
    {
        Entity row; EntityReference owner;
        switch (kind)
        {
            case MemberNotifyRelayKind.LessonSigning:
                row = Read(sdk,"new_stor_lessons",id,ct,"new_contact_new_stor_lessons"); owner = Lookup(row,"new_contact_new_stor_lessons","contact"); break;
            case MemberNotifyRelayKind.AppointmentSigning:
                row = Read(sdk,"appointment",id,ct,"new_applier_appointment"); owner = Lookup(row,"new_applier_appointment","contact"); break;
            case MemberNotifyRelayKind.FeeNotify:
                row = Read(sdk,"new_fee",id,ct,"new_contact_new_fee"); owner = Lookup(row,"new_contact_new_fee","contact"); break;
            default:
                row = Read(sdk,"letter",id,ct,"directioncode","statecode","from","new_message_category");
                if (row.GetAttributeValue<bool?>("directioncode") != true) return null;
                var state = row.GetAttributeValue<OptionSetValue>("statecode") ?? throw new InvalidOperationException("Letter state is required.");
                if (state.Value != 0) return null;
                if (row.GetAttributeValue<OptionSetValue>("new_message_category") is { Value: not 100000000 }) throw new InvalidOperationException("Unsupported letter media.");
                var parties = row.GetAttributeValue<EntityCollection>("from");
                if (parties is null || parties.Entities.Count != 1 || parties.Entities[0].LogicalName != "activityparty") throw new InvalidOperationException("A sender is required.");
                owner = Lookup(parties.Entities[0],"partyid",null);
                if (owner.LogicalName == "account") return owner.Id;
                if (owner.LogicalName != "contact") throw new InvalidOperationException("Sender scope is unavailable."); break;
        }
        var contact = Read(sdk,"contact",owner.Id,ct,"parentcustomerid");
        return Lookup(contact,"parentcustomerid","account").Id;
    }

    /// <summary>同步SDK前後檢查caller取消，驗正式回應ID，不建立可提前歸還SDK的背景Task。</summary>
    private static Entity Read(IOrganizationService sdk, string table, Guid id, CancellationToken ct, params string[] columns)
    {
        ct.ThrowIfCancellationRequested(); var row = sdk.Retrieve(table,id,new ColumnSet(columns)); ct.ThrowIfCancellationRequested();
        if (row is null || row.Id != id || row.LogicalName != table || row.KeyAttributes.Count != 0) throw new InvalidOperationException("Relay source identity is invalid.");
        return row;
    }

    /// <summary>只接受精確實體及正式非空主鍵，拒絕名稱或alternate key推導。</summary>
    private static EntityReference Lookup(Entity row, string field, string? table)
    {
        var reference = row.GetAttributeValue<EntityReference>(field);
        if (reference is null || reference.Id == Guid.Empty || reference.KeyAttributes.Count != 0 || table is not null && reference.LogicalName != table)
            throw new InvalidOperationException("Relay source relationship is invalid.");
        return reference;
    }
}

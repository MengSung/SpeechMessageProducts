/*
 * 檔案整體說明
 *
 * 目的與定位：P5-08 預約簽核的 Core 規則，供 Plugin 與 CodeActivity 共用。
 * 本檔只接收平台已借用的 IOrganizationService、正式 appointment ID、純值模式
 * 與通知介面，不認識 PluginInput、Workflow context 或外殼欄位。
 *
 * 主要功能與流程：讀取最小 appointment 欄位，確認 new_signing_contact 是正式 contact，
 * 先依舊訊息／參數 Contains 分派，再以 new_meeting_kind 決定標題。從正式申請人
 * 獨立推導來源教會、重驗簽核者同教會及綁定，才查有界年度休假摘要並組合 Carousel。
 * 內文保留 new_signing_content，最後經 MemberNotificationService 的共用入口。
 * Shadow 只記推播意圖；Live 的 key 由外殼提供且在本檔不保存。
 *
 * 使用邊界與維護：所有 SDK、source、訊息與 key 僅屬單次同步操作，不使用 static
 * 業務狀態、Session、cache、timer 或無界背景工作。LineRecipientQueries 由本次
 * 呼叫擁有並在通知排空後 Dispose；key 的清零責任仍由外殼持有。跨教會／未綁定／
 * 缺少簽核者時由共用服務 fail-closed。測試為離線契約，不宣稱實機送達。
 */
using System;
using SigningText = SpeechMessageProducts.GoodShepherd.Core.Lessons.LessonText;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessageProducts.GoodShepherd.Core.Appointments;

/// <summary>預約簽核的無狀態 Core 分派與通知規則。</summary>
public static class AppointmentSigningRule
{
    private const string Appointment = "appointment";
    private const string Contact = "contact";
    private const string ImageUrl = "https://web.opendrive.com/api/v1/download/file.json/ODdfMzk3Nzc5Nl8?inline=1";

    /// <summary>活動／Plugin 的固定訊息入口名稱，供測試及外殼保持相同契約。</summary>
    public static string DefaultParameter(string message) => message switch
    {
        "ExecuteWorkflow" => SigningText.Get("Appointments.Signing.WorkflowParameter"),
        // 退役 Plugin 的 MajorExecute 實際收到空字串，不能自行補入關鍵字而新增通知。
        "Create" or "Update" => string.Empty,
        _ => throw new InvalidPluginExecutionException("Appointment signing message is unsupported.")
    };

    /// <summary>
    /// 讀取並通知下一位正式簽核者。Live 與 Shadow 僅在最後通知模式不同，
    /// 查詢、收件者隔離及訊息組合保持一致。
    /// </summary>
    public static async Task NotifyAsync(IOrganizationService organization, ILineMemberPushClient push,
        Guid appointmentId, Guid organizationId, string message, string? parameter,
        IShadowPushSink? shadowSink, byte[]? key, CancellationToken ct)
    {
        ValidateInputs(organization, push, appointmentId, organizationId, shadowSink, key, ct);
        if (!TryRoute(message, parameter, out var route)) return;
        var result = await NotifyRouteAsync(organization, push, appointmentId, organizationId, route, shadowSink, key, null, ct).ConfigureAwait(false);
        if (result is null) return;
        if (result.TransportException is { } transport) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(transport).Throw();
        if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
            throw new InvalidPluginExecutionException("Appointment signing notification failed.");
    }

    /// <summary>已解析分派的共用入口；重讀來源後先驗expectedChurch再查頻道及解密。回傳真傳輸結果，由呼叫邊界決定重試；key只借用至await排空。</summary>
    public static async Task<LinePushResult?> NotifyRouteAsync(IOrganizationService organization, ILineMemberPushClient push, Guid appointmentId,
        Guid organizationId, int route, IShadowPushSink? shadowSink, byte[]? key, Guid? expectedChurch, CancellationToken ct)
    {
        ValidateInputs(organization, push, appointmentId, organizationId, shadowSink, key, ct);
        if (route is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(route));
        var appointment = organization.Retrieve(Appointment, appointmentId, new ColumnSet(
            "new_signing_contact", "new_signing_content", "new_meeting_kind", "new_applier_appointment"));
        ValidateRow(appointment, Appointment, appointmentId);
        var signer = Lookup(appointment, "new_signing_contact", Contact);
        var applicantId = Lookup(appointment, "new_applier_appointment", Contact);
        var applicant = organization.Retrieve(Contact, applicantId, new ColumnSet(
            "fullname", "parentcustomerid", "new_special_leave_days"));
        ValidateRow(applicant, Contact, applicantId);
        var church = Lookup(applicant, "parentcustomerid", "account");
        if (expectedChurch.HasValue && church != expectedChurch.Value) return null;
        using var source = new LineRecipientQueries(organization);
        var recipient = source.ReadMember(signer, ct);
        // 來源 scope 必須先於 Channel／摘要查詢成立，不能用 signer 自己的教會反向授權。
        if (LinePushClient.ValidateRecipient(recipient, church) is { } rejected) return rejected;
        var subject = Option(appointment, "new_meeting_kind") switch
        {
            6 => SigningText.Get("Appointments.Signing.LeaveSubject"),
            4 => SigningText.Get("Appointments.Signing.LocationSubject"),
            _ => SigningText.Get("Appointments.Signing.GeneralSubject")
        };
        var contact = organization.Retrieve(Contact, signer, new ColumnSet("fullname"));
        ValidateRow(contact, Contact, signer);
        var reference = (EntityReference)appointment["new_signing_contact"];
        var signerName = string.IsNullOrWhiteSpace(reference.Name)
            ? OptionalText(contact, "fullname", SigningText.Get("Appointments.Signing.UnnamedSigner")) : reference.Name!;
        var content = OptionalText(appointment, "new_signing_content", string.Empty);
        if (subject == SigningText.Get("Appointments.Signing.LeaveSubject")) content += LeaveSummary(organization, applicant, ct);
        if (content.Length > 2000)
            throw new InvalidPluginExecutionException("Appointment signing content exceeds capacity.");
        var account = organization.Retrieve("account", church, new ColumnSet("new_defaultlanguage"));
        ValidateRow(account, "account", church);
        var culture = OptionalText(account, "new_defaultlanguage", "zh-TW");
        var context = new TextContext(church, culture, culture, organizationId.ToString("D"), "GoodShepherd");
        var notifications = new MemberNotificationService(push,
            new MemberCultureResolver(new SpeechMessage.Localization.LocalizationOptions()),
            new GoodShepherdTextCatalog(new NoOverrides()));
        IReadOnlyList<ISendMessage> Compose(TextContext _) =>
            new ISendMessage[] { AppointmentSigningMessageFactory.Create(
                new AppointmentSigningMessageInput(appointmentId, signer, subject, content,
                    OptionalText(applicant, "fullname", string.Empty), signerName, Image(route))) };
        var retryKey = DeterministicGuid.Create("appointments.signing", appointmentId, signer);
        var result = shadowSink is not null
            ? await notifications.NotifyComposedAsync(source, context, signer, retryKey,
                "Appointments.Signing", Compose, shadowSink, ct).ConfigureAwait(false)
            : await notifications.NotifyComposedAsync(source, context, signer, retryKey,
                "Appointments.Signing", Compose, key!, ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>保留原入口在分派前的依賴、正式身分、互斥模式及取消驗證；不讀SDK、不保存任何狀態。</summary>
    private static void ValidateInputs(IOrganizationService organization, ILineMemberPushClient push, Guid appointmentId,
        Guid organizationId, IShadowPushSink? shadowSink, byte[]? key, CancellationToken ct)
    {
#if NETFRAMEWORK
        if (organization is null) throw new ArgumentNullException(nameof(organization));
        if (push is null) throw new ArgumentNullException(nameof(push));
#else
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(push);
#endif
        if (appointmentId == Guid.Empty || organizationId == Guid.Empty)
            throw new InvalidPluginExecutionException("Appointment signing identity is invalid.");
        if ((shadowSink is null) == (key is null) || (key is not null && key.Length != 64))
            throw new InvalidPluginExecutionException("Appointment signing secure mode is unavailable.");
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 只查正式申請人今年且位於三個舊簽核狀態的休假，完整候選最多 256 列。
    /// 回應分頁、超量、重複正式 ID 或錯誤申請人一律拒絕，不發布部分統計。
    /// 年度採 Dataverse ThisYear 的原語意，不冒稱已改成教會時區年度。
    /// 字典、順序及文字都屬此次呼叫，取消／失敗不保留給下次 A/B/A 執行。
    /// </summary>
    private static string LeaveSummary(IOrganizationService organization, Entity applicant, CancellationToken ct)
    {
        var query = new QueryExpression(Appointment)
        {
            ColumnSet = new ColumnSet("activityid", "new_applier_appointment", "new_leave_kind", "new_leave_signing_status", "new_days"),
            PageInfo = new PagingInfo { Count = 257, PageNumber = 1 }
        };
        query.Criteria.AddCondition("new_applier_appointment", ConditionOperator.Equal, applicant.Id);
        query.Criteria.AddCondition("scheduledstart", ConditionOperator.ThisYear);
        query.Criteria.AddCondition("new_leave_signing_status", ConditionOperator.In, 100000004, 100000001, 100000007);
        query.AddOrder("subject", OrderType.Ascending);
        query.AddOrder("activityid", OrderType.Ascending);
        ct.ThrowIfCancellationRequested();
        var rows = organization.RetrieveMultiple(query);
        ct.ThrowIfCancellationRequested();
        if (rows is null || rows.MoreRecords || rows.Entities.Count > 256)
            throw new InvalidPluginExecutionException("Appointment leave summary is incomplete.");
        var name = OptionalText(applicant, "fullname", string.Empty);
        var prefix = "\r\n------------------------ \r\n" + name;
        if (rows.Entities.Count == 0) return prefix + SigningText.Get("Appointments.Signing.NoLeaveRecords");
        var ids = new HashSet<Guid>();
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var row in rows.Entities)
        {
            ct.ThrowIfCancellationRequested();
            if (row.LogicalName != Appointment || row.Id == Guid.Empty || !ids.Add(row.Id) ||
                Lookup(row, "new_applier_appointment", Contact) != applicant.Id)
                throw new InvalidPluginExecutionException("Appointment leave summary identity is invalid.");
            var status = Option(row, "new_leave_signing_status") switch
            {
                100000001 => SigningText.Get("Appointments.Signing.PendingDeputy"),
                100000004 => SigningText.Get("Appointments.Signing.PendingManager"),
                100000007 => SigningText.Get("Appointments.Signing.ApprovedManager"),
                _ => throw new InvalidPluginExecutionException("Appointment leave summary status is invalid.")
            };
            var leave = Option(row, "new_leave_kind") switch
            {
                2 => SigningText.Get("Appointments.Signing.LeaveRest"), 3 => SigningText.Get("Appointments.Signing.LeaveSpecial"), 4 => SigningText.Get("Appointments.Signing.LeaveSick"), 5 => SigningText.Get("Appointments.Signing.LeavePersonal"),
                6 => SigningText.Get("Appointments.Signing.LeaveOfficial"), 7 => SigningText.Get("Appointments.Signing.LeaveMarriage"), 8 => SigningText.Get("Appointments.Signing.LeaveBereavement"), _ => SigningText.Get("Appointments.Signing.LeaveUnset")
            };
            var days = !row.Attributes.TryGetValue("new_days", out var value) ? 0d : value is double number
                ? number : throw new InvalidPluginExecutionException("Appointment leave summary days are invalid.");
            var group = leave + ":" + status;
            if (!totals.TryGetValue(group, out var total)) order.Add(group);
            total += days;
            if (double.IsNaN(total) || double.IsInfinity(total))
                throw new InvalidPluginExecutionException("Appointment leave summary days are invalid.");
            totals[group] = total;
        }
        var entitled = !applicant.Attributes.TryGetValue("new_special_leave_days", out var entitlement) ? 0 :
            entitlement is int count ? count : throw new InvalidPluginExecutionException("Appointment leave entitlement is invalid.");
        var report = new StringBuilder(prefix).Append(SigningText.Get("Appointments.Signing.LeaveReportHeading"))
            .Append(entitled.ToString(CultureInfo.InvariantCulture)).Append(SigningText.Get("Appointments.Signing.DaysSeparator"));
        foreach (var group in order)
            report.Append(group).Append('=').Append(totals[group].ToString(CultureInfo.InvariantCulture)).Append(SigningText.Get("Appointments.Signing.DaysSuffix"));
        return report.ToString();
    }

    /// <summary>只將原Contains關卡及圖片優先序轉為數字，不把流程文字放進待辦；未匹配不查SDK也不入列。</summary>
    public static bool TryRoute(string message, string? parameter, out int route)
    {
        route = 0;
        if (parameter is null || parameter.Length > 2000) throw new InvalidPluginExecutionException("Appointment signing parameter is invalid.");
        if (message is not ("ExecuteWorkflow" or "Create" or "Update")) throw new InvalidPluginExecutionException("Appointment signing message is unsupported.");
        var keyword = message == "ExecuteWorkflow" ? SigningText.Get("Appointments.Signing.WorkflowParameter") : message == "Create" ? SigningText.Get("Lessons.Signing.CreateParameter") : SigningText.Get("Lessons.Signing.UpdateParameter");
        if (!Contains(parameter, keyword)) return false;
        route = Contains(parameter, SigningText.Get("Appointments.Signing.TpeImageParameter")) ? 2 :
            Contains(parameter, SigningText.Get("Appointments.Signing.AbundanceImageParameter")) ? 3 : 1;
        return true;
    }

    /// <summary>活動沿舊參數的圖片選擇，字串只分派樣式，不提供授權或組織 scope。</summary>
    private static string Image(int route) =>
        route == 2
            ? "https://od.lk/s/ODdfNTg5ODc5OF8/2017_06_sermon_6-18.jpg"
            : route == 3
                ? "https://web.opendrive.com/api/v1/download/file.json/ODdfMTc5OTg5NTNf?inline=1" : ImageUrl;

    /// <summary>在雙目標使用 ordinal 分派，不能讓執行環境文化改變舊關鍵字語意。</summary>
    private static bool Contains(string value, string keyword)
    {
#if NETFRAMEWORK
        return value.IndexOf(keyword, StringComparison.Ordinal) >= 0;
#else
        return value.Contains(keyword, StringComparison.Ordinal);
#endif
    }

    /// <summary>SDK 回應須對應本次正式表與 ID，不能把其他來源誤當已驗證候選。</summary>
    private static void ValidateRow(Entity row, string table, Guid id)
    {
        if (row is null || row.LogicalName != table || row.Id != id)
            throw new InvalidPluginExecutionException("Appointment signing source identity is invalid.");
    }

    /// <summary>只接受精確表名、正式 ID、無 alternate key 的 lookup。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference &&
        reference.LogicalName == table && reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0
            ? reference.Id : throw new InvalidPluginExecutionException("Appointment signing source scope is invalid.");

    /// <summary>缺省 option set 視為未填，其他型別拒絕而不猜測。</summary>
    private static int Option(Entity row, string field) =>
        !row.Attributes.TryGetValue(field, out var value) ? 0 :
        value is OptionSetValue option ? option.Value :
        throw new InvalidPluginExecutionException("Appointment signing option is invalid.");

    /// <summary>選用文字缺省為固定值；錯誤型別或超長拒絕，不能靜默截斷簽核本文。</summary>
    private static string OptionalText(Entity row, string field, string fallback)
    {
        if (!row.Attributes.TryGetValue(field, out var value) || value is null) return fallback;
        return value is string text && text.Length <= 2000 ? text :
            throw new InvalidPluginExecutionException("Appointment signing source text is invalid.");
    }

    /// <summary>本模組暫使用空覆寫快照，避免把組織狀態存入服務欄位。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>回傳本次 context 的空快照。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

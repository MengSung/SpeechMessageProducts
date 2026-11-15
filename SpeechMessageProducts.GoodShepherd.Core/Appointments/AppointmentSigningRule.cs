/*
 * 檔案整體說明
 *
 * 目的與定位：P5-08 預約簽核的 Core 規則，供 Plugin 與 CodeActivity 共用。
 * 本檔只接收平台已借用的 IOrganizationService、正式 appointment ID、純值模式
 * 與通知介面，不認識 PluginInput、Workflow context 或外殼欄位。
 *
 * 主要功能與流程：讀取最小 appointment 欄位，確認 new_signing_contact 是正式 contact，
 * 依 new_leave_kind／new_location_kind 判定請假或場地簽核；再讀簽核者、其教會
 * 與顯示名稱，組合固定 Flex，最後經 MemberNotificationService 的共用入口。
 * Shadow 只記推播意圖；Live 的 key 由外殼提供且在本檔不保存。
 *
 * 使用邊界與維護：所有 SDK、source、訊息與 key 僅屬單次同步操作，不使用 static
 * 業務狀態、Session、cache、timer 或無界背景工作。LineRecipientQueries 由本次
 * 呼叫擁有並在通知排空後 Dispose；key 的清零責任仍由外殼持有。跨教會／未綁定／
 * 缺少簽核者時由共用服務 fail-closed。測試為離線契約，不宣稱實機送達。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
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
        "ExecuteWorkflow" => "工作流程",
        "Create" => "新增",
        "Update" => "更新",
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
        if (parameter is null || parameter.Length > 2000)
            throw new InvalidPluginExecutionException("Appointment signing parameter is invalid.");
        if (message is not ("ExecuteWorkflow" or "Create" or "Update"))
            throw new InvalidPluginExecutionException("Appointment signing message is unsupported.");
        ct.ThrowIfCancellationRequested();

        var appointment = organization.Retrieve(Appointment, appointmentId, new ColumnSet(
            "new_signing_contact", "new_signing_content", "new_leave_kind", "new_location_kind",
            "scheduledstart", "scheduledend", "description"));
        var signer = Lookup(appointment, "new_signing_contact", Contact);
        var leave = Option(appointment, "new_leave_kind");
        var location = Option(appointment, "new_location_kind");
        var subject = Subject(leave, location);
        if (subject is null) return;

        var contact = organization.Retrieve(Contact, signer, new ColumnSet(
            "fullname", "parentcustomerid"));
        var church = Lookup(contact, "parentcustomerid", "account");
        var signerName = Text(contact, "fullname");
        var content = Content(appointment, subject);
        var context = new TextContext(church, "zh-TW", "zh-TW", organizationId.ToString("D"), "GoodShepherd");
        using var source = new LineRecipientQueries(organization);
        var notifications = new MemberNotificationService(push,
            new MemberCultureResolver(new SpeechMessage.Localization.LocalizationOptions()),
            new GoodShepherdTextCatalog(new NoOverrides()));
        IReadOnlyList<ISendMessage> Compose(TextContext _) =>
            new ISendMessage[] { AppointmentSigningMessageFactory.Create(
                new AppointmentSigningMessageInput(appointmentId, signer, subject, content, signerName, signerName, ImageUrl)) };
        var retryKey = DeterministicGuid.Create("appointments.signing", appointmentId, signer);
        var result = shadowSink is not null
            ? await notifications.NotifyComposedAsync(source, context, signer, retryKey,
                "Appointments.Signing", Compose, shadowSink, ct).ConfigureAwait(false)
            : await notifications.NotifyComposedAsync(source, context, signer, retryKey,
                "Appointments.Signing", Compose, key!, ct).ConfigureAwait(false);
        if (result.TransportException is { } transport)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(transport).Throw();
        if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
            throw new InvalidPluginExecutionException("Appointment signing notification failed.");
    }

    /// <summary>依退役 option set 保留請假／場地簽核的分派，其他組合不通知。</summary>
    private static string? Subject(int leave, int location)
    {
        var hasLeave = leave > 1;
        var hasLocation = location > 100000000;
        if (hasLeave && !hasLocation) return "請假簽核";
        if (!hasLeave && hasLocation) return "場地或資源簽核";
        return null;
    }

    /// <summary>保留舊簽核內容並附上有界的時間／描述欄位，不查詢其他資料列。</summary>
    private static string Content(Entity appointment, string subject)
    {
        var content = OptionalText(appointment, "new_signing_content", "未提供簽核內容");
        if (subject == "請假簽核")
        {
            content += "\r\n開始時間: " + OptionalDate(appointment, "scheduledstart") +
                "\r\n結束時間: " + OptionalDate(appointment, "scheduledend");
        }
        var description = OptionalText(appointment, "description", string.Empty);
        if (description.Length > 0) content += "\r\n描述: " + description;
        return content.Length <= 2000 ? content : content.Substring(0, 2000);
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

    /// <summary>只接受有界純文字，避免將錯誤內容回顯至平台。</summary>
    private static string Text(Entity row, string field) => OptionalText(row, field, "未命名簽核者");

    /// <summary>選用欄位可缺省，但仍限制單次訊息的最大保留量。</summary>
    private static string OptionalText(Entity row, string field, string fallback) =>
        row.Attributes.TryGetValue(field, out var value) && value is string text &&
        text.Length <= 2000 ? text : fallback;

    /// <summary>日期僅格式化當次值，不保存 DateTime 或跨操作時區狀態。</summary>
    private static string OptionalDate(Entity row, string field) =>
        row.Attributes.TryGetValue(field, out var value) && value is DateTime date
            ? date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "未提供";

    /// <summary>本模組暫使用空覆寫快照，避免把組織狀態存入服務欄位。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>回傳本次 context 的空快照。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

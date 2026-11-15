/*
 * 檔案整體說明
 * 目的與定位：P5-06出席牧者回應通知的PostOperation非同步Plugin薄外殼。
 * 主要功能與流程：Update須命中回應欄位及必要PreImage，重讀正式出席列並以小組
 * list→組長contact→account決定來源教會，再驗本人contact歸屬與綁定；規則只組純文字，
 * 通知經MemberNotificationService.NotifyComposedAsync重驗語系及收件者。Create保留舊無效果，
 * Depth>2且非Create返回。異常保留原例外交平台，正常未綁定／錯教會不通知。
 * 使用邊界與維護：外殼欄位只有無狀態策略；SDK、context、出席、回應與key皆限一次Execute。
 * adapter借用平台SDK並在using清除參照；Live的64-byte key在finally清零，Shadow不讀key、
 * 不解密或真正發送。HTTP十秒deadline與drain由共用client擁有，Execute同步等待完成，
 * 沒有背景Task、timer、queue、Session或跨教會快取。本階段不得以Live啟用。
 */
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.WeeklyReports;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Attendance;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessage.Dynamics.Plugins.Attendance;

/// <summary>一次只通知正式出席列的本人，不保存SDK、文字或身分供其他Execute使用。</summary>
public sealed class PresentRecordPlugin : PluginShellBase
{
    /// <summary>只保存無操作狀態通知策略，傳輸連線由共用client owner擁有。</summary>
    private readonly MemberNotificationService notifications;
    /// <summary>平台入口，只解析不可變註冊設定，不建立租戶或使用者scope。</summary>
    /// <param name="unsecureConfig">明確Shadow／Live模式。</param><param name="secureConfig">Live專用Base64 key，Shadow空白。</param>
    public PresentRecordPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(2, DepthException.Create))
    {
        notifications = new MemberNotificationService(new LinePushClient(),
            new MemberCultureResolver(new LocalizationOptions()), new GoodShepherdTextCatalog(new NoOverrides()));
    }
    /// <summary>驗證來源教會鏈後同步排空唯一本人通知，借用SDK但不Dispose平台服務。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_present_record" || input.MessageName is not ("Create" or "Update")) return;
        if (input.Stage != 40 || input.Mode != 1) throw WeeklyReportShellData.Invalid();
        if (input.MessageName == "Create") return;
        var target = input.Target;
        WeeklyReportShellData.RequireIdentity(target, "new_present_record", input.PrimaryEntityId);
        if (!target!.Contains("new_paster_reply")) return;
        var pre = input.PreImage;
        WeeklyReportShellData.RequireIdentity(pre, "new_present_record", input.PrimaryEntityId);
        if (input.OrganizationId == Guid.Empty) throw WeeklyReportShellData.Invalid();
        var record = WeeklyReportShellData.Retrieve(services.Organization, "new_present_record", input.PrimaryEntityId,
            new ColumnSet("new_paster_reply", "new_explanation", "new_contact_new_present_record", "new_list_new_present_record"));
        var prior = WeeklyReportShellData.Text(pre!, "new_paster_reply", 2000);
        var current = WeeklyReportShellData.Text(record, "new_paster_reply", 2000);
        if (string.Equals(prior, current, StringComparison.Ordinal)) return;
        var contactId = WeeklyReportShellData.Lookup(record, "new_contact_new_present_record", "contact");
        var list = WeeklyReportShellData.Retrieve(services.Organization, "list",
            WeeklyReportShellData.Lookup(record, "new_list_new_present_record", "list"), new ColumnSet("new_contact_family_leader_list"));
        var church = WeeklyReportShellData.Church(services.Organization, list);
        using var source = new LineRecipientQueries(services.Organization);
        var member = source.ReadMember(contactId, CancellationToken.None);
        if (LinePushClient.ValidateRecipient(member, church) is not null) return;
        var account = WeeklyReportShellData.Retrieve(services.Organization, "account", church, new ColumnSet("new_defaultlanguage"));
        var culture = WeeklyReportShellData.Text(account, "new_defaultlanguage", 128);
        if (string.IsNullOrWhiteSpace(culture)) culture = "zh-TW";
        var context = new TextContext(church, culture!, culture!, input.OrganizationId.ToString("D"), "GoodShepherd");
        var name = record.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("new_contact_new_present_record").Name;
        var prayer = WeeklyReportShellData.Text(record, "new_explanation", 2000);
        IReadOnlyList<ISendMessage> Compose(TextContext resolved)
        {
            var text = PresentRecordNotificationRule.Evaluate(input.PrimaryEntityId, input.MessageName,
                prior, current, name, prayer, PresentRecordNotificationRule.Template(resolved.TextCulture));
            return new ISendMessage[] { new TextMessage(text!) };
        }
        byte[]? key = null;
        try
        {
            var retryKey = PresentRecordNotificationRule.RetryKey(input.PrimaryEntityId);
            LinePushResult result;
            if (services.IsShadow)
                result = PluginServices.Wait(notifications.NotifyComposedAsync(source, context, contactId, retryKey,
                    "Attendance.PastorReply", Compose, services.ShadowPush!, CancellationToken.None));
            else
            {
                key = DecodeKey(Configuration.SecureConfig);
                result = PluginServices.Wait(notifications.NotifyComposedAsync(source, context, contactId, retryKey,
                    "Attendance.PastorReply", Compose, key, CancellationToken.None));
            }
            if (result.TransportException is { } failure) ExceptionDispatchInfo.Capture(failure).Throw();
            if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
                throw WeeklyReportShellData.Invalid();
        }
        finally { if (key is not null) Array.Clear(key, 0, key.Length); }
    }
    /// <summary>Live有界解碼，錯長度先清零；成功值由Execute唯一finally清零。</summary>
    private static byte[] DecodeKey(string secure)
    {
        if (string.IsNullOrWhiteSpace(secure) || secure.Length > 128) throw WeeklyReportShellData.Invalid();
        byte[] key;
        try { key = Convert.FromBase64String(secure); } catch (FormatException) { throw WeeklyReportShellData.Invalid(); }
        if (key.Length == 64) return key;
        Array.Clear(key, 0, key.Length); throw WeeklyReportShellData.Invalid();
    }
    /// <summary>固定無覆寫策略，不保存context或教會資料；文字仍取產品資源。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>空快照僅屬本次通知，沒有全域授權或資料快取。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：P5-05 週報回應的非同步 PostOperation Plugin 薄外殼。非同步表示
 * 平台系統作業，方法本身在返回前等待共用通知鏈完成，沒有未觀測背景工作。
 *
 * 主要功能與流程：Update 必須有正式 PreImage，重讀回應並保留 null／空字串差異；
 * 沿正式週報 list lookup、小組長 contact、parentcustomerid(account) 決定來源教會。
 * Core 產生四角色依序去重與穩定事件鍵；每位收件者先驗教會與綁定，再經
 * MemberNotificationService.NotifyComposedAsync 重驗、解析語系、組字與推播。
 * Create 沿用舊防遞迴例外但無通知副作用；Depth>2 且非 Create 才由基底返回。
 *
 * 使用邊界與維護：外殼只保存固定文字及無操作狀態的通知策略，SDK、context、
 * 名單、回應、來源 adapter 和金鑰僅屬單次 Execute。Live 只從 SecureConfig
 * 解碼 key，finally 清零；Shadow 不解碼、不解密、不真推播。每次最多四位正式
 * contact，HTTP 的十秒 deadline、response Dispose 與傳輸排空由既有 client 擁有；
 * adapter using 清除 SDK 參照，不處置平台連線。沒有 request/session 快取或 queue。
 * 未綁定與教會不符正常拒絕，其餘真實失敗向平台重拋；本階段不得以 Live 啟用。
 */
using System;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessage.Dynamics.Plugins.WeeklyReports;

/// <summary>將已驗證週報事件交給純規則與共用通知入口，不保存任何 SDK 或會友狀態。</summary>
public sealed class WeeklyReportNotificationPlugin : PluginShellBase
{
    /// <summary>無操作狀態的策略與 client，由執行期共用傳輸 owner 管理連線。</summary>
    private readonly MemberNotificationService notifications;

    /// <summary>平台建構入口；只建立不可變策略，HTTP 共用 owner 不由個別 Execute 處置。</summary>
    /// <param name="unsecureConfig">Shadow／Live 註冊設定，不能指定來源教會。</param>
    /// <param name="secureConfig">Live 的 Base64 token key；Shadow 必須空白。</param>
    public WeeklyReportNotificationPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(2, DepthException.Create))
    {
        var catalog = new GoodShepherdTextCatalog(new NoOverrides());
        notifications = new MemberNotificationService(new LinePushClient(),
            new MemberCultureResolver(new LocalizationOptions()), catalog);
    }

    /// <summary>只處理 Update 回應差異，每位收件者獨立驗證與同步排空，所有局部 key 結束即清零。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != WeeklyReportShellData.ReportTable ||
            input.MessageName is not ("Update" or "Create")) return;
        // 舊 Create 分支沒有推播；仍驗 stage/mode，避免錯誤註冊被默默接受。
        if (input.Stage != 40 || input.Mode != 1) throw WeeklyReportShellData.Invalid();
        if (input.MessageName == "Create") return;
        WeeklyReportShellData.ValidateInput(input, 1);
        var report = WeeklyReportShellData.Retrieve(services.Organization, WeeklyReportShellData.ReportTable,
            input.PrimaryEntityId, new ColumnSet("new_paster_reply", "new_sunday_date", "new_list_group_present_weekly_report"));
        var prior = WeeklyReportShellData.Text(input.PreImage!, "new_paster_reply", 2000);
        var current = WeeklyReportShellData.Text(report, "new_paster_reply", 2000);
        if (string.Equals(prior, current, StringComparison.Ordinal)) return;
        var listId = WeeklyReportShellData.Lookup(report, "new_list_group_present_weekly_report", "list");
        var list = WeeklyReportShellData.Retrieve(services.Organization, "list", listId,
            new ColumnSet("new_contact_family_leader_list", "new_contact_list_vice_family_leader",
                "new_contact_race_leager_list", "new_contact_co_race_leager_list", "new_contact_list_arealeader"));
        var church = WeeklyReportShellData.Church(services.Organization, list);
        var account = WeeklyReportShellData.Retrieve(services.Organization, "account", church,
            new ColumnSet("new_timezone", "new_defaultlanguage"));
        var zone = WeeklyReportShellData.Text(account, "new_timezone", 128) ?? throw WeeklyReportShellData.Invalid();
        var configuredCulture = WeeklyReportShellData.Text(account, "new_defaultlanguage", 128);
        var culture = string.IsNullOrWhiteSpace(configuredCulture) ? "zh-TW" : configuredCulture!;
        if (!report.Attributes.TryGetValue("new_sunday_date", out var dateValue) ||
            dateValue is not DateTime sunday || sunday.Kind != DateTimeKind.Utc)
            throw WeeklyReportShellData.Invalid();
        var pastor = OptionalRole(list, "new_contact_list_arealeader");
        var pastorName = pastor == Guid.Empty ? null :
            list.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>("new_contact_list_arealeader").Name;
        var roles = new[]
        {
            OptionalRole(list, "new_contact_family_leader_list"),
            OptionalRole(list, "new_contact_list_vice_family_leader"),
            OptionalRole(list, "new_contact_race_leager_list"),
            OptionalRole(list, "new_contact_co_race_leager_list")
        };
        var context = new TextContext(church, culture, culture, input.OrganizationId.ToString("D"), "GoodShepherd");
        var plan = WeeklyReportNotificationRule.Evaluate(church, input.PrimaryEntityId, input.CorrelationId,
            prior, current, pastorName, sunday, zone, CultureInfo.GetCultureInfo(culture), roles,
            WeeklyReportNotificationRule.Template(context.TextCulture));
        using var source = new LineRecipientQueries(services.Organization);
        byte[]? key = null;
        try
        {
            foreach (var recipient in plan.Recipients)
            {
                // 拒絕先於 key 解碼；共用通知入口還會再讀同一人，防止重讀時變更教會。
                var member = source.ReadMember(recipient.ContactId, CancellationToken.None);
                if (LinePushClient.ValidateRecipient(member, church) is not null) continue;
                System.Collections.Generic.IReadOnlyList<ISendMessage> Compose(TextContext resolved)
                {
                    var localized = WeeklyReportNotificationRule.Evaluate(church, input.PrimaryEntityId, input.CorrelationId,
                        prior, current, pastorName, sunday, zone, CultureInfo.GetCultureInfo(resolved.FormatCulture), roles,
                        WeeklyReportNotificationRule.Template(resolved.TextCulture));
                    return new ISendMessage[] { new TextMessage(string.Empty) { Text = localized.Message! } };
                }
                LinePushResult result;
                if (services.IsShadow)
                    result = PluginServices.Wait(notifications.NotifyComposedAsync(source, context, recipient.ContactId,
                        recipient.RetryKey, "WeeklyReport.ReplyNotification", Compose, services.ShadowPush!, CancellationToken.None));
                else
                {
                    key ??= DecodeKey(Configuration.SecureConfig);
                    result = PluginServices.Wait(notifications.NotifyComposedAsync(source, context, recipient.ContactId,
                        recipient.RetryKey, "WeeklyReport.ReplyNotification", Compose, key, CancellationToken.None));
                }
                // 共用 client 將 transport 失敗裝入結果；保留真實例外與原堆疊交平台，
                // 不把內部逾時誤分類成正常取消，也不只丟掉真例外後回報固定業務錯誤。
                if (result.TransportException is { } transport) ExceptionDispatchInfo.Capture(transport).Throw();
                if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
                    throw WeeklyReportShellData.Invalid();
            }
        }
        finally { if (key is not null) Array.Clear(key, 0, key.Length); }
    }

    /// <summary>四個可空角色保持空缺；非空角色必須為正式 contact lookup，區牧僅供文字不當收件者。</summary>
    private static Guid OptionalRole(Microsoft.Xrm.Sdk.Entity list, string field) =>
        !list.Attributes.TryGetValue(field, out var value) || value is null ? Guid.Empty :
        WeeklyReportShellData.Lookup(list, field, "contact");

    /// <summary>Live 專用有界 Base64 解碼；錯長度先清零再拒絕，成功由 Execute finally 唯一清零。</summary>
    private static byte[] DecodeKey(string secure)
    {
        if (string.IsNullOrWhiteSpace(secure) || secure.Length > 128) throw WeeklyReportShellData.Invalid();
        byte[] key;
        try { key = Convert.FromBase64String(secure); }
        catch (FormatException) { throw WeeklyReportShellData.Invalid(); }
        if (key.Length == 64) return key;
        Array.Clear(key, 0, key.Length);
        throw WeeklyReportShellData.Invalid();
    }

    /// <summary>無狀態的固定系統文字政策；每次回傳同範圍空覆寫，不保留任何 context 或教會快照。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>來源 context 已由正式教會鏈驗證；空覆寫只借固定系統文字，不讀跨租戶資料。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

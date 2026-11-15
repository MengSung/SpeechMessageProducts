/*
 * 檔案整體說明
 *
 * 目的與定位：P5-07 課程簽核的 PostOperation Plugin 薄外殼，取代舊
 * StorLessonSigningLibrary 的 static／寫死收件者入口。Create 通知課程本人，
 * Update 通知本人所屬小組的正式組長；訊息組合委派 GoodShepherd.Core，
 * 收件者、教會、語系、Token 與傳輸委派共用 MemberNotificationService。
 *
 * 主要功能與流程：只接受 new_stor_lessons 的 Create／Update、PostOperation、
 * 非同步模式及舊入口 Depth 大於 1 的返回政策；重讀最小來源列、課程圖片、
 * member→list→leader 正式 lookup 及來源 account，先以伺服器資料決定收件者，
 * 再在 Shadow 或 Live 模式組合一次課程簽核 Flex。Live 的 secure key 只在
 * 單次 Execute 內解碼並 finally 清零，Shadow 不解密、不推播。
 *
 * 使用邊界與維護：Plugin 欄位只有固定通知策略；SDK、Entity、Token、context
 * 及 recorder 均屬單次執行。不得以組織名稱、DEVELOPER_LINE_ID、資料列 token
 * 或 caller 提供的教會值回落；lookup 缺失、錯表或圖片 URL 非 HTTPS 直接拒絕。
 * 不建立 Session、cache、timer、背景 Task 或自有網路連線；共用通知 client
 * 由 Host／外殼依既有契約擁有。
 *
 * 驗證方式：離線 Shadow shell 測試覆蓋 Create／Update、正式 recipient chain、
 * Flex action 及零真寫；Live 只驗 secure key 邊界，不代表 goodshepherdback
 * 已啟用或已完成實機影子驗收。
 */
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessage.Dynamics.Plugins.Lessons;

/// <summary>一次執行的課程簽核通知入口，不保存任何來源資料或 credential。</summary>
public sealed class StorLessonSigningPlugin : PluginShellBase
{
    private readonly MemberNotificationService notifications;

    /// <summary>建立固定模式及 Depth 大於一返回的舊入口政策。</summary>
    public StorLessonSigningPlugin(string unsecureConfig, string secureConfig)
        : base(unsecureConfig, secureConfig, new DepthPolicy(1))
    {
        notifications = new MemberNotificationService(new LinePushClient(),
            new MemberCultureResolver(new LocalizationOptions()),
            new GoodShepherdTextCatalog(new NoOverrides()));
    }

    /// <summary>重讀正式資料並同步排空單一簽核通知。</summary>
    internal override void ExecuteRule(PluginInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_stor_lessons" ||
            input.MessageName is not ("Create" or "Update"))
            return;
        if (input.Stage != 40 || input.Mode != 1)
            throw new InvalidPluginExecutionException("Lesson signing requires asynchronous PostOperation.");

        var stor = services.Organization.Retrieve("new_stor_lessons", input.PrimaryEntityId,
            new ColumnSet("new_contact_new_stor_lessons", "new_new_disciple_lessons_new_stor_les"));
        var memberId = Lookup(stor, "new_contact_new_stor_lessons", "contact");
        var courseId = Lookup(stor, "new_new_disciple_lessons_new_stor_les", "new_disciple_lessons");
        var member = services.Organization.Retrieve("contact", memberId,
            new ColumnSet("fullname", "new_cell_list_contact", "parentcustomerid",
                "new_equipment_status"));
        var churchId = Lookup(member, "parentcustomerid", "account");

        var recipientId = memberId;
        var signerName = Text(member, "fullname");
        if (input.MessageName == "Update")
        {
            var listId = Lookup(member, "new_cell_list_contact", "list");
            var list = services.Organization.Retrieve("list", listId,
                new ColumnSet("new_contact_family_leader_list"));
            recipientId = Lookup(list, "new_contact_family_leader_list", "contact");
            var signer = services.Organization.Retrieve("contact", recipientId,
                new ColumnSet("fullname"));
            signerName = Text(signer, "fullname");
        }

        var course = services.Organization.Retrieve("new_disciple_lessons", courseId,
            new ColumnSet("new_name", "new_picture_link"));
        var imageUrl = Text(course, "new_picture_link");
        var courseName = Text(course, "new_name");
        var account = services.Organization.Retrieve("account", churchId,
            new ColumnSet("new_defaultlanguage"));
        var culture = Text(account, "new_defaultlanguage");
        if (string.IsNullOrWhiteSpace(culture)) culture = "zh-TW";

        using var source = new LineRecipientQueries(services.Organization);
        var recipient = source.ReadMember(recipientId, CancellationToken.None);
        if (LinePushClient.ValidateRecipient(recipient, churchId) is not null)
            return;
        var context = new TextContext(churchId, culture!, culture!,
            input.OrganizationId.ToString("D"), "GoodShepherd");
        var inputValues = new LessonSigningMessageInput(stor.Id, recipientId, courseName,
            Text(member, "fullname"), OptionalText(member, "new_equipment_status", "未設定"),
            imageUrl, signerName);

        byte[]? key = null;
        try
        {
            IReadOnlyList<ISendMessage> Compose(TextContext _) =>
                new ISendMessage[] { LessonSigningMessageFactory.Create(inputValues) };
            var retryKey = DeterministicGuid.Create("lessons.signing", stor.Id, recipientId);
            LinePushResult result;
            if (services.IsShadow)
                result = PluginServices.Wait(notifications.NotifyComposedAsync(source, context,
                    recipientId, retryKey, "Lessons.Signing", Compose, services.ShadowPush!,
                    CancellationToken.None));
            else
            {
                key = DecodeKey(Configuration.SecureConfig);
                result = PluginServices.Wait(notifications.NotifyComposedAsync(source, context,
                    recipientId, retryKey, "Lessons.Signing", Compose, key,
                    CancellationToken.None));
            }
            if (result.TransportException is { } transport)
                ExceptionDispatchInfo.Capture(transport).Throw();
            if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
                throw new InvalidPluginExecutionException("Lesson signing notification failed.");
        }
        finally
        {
            if (key is not null) Array.Clear(key, 0, key.Length);
        }
    }

    /// <summary>只接受正式 lookup 的目標表與非空資料庫 ID。</summary>
    private static Guid Lookup(Entity row, string field, string table)
    {
        if (row.Attributes.TryGetValue(field, out var value) &&
            value is EntityReference reference && reference.LogicalName == table &&
            reference.Id != Guid.Empty && reference.KeyAttributes.Count == 0)
            return reference.Id;
        throw new InvalidPluginExecutionException("Lesson signing source scope is invalid.");
    }

    /// <summary>只讀 bounded 文字；缺失欄位固定拒絕，不回落顯示名稱猜測。</summary>
    private static string Text(Entity row, string field)
    {
        if (row.Attributes.TryGetValue(field, out var value) && value is string text &&
            !string.IsNullOrWhiteSpace(text) && text.Length <= 2000)
            return text;
        throw new InvalidPluginExecutionException("Lesson signing source text is invalid.");
    }

    /// <summary>設備狀態是舊資料可缺少的顯示欄位，缺失時使用固定非敏感文字。</summary>
    private static string OptionalText(Entity row, string field, string fallback) =>
        row.Attributes.TryGetValue(field, out var value) && value is string text &&
        !string.IsNullOrWhiteSpace(text) && text.Length <= 2000 ? text : fallback;

    /// <summary>Live 專用 64-byte secure key，失敗資料先清零且不回顯秘密。</summary>
    private static byte[] DecodeKey(string secure)
    {
        if (string.IsNullOrWhiteSpace(secure) || secure.Length > 128)
            throw new InvalidPluginExecutionException("Lesson signing secure configuration is invalid.");
        byte[] key;
        try { key = Convert.FromBase64String(secure); }
        catch (FormatException) { throw new InvalidPluginExecutionException("Lesson signing secure configuration is invalid."); }
        if (key.Length == 64) return key;
        Array.Clear(key, 0, key.Length);
        throw new InvalidPluginExecutionException("Lesson signing secure configuration is invalid.");
    }

    /// <summary>固定文字政策不讀跨教會覆寫快取，生命期只屬單次通知。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>回傳空覆寫快照，不保存 context 或租戶狀態。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

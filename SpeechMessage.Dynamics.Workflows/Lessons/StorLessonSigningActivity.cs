/*
 * 檔案整體說明
 *
 * 目的與定位：P5-07 課程簽核的 CodeActivity 薄外殼，保留舊活動的
 * InputParameter、InputStorLesson 及 OutputResult 介面；真正來源仍以平台
 * PrimaryEntityId 為準，並與 Plugin 使用同一份 Core Flex 組合及通知驗證。
 *
 * 主要功能與流程：活動只接受 new_stor_lessons 的 ExecuteWorkflow／Create／Update，
 * 讀取課程、學員、教會及更新時的小組長正式 lookup，Shadow 模式下以
 * MemberNotificationService 記錄安全通知意圖。InputStorLesson 不作授權來源，
 * OutputResult 維持舊流程未設定的預設值；Live 因沒有活動 secure key 而固定拒絕，
 * 避免以空值或寫死 token 送出。
 *
 * 使用邊界與維護：活動欄位只保存 WF expression，不保存執行值、SDK、Entity、
 * Session、credential、cache、timer 或背景 Task。平台 SDK 由 ActivityShellBase
 * 借用，Shadow recorder 由外殼 finally 排空及 Dispose；lookup 缺失、圖片 URL
 * 非 HTTPS 或收件者教會不符均 fail closed，不回落 developer LINE ID。
 *
 * 驗證方式：Workflow 離線測試覆蓋 Shadow Create／Update、InputStorLesson
 * 不越權、Flex action 與 Live 拒絕；測試不代表正式組織啟用或 LINE 實機送達。
 */
using System;
using System.Activities;
using System.Collections.Generic;
using System.Threading;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Workflows.Infrastructure;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using ToolUtilityNameSpace.LineRecipients;

namespace SpeechMessage.Dynamics.Workflows.Lessons;

/// <summary>課程簽核活動；執行資料只存於單次 ActivityShellBase 呼叫。</summary>
public sealed class StorLessonSigningActivity : ActivityShellBase
{
    /// <summary>保留舊活動顯示參數；不把它當作課程或教會授權。</summary>
    [RequiredArgument]
    [Input("InputParameter")]
    public InArgument<string> InputParameter { get; set; } = new();

    /// <summary>保留舊活動的來源列參數；實際授權仍只信平台 PrimaryEntityId。</summary>
    [RequiredArgument]
    [Input("InputStorLesson")]
    public InArgument<EntityReference> InputStorLesson { get; set; } = new();

    /// <summary>保留舊活動輸出，舊流程未設定故不宣稱成功碼。</summary>
    [Output("OutputResult")]
    public OutArgument<int> OutputResult { get; set; } = new();

    /// <summary>只在 Shadow 模式執行通知意圖，避免活動缺少 secure key 時誤發 Live。</summary>
    internal override void ExecuteRule(ActivityInput input, PluginServices services)
    {
        if (input.PrimaryEntityName != "new_stor_lessons" ||
            input.MessageName is not ("ExecuteWorkflow" or "Create" or "Update"))
            return;
        if (!services.IsShadow)
            throw new InvalidPluginExecutionException("Lesson signing activity requires Shadow mode.");

        var stor = services.Organization.Retrieve("new_stor_lessons", input.PrimaryEntityId,
            new ColumnSet("new_contact_new_stor_lessons",
                "new_new_disciple_lessons_new_stor_les"));
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
            signerName = Text(services.Organization.Retrieve("contact", recipientId,
                new ColumnSet("fullname")), "fullname");
        }
        var course = services.Organization.Retrieve("new_disciple_lessons", courseId,
            new ColumnSet("new_name", "new_picture_link"));
        var account = services.Organization.Retrieve("account", churchId,
            new ColumnSet("new_defaultlanguage"));
        var culture = OptionalText(account, "new_defaultlanguage", "zh-TW");
        var context = new TextContext(churchId, culture, culture,
            input.OrganizationId.ToString("D"), "GoodShepherd");
        var source = new LineRecipientQueries(services.Organization);
        try
        {
            var recipient = source.ReadMember(recipientId, CancellationToken.None);
            if (LinePushClient.ValidateRecipient(recipient, churchId) is not null) return;
            var messageInput = new LessonSigningMessageInput(input.PrimaryEntityId, recipientId,
                Text(course, "new_name"), Text(member, "fullname"),
                OptionalText(member, "new_equipment_status", "未設定"),
                Text(course, "new_picture_link"), signerName);
            var notifications = new MemberNotificationService(new LinePushClient(),
                new MemberCultureResolver(new LocalizationOptions()),
                new GoodShepherdTextCatalog(new NoOverrides()));
            IReadOnlyList<ISendMessage> Compose(TextContext _) =>
                new ISendMessage[] { LessonSigningMessageFactory.Create(messageInput) };
            var result = PluginServices.Wait(notifications.NotifyComposedAsync(source, context,
                recipientId, DeterministicGuid.Create("lessons.signing", input.PrimaryEntityId, recipientId),
                "Lessons.Signing", Compose, services.ShadowPush!, CancellationToken.None));
            if (!result.Success && result.Reason is not ("NotBound" or "ChurchMismatch" or "ShadowRecorded"))
                throw new InvalidPluginExecutionException("Lesson signing notification failed.");
        }
        finally
        {
            source.Dispose();
        }
    }

    /// <summary>只接受正式 lookup 的目標表與非空資料庫 ID。</summary>
    private static Guid Lookup(Entity row, string field, string table) =>
        row.Attributes.TryGetValue(field, out var value) && value is EntityReference reference &&
        reference.LogicalName == table && reference.Id != Guid.Empty &&
        reference.KeyAttributes.Count == 0
            ? reference.Id
            : throw new InvalidPluginExecutionException("Lesson signing source scope is invalid.");

    /// <summary>必需文字缺失時固定拒絕，不從輸入參數猜測內容。</summary>
    private static string Text(Entity row, string field) =>
        row.Attributes.TryGetValue(field, out var value) && value is string text &&
        !string.IsNullOrWhiteSpace(text) && text.Length <= 2000
            ? text
            : throw new InvalidPluginExecutionException("Lesson signing source text is invalid.");

    /// <summary>設備狀態及語系缺失時使用固定非敏感文字。</summary>
    private static string OptionalText(Entity row, string field, string fallback) =>
        row.Attributes.TryGetValue(field, out var value) && value is string text &&
        !string.IsNullOrWhiteSpace(text) && text.Length <= 2000 ? text : fallback;

    /// <summary>活動使用空覆寫快照，避免把教會文字狀態留在活動實例。</summary>
    private sealed class NoOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>只回傳本次 context 對應的空快照。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

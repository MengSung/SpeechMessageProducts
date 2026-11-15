/*
 * 檔案整體說明
 *
 * 目的與定位：提供 P5-05 週報回應通知的雙目標純規則，輸入為外殼已授權的正式
 * 教會／週報／事件識別、回應、四角色 contact IDs、明確 UTC 日期與教會時區文化。
 * 此檔不持有 SDK Entity、LINE ID、channel、token、通知 Host 或任何資料存取能力。
 *
 * 主要功能與流程：回應以 Ordinal 比較並保留 null／空字串差異；有改變時，透過
 * ChurchClock 將 UTC 轉教會日曆日期，再以明確文化的短日期與外殼選定資源模板
 * 交共用具名 formatter。四角色保留首次出現順序、略過 Empty 並按正式 contact 去重。
 * 每個意圖含用途／教會／週報／事件／contact 的 DeterministicGuid 重試鍵。
 * Template accessor 只從共用產品固定資源表讀原文，保留具名參數與空格換行；
 * 語系缺翻譯沿用 SystemTextTable 後備，不建立教會覆寫或授權狀態。
 *
 * 使用邊界與維護：外殼須先驗證完整 organization／product／church 範圍及每位收件者
 * 綁定與教會歸屬，並依 context 取模板。本規則不以 GUID 取代授權、不真正發送，
 * 也不宣稱重試鍵取代資料庫原子唯一性。候選最多四人、模板與參數有界；未完整成功
 * 不發布任何意圖。所有集合只在同步呼叫存在，結果由 caller 擁有；無快取、非同步、
 * 背景工作或需 Dispose 資源。時區、格式與真正失敗原例外交回外殼，不吞錯或 fallback。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;

namespace SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;

/// <summary>單一正式 contact 的不可變通知意圖，不含可直接發送的 LINE 識別或憑證。</summary>
public sealed class WeeklyReportNotificationRecipient
{
    /// <summary>只由規則發布完整且已依角色去重的收件者與穩定事件鍵。</summary>
    internal WeeklyReportNotificationRecipient(Guid contactId, Guid retryKey)
    {
        ContactId = contactId;
        RetryKey = retryKey;
    }
    /// <summary>首次出現角色所指的正式會友識別；Host 仍須驗證綁定及教會。</summary>
    public Guid ContactId { get; }
    /// <summary>分用途、教會、週報、事件與會友的穩定 GUID，不含個資或文字內容。</summary>
    public Guid RetryKey { get; }
}

/// <summary>完整文字與有序收件意圖的不可變快照；不保留輸入角色陣列或格式文化。</summary>
public sealed class WeeklyReportNotificationPlan
{
    /// <summary>完成全體驗證後才發布；複製區域候選，不讓 caller 修改既有意圖。</summary>
    internal WeeklyReportNotificationPlan(string? message, List<WeeklyReportNotificationRecipient> recipients)
    {
        Message = message;
        Recipients = new List<WeeklyReportNotificationRecipient>(recipients).AsReadOnly();
    }
    /// <summary>完整格式化文字；null 僅表示回應未改變，不應推播。</summary>
    public string? Message { get; }
    /// <summary>按四角色首次出現排序的唯一正式收件者，零至四位。</summary>
    public IReadOnlyList<WeeklyReportNotificationRecipient> Recipients { get; }
}

/// <summary>只產生通知純值的無狀態規則，不自行授權、保存收件者或執行通知 I/O。</summary>
public static class WeeklyReportNotificationRule
{
    /// <summary>共用文字表要求的固定中性語言標記；唯讀集合不含教會、模板或請求資料。</summary>
    private static readonly IReadOnlyList<string> TemplateCultures = Array.AsReadOnly(new[] { "zh-TW" });

    /// <summary>
    /// 取得產品 WeeklyReport.ReplyNotification 的指定語系原文，仍保留具名參數，
    /// 不先格式化、不套教會覆寫，也不執行授權或 scope 選擇。未知語系及缺翻譯
    /// 沿用共用文字表的 zh-TW 後備，不能改成空白模板或共享產品以外的資源表。
    /// </summary>
    /// <param name="culture">由薄殼明確選定的文字語系，不作為教會或授權身分。</param>
    /// <returns>尚未插入 pastor/date/reply 的產品模板原文。</returns>
    public static string Template(string culture) =>
        WeeklyReportStatisticsRule.ProductResourceTable.GetText(culture, new TextKey("WeeklyReport.ReplyNotification"));

    /// <summary>
    /// 使用外殼選定的模板產生精確文字，絕不 trim 回應、姓名或模板空格換行。
    /// 預期模板參數為 pastor/date/reply 各一次，插入內容不再次解析，以防回應中的
    /// 大括號改變模板。沒有文字變更時不轉日期、不建立重試鍵，仍先驗證輸入預算。
    /// </summary>
    /// <param name="churchId">已由伺服器授權的非空正式 account 教會識別。</param>
    /// <param name="reportId">同教會週報的非空正式識別。</param>
    /// <param name="eventId">外殼提供的穩定非空事件識別；重試不得重新產生。</param>
    /// <param name="priorReply">PreImage 原回應；null 不同於空字串。</param>
    /// <param name="currentReply">合併 Target 後回應，最多 2000 字元；null 以空字串插入。</param>
    /// <param name="areaLeaderName">來源名單區牧 lookup 的 Name，最多 100 字元；null 插入空字串。</param>
    /// <param name="sundayUtc">以 UserLocal metadata 語意讀出的明確 UTC 瞬間，不能猜 Kind。</param>
    /// <param name="timeZoneId">已授權來源教會的精確 IANA 時區；不以格式文化替代。</param>
    /// <param name="formatCulture">外殼明確選定且操作期間不修改的日期文化；本規則不保存參照。</param>
    /// <param name="roleContactIds">恰四項：組長、副組長、上代、共同上代；Empty 表示缺角色。</param>
    /// <param name="notificationTemplate">依 context 選定的 WeeklyReport.ReplyNotification 原文，最多 1024 字元。</param>
    /// <returns>完全驗證後的通知純值，最多四位收件者；不執行真正推播。</returns>
    /// <exception cref="ArgumentException">識別、預算、角色數、時區或 UTC Kind 不符合契約。</exception>
    /// <exception cref="ArgumentNullException">角色、模板或格式文化為 null。</exception>
    /// <exception cref="FormatException">模板參數集合或出現次數不符合契約，或日期字串超界。</exception>
    public static WeeklyReportNotificationPlan Evaluate(Guid churchId, Guid reportId, Guid eventId,
        string? priorReply, string? currentReply, string? areaLeaderName, DateTime sundayUtc,
        string timeZoneId, CultureInfo formatCulture, IReadOnlyList<Guid> roleContactIds, string notificationTemplate)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(roleContactIds);
        ArgumentNullException.ThrowIfNull(formatCulture);
        ArgumentNullException.ThrowIfNull(notificationTemplate);
#else
        if (roleContactIds is null) throw new ArgumentNullException(nameof(roleContactIds));
        if (formatCulture is null) throw new ArgumentNullException(nameof(formatCulture));
        if (notificationTemplate is null) throw new ArgumentNullException(nameof(notificationTemplate));
#endif
        ValidateIdentities(churchId, reportId, eventId);
        if (roleContactIds.Count != 4 || priorReply?.Length > 2000 || currentReply?.Length > 2000
            || areaLeaderName?.Length > 100 || notificationTemplate.Length > 1024)
            throw new ArgumentException("Notification input exceeds its bounded budget.");
        var recipients = new List<WeeklyReportNotificationRecipient>();
        if (string.Equals(priorReply, currentReply, StringComparison.Ordinal))
            return new WeeklyReportNotificationPlan(null, recipients);

        RequireSingleParameter(notificationTemplate, "{pastor}");
        RequireSingleParameter(notificationTemplate, "{date}");
        RequireSingleParameter(notificationTemplate, "{reply}");
        var local = new ChurchClock().ToChurchTime(sundayUtc, timeZoneId);
        if (formatCulture.DateTimeFormat.ShortDatePattern.Length > 128)
            throw new FormatException("Date format exceeds its bounded budget.");
        var date = local.ToString("d", formatCulture);
        if (date.Length > 128) throw new FormatException("Formatted date exceeds its bounded budget.");
        // 共用 formatter 為 internal，經公開系統文字表接縫借用其驗證與單次替換；
        // 表只保存此次已選定模板，呼叫結束即釋放，不快取任何教會覆寫或個人回應。
        var table = SystemTextTable.FromRows(TemplateCultures,
            new[] { new string?[] { "WeeklyReport.ReplyNotification", notificationTemplate } });
        var message = table.GetFormattedText(formatCulture.Name, new TextKey("WeeklyReport.ReplyNotification"),
            new TextArg("pastor", areaLeaderName ?? string.Empty), new TextArg("date", date),
            new TextArg("reply", currentReply ?? string.Empty));
        var seen = new HashSet<Guid>();
        // 恰四次索引；不依賴 caller 可能實作成無界來源的 IEnumerable。
        for (var index = 0; index < 4; index++)
        {
            var contact = roleContactIds[index];
            if (contact != Guid.Empty && seen.Add(contact))
                recipients.Add(new WeeklyReportNotificationRecipient(contact, RetryKey(churchId, reportId, eventId, contact)));
        }
        return new WeeklyReportNotificationPlan(message, recipients);
    }

    /// <summary>
    /// 以固定用途及四個正式 GUID 產生穩定重試鍵；同事件同會友不因角色、文字或時區變化而換鍵。
    /// 鍵只在外殼驗證的組織／產品儲存範圍內使用，不能作為跨資料庫的授權憑證。
    /// </summary>
    /// <param name="churchId">已授權教會正式識別。</param>
    /// <param name="reportId">同教會的週報正式識別。</param>
    /// <param name="eventId">重試間不變的來源事件識別。</param>
    /// <param name="contactId">已驗證同教會的收件者正式識別。</param>
    /// <returns>使用既有共用演算法的跨目標穩定 GUID。</returns>
    /// <exception cref="ArgumentException">任一識別為 Empty。</exception>
    public static Guid RetryKey(Guid churchId, Guid reportId, Guid eventId, Guid contactId)
    {
        ValidateIdentities(churchId, reportId, eventId);
        if (contactId == Guid.Empty) throw new ArgumentException("A contact identity is required.", nameof(contactId));
        return DeterministicGuid.Create("WeeklyReportNotification", churchId, reportId, eventId, contactId);
    }

    /// <summary>只驗正式身分非空；真正的來源教會及產品授權必須由外殼驗證，錯誤不回顯識別值。</summary>
    private static void ValidateIdentities(Guid churchId, Guid reportId, Guid eventId)
    {
        if (churchId == Guid.Empty || reportId == Guid.Empty || eventId == Guid.Empty)
            throw new ArgumentException("Church, report and event identities are required.");
    }

    /// <summary>限制每個具名參數恰好一次，將展開長度固定在模板與三個有界字串之和。</summary>
    private static void RequireSingleParameter(string template, string parameter)
    {
        var index = template.IndexOf(parameter, StringComparison.Ordinal);
        if (index < 0 || template.IndexOf(parameter, index + parameter.Length, StringComparison.Ordinal) >= 0)
            throw new FormatException("Notification template requires each declared parameter exactly once.");
    }
}

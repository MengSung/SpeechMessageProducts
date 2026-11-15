/*
 * 檔案整體說明
 * 目的與定位：P5-06出席牧者回應的雙目標純規則，不讀SDK、LINE ID、token或組織設定。
 * 主要功能與流程：Create保留舊無推播行為；Update以Ordinal比較前後回應，差異才用
 * 外殼選定的產品模板單次插入會員、代禱與回應，完整保留空格、CRLF及null／空字串差異。
 * RetryKey直接使用正式出席主鍵，依05A同記錄重試穩定；不宣稱可超過LINE重試期限去重。
 * 使用邊界與維護：外殼先核對來源教會及本人綁定，GUID本身不是授權。字串預算固定，
 * 未知模板參數、空主鍵或超量一律拒絕，不發布部分文字；沒有背景工作、快取或可清理資源。
 * 固定資源表只含編譯時產品文字，無教會／會友狀態；同步呼叫結果由caller持有。
 */
using System;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Resources;

namespace SpeechMessageProducts.GoodShepherd.Core.Attendance;

/// <summary>無狀態出席通知純規則，授權與傳輸由呼叫外殼及共用通知服務擁有。</summary>
public static class PresentRecordNotificationRule
{
    /// <summary>固定純字串文化白名單，沒有操作或租戶狀態。</summary>
    private static readonly string[] TemplateCultures = { "zh-TW" };
    /// <summary>模板必要參數，固定容量且不對外暴露。</summary>
    private static readonly string[] Parameters = { "{member}", "{prayer}", "{reply}" };
    /// <summary>容量隨版控文字有界的固定產品表，沒有請求、教會覆寫或個人回應。</summary>
    private static readonly SystemTextTable Texts = SystemTextTable.FromRows(GoodShepherdTextData.Cultures, GoodShepherdTextData.Rows);
    /// <summary>取指定文化的產品原文；缺翻譯沿共用zh-TW後備，不自行改寫文字。</summary>
    /// <param name="culture">由通知context明確選定的文字文化。</param><returns>尚未插值的固定模板。</returns>
    public static string Template(string culture) => Texts.GetText(culture, new TextKey("Attendance.PastorReply"));

    /// <summary>完整驗輸入後才發布文字；非Update或相同回應為null，不會執行任何I/O。</summary>
    /// <param name="recordId">已驗證的正式出席主鍵。</param><param name="message">平台訊息名稱。</param>
    /// <param name="prior">必要前映像回應，可null。</param><param name="current">合併後回應，可null。</param>
    /// <param name="memberName">正式contact lookup顯示名稱，保留原文字；null視空字串。</param>
    /// <param name="prayer">出席代禱原文，可null。</param><param name="template">明確選定的產品模板原文。</param>
    /// <returns>完整訊息或不應推播的null；不保存輸入參照。</returns>
    /// <exception cref="ArgumentException">空身分、未知訊息或字串超過預算。</exception>
    /// <exception cref="FormatException">模板未知／缺少／重複參數。</exception>
    public static string? Evaluate(Guid recordId, string message, string? prior, string? current,
        string? memberName, string? prayer, string template)
    {
        _ = RetryKey(recordId);
        if (message is not ("Create" or "Update") || prior?.Length > 2000 || current?.Length > 2000 ||
            memberName?.Length > 200 || prayer?.Length > 2000 || template is null || template.Length > 1024)
            throw new ArgumentException("Attendance notification input exceeds its contract.");
        if (message == "Create" || string.Equals(prior, current, StringComparison.Ordinal)) return null;
        foreach (var parameter in Parameters)
        {
            var index = template.IndexOf(parameter, StringComparison.Ordinal);
            if (index < 0 || template.IndexOf(parameter, index + parameter.Length, StringComparison.Ordinal) >= 0)
                throw new FormatException("Attendance template parameters must occur exactly once.");
        }
        var table = SystemTextTable.FromRows(TemplateCultures,
            new[] { new string?[] { "Attendance.PastorReply", template } });
        return table.GetFormattedText("zh-TW", new TextKey("Attendance.PastorReply"),
            new TextArg("member", memberName ?? string.Empty), new TextArg("prayer", prayer ?? string.Empty),
            new TextArg("reply", current ?? string.Empty));
    }

    /// <summary>使用05A指定recordId，不以會友姓名、輸入文字或每次隨機GUID形成重試身分。</summary>
    /// <param name="recordId">正式非空資料庫主鍵；caller仍須驗授權。</param><returns>原主鍵。</returns>
    /// <exception cref="ArgumentException">空主鍵拒絕。</exception>
    public static Guid RetryKey(Guid recordId) => recordId != Guid.Empty
        ? recordId : throw new ArgumentException("A formal attendance identity is required.", nameof(recordId));
}

/*
 * 檔案整體說明
 * 目的與定位：P5-11 繳費通知的雙目標純文字規則，承接退役 FeePushLineWorkflow
 * 的 SendGratitudeLineMessage 原文、付款方式與奉獻類別，不擁有資料查詢或通知連線。
 * 主要流程：呼叫端先驗正式收費單、會友與教會，傳入有界文字、UTC 付款日、教會
 * 時區及已解析文字 context；本檔經產品字典具名格式化，保留舊空格、CRLF 與
 * Convert.ToInt32(decimal) 的中點取偶數語意。日期明確使用教會時區與格式文化。
 * 資源與隔離：所有輸入輸出只屬本次同步呼叫，無快取、Session、token、SDK、
 * 背景工作或需 Dispose 的資源；借用 catalog 不保存個資。超量、型別及日期錯誤
 * 直接交外殼，不截斷或拿其他教會的資料補值。離線期望檔驗證不代表實機送達。
 */
using System;
using System.Globalization;
using SpeechMessage.Localization;

namespace SpeechMessageProducts.GoodShepherd.Core.Fees;

/// <summary>保留退役通知內容的無狀態格式化規則；授權與收件者檢查由呼叫端負責。</summary>
public static class FeeNotificationRule
{
    /// <summary>
    /// 產生單一收費單通知全文；未知類別沿舊值為月定、未知付款方式沿舊值為未知。
    /// 文化及 UTC Kind 必須明確，不依賴執行緒、伺服器時區或浮點數金額。
    /// </summary>
    /// <param name="catalog">本次已選定的產品與共用文字來源，僅借用。</param>
    /// <param name="context">由正式 fee→contact→account 推導的文字範圍，不自行授權。</param>
    /// <param name="name">正式會友姓名，最多一百字，保留原空白。</param>
    /// <param name="payDateUtc">D365 UserLocal 欄位已正規化的 UTC 瞬間。</param>
    /// <param name="timeZone">來源教會時區，不能由呼叫者路由替換。</param>
    /// <param name="category">退役奉獻類別 OptionSet 整數。</param>
    /// <param name="payWay">退役付款方式 OptionSet 整數。</param>
    /// <param name="others">其他說明，最多兩千字，不修剪。</param>
    /// <param name="amount">實收 decimal 金額，中點取偶數後維持舊整數文字。</param>
    /// <param name="categoryName">呼叫端已依正式 lookup 與教會範圍驗證的新類別名稱；null 時沿用舊 OptionSet 字典標籤。</param>
    /// <returns>含 CRLF 的完整有界訊息，無任何 token 或 LINE ID。</returns>
    public static string Compose(ITextCatalog catalog, TextContext context, string name, DateTime payDateUtc,
        string timeZone, int category, int payWay, string others, decimal amount, string? categoryName = null)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(context);
#else
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (context is null) throw new ArgumentNullException(nameof(context));
#endif
        if (name is null || name.Length > 100 || others is null || others.Length > 2000)
            throw new ArgumentException("Fee notification text exceeds its bounded budget.");
        if (categoryName is { } suppliedCategoryName
            && (string.IsNullOrWhiteSpace(suppliedCategoryName) || suppliedCategoryName.Length > 200))
            throw new ArgumentException("The validated offering category name is invalid.", nameof(categoryName));
        var local = new ChurchClock().ToChurchTime(payDateUtc, timeZone);
        var culture = CultureInfo.GetCultureInfo(context.FormatCulture);
        var categoryIndex = category is >= 100000000 and <= 100000017 ? category - 100000000 : 0;
        var payIndex = payWay is >= 100000000 and <= 100000008 ? payWay - 100000000 : 4;
        var categoryText = categoryName
            ?? catalog.GetText(context, new TextKey("Fees.Category.C" + categoryIndex.ToString(CultureInfo.InvariantCulture)));
        return catalog.GetText(context, new TextKey("Fees.Notify.Template"),
            new TextArg("member", name),
            new TextArg("date", local.ToString("d", culture)),
            new TextArg("category", categoryText),
            new TextArg("others", others),
            new TextArg("payway", catalog.GetText(context, new TextKey("Fees.PayWay.P" + payIndex.ToString(CultureInfo.InvariantCulture)))),
            new TextArg("amount", Convert.ToInt32(amount).ToString(culture)));
    }
}

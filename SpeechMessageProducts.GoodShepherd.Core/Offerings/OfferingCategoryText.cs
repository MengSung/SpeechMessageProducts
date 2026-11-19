/*
 * 檔案整體說明
 *
 * 目的與定位：集中奉獻類別文字的相容性回退次序，供收費單與預約等消費端共用，
 * 避免各頁面自行把未知選項誤顯示成某一特定奉獻名稱。
 * 主要流程：優先採用已讀取的 lookup 類別名稱；lookup 不可用時使用 SDK 已格式化的舊選項標籤；
 * 兩者皆無可用文字時回退至資源字典提供的通用「奉獻」文字。
 * 使用邊界與維護：本型別為無狀態純函式，不讀 SDK、教會設定、語系快取或 request；字典回退
 * 由呼叫端依完整 TextContext 解析後傳入，不能以使用者提供的教會或未驗證顯示文字建立授權。
 * 失敗與驗證：空白候選依序回退；若連通用字典文字也空白則拒絕，避免回傳空 UI 標籤。
 * 單元測試應驗三階段優先順序與未知類別不回退成「十一奉獻」等硬編碼業務名稱。
 */
using System;

namespace SpeechMessageProducts.GoodShepherd.Core.Offerings;

/// <summary>奉獻類別顯示文字的純回退函式；不保存翻譯、組織或會友狀態。</summary>
public static class OfferingCategoryText
{
    /// <summary>依 lookup 名稱、舊選項格式化標籤、通用字典文字的固定優先序選出非空顯示文字。</summary>
    /// <param name="categoryName">已驗證 lookup 指向的類別名稱；不存在時傳 null。</param>
    /// <param name="legacyOptionLabel">SDK 格式化舊選項標籤；不得以代碼或數值自行翻譯。</param>
    /// <param name="genericOfferingText">由呼叫端依正確字典／語境解析的通用奉獻文字。</param>
    /// <returns>第一個有效的顯示字串。</returns>
    public static string Resolve(string? categoryName, string? legacyOptionLabel, string genericOfferingText)
    {
        if (!string.IsNullOrWhiteSpace(categoryName)) return categoryName!;
        if (!string.IsNullOrWhiteSpace(legacyOptionLabel)) return legacyOptionLabel!;
        if (!string.IsNullOrWhiteSpace(genericOfferingText)) return genericOfferingText;
        throw new ArgumentException("Generic offering text is required.", nameof(genericOfferingText));
    }
}

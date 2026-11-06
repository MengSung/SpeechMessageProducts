/*
 * 檔案整體說明
 *
 * 目的與定位：承載伺服器授權後的不可變文字範圍；建立物件不代表取得資料存取權，不持有 request 或憑證。
 *
 * 主要功能與流程：
 * 本檔宣告 TextContext。
 * - 建立不可變範圍；預設值只支援既有產品呼叫，不取代 host 授權。
 * - 限制範圍值長度並拒絕空白及控制字元，避免模糊識別或無界保留；不自行修剪或改寫授權識別。
 * 主要操作入口包含 WithCultures、HasSameScope、RequireBoundary。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ChurchReport.MemberInfo.Tests/LineSharedWorkflow/OverrideCacheExceptionTests.cs、
 * SpeechMessage.Localization.Tests/ChurchTextOverrideValidatorTests.cs、
 * SpeechMessage.Localization.Tests/TextCatalogTests.cs、
 * SpeechMessage.Tenancy.Tests/ChurchTextOverrideCacheTests.cs、
 * SpeechMessage.Tenancy.Tests/OverrideCompositionTests.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Globalization;

namespace SpeechMessage.Localization;

/// <summary>承載伺服器授權後的不可變文字範圍；建立物件不代表取得資料存取權，不持有 request 或憑證。</summary>
public sealed class TextContext
{
    /// <summary>伺服器驗證的教會識別。</summary>
    public Guid ChurchId { get; }
    /// <summary>文字選擇語系，不能改變授權範圍。</summary>
    public string TextCulture { get; } = string.Empty;
    /// <summary>明確格式文化，不依賴執行緒文化。</summary>
    public string FormatCulture { get; } = string.Empty;
    /// <summary>伺服器驗證的組織範圍。</summary>
    public string Organization { get; } = string.Empty;
    /// <summary>伺服器驗證的產品範圍。</summary>
    public string Product { get; } = string.Empty;
    /// <summary>建立不可變範圍；預設值只支援既有產品呼叫，不取代 host 授權。</summary>
    /// <param name="churchId">非空教會 ID。</param>
    /// <param name="textCulture">文字語系。</param>
    /// <param name="formatCulture">格式文化。</param>
    /// <param name="organization">伺服器組織識別。</param>
    /// <param name="product">伺服器產品識別。</param>
    public TextContext(Guid churchId, string textCulture, string formatCulture, string organization = "default", string product = "ChurchReport")
    {
        if (churchId == Guid.Empty) throw new ArgumentException("A validated church identity is required.", nameof(churchId));
        ChurchId = churchId;
        Organization = RequireBoundary(organization, nameof(organization));
        Product = RequireBoundary(product, nameof(product));
        TextCulture = RequireBoundary(textCulture, nameof(textCulture));
        // 格式文化先驗證；偽語系亦不容許無效輸入，且永遠使用核定的 en-US。
        _ = CultureInfo.GetCultureInfo(RequireBoundary(formatCulture, nameof(formatCulture)));
        FormatCulture = string.Equals(textCulture, "qps-ploc", StringComparison.OrdinalIgnoreCase) ? "en-US" : formatCulture;
    }
    /// <summary>只替換語系，保留教會、組織及產品。</summary>
    /// <param name="textCulture">新文字語系。</param>
    /// <param name="formatCulture">新格式文化。</param>
    /// <returns>新的不可變範圍。</returns>
    public TextContext WithCultures(string textCulture, string formatCulture) => new(ChurchId, textCulture, formatCulture, Organization, Product);
    /// <summary>按完整教會邊界比較，不以語言或格式文化代表授權。</summary>
    /// <param name="other">另一個範圍。</param>
    /// <returns>全部 scope 欄位相同時為真。</returns>
    public bool HasSameScope(TextContext other) => other is not null && ChurchId == other.ChurchId
        && string.Equals(Organization, other.Organization, StringComparison.Ordinal)
        && string.Equals(Product, other.Product, StringComparison.Ordinal);

    /// <summary>限制範圍值長度並拒絕空白及控制字元，避免模糊識別或無界保留；不自行修剪或改寫授權識別。</summary>
    /// <param name="value">伺服器驗證後的識別或語系。</param>
    /// <param name="parameter">固定參數名稱。</param>
    /// <returns>原始且不可變的有效值。</returns>
    private static string RequireBoundary(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("A bounded nonempty scope value is required.", parameter);
        foreach (var character in value)
            if (char.IsControl(character)) throw new ArgumentException("Scope values cannot contain control characters.", parameter);
        return value;
    }
}

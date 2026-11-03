using System;
using System.Collections.Generic;

namespace SpeechMessage.Localization;

/// <summary>宣告系統語系的發布狀態；只提供中繼資料，不代替主機開放或授權檢查。</summary>
public enum CultureReleaseStatus
{
    /// <summary>完整發布的基礎語系。</summary>
    Released,
    /// <summary>只有結構的英文空殼，缺字回繁中，目前不開放。</summary>
    Shell,
    /// <summary>僅供測試環境驗收的偽語系。</summary>
    Pseudo
}

/// <summary>單一語系的不可變發布資訊，沒有設定覆寫、使用者或教會狀態。</summary>
public sealed class SupportedCulture
{
    /// <summary>標準語系代碼，字串不可變。</summary>
    public string Name { get; }

    /// <summary>固定發布狀態，不提供公開 setter。</summary>
    public CultureReleaseStatus Status { get; }

    /// <summary>只供本組件以固定常值建立，不接受外部可變設定來源。</summary>
    /// <param name="name">語系代碼。</param>
    /// <param name="status">固定發布狀態。</param>
    internal SupportedCulture(string name, CultureReleaseStatus status)
    {
        Name = name;
        Status = status;
    }
}

/// <summary>固定且唯讀的三語系清單，容量不隨請求、使用者或語系查詢增加。</summary>
public static class SupportedCultures
{
    /// <summary>回傳已發布繁中、英文空殼及偽語系的不可變清單，不暴露可變陣列。</summary>
    public static IReadOnlyList<SupportedCulture> All { get; } = Array.AsReadOnly(new[]
    {
        new SupportedCulture("zh-TW", CultureReleaseStatus.Released),
        new SupportedCulture("en", CultureReleaseStatus.Shell),
        new SupportedCulture("qps-ploc", CultureReleaseStatus.Pseudo)
    });
}

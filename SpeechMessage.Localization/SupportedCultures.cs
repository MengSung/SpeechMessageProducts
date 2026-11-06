/*
 * 檔案整體說明
 *
 * 目的與定位：固定且唯讀的三語系清單，容量不隨請求、使用者或語系查詢增加。
 *
 * 主要功能與流程：
 * 本檔宣告 CultureReleaseStatus、SupportedCulture、SupportedCultures。
 * - 回傳已發布繁中、英文空殼及偽語系的不可變清單，不暴露可變陣列。
 * 資料／選項欄位包含 CultureReleaseStatus、SupportedCulture、Name、Status、SupportedCultures、All，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessage.Localization.Tests/SystemTextCatalogTests.cs 追查目前工作樹中的合作／使用位置。
 */
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

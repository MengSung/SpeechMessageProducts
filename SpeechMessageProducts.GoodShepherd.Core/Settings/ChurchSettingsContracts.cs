/*
 * 檔案整體說明
 *
 * 目的與定位：定義 ChurchSettingsService 使用的儲存、時鐘與快取失效 ports，以及跨層傳遞的純資料值。
 * 主要流程：核心服務將已授權教會與完整文字 context 轉成覆寫、類別及稽核操作；Host adapter 負責實際 Dataverse I/O，
 * 將 UTC 變更時間寫回 account，並把相同 scope 傳給既有快取 owner。
 * 使用邊界與維護：介面不暴露 SDK Entity、連線或 request 狀態；實作必須以教會主鍵隔離所有讀寫，查詢受 200 筆上限約束。
 * 此檔沒有快取、背景工作、timer 或 IDisposable 資源；取消 token 僅屬本次操作，不能保留至後續 request。
 * 失敗與驗證：儲存故障需原樣向呼叫者傳遞；服務只在前序持久化步驟成功後呼叫下一 port。專案測試以操作紀錄 fake 驗證順序。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Offerings;

namespace SpeechMessageProducts.GoodShepherd.Core.Settings;

/// <summary>覆寫 upsert 的純值投影，替代索引鍵固定為教會、穩定鍵與語系。</summary>
public sealed record ChurchTextOverrideWrite(Guid ChurchId, string Key, string Culture, string Text);

/// <summary>最近一次設定修改的有限純值稽核列，不含 SDK、request 或登入工作階段參考。</summary>
public sealed record ChurchSettingsAuditRecord(Guid Id, Guid ChurchId, DateTime ChangedOnUtc,
    string Field, string Before, string After, string ActorDisplayName);

/// <summary>提供本次服務操作所需的 UTC 時刻；實作不得回傳 local 或 unspecified 時間。</summary>
public interface IChurchSettingsClock
{
    /// <summary>取得 UTC 時鐘值，不建立背景工作或跨操作狀態。</summary>
    DateTime UtcNow { get; }
}

/// <summary>使單一完整文字 scope 的本程序覆寫快照立即失效；不持有或釋放快取 owner。</summary>
public interface IChurchSettingsCacheInvalidator
{
    /// <summary>以服務端確認的完整 context 與已持久化 UTC 版本失效對應快取。</summary>
    /// <param name="context">已授權的組織、產品及教會文字範圍。</param>
    /// <param name="changedOnUtc">剛寫入 account 的 UTC 版本。</param>
    void Invalidate(TextContext context, DateTime changedOnUtc);
}

/// <summary>教會設定的儲存 port；所有方法都必須以正式教會 ID 限定資料範圍。</summary>
public interface IChurchSettingsStore
{
    /// <summary>依教會／鍵／語系替代索引鍵原子新增或更新覆寫。</summary>
    Task UpsertOverrideAsync(ChurchTextOverrideWrite value, CancellationToken cancellationToken);
    /// <summary>移除指定教會的單一鍵與語系覆寫。</summary>
    Task RemoveOverrideAsync(Guid churchId, string key, string culture, CancellationToken cancellationToken);
    /// <summary>更新指定 account 的 new_textchangedon，輸入必須為 UTC。</summary>
    Task TouchTextChangedOnAsync(Guid churchId, DateTime changedOnUtc, CancellationToken cancellationToken);
    /// <summary>讀取本教會最多 200 筆奉獻類別純值。</summary>
    Task<IReadOnlyList<OfferingCategory>> ReadCategoriesAsync(Guid churchId, CancellationToken cancellationToken);
    /// <summary>檢查代碼是否已由本教會使用。</summary>
    Task<bool> CategoryCodeExistsAsync(Guid churchId, string code, CancellationToken cancellationToken);
    /// <summary>使用服務端代碼新增單一教會類別並回傳正式資料庫主鍵。</summary>
    Task<Guid> AddCategoryAsync(Guid churchId, string code, string name, int sortOrder,
        string? accountingCode, int? legacyOptionValue, CancellationToken cancellationToken);
    /// <summary>只更新已由同教會查詢確認的正式類別。</summary>
    Task UpdateCategoryAsync(Guid churchId, Guid id, string name, int sortOrder,
        string? accountingCode, int? legacyOptionValue, bool isActive, CancellationToken cancellationToken);
    /// <summary>依教會及上限讀取最新稽核純值；實作須在資料來源端先過濾教會。</summary>
    Task<IReadOnlyList<ChurchSettingsAuditRecord>> ReadAuditAsync(Guid churchId, int maximum,
        CancellationToken cancellationToken);
}

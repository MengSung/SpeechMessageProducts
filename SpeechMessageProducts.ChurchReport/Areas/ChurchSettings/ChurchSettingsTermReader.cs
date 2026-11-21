/*
 * 檔案整體說明
 *
 * 目的與定位：定義 ChurchSettings 名詞頁讀取教會覆寫的純值邊界，避免 MVC 直接操作 Dataverse Entity。
 * 主要流程：呼叫端傳入已由 ChurchSettingsAccess 驗證的 account ID，實作最多回傳 200 筆覆寫，
 * 每列包含正式資料列 ID、教會 ID、穩定詞彙鍵、語系及原始覆寫文字；Controller 再與系統原文及
 * 有覆寫 catalog 預覽組合成頁面模型。
 * 使用邊界與維護：介面不執行授權、不保存 request／Session／教會狀態，也不擁有 CRM 連線；
 * Dataverse adapter 借用 Host request scope，必須先以 account 條件查詢並驗證回傳 owner、資料列 ID
 * 與 key/culture 唯一性。結果僅在單次 action 存活。
 * 失敗與驗證：超過上限、空或重複 ID、外教會資料及重複 key/culture 一律拒絕整批發布；
 * SDK 例外原樣傳遞。離線合成測試覆蓋範圍、上限與重複資料，不宣稱 D365 實機結果。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Areas.ChurchSettings;

/// <summary>名詞頁可顯示的單筆教會覆寫純值；Id 是 Dataverse 正式資料列主鍵。</summary>
/// <param name="Id">來源 new_localizedtext 正式資料列 ID。</param>
/// <param name="ChurchId">資料列 account owner，必須等於本次授權教會。</param>
/// <param name="Key">固定系統詞彙鍵；呼叫端只允許核心白名單中的鍵進入編輯模型。</param>
/// <param name="Culture">覆寫語系或共用 fallback 標記。</param>
/// <param name="Text">保留原樣的覆寫文字，由 catalog 做實際預覽解析。</param>
public sealed record ChurchSettingsTermOverride(
    Guid Id,
    Guid ChurchId,
    string Key,
    string Culture,
    string Text);

/// <summary>依已驗證教會 scope 讀取有界名詞覆寫的 request adapter 契約。</summary>
public interface IChurchSettingsTermReader
{
    /// <summary>讀取單一教會最多 200 筆覆寫；實作須先在資料來源套用教會條件再發布結果。</summary>
    /// <param name="churchId">由本次登入 contact 的 account 關聯解析出的教會 ID。</param>
    /// <param name="cancellationToken">只取消本次 HTTP request 的讀取。</param>
    /// <returns>完全驗證且不含 SDK Entity 的覆寫候選集合。</returns>
    Task<IReadOnlyList<ChurchSettingsTermOverride>> ReadTermOverridesAsync(
        Guid churchId,
        CancellationToken cancellationToken);
}

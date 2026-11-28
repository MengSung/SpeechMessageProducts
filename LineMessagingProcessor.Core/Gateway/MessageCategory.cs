/*
檔案整體說明
目的與定位：F5 發送紀錄的固定分類字典，供 Gateway 與 Dataverse adapter 共同映射選項。
主要功能與流程：明確區分交易、營運及行銷訊息；數值固定為 schema 的 100000000 起始選項，
交易通知此階段只記成本，並未實作 G4 點數扣除或費率規則。
使用邊界與維護：此列舉沒有資料存取、狀態、快取或資源；呼叫端仍須驗證 enum 是否定義，
不能把任意數字寫入資料庫。更新映射時須連同 schema 及離線 adapter 測試一起核對。
*/
namespace LineMessagingProcessor.Core.Gateway;

/// <summary>持久發送紀錄的穩定分類，此階段不扣點。</summary>
public enum MessageCategory
{
    /// <summary>交易通知，只記成本。</summary>
    Transactional = 100000000,
    /// <summary>租戶營運通知。</summary>
    Operational = 100000001,
    /// <summary>行銷訊息，額度規則留待核定階段。</summary>
    Marketing = 100000002
}

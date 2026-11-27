/*
檔案整體說明
目的與定位：定義三產品共用 Channel 名額池的名額持久狀態，清理中及清理失敗不得再次配發，提供純規則與平台 Plugin 共用的持久數值契約。
主要功能與流程：列舉精確對應 D365 choice，不接受顯示文字推測或零值預設；讀取端須先確認欄位存在並驗證列舉範圍，再決定交易狀態轉換。
使用邊界與維護：本檔只有不可變數值，沒有 SDK、查詢、憑證、快取或可釋放資源，不負責伺服器授權。修改數值必須與 schema 及離線狀態測試同步，不可為相容而把未知狀態當成安全可用。
*/
namespace LineMessagingProcessor.Core.ChannelPool;

/// <summary>名額持久狀態，清理中及清理失敗不得再次配發；持久數值不可重排，未知值必須由讀取邊界拒絕。</summary>
public enum SlotStatus
{
    /// <summary>可配發的乾淨空名額；資料讀取端仍須確認沒有租戶及使用中的宮格。</summary>
    Available = 100000000,
    /// <summary>已配發給單一租戶；釋放必須在交易鎖內重新讀取使用量。</summary>
    Allocated = 100000001,
    /// <summary>保留中的名額，不因沒有使用量就自動變成可配發。</summary>
    Reserved = 100000002,
    /// <summary>仍有待清理宮格的名額，清理確認完成以前禁止重配。</summary>
    Cleaning = 100000003,
    /// <summary>清理失敗的隔離名額，必須由明確修復流程處理。</summary>
    CleanupFailed = 100000004,
}

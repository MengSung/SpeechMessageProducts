/*
檔案整體說明
目的與定位：定義三產品共用 Channel 名額池的頻道池生命週期，只有營運中可供純規則選取，提供純規則與平台 Plugin 共用的持久數值契約。
主要功能與流程：列舉精確對應 D365 choice，不接受顯示文字推測或零值預設；讀取端須先確認欄位存在並驗證列舉範圍，再決定交易狀態轉換。
使用邊界與維護：本檔只有不可變數值，沒有 SDK、查詢、憑證、快取或可釋放資源，不負責伺服器授權。修改數值必須與 schema 及離線狀態測試同步，不可為相容而把未知狀態當成安全可用。
*/
namespace LineMessagingProcessor.Core.ChannelPool;

/// <summary>頻道池生命週期，只有營運中可供純規則選取；持久數值不可重排，未知值必須由讀取邊界拒絕。</summary>
public enum ChannelStatus
{
    /// <summary>僅登錄而尚未開放的頻道，不能配發。</summary>
    Registered = 100000000,
    /// <summary>已營運的頻道，但仍須通過產品、名額及容量驗證。</summary>
    Operating = 100000001,
    /// <summary>沒有可配發名額；安全釋放空名額後可恢復營運中。</summary>
    Full = 100000002,
    /// <summary>停用頻道，不得因釋放而意外重新啟用。</summary>
    Disabled = 100000003,
    /// <summary>已退役頻道，不能再接受新租戶。</summary>
    Retired = 100000004,
}

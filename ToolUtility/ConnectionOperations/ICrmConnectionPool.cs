// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility/ConnectionOperations/ICrmConnectionPool.cs
// 所屬區塊：ChurchReport 共用工具與整合輔助層，包含通知、付款、CRM 或跨模組 helper。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface ICrmConnectionPool、class ConnectionPoolStats
// 主要成員：ToString、TotalConnections、ActiveConnections、IdleConnections、WaitingRequests、CreatedAt、LastActivityAt、TotalAcquireCount、TotalReleaseCount、TimeoutCount
// 引用命名空間：Microsoft.Xrm.Sdk、System
// 閱讀路徑：閱讀此檔案時應先確認 CRM entity 名稱、欄位 logical name、查詢條件與外部服務例外如何被轉換或記錄。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Xrm.Sdk;
using System;

namespace ToolUtilityNameSpace.ConnectionOperations
{
    /// <summary>
    /// CRM 連接池介面
    /// 實現 Object Pool Pattern 以優化連接管理
    /// 遵循 LINUS 代碼原則：簡潔、高效、可靠
    /// </summary>
    public interface ICrmConnectionPool : IDisposable
    {
        /// <summary>
        /// 從連接池取得可用連接
        /// </summary>
        /// <returns>IOrganizationService 連接實例</returns>
        IOrganizationService AcquireConnection();

        /// <summary>
        /// 歸還連接至連接池
        /// </summary>
        /// <param name="service">要歸還的連接</param>
        void ReleaseConnection(IOrganizationService service);

        /// <summary>
        /// 取得連接池統計資訊
        /// </summary>
        /// <returns>連接池統計資料</returns>
        ConnectionPoolStats GetStats();

        /// <summary>
        /// 驗證連接是否有效
        /// </summary>
        /// <param name="service">要驗證的連接</param>
        /// <returns>true 表示連接有效</returns>
        bool ValidateConnection(IOrganizationService service);
    }

    /// <summary>
    /// 連接池統計資訊
    /// </summary>
    public class ConnectionPoolStats
    {
        /// <summary>
        /// 連接池中總連接數
        /// </summary>
        public int TotalConnections { get; set; }

        /// <summary>
        /// 當前活躍（使用中）的連接數
        /// </summary>
        public int ActiveConnections { get; set; }

        /// <summary>
        /// 當前閒置（可用）的連接數
        /// </summary>
        public int IdleConnections { get; set; }

        /// <summary>
        /// 等待連接的請求數
        /// </summary>
        public int WaitingRequests { get; set; }

        /// <summary>
        /// 連接池創建時間
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// 最後活動時間
        /// </summary>
        public DateTime LastActivityAt { get; set; }

        /// <summary>
        /// 總共取得連接次數
        /// </summary>
        public long TotalAcquireCount { get; set; }

        /// <summary>
        /// 總共歸還連接次數
        /// </summary>
        public long TotalReleaseCount { get; set; }

        /// <summary>
        /// 連接超時次數
        /// </summary>
        public long TimeoutCount { get; set; }

        /// <summary>
        /// 連接驗證失敗次數
        /// </summary>
        public long ValidationFailureCount { get; set; }

        public override string ToString()
        {
            return $"Total: {TotalConnections}, Active: {ActiveConnections}, Idle: {IdleConnections}, " +
                   $"Waiting: {WaitingRequests}, Acquired: {TotalAcquireCount}, Released: {TotalReleaseCount}, " +
                   $"Timeouts: {TimeoutCount}, ValidationFailures: {ValidationFailureCount}";
        }
    }
}

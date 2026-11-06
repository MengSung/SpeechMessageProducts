/*
 * 檔案整體說明
 *
 * 目的與定位：CRM 連接池介面 實現 Object Pool Pattern 以優化連接管理 遵循 LINUS 代碼原則：簡潔、高效、可靠
 *
 * 主要功能與流程：
 * 本檔宣告 ICrmConnectionPool、ConnectionPoolStats。
 * 主要操作入口包含 ToString。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 ToolUtility/Dataverse/ConnectionPoolStatsAdapter.cs、
 * ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs 追查目前工作樹中的合作／使用位置。
 */
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

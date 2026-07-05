// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Middleware/IdentityAuditCleanupService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 IdentityAuditCleanupService 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class IdentityAuditCleanupService
// 主要成員：ExecuteAsync、StopAsync
// 引用命名空間：Microsoft.Extensions.Hosting、Microsoft.Extensions.Logging、System、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Middleware
{
    /// <summary>
    /// 身份審計清理服務 (Background Service)
    ///
    /// 設計原則:
    /// - Single Responsibility Principle (SRP): 專注於定期清理追蹤資料
    /// - Open/Closed Principle: 繼承 BackgroundService，擴展而不修改框架
    /// - Dependency Inversion Principle: 依賴 ILogger 抽象
    ///
    /// 作用:
    /// 定期清理 IdentityAuditMiddleware 中的追蹤資料，防止記憶體洩漏。
    ///
    /// 清理策略:
    /// - 每 30 分鐘執行一次
    /// - 清除超過 1 小時未活動的記錄
    /// - 記錄清理結果到日誌
    ///
    /// 記憶體管理最佳實務:
    /// - 避免無限增長的靜態集合
    /// - 定期清理舊資料
    /// - 監控清理效果
    ///
    /// 使用方式:
    /// 在 Startup.cs 的 ConfigureServices 中註冊為 HostedService:
    /// <code>
    /// #if DEBUG
    /// services.AddHostedService&lt;IdentityAuditCleanupService&gt;();
    /// #endif
    /// </code>
    ///
    /// ?? 注意: 僅在 DEBUG 模式下啟用
    /// </summary>
    public class IdentityAuditCleanupService : BackgroundService
    {
        private readonly ILogger<IdentityAuditCleanupService> _logger;
        private readonly TimeSpan _cleanupInterval;
        private readonly TimeSpan _dataRetention;

        /// <summary>
        /// 建構函式：注入日誌服務並設定清理參數
        /// </summary>
        /// <param name="logger">日誌服務</param>
        public IdentityAuditCleanupService(ILogger<IdentityAuditCleanupService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cleanupInterval = TimeSpan.FromMinutes(30);  // 每 30 分鐘執行一次
            _dataRetention = TimeSpan.FromHours(1);       // 保留 1 小時內的資料
        }

        /// <summary>
        /// 背景服務主要執行方法
        ///
        /// 執行流程:
        /// 1. 啟動日誌記錄
        /// 2. 進入無限迴圈
        /// 3. 每 30 分鐘執行一次清理
        /// 4. 記錄清理結果
        /// 5. 處理取消請求
        ///
        /// 錯誤處理:
        /// - 使用 try-catch 確保服務不會因單次錯誤而停止
        /// - 記錄錯誤到日誌
        /// - 繼續下一次清理週期
        /// </summary>
        /// <param name="stoppingToken">取消令牌</param>
        /// <returns>非同步任務</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[IdentityAuditCleanup] 服務已啟動");
            _logger.LogInformation("[IdentityAuditCleanup] 清理間隔: {Interval} 分鐘", _cleanupInterval.TotalMinutes);
            _logger.LogInformation("[IdentityAuditCleanup] 資料保留: {Retention} 小時", _dataRetention.TotalHours);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 延遲到下一次清理時間
                    await Task.Delay(_cleanupInterval, stoppingToken);

                    // 執行清理
                    _logger.LogInformation("[IdentityAuditCleanup] 開始清理舊資料...");

                    var removedCount = IdentityAuditMiddleware.CleanupOldTracking(_dataRetention);

                    if (removedCount > 0)
                    {
                        _logger.LogInformation(
                            "[IdentityAuditCleanup] ? 已清理 {Count} 筆舊資料",
                            removedCount);
                    }
                    else
                    {
                        _logger.LogDebug("[IdentityAuditCleanup] 無需清理（沒有舊資料）");
                    }

                    // 取得當前追蹤資料數量
                    var currentCount = IdentityAuditMiddleware.GetTrackingSnapshot().Count;
                    _logger.LogInformation(
                        "[IdentityAuditCleanup] 當前追蹤資料數量: {Count}",
                        currentCount);
                }
                catch (OperationCanceledException)
                {
                    // 正常的取消操作，不需要記錄錯誤
                    _logger.LogInformation("[IdentityAuditCleanup] 服務正在停止...");
                    break;
                }
                catch (Exception ex)
                {
                    // 記錄錯誤但不中斷服務
                    _logger.LogError(
                        ex,
                        "[IdentityAuditCleanup] ? 清理過程發生錯誤");
                }
            }

            _logger.LogInformation("[IdentityAuditCleanup] 服務已停止");
        }

        /// <summary>
        /// 服務停止時的清理方法
        ///
        /// 執行最後一次清理，確保資料不會殘留
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>非同步任務</returns>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[IdentityAuditCleanup] 執行最後一次清理...");

            try
            {
                var removedCount = IdentityAuditMiddleware.CleanupOldTracking(TimeSpan.Zero);
                _logger.LogInformation(
                    "[IdentityAuditCleanup] 最後清理完成，移除 {Count} 筆資料",
                    removedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[IdentityAuditCleanup] 最後清理失敗");
            }

            await base.StopAsync(cancellationToken);
        }
    }
}

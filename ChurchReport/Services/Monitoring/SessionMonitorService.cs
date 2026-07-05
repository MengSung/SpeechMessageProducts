// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Monitoring/SessionMonitorService.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface ISessionMonitorService、class SessionMonitorService、class SessionRecord、class SessionStatistics
// 主要成員：RecordSessionActivity、GetStatistics、CleanupExpiredRecords、LogStatistics、StartAsync、StopAsync、Dispose、SessionId、CreatedAt、LastActivityAt
// 引用命名空間：System、System.Collections.Concurrent、System.Collections.Generic、System.Linq、System.Threading、System.Threading.Tasks、Microsoft.AspNetCore.Http、Microsoft.Extensions.Hosting
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChurchReport.Services.Monitoring
{
    /// <summary>
    /// Session 監控服務介面
    /// ? Phase 8: 實現 Session 監控功能
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public interface ISessionMonitorService
    {
        /// <summary>
        /// 記錄 Session 活動
        /// </summary>
        void RecordSessionActivity(string sessionId);

        /// <summary>
        /// 取得 Session 統計資訊
        /// </summary>
        SessionStatistics GetStatistics();

        /// <summary>
        /// 清理過期的 Session 記錄
        /// </summary>
        void CleanupExpiredRecords();
    }

    /// <summary>
    /// Session 監控服務實作
    /// ? Phase 8: 追蹤活躍 Session 數量和記憶體使用
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public class SessionMonitorService : ISessionMonitorService, IHostedService, IDisposable
    {
        private readonly ILogger<SessionMonitorService> _logger;
        private readonly ConcurrentDictionary<string, SessionRecord> _activeSessions;
        private readonly Timer _cleanupTimer;
        private readonly Timer _reportTimer;

        // 設定
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _reportInterval = TimeSpan.FromMinutes(10);

        // 統計
        private long _totalSessionsCreated = 0;
        private long _peakActiveSessions = 0;
        private DateTime _startTime = DateTime.UtcNow;

        public SessionMonitorService(ILogger<SessionMonitorService> logger)
        {
            _logger = logger;
            _activeSessions = new ConcurrentDictionary<string, SessionRecord>();

            // 清理計時器
            _cleanupTimer = new Timer(
                _ => CleanupExpiredRecords(),
                null,
                _cleanupInterval,
                _cleanupInterval);

            // 報告計時器
            _reportTimer = new Timer(
                _ => LogStatistics(),
                null,
                _reportInterval,
                _reportInterval);
        }

        /// <summary>
        /// 記錄 Session 活動
        /// </summary>
        public void RecordSessionActivity(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return;

            var now = DateTime.UtcNow;

            _activeSessions.AddOrUpdate(
                sessionId,
                // 新增
                key =>
                {
                    Interlocked.Increment(ref _totalSessionsCreated);
                    return new SessionRecord
                    {
                        SessionId = key,
                        CreatedAt = now,
                        LastActivityAt = now,
                        RequestCount = 1
                    };
                },
                // 更新
                (key, existing) =>
                {
                    existing.LastActivityAt = now;
                    existing.RequestCount++;
                    return existing;
                });

            // 更新峰值
            var currentCount = _activeSessions.Count;
            long currentPeak;
            do
            {
                currentPeak = Interlocked.Read(ref _peakActiveSessions);
                if (currentCount <= currentPeak)
                    break;
            }
            while (Interlocked.CompareExchange(ref _peakActiveSessions, currentCount, currentPeak) != currentPeak);
        }

        /// <summary>
        /// 取得 Session 統計資訊
        /// </summary>
        public SessionStatistics GetStatistics()
        {
            var now = DateTime.UtcNow;
            var activeSessions = _activeSessions.Values.ToList();

            // 計算活躍 Session（30 分鐘內有活動）
            var activeCount = activeSessions.Count(s => (now - s.LastActivityAt) <= _sessionTimeout);

            // 計算閒置 Session
            var idleCount = activeSessions.Count - activeCount;

            // 計算平均請求數
            var avgRequests = activeSessions.Count > 0
                ? activeSessions.Average(s => s.RequestCount)
                : 0;

            // 估算記憶體使用（每個 Session 約 2KB 基礎 + 變動資料）
            var estimatedMemoryKB = activeSessions.Count * 2.0;

            return new SessionStatistics
            {
                Timestamp = now,
                ActiveSessionCount = activeCount,
                IdleSessionCount = idleCount,
                TotalTrackedSessions = activeSessions.Count,
                TotalSessionsCreated = Interlocked.Read(ref _totalSessionsCreated),
                PeakActiveSessions = Interlocked.Read(ref _peakActiveSessions),
                AverageRequestsPerSession = avgRequests,
                EstimatedMemoryUsageKB = estimatedMemoryKB,
                UptimeMinutes = (now - _startTime).TotalMinutes,
                SessionTimeoutMinutes = _sessionTimeout.TotalMinutes,
                OldestSessionAge = activeSessions.Count > 0
                    ? (now - activeSessions.Min(s => s.CreatedAt)).TotalMinutes
                    : 0,
                NewestSessionAge = activeSessions.Count > 0
                    ? (now - activeSessions.Max(s => s.CreatedAt)).TotalMinutes
                    : 0
            };
        }

        /// <summary>
        /// 清理過期的 Session 記錄
        /// </summary>
        public void CleanupExpiredRecords()
        {
            try
            {
                var now = DateTime.UtcNow;
                var expiredKeys = _activeSessions
                    .Where(kvp => (now - kvp.Value.LastActivityAt) > _sessionTimeout)
                    .Select(kvp => kvp.Key)
                    .ToList();

                var removedCount = 0;
                foreach (var key in expiredKeys)
                {
                    if (_activeSessions.TryRemove(key, out _))
                    {
                        removedCount++;
                    }
                }

                if (removedCount > 0)
                {
                    _logger.LogDebug(
                        "[Session Monitor] 已清理 {RemovedCount} 個過期 Session 記錄，目前追蹤 {CurrentCount} 個",
                        removedCount,
                        _activeSessions.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session Monitor] 清理過期記錄時發生錯誤");
            }
        }

        /// <summary>
        /// 記錄統計資訊到日誌
        /// </summary>
        private void LogStatistics()
        {
            try
            {
                var stats = GetStatistics();

                _logger.LogInformation(
                    "[Session Monitor] 活躍: {Active}, 閒置: {Idle}, 總追蹤: {Total}, " +
                    "峰值: {Peak}, 估計記憶體: {Memory:F2} KB",
                    stats.ActiveSessionCount,
                    stats.IdleSessionCount,
                    stats.TotalTrackedSessions,
                    stats.PeakActiveSessions,
                    stats.EstimatedMemoryUsageKB);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Session Monitor] 記錄統計資訊時發生錯誤");
            }
        }

        #region IHostedService

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[Session Monitor] Session 監控服務已啟動");
            _startTime = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[Session Monitor] Session 監控服務正在停止...");

            // 記錄最終統計
            var stats = GetStatistics();
            _logger.LogInformation(
                "[Session Monitor] 最終統計 - 總建立: {TotalCreated}, 峰值: {Peak}, 運行時間: {Uptime:F2} 分鐘",
                stats.TotalSessionsCreated,
                stats.PeakActiveSessions,
                stats.UptimeMinutes);

            return Task.CompletedTask;
        }

        #endregion

        #region IDisposable

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _cleanupTimer?.Dispose();
                _reportTimer?.Dispose();
                _activeSessions.Clear();
            }

            _disposed = true;
        }

        #endregion
    }

    /// <summary>
    /// Session 記錄
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public class SessionRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public long RequestCount { get; set; }
    }

    /// <summary>
    /// Session 統計資訊
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public class SessionStatistics
    {
        /// <summary>統計時間戳</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>活躍 Session 數量（30 分鐘內有活動）</summary>
        public int ActiveSessionCount { get; set; }

        /// <summary>閒置 Session 數量</summary>
        public int IdleSessionCount { get; set; }

        /// <summary>總追蹤 Session 數量</summary>
        public int TotalTrackedSessions { get; set; }

        /// <summary>歷史總建立 Session 數量</summary>
        public long TotalSessionsCreated { get; set; }

        /// <summary>峰值活躍 Session 數量</summary>
        public long PeakActiveSessions { get; set; }

        /// <summary>每 Session 平均請求數</summary>
        public double AverageRequestsPerSession { get; set; }

        /// <summary>估計記憶體使用 (KB)</summary>
        public double EstimatedMemoryUsageKB { get; set; }

        /// <summary>運行時間 (分鐘)</summary>
        public double UptimeMinutes { get; set; }

        /// <summary>Session 超時設定 (分鐘)</summary>
        public double SessionTimeoutMinutes { get; set; }

        /// <summary>最舊 Session 年齡 (分鐘)</summary>
        public double OldestSessionAge { get; set; }

        /// <summary>最新 Session 年齡 (分鐘)</summary>
        public double NewestSessionAge { get; set; }
    }
}
#endif

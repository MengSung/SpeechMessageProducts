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
        /// 記錄目前 request 已由 ASP.NET Core 建立之 Session 的診斷活動。
        /// </summary>
        /// <param name="sessionId">僅用於程序內去重的 ASP.NET Core Session 識別值；不得傳入使用者帳號、權杖或其他個資。</param>
        /// <remarks>
        /// 實作只保存受容量與逾時限制的診斷索引，不能把任何 Session 內容、身份或授權資料提升到 singleton。
        /// Host 已停止後呼叫必須無副作用，避免 shutdown 期間重新保留資料。
        /// </remarks>
        void RecordSessionActivity(string sessionId);

        /// <summary>
        /// 取得不含 Session 識別值與個資的監控彙總數值。
        /// </summary>
        /// <returns>呼叫當下的安全彙總快照。</returns>
        SessionStatistics GetStatistics();

        /// <summary>
        /// 移除超過診斷存活時間的 Session 索引。
        /// </summary>
        /// <remarks>
        /// 清理只刪除本服務擁有的字典項目；不會寫入 HTTP Session、快取、Cookie 或任一使用者資料來源。
        /// </remarks>
        void CleanupExpiredRecords();
    }

    /// <summary>
    /// DEBUG 專用的 Session 監控服務實作。
    /// </summary>
    /// <remarks>
    /// 本 singleton 僅保存固定上限的短命 Session 診斷索引，以觀察活躍數量；絕不保存 Session 值、使用者、
    /// tenant、認證或授權資料。索引會在逾時、容量淘汰、Host 停止與 <see cref="Dispose"/> 時由本服務確定清除。
    /// 兩個 timer 的唯一 owner 是此服務：它們只在 <see cref="StartAsync"/> 後建立，並在停止或釋放時停用、
    /// Dispose 並等待正在執行的 callback 排空，避免背景回呼將 singleton 或 Session 記錄延長到 Host 之外。
    /// </remarks>
    public class SessionMonitorService : ISessionMonitorService, IHostedService, IDisposable
    {
        /// <summary>程序級監控索引可保留的最大 Session 筆數。</summary>
        /// <remarks>此值限制 DEBUG 診斷資源，不是授權或連線容量；達到上限時只會遺失最舊的統計樣本。</remarks>
        private const int MaximumTrackedSessions = 4_096;

        private readonly ILogger<SessionMonitorService> _logger;
        private readonly ConcurrentDictionary<string, SessionRecord> _activeSessions;
        private readonly object _capacityGate = new();
        private readonly object _lifecycleGate = new();
        private Timer? _cleanupTimer;
        private Timer? _reportTimer;

        // 設定
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _reportInterval = TimeSpan.FromMinutes(10);

        // 統計
        private long _totalSessionsCreated;
        private long _peakActiveSessions;
        private DateTime _startTime = DateTime.UtcNow;
        private int _stopped;
        private int _disposed;

        /// <summary>
        /// 建立 DEBUG Session 監控服務。
        /// </summary>
        /// <param name="logger">由 DI 擁有的日誌器；只接收不含 Session ID 的彙總資料。</param>
        /// <remarks>
        /// 建構式刻意不建立 timer，避免 Host 尚未啟動或 DI 建構失敗時出現無 owner 的背景 callback。
        /// Timer 由 <see cref="StartAsync"/> 建立，並由停止／釋放路徑成對回收。
        /// </remarks>
        public SessionMonitorService(ILogger<SessionMonitorService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _activeSessions = new ConcurrentDictionary<string, SessionRecord>();
        }

        /// <summary>
        /// 記錄目前 Session 的活動，並在新 Session 流量湧入時保持診斷索引硬上限。
        /// </summary>
        /// <param name="sessionId">由 ASP.NET Core Session middleware 建立的 opaque ID。</param>
        /// <remarks>
        /// 已知 Session 的更新不取全域容量鎖；只有第一次看到的 ID 會短暫進入鎖定區，先淘汰逾時或最舊的
        /// 純診斷項目再新增。鎖內沒有 I/O、日誌、Session 存取或外部服務呼叫，因此不會放大 request 延遲。
        /// <see cref="SessionRecord"/> 的活動時間與計數採原子更新，讀取統計時不會觀察到撕裂值。
        /// </remarks>
        public void RecordSessionActivity(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) ||
                Volatile.Read(ref _stopped) != 0 ||
                Volatile.Read(ref _disposed) != 0)
                return;

            var now = DateTime.UtcNow;

            if (_activeSessions.TryGetValue(sessionId, out var existingRecord))
            {
                existingRecord.RecordActivity(now);
                return;
            }

            lock (_capacityGate)
            {
                if (Volatile.Read(ref _stopped) != 0 || Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

                if (_activeSessions.TryGetValue(sessionId, out existingRecord))
                {
                    existingRecord.RecordActivity(now);
                    return;
                }

                while (_activeSessions.Count >= MaximumTrackedSessions)
                {
                    if (!EvictOneSessionRecordLocked(now))
                    {
                        return;
                    }
                }

                if (!_activeSessions.TryAdd(sessionId, new SessionRecord(sessionId, now)))
                {
                    return;
                }

                Interlocked.Increment(ref _totalSessionsCreated);
            }

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
        /// 在容量鎖已持有時淘汰一筆診斷 Session 索引。
        /// </summary>
        /// <param name="now">本次記錄的 UTC 時間，讓所有比較使用同一個時間點。</param>
        /// <returns>成功移除一筆項目時為 <see langword="true"/>；字典意外為空時為 <see langword="false"/>。</returns>
        /// <remarks>
        /// 優先淘汰已逾時資料；若全部仍活躍，淘汰最久未活動的一筆。這只降低 DEBUG 的統計完整度，
        /// 不會影響真實 Session、使用者登入、Cookie 或授權決策。
        /// </remarks>
        private bool EvictOneSessionRecordLocked(DateTime now)
        {
            string? selectedSessionId = null;
            var selectedActivity = DateTime.MaxValue;

            foreach (var pair in _activeSessions)
            {
                var lastActivity = pair.Value.LastActivityAt;
                if (now - lastActivity > _sessionTimeout)
                {
                    selectedSessionId = pair.Key;
                    break;
                }

                if (lastActivity < selectedActivity)
                {
                    selectedActivity = lastActivity;
                    selectedSessionId = pair.Key;
                }
            }

            return selectedSessionId != null && _activeSessions.TryRemove(selectedSessionId, out _);
        }

        /// <summary>
        /// 取得不含 Session ID 的即時統計快照。
        /// </summary>
        /// <returns>僅包含數量、年齡與估算記憶體的彙總資料。</returns>
        /// <remarks>
        /// <see cref="ConcurrentDictionary{TKey,TValue}.Values"/> 會在短暫快照中列舉，列舉結果只用於
        /// 目前呼叫的數值計算，離開方法即不再保留任何 record 參考。
        /// </remarks>
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
        /// 移除超過診斷逾時時間的 Session 索引。
        /// </summary>
        /// <remarks>
        /// callback 與人工呼叫共用此方法。停止或釋放後立即略過，確保已排程的 timer callback 無法在
        /// shutdown 期間重新操作服務狀態；清理只接觸本服務的 dictionary，無 HTTP 或外部 I/O。
        /// </remarks>
        public void CleanupExpiredRecords()
        {
            if (Volatile.Read(ref _stopped) != 0 || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

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
        /// 將不含 Session ID 的彙總統計寫入日誌。
        /// </summary>
        /// <remarks>
        /// 此方法只由本服務擁有的 timer 呼叫；停止後不再寫入，避免 shutdown 後的 callback 保留服務或日誌資源。
        /// </remarks>
        private void LogStatistics()
        {
            if (Volatile.Read(ref _stopped) != 0 || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

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

        /// <summary>
        /// 在 Host 啟動後建立本服務擁有的定期清理與彙總 timer。
        /// </summary>
        /// <param name="cancellationToken">Host 啟動取消權杖；timer 不保存或註冊此短命權杖。</param>
        /// <returns>timer 建立完成後立即完成的工作。</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            lock (_lifecycleGate)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(SessionMonitorService));
                }

                if (_cleanupTimer == null && _reportTimer == null)
                {
                    _cleanupTimer = new Timer(_ => CleanupExpiredRecords(), null, _cleanupInterval, _cleanupInterval);
                    _reportTimer = new Timer(_ => LogStatistics(), null, _reportInterval, _reportInterval);
                }

                Volatile.Write(ref _stopped, 0);
            }

            _logger.LogInformation("[Session Monitor] Session 監控服務已啟動");
            _startTime = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止新增診斷資料、排空並釋放 timer，最後清除本服務擁有的 Session 索引。
        /// </summary>
        /// <param name="cancellationToken">Host 停止取消權杖；清理不啟動可取消的外部 I/O，故不保存此權杖。</param>
        /// <returns>所有 timer callback 排空、統計記錄與字典清除完成後的工作。</returns>
        /// <remarks>
        /// 先關閉寫入閘門，再 Dispose timer 並等待已開始的 callback 完成，最後清空字典；這個順序防止
        /// callback 與 shutdown 競態造成 Session ID 在 Host 停止後重新變成可達物件。
        /// </remarks>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[Session Monitor] Session 監控服務正在停止...");

            Volatile.Write(ref _stopped, 1);
            StopTimersAndWaitForCallbacks();

            var stats = GetStatistics();
            _logger.LogInformation(
                "[Session Monitor] 最終統計 - 總建立: {TotalCreated}, 峰值: {Peak}, 運行時間: {Uptime:F2} 分鐘",
                stats.TotalSessionsCreated,
                stats.PeakActiveSessions,
                stats.UptimeMinutes);

            _activeSessions.Clear();

            return Task.CompletedTask;
        }

        /// <summary>
        /// 停用、釋放並等待此服務建立的 timer callback 完成。
        /// </summary>
        /// <remarks>
        /// 先在生命週期鎖內交換欄位，確保 Stop 與 Dispose 競態時仍只有一方擁有 timer；再在鎖外等待，
        /// 不讓 callback 或日誌路徑被鎖住。等待完成後 timer 不再持有 delegate、服務或 Session 索引的參考。
        /// </remarks>
        private void StopTimersAndWaitForCallbacks()
        {
            Timer? cleanupTimer;
            Timer? reportTimer;

            lock (_lifecycleGate)
            {
                cleanupTimer = _cleanupTimer;
                reportTimer = _reportTimer;
                _cleanupTimer = null;
                _reportTimer = null;
            }

            DisposeTimerAndWait(cleanupTimer);
            DisposeTimerAndWait(reportTimer);
        }

        /// <summary>
        /// 釋放一個 timer，並等待開始中的 callback 完成後才讓其 owner 繼續清理。
        /// </summary>
        /// <param name="timer">由本服務建立且尚未被其他停止路徑取得的 timer。</param>
        private static void DisposeTimerAndWait(Timer? timer)
        {
            if (timer == null)
            {
                return;
            }

            timer.Change(Timeout.Infinite, Timeout.Infinite);
            using var callbackDrained = new ManualResetEvent(false);
            if (timer.Dispose(callbackDrained))
            {
                callbackDrained.WaitOne();
            }
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// 釋放本服務擁有的 timer 與程序級診斷索引。
        /// </summary>
        /// <remarks>
        /// 此方法可由 Host Dispose 與測試 using 重複呼叫；原子旗標與 timer 欄位交換使其具冪等性。
        /// 注入的 logger 不由本服務擁有，故不會 Dispose。
        /// </remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Volatile.Write(ref _stopped, 1);
            StopTimersAndWaitForCallbacks();
            _activeSessions.Clear();
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    /// <summary>
    /// 一筆只供 DEBUG 統計使用的 Session 活動記錄。
    /// </summary>
    /// <remarks>
    /// 此類只保存 opaque Session ID、建立時間、最後活動時間與數量；不保存任何 Session 值、身份、權杖、
    /// tenant 或使用者資料。可變數值一律以 Interlocked 操作，讓讀寫同步且不需要把 request 熱路徑序列化。
    /// </remarks>
    public class SessionRecord
    {
        private long _lastActivityUtcTicks;
        private long _requestCount;

        /// <summary>建立一筆由監控器唯一擁有的 Session 診斷記錄。</summary>
        /// <param name="sessionId">不透明 Session ID；僅存在於受限 dictionary 直到淘汰或服務停止。</param>
        /// <param name="createdAt">建立紀錄的 UTC 時間。</param>
        public SessionRecord(string sessionId, DateTime createdAt)
        {
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            CreatedAt = createdAt;
            _lastActivityUtcTicks = createdAt.Ticks;
            _requestCount = 1;
        }

        /// <summary>僅供程序內字典去重與淘汰的 opaque Session ID。</summary>
        public string SessionId { get; }

        /// <summary>此診斷項目第一次出現的 UTC 時間。</summary>
        public DateTime CreatedAt { get; }

        /// <summary>最後一次活動的 UTC 時間，由原子 tick 值讀取。</summary>
        public DateTime LastActivityAt => new(Interlocked.Read(ref _lastActivityUtcTicks), DateTimeKind.Utc);

        /// <summary>本項目累計的 request 數，由原子計數讀取。</summary>
        public long RequestCount => Interlocked.Read(ref _requestCount);

        /// <summary>以無鎖原子操作更新目前 Session 的活動時間與 request 計數。</summary>
        /// <param name="now">呼叫端在 request 路徑取得的 UTC 時間。</param>
        internal void RecordActivity(DateTime now)
        {
            Interlocked.Exchange(ref _lastActivityUtcTicks, now.Ticks);
            Interlocked.Increment(ref _requestCount);
        }
    }

    /// <summary>
    /// 不含個別 Session ID 或個資的 DEBUG 診斷統計快照。
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

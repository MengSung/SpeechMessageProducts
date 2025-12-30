#if DEBUG
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ChurchReport.Services.Performance;
using ChurchReport.Services.Monitoring;

namespace ChurchReport.Controllers
{
    /// <summary>
    /// 效能監控 API 控制器
    /// 提供效能報告和驗證端點
    /// ?? 僅在 DEBUG 編譯模式下啟用（Release 版本不會包含此控制器）
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class PerformanceController : ControllerBase
    {
        private readonly IPerformanceMonitor _performanceMonitor;
        private readonly ISessionMonitorService _sessionMonitor;
        private readonly ILogger<PerformanceController> _logger;

        public PerformanceController(
            IPerformanceMonitor performanceMonitor,
            ISessionMonitorService sessionMonitor,
            ILogger<PerformanceController> logger)
        {
            _performanceMonitor = performanceMonitor;
            _sessionMonitor = sessionMonitor;
            _logger = logger;
        }

        /// <summary>
        /// 取得效能報告
        /// GET /api/performance/report
        /// </summary>
        [HttpGet("report")]
        public IActionResult GetReport()
        {
            try
            {
                var report = _performanceMonitor.GetReport();
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得效能報告失敗");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 取得 Session 統計資訊
        /// GET /api/performance/sessions
        /// ? Phase 8: 新增 Session 監控端點
        /// </summary>
        [HttpGet("sessions")]
        public IActionResult GetSessionStatistics()
        {
            try
            {
                var stats = _sessionMonitor.GetStatistics();
                return Ok(new
                {
                    timestamp = stats.Timestamp,
                    activeSessions = stats.ActiveSessionCount,
                    idleSessions = stats.IdleSessionCount,
                    totalTracked = stats.TotalTrackedSessions,
                    totalCreated = stats.TotalSessionsCreated,
                    peakActive = stats.PeakActiveSessions,
                    avgRequestsPerSession = $"{stats.AverageRequestsPerSession:F2}",
                    estimatedMemoryKB = $"{stats.EstimatedMemoryUsageKB:F2}",
                    uptimeMinutes = $"{stats.UptimeMinutes:F2}",
                    sessionTimeoutMinutes = stats.SessionTimeoutMinutes,
                    oldestSessionAgeMinutes = $"{stats.OldestSessionAge:F2}",
                    newestSessionAgeMinutes = $"{stats.NewestSessionAge:F2}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得 Session 統計資訊失敗");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 驗證效能目標
        /// GET /api/performance/validate
        /// </summary>
        [HttpGet("validate")]
        public IActionResult ValidateTargets(
            [FromQuery] double? maxResponseTimeMs = null,
            [FromQuery] double? maxMemoryMB = null,
            [FromQuery] double? maxErrorRate = null)
        {
            try
            {
                var targets = new PerformanceTargets
                {
                    MaxAverageResponseTimeMs = maxResponseTimeMs ?? 500,
                    MaxWorkingSetMB = maxMemoryMB ?? 1024,
                    MaxErrorRatePercent = maxErrorRate ?? 1
                };

                var report = _performanceMonitor.GetReport();
                var result = report.ValidateTargets(targets);

                return Ok(new
                {
                    passed = result.Passed,
                    details = result.Details,
                    report = report
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "驗證效能目標失敗");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 重設效能指標
        /// POST /api/performance/reset
        /// </summary>
        [HttpPost("reset")]
        public IActionResult Reset()
        {
            try
            {
                _performanceMonitor.Reset();
                return Ok(new { message = "效能指標已重設" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重設效能指標失敗");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 取得系統狀態摘要
        /// GET /api/performance/summary
        /// ? Phase 8: 更新摘要包含 Session 資訊
        /// </summary>
        [HttpGet("summary")]
        public IActionResult GetSummary()
        {
            try
            {
                var report = _performanceMonitor.GetReport();
                var sessionStats = _sessionMonitor.GetStatistics();
                
                var summary = new
                {
                    status = "OK",
                    buildMode = "DEBUG",
                    uptime = TimeSpan.FromSeconds(report.UptimeSeconds).ToString(@"dd\.hh\:mm\:ss"),
                    totalRequests = report.TotalRequests,
                    failedRequests = report.FailedRequests,
                    errorRate = report.TotalRequests > 0 
                        ? $"{(double)report.FailedRequests / report.TotalRequests * 100:F2}%" 
                        : "0%",
                    memory = new
                    {
                        workingSetMB = $"{report.SystemInfo.WorkingSetMB:F2} MB",
                        gcTotalMemoryMB = $"{report.SystemInfo.GCTotalMemoryMB:F2} MB"
                    },
                    gc = new
                    {
                        gen0 = report.SystemInfo.GCGen0Count,
                        gen1 = report.SystemInfo.GCGen1Count,
                        gen2 = report.SystemInfo.GCGen2Count
                    },
                    threads = report.SystemInfo.ThreadCount,
                    averageResponseTime = report.Metrics.TryGetValue("RequestDuration", out var metric)
                        ? $"{metric.Average:F2} ms"
                        : "N/A",
                    // ? Phase 8: 新增 Session 統計
                    sessions = new
                    {
                        active = sessionStats.ActiveSessionCount,
                        idle = sessionStats.IdleSessionCount,
                        peak = sessionStats.PeakActiveSessions,
                        totalCreated = sessionStats.TotalSessionsCreated,
                        estimatedMemoryKB = $"{sessionStats.EstimatedMemoryUsageKB:F2}"
                    }
                };

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取得系統狀態摘要失敗");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
#endif

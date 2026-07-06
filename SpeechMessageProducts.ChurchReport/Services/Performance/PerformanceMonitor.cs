// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Performance/PerformanceMonitor.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：interface IPerformanceMonitor、class PerformanceMonitor、class OperationScope、class PerformanceReport、class MetricSummary、class SystemInfo、class PerformanceTargets、class ValidationResult
// 主要成員：BeginScope、RecordMetric、IncrementRequests、IncrementFailedRequests、GetReport、Reset、CalculateAverage、CalculateMedian、CalculatePercentile、Dispose
// 引用命名空間：System、System.Collections.Generic、System.Diagnostics、System.Threading、System.Threading.Tasks、Microsoft.Extensions.Logging
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ChurchReport.Services.Performance
{
#if DEBUG
    /// <summary>
    /// 效能監控服務
    /// 用於最終驗證階段，監控應用程式效能指標
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public interface IPerformanceMonitor
    {
        /// <summary>
        /// 開始計時
        /// </summary>
        IDisposable BeginScope(string operationName);

        /// <summary>
        /// 記錄效能指標
        /// </summary>
        void RecordMetric(string name, double value, string unit = "ms");

        /// <summary>
        /// 取得效能報告
        /// </summary>
        PerformanceReport GetReport();

        /// <summary>
        /// 重設所有指標
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// 效能監控服務實作
    /// ?? 僅在 DEBUG 編譯模式下啟用
    /// </summary>
    public class PerformanceMonitor : IPerformanceMonitor
    {
        private readonly ILogger<PerformanceMonitor> _logger;
        private readonly Dictionary<string, List<double>> _metrics = new();
        private readonly object _lock = new();
        private long _totalRequests = 0;
        private long _failedRequests = 0;
        private readonly Stopwatch _uptime = Stopwatch.StartNew();

        public PerformanceMonitor(ILogger<PerformanceMonitor> logger)
        {
            _logger = logger;
        }

        public IDisposable BeginScope(string operationName)
        {
            return new OperationScope(this, operationName);
        }

        public void RecordMetric(string name, double value, string unit = "ms")
        {
            lock (_lock)
            {
                if (!_metrics.ContainsKey(name))
                {
                    _metrics[name] = new List<double>();
                }
                _metrics[name].Add(value);

                // 保留最近 1000 筆記錄
                if (_metrics[name].Count > 1000)
                {
                    _metrics[name].RemoveAt(0);
                }
            }

            _logger.LogDebug("[效能] {Name}: {Value} {Unit}", name, value, unit);
        }

        public void IncrementRequests() => Interlocked.Increment(ref _totalRequests);
        public void IncrementFailedRequests() => Interlocked.Increment(ref _failedRequests);

        public PerformanceReport GetReport()
        {
            lock (_lock)
            {
                var report = new PerformanceReport
                {
                    GeneratedAt = DateTime.Now,
                    UptimeSeconds = _uptime.Elapsed.TotalSeconds,
                    TotalRequests = _totalRequests,
                    FailedRequests = _failedRequests,
                    Metrics = new Dictionary<string, MetricSummary>()
                };

                foreach (var kvp in _metrics)
                {
                    if (kvp.Value.Count > 0)
                    {
                        var sorted = new List<double>(kvp.Value);
                        sorted.Sort();

                        report.Metrics[kvp.Key] = new MetricSummary
                        {
                            Count = sorted.Count,
                            Min = sorted[0],
                            Max = sorted[sorted.Count - 1],
                            Average = CalculateAverage(sorted),
                            Median = CalculateMedian(sorted),
                            P95 = CalculatePercentile(sorted, 95),
                            P99 = CalculatePercentile(sorted, 99)
                        };
                    }
                }

                // 加入系統資訊
                var process = Process.GetCurrentProcess();
                report.SystemInfo = new SystemInfo
                {
                    WorkingSetMB = process.WorkingSet64 / 1024.0 / 1024.0,
                    PrivateMemoryMB = process.PrivateMemorySize64 / 1024.0 / 1024.0,
                    ThreadCount = process.Threads.Count,
                    GCGen0Count = GC.CollectionCount(0),
                    GCGen1Count = GC.CollectionCount(1),
                    GCGen2Count = GC.CollectionCount(2),
                    GCTotalMemoryMB = GC.GetTotalMemory(false) / 1024.0 / 1024.0
                };

                return report;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _metrics.Clear();
                Interlocked.Exchange(ref _totalRequests, 0);
                Interlocked.Exchange(ref _failedRequests, 0);
            }
        }

        private static double CalculateAverage(List<double> sorted)
        {
            double sum = 0;
            foreach (var v in sorted) sum += v;
            return sum / sorted.Count;
        }

        private static double CalculateMedian(List<double> sorted)
        {
            int mid = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[mid - 1] + sorted[mid]) / 2
                : sorted[mid];
        }

        private static double CalculatePercentile(List<double> sorted, int percentile)
        {
            int index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
        }

        /// <summary>
        /// 操作範圍，用於自動計時
        /// </summary>
        private class OperationScope : IDisposable
        {
            private readonly PerformanceMonitor _monitor;
            private readonly string _operationName;
            private readonly Stopwatch _stopwatch;

            public OperationScope(PerformanceMonitor monitor, string operationName)
            {
                _monitor = monitor;
                _operationName = operationName;
                _stopwatch = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _stopwatch.Stop();
                _monitor.RecordMetric(_operationName, _stopwatch.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>
    /// 效能報告
    /// </summary>
    public class PerformanceReport
    {
        public DateTime GeneratedAt { get; set; }
        public double UptimeSeconds { get; set; }
        public long TotalRequests { get; set; }
        public long FailedRequests { get; set; }
        public Dictionary<string, MetricSummary> Metrics { get; set; }
        public SystemInfo SystemInfo { get; set; }

        /// <summary>
        /// 驗證效能目標
        /// </summary>
        public ValidationResult ValidateTargets(PerformanceTargets targets)
        {
            var result = new ValidationResult { Passed = true, Details = new List<string>() };

            // 驗證平均回應時間
            if (Metrics.TryGetValue("RequestDuration", out var requestMetric))
            {
                if (requestMetric.Average > targets.MaxAverageResponseTimeMs)
                {
                    result.Passed = false;
                    result.Details.Add($"? 平均回應時間 {requestMetric.Average:F2}ms 超過目標 {targets.MaxAverageResponseTimeMs}ms");
                }
                else
                {
                    result.Details.Add($"? 平均回應時間 {requestMetric.Average:F2}ms (目標 < {targets.MaxAverageResponseTimeMs}ms)");
                }
            }

            // 驗證記憶體使用
            if (SystemInfo.WorkingSetMB > targets.MaxWorkingSetMB)
            {
                result.Passed = false;
                result.Details.Add($"? 記憶體使用 {SystemInfo.WorkingSetMB:F2}MB 超過目標 {targets.MaxWorkingSetMB}MB");
            }
            else
            {
                result.Details.Add($"? 記憶體使用 {SystemInfo.WorkingSetMB:F2}MB (目標 < {targets.MaxWorkingSetMB}MB)");
            }

            // 驗證錯誤率
            var errorRate = TotalRequests > 0 ? (double)FailedRequests / TotalRequests * 100 : 0;
            if (errorRate > targets.MaxErrorRatePercent)
            {
                result.Passed = false;
                result.Details.Add($"? 錯誤率 {errorRate:F2}% 超過目標 {targets.MaxErrorRatePercent}%");
            }
            else
            {
                result.Details.Add($"? 錯誤率 {errorRate:F2}% (目標 < {targets.MaxErrorRatePercent}%)");
            }

            return result;
        }
    }

    /// <summary>
    /// 指標摘要
    /// </summary>
    public class MetricSummary
    {
        public int Count { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Average { get; set; }
        public double Median { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
    }

    /// <summary>
    /// 系統資訊
    /// </summary>
    public class SystemInfo
    {
        public double WorkingSetMB { get; set; }
        public double PrivateMemoryMB { get; set; }
        public int ThreadCount { get; set; }
        public int GCGen0Count { get; set; }
        public int GCGen1Count { get; set; }
        public int GCGen2Count { get; set; }
        public double GCTotalMemoryMB { get; set; }
    }

    /// <summary>
    /// 效能目標
    /// </summary>
    public class PerformanceTargets
    {
        /// <summary>
        /// 最大平均回應時間 (毫秒)
        /// </summary>
        public double MaxAverageResponseTimeMs { get; set; } = 500;

        /// <summary>
        /// 最大記憶體使用 (MB)
        /// </summary>
        public double MaxWorkingSetMB { get; set; } = 1024;

        /// <summary>
        /// 最大錯誤率 (%)
        /// </summary>
        public double MaxErrorRatePercent { get; set; } = 1;

        /// <summary>
        /// 最小每秒請求數
        /// </summary>
        public double MinRequestsPerSecond { get; set; } = 10;
    }

    /// <summary>
    /// 驗證結果
    /// </summary>
    public class ValidationResult
    {
        public bool Passed { get; set; }
        public List<string> Details { get; set; }
    }
#endif
}

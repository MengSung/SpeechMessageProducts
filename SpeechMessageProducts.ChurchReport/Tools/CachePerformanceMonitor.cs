/*
 * 檔案整體說明
 *
 * 目的與定位：? Phase 3.2: 快取效能監控工具 用於測量快取帶來的效能改善
 *
 * 主要功能與流程：
 * 本檔宣告 CachePerformanceMonitor。
 * 主要操作入口包含 StartFirstCall、EndFirstCall、StartSecondCall、EndSecondCall、GetPerformanceReport、GetPerformanceLevel、
 * GetSimpleReport、MeasureOperation。
 *
 * 使用邊界與維護：
 * 本檔的前置條件、錯誤及資源責任以各型別／成員契約為準；此導讀不把呼叫參數視為授權，也不擴張被借入依賴的所有權。
 * 變更介面、資料格式、取消或清理順序時，須一併核對下列合作檔案及對應測試，不能只改目前檔案。
 * 可沿 SpeechMessageProducts.ChurchReport/Controllers/HomeController.cs 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Diagnostics;

namespace ChurchReport.Tools
{
    /// <summary>
    /// ? Phase 3.2: 快取效能監控工具
    /// 用於測量快取帶來的效能改善
    /// </summary>
    public class CachePerformanceMonitor
    {
        private readonly Stopwatch _stopwatch;
        private long _firstCallTime;
        private long _secondCallTime;
        private string _operationName;

        public CachePerformanceMonitor()
        {
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 開始監控（第一次呼叫 - Cache Miss）
        /// </summary>
        public void StartFirstCall(string operationName)
        {
            _operationName = operationName;
            _stopwatch.Restart();
        }

        /// <summary>
        /// 結束第一次呼叫
        /// </summary>
        public void EndFirstCall()
        {
            _stopwatch.Stop();
            _firstCallTime = _stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// 開始第二次呼叫（Cache Hit）
        /// </summary>
        public void StartSecondCall()
        {
            _stopwatch.Restart();
        }

        /// <summary>
        /// 結束第二次呼叫
        /// </summary>
        public void EndSecondCall()
        {
            _stopwatch.Stop();
            _secondCallTime = _stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// 取得效能報告
        /// </summary>
        public string GetPerformanceReport()
        {
            if (_firstCallTime == 0 || _secondCallTime == 0)
            {
                return "尚未完成測試";
            }

            double improvement = _firstCallTime > 0
                ? (double)_firstCallTime / _secondCallTime
                : 0;

            return $@"
╔════════════════════════════════════════════════════════════╗
║         ?? 快取效能測試報告 - {_operationName,-30} ║
╠════════════════════════════════════════════════════════════╣
║ 第一次呼叫 (Cache Miss): {_firstCallTime,10} ms                    ║
║ 第二次呼叫 (Cache Hit):  {_secondCallTime,10} ms                    ║
║ 速度提升:                {improvement,10:F1}x 倍                 ║
║ 時間節省:                {_firstCallTime - _secondCallTime,10} ms                    ║
╠════════════════════════════════════════════════════════════╣
║ ?? 評估:                                                   ║
║   {GetPerformanceLevel(improvement),-56} ║
╚════════════════════════════════════════════════════════════╝
";
        }

        /// <summary>
        /// 取得效能等級評估
        /// </summary>
        private string GetPerformanceLevel(double improvement)
        {
            if (improvement >= 100)
                return "????? 極致優化！效能提升驚人！";
            else if (improvement >= 50)
                return "???? 卓越優化！效能大幅改善！";
            else if (improvement >= 20)
                return "??? 良好優化！效能明顯提升！";
            else if (improvement >= 5)
                return "?? 中等優化，效能有所改善";
            else if (improvement >= 2)
                return "? 輕微優化，有改善空間";
            else
                return "?? 優化效果不明顯，建議檢查快取策略";
        }

        /// <summary>
        /// 簡化版報告（單行）
        /// </summary>
        public string GetSimpleReport()
        {
            if (_firstCallTime == 0 || _secondCallTime == 0)
            {
                return "尚未完成測試";
            }

            double improvement = _firstCallTime > 0
                ? (double)_firstCallTime / _secondCallTime
                : 0;

            return $"[{_operationName}] Cache Miss: {_firstCallTime}ms | Cache Hit: {_secondCallTime}ms | 提升: {improvement:F1}x 倍";
        }

        /// <summary>
        /// 測量單次操作執行時間
        /// </summary>
        public static long MeasureOperation(Action operation, out string operationTime)
        {
            var stopwatch = Stopwatch.StartNew();
            operation();
            stopwatch.Stop();
            operationTime = $"{stopwatch.ElapsedMilliseconds} ms";
            return stopwatch.ElapsedMilliseconds;
        }

        /// <summary>
        /// 測量單次操作執行時間（非同步）
        /// </summary>
        public static async System.Threading.Tasks.Task<(long elapsed, string display)> MeasureOperationAsync(Func<System.Threading.Tasks.Task> operation)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();
            return (stopwatch.ElapsedMilliseconds, $"{stopwatch.ElapsedMilliseconds} ms");
        }
    }
}

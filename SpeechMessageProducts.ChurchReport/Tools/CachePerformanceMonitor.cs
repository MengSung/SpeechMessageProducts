// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Tools/CachePerformanceMonitor.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class CachePerformanceMonitor
// 主要成員：StartFirstCall、EndFirstCall、StartSecondCall、EndSecondCall、GetPerformanceReport、GetPerformanceLevel、GetSimpleReport、MeasureOperation
// 引用命名空間：System、System.Diagnostics
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Dataverse.Tests/FileToolUtilityTracerTests.cs
// 所屬區塊：Dataverse 連線與診斷資源的生命週期測試。
// 檔案責任：驗證追蹤資源為程序級單一擁有者，且不會隨使用次數累積於全域集合。
// 主要型別：class FileToolUtilityTracerTests
// 主要成員：Write_ManyTimes_DoesNotGrowTraceListeners、Dispose_RemovesListenerFromGlobalCollection、
//           Write_BelowQualifiedLevel_DoesNotOpenTraceFile
// 引用命名空間：System、System.Diagnostics、System.IO、Xunit、ToolUtilityNameSpace.Diagnostics
// 閱讀路徑：先看每個測試保護的契約說明，再看斷言為何具決定性。
// 維護重點：這些測試守住「追蹤資源不可隨請求累積」的規則，是 ToolUtilityClass
//           能否安全改為 request 範圍的前提，不可因為不方便而放寬。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig。
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests
{
    /// <summary>
    /// <see cref="FileToolUtilityTracer"/> 的資源生命週期測試。
    /// </summary>
    /// <remarks>
    /// 保護的契約：追蹤資源是「程序級」的，其 <see cref="TraceListener"/> 只能加入
    /// <see cref="Trace.Listeners"/>（行程內靜態集合）一次。這條規則若被破壞，
    /// 後果是 listener 無界成長（記憶體洩漏）與每行日誌重複輸出 N 份，
    /// 且會讓 ToolUtilityClass 無法安全地改為 request 範圍。
    ///
    /// 所有測試都寫入暫存檔而非正式追蹤路徑，避免污染維運用的日誌。
    /// </remarks>
    public class FileToolUtilityTracerTests
    {
        private static string NewTempTracePath()
            => Path.Combine(Path.GetTempPath(), $"toolutility-trace-{Guid.NewGuid():N}.log");

        /// <summary>
        /// 保護的契約：單一 tracer 反覆輸出時，全域 listener 集合不得成長。
        /// </summary>
        /// <remarks>
        /// 故障注入方式：連續呼叫 <c>Write</c> 100 次，模擬 100 個請求各輸出一次追蹤。
        /// 決定性斷言：<see cref="Trace.Listeners"/> 的數量在 100 次輸出後，
        /// 相對於「第一次輸出後」的基準完全不變。
        /// 若未來有人把 listener 的加入動作移進 <c>Write</c>，此測試會立即失敗。
        /// </remarks>
        [Fact]
        public void Write_ManyTimes_DoesNotGrowTraceListeners()
        {
            var path = NewTempTracePath();
            using var tracer = new FileToolUtilityTracer(path);

            // 第一次輸出才會延遲建立串流並掛上 listener，故以此後的數量為基準。
            tracer.Write(5, 1, "第一次輸出", new StackFrame(0, true));
            var baseline = Trace.Listeners.Count;

            for (var i = 0; i < 100; i++)
            {
                tracer.Write(5, 1, $"第 {i} 次輸出", new StackFrame(0, true));
            }

            Assert.Equal(baseline, Trace.Listeners.Count);
        }

        /// <summary>
        /// 保護的契約：釋放時必須把 listener 自全域集合移除，否則後續寫入會落到已釋放的 writer。
        /// </summary>
        /// <remarks>
        /// 故障注入方式：建立 tracer、輸出一次使其掛上 listener，然後 Dispose。
        /// 決定性斷言：Dispose 後的 listener 數量回到「建立 tracer 之前」的基準。
        /// 這同時證明釋放路徑是確定性的，不依賴 GC。
        /// </remarks>
        [Fact]
        public void Dispose_RemovesListenerFromGlobalCollection()
        {
            var path = NewTempTracePath();
            var before = Trace.Listeners.Count;

            var tracer = new FileToolUtilityTracer(path);
            tracer.Write(5, 1, "掛上 listener", new StackFrame(0, true));
            Assert.True(Trace.Listeners.Count > before, "輸出後應已掛上 listener");

            tracer.Dispose();

            Assert.Equal(before, Trace.Listeners.Count);
        }

        /// <summary>
        /// 保護的契約：層級未達門檻時不得產生任何副作用，包含不得開啟追蹤檔。
        /// </summary>
        /// <remarks>
        /// 故障注入方式：以 <c>totalLevel &lt; qualifiedLevel</c> 呼叫。
        /// 決定性斷言：目標檔案不存在 —— 證明串流仍維持延遲建立，
        /// 與重構前「第一次實際輸出才開檔」的行為一致。
        /// </remarks>
        [Fact]
        public void Write_BelowQualifiedLevel_DoesNotOpenTraceFile()
        {
            var path = NewTempTracePath();
            using var tracer = new FileToolUtilityTracer(path);

            tracer.Write(1, 5, "不應輸出", new StackFrame(0, true));

            Assert.False(File.Exists(path), "層級未達門檻時不應開啟追蹤檔");
        }
    }
}

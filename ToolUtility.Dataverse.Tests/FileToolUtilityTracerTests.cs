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
using System.Text;
using System.Text.RegularExpressions;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Dataverse.Tests
{
    /// <summary>
    /// <see cref="FileToolUtilityTracer"/> 的資源生命週期測試。
    /// </summary>
    /// <remarks>
    /// 保護的契約：legacy tracer 是程序級單一檔案擁有者，但不得把自己的 listener
    /// 加入 <see cref="Trace.Listeners"/>。全域集合只由 ChurchReport Program 擁有
    /// Trace.log listener；若 legacy tracer 也加入，所有 Trace.WriteLine 都會污染
    /// CHURCH_REPORT_TRACE.TXT，並可能造成重複輸出、記憶體 retention 與關閉競態。
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
        /// 相對於 tracer 建立前的基準完全不變。
        /// 若未來有人把 listener 的加入動作移進 <c>Write</c>，此測試會立即失敗。
        /// </remarks>
        [Fact]
        public void Write_ManyTimes_DoesNotGrowTraceListeners()
        {
            var path = NewTempTracePath();
            using var tracer = new FileToolUtilityTracer(path);

            // 第一次輸出才會延遲建立私有串流；legacy tracer 不得掛上全域 listener。
            tracer.Write(5, 1, "第一次輸出", new StackFrame(0, true));
            var baseline = Trace.Listeners.Count;

            for (var i = 0; i < 100; i++)
            {
                tracer.Write(5, 1, $"第 {i} 次輸出", new StackFrame(0, true));
            }

            Assert.Equal(baseline, Trace.Listeners.Count);
        }

        /// <summary>
        /// 保護的契約：釋放時必須確定性關閉私有 writer，且不應改動全域 listener 集合。
        /// </summary>
        /// <remarks>
        /// 故障注入方式：建立 tracer、輸出一次使其掛上 listener，然後 Dispose。
        /// 決定性斷言：Dispose 前後的 listener 數量維持不變，且檔案 handle 可重新開啟。
        /// 這同時證明釋放路徑是確定性的，不依賴 GC。
        /// </remarks>
        [Fact]
        public void Dispose_RemovesListenerFromGlobalCollection()
        {
            var path = NewTempTracePath();
            var before = Trace.Listeners.Count;

            var tracer = new FileToolUtilityTracer(path);
            tracer.Write(5, 1, "寫入私有 writer", new StackFrame(0, true));
            Assert.Equal(before, Trace.Listeners.Count);

            tracer.Dispose();

            Assert.Equal(before, Trace.Listeners.Count);

            using var reopened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Assert.True(reopened.Length > 0, "Dispose 後檔案應可重新開啟且保留輸出");
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

        /// <summary>
        /// 保護 legacy 時間欄位的跨文化穩定格式。故障注入是以目前程序文化寫入一筆
        /// legacy 訊息；決定性斷言是以 Big5 讀回後符合 ISO-like 的固定毫秒格式，避免
        /// 分析器因伺服器地區設定不同而無法建立時間範圍。
        /// </summary>
        [Fact]
        public void Write_uses_culture_invariant_legacy_timestamp_format()
        {
            var path = NewTempTracePath();
            using (var tracer = new FileToolUtilityTracer(path))
            {
                tracer.Write(5, 1, "固定時間格式測試", new StackFrame(0, true));
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var big5 = Encoding.GetEncoding(950);
            var text = File.ReadAllText(path, big5);
            Assert.Matches(
                new Regex(@"Time\s+=\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}", RegexOptions.CultureInvariant),
                text);
        }
    }
}

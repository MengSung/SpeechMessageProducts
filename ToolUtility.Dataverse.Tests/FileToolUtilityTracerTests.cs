/*
 * 檔案整體說明
 *
 * 目的與定位：FileToolUtilityTracer 的資源生命週期測試。
 *
 * 主要功能與流程：
 * 本檔宣告 FileToolUtilityTracerTests。
 * - 為當次案例配置唯一暫存路徑但不建立檔案，讓 Release 的無檔案副作用可以被檢驗。 路徑只由此 fixture 擁有，最長存活到案例結束；Dispose 會在串流釋放後刪除檔案。
 * - 在每個案例成功或失敗後清除本 fixture 的唯一暫存檔，避免重跑測試累積檔案。 所有 tracer 與讀取串流都由測試方法的 using 先行釋放；清理失敗必須讓測試失敗，不能隱藏資源殘留。 短暫鎖檔最多嘗試五次、
 * 間隔一百毫秒，等待總量最多四百毫秒，不建立背景工作或計時器。
 * - 保護每個 fixture 只能配置一次暫存檔；注入第二次配置，斷言立即拒絕且第一個檔案仍由原 fixture 清理。 即使拒絕斷言失敗，finally 也只清除本案例建立的檔案，避免紅燈本身留下孤兒檔案。
 * - 保護防毒軟體短暫鎖檔時仍能有界清理；注入前兩次刪除失敗，第三次才刪除實際檔案。 斷言嘗試三次、等待兩次且檔案消失；等待用區域計數代替真實睡眠，測試不依賴排程或時間。
 * 測試案例入口包含 NewTempTracePath、Dispose、NewTempTracePath_SecondCall_ThrowsAndKeepsOriginalOwner、
 * DeleteTraceFile_TransientLock_RetriesThenDrains、DeleteTraceFile_PersistentRetention_FailsAfterFiveAttempts、
 * DeleteTraceFileWithRetry、Write_ManyTimes_DoesNotGrowTraceListeners、Dispose_RemovesListenerFromGlobalCollection；
 * 其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
    /// 每例只使用自己擁有的隨機暫存路徑；Debug 可寫入並於結束清理，Release 必須維持不建檔。
    /// </remarks>
    public class FileToolUtilityTracerTests : IDisposable
    {
        // xUnit 每個案例建立一個 fixture；每例最多擁有一個隨機暫存檔，不共用使用者或測試狀態。
        private string? _tracePath;

        /// <summary>
        /// 為當次案例配置唯一暫存路徑但不建立檔案，讓 Release 的無檔案副作用可以被檢驗。
        /// 路徑只由此 fixture 擁有，最長存活到案例結束；Dispose 會在串流釋放後刪除檔案。
        /// </summary>
        /// <returns>只供本次測試使用的追蹤檔路徑。</returns>
        private string NewTempTracePath()
        {
            if (_tracePath != null)
            {
                throw new InvalidOperationException("Each fixture can own only one temporary trace path.");
            }

            return _tracePath = Path.Combine(Path.GetTempPath(), $"toolutility-trace-{Guid.NewGuid():N}.log");
        }

        /// <summary>
        /// 在每個案例成功或失敗後清除本 fixture 的唯一暫存檔，避免重跑測試累積檔案。
        /// 所有 tracer 與讀取串流都由測試方法的 using 先行釋放；清理失敗必須讓測試失敗，不能隱藏資源殘留。
        /// 短暫鎖檔最多嘗試五次、間隔一百毫秒，等待總量最多四百毫秒，不建立背景工作或計時器。
        /// </summary>
        public void Dispose()
        {
            if (_tracePath != null && File.Exists(_tracePath))
            {
                DeleteTraceFileWithRetry(_tracePath, File.Delete, File.Exists, () => Thread.Sleep(100));
            }
        }

        /// <summary>
        /// 保護每個 fixture 只能配置一次暫存檔；注入第二次配置，斷言立即拒絕且第一個檔案仍由原 fixture 清理。
        /// 即使拒絕斷言失敗，finally 也只清除本案例建立的檔案，避免紅燈本身留下孤兒檔案。
        /// </summary>
        [Fact]
        public void NewTempTracePath_SecondCall_ThrowsAndKeepsOriginalOwner()
        {
            var path = NewTempTracePath();
            File.WriteAllText(path, "fixture");
            try
            {
                Assert.Throws<InvalidOperationException>(() => NewTempTracePath());
                Dispose();
                Assert.False(File.Exists(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// 保護防毒軟體短暫鎖檔時仍能有界清理；注入前兩次刪除失敗，第三次才刪除實際檔案。
        /// 斷言嘗試三次、等待兩次且檔案消失；等待用區域計數代替真實睡眠，測試不依賴排程或時間。
        /// </summary>
        [Fact]
        public void DeleteTraceFile_TransientLock_RetriesThenDrains()
        {
            var path = NewTempTracePath();
            File.WriteAllText(path, "fixture");
            var attempts = 0;
            var waits = 0;
            DeleteTraceFileWithRetry(path, candidate =>
            {
                attempts++;
                if (attempts < 3) { throw new IOException("Injected file lock."); }
                File.Delete(candidate);
            }, File.Exists, () => waits++);
            Assert.Equal(3, attempts);
            Assert.Equal(2, waits);
            Assert.False(File.Exists(path));
        }

        /// <summary>
        /// 保護無法清除檔案時必須在有限次嘗試後明確失敗；注入每次刪除後檔案仍存在的情境。
        /// 決定性斷言是五次刪除、四次等待及測試斷言例外，不能無限等待或假稱已清理。
        /// </summary>
        [Fact]
        public void DeleteTraceFile_PersistentRetention_FailsAfterFiveAttempts()
        {
            var attempts = 0;
            var waits = 0;
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                DeleteTraceFileWithRetry("owned-fixture", _ => attempts++, _ => true, () => waits++));
            Assert.Equal(5, attempts);
            Assert.Equal(4, waits);
        }

        /// <summary>
        /// 將暫時檔案系統故障限制在最多五次清理；委派僅在本次呼叫使用，絕不保存到共用狀態。
        /// </summary>
        /// <param name="path">目前 fixture 唯一擁有的暫存檔。</param>
        /// <param name="delete">刪除檔案的同步操作。</param>
        /// <param name="exists">檢查殘留檔案的同步操作。</param>
        /// <param name="wait">生產清理路徑每次等待一百毫秒；故障測試使用不睡眠的區域計數。</param>
        private static void DeleteTraceFileWithRetry(string path, Action<string> delete, Func<string, bool> exists, Action wait)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    delete(path);
                    if (!exists(path)) { return; }
                }
                catch (IOException) when (attempt < 4)
                {
                    // 只容忍短暫檔案鎖；最後一次的真實刪除失敗仍須直接回報。
                }

                if (attempt < 4) { wait(); }
            }

            Assert.False(exists(path), "案例結束後暫存檔必須在五次有界重試內回到不存在基線");
        }

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
            var baseline = Trace.Listeners.Count;
            using var tracer = new FileToolUtilityTracer(path);

            // 第一次輸出才會延遲建立私有串流；legacy tracer 不得掛上全域 listener。
            tracer.Write(5, 1, "第一次輸出", new StackFrame(0, true));

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
        /// 故障注入方式：建立 tracer、嘗試輸出一次後 Dispose；tracer 始終不得加入全域 listener。
        /// 決定性斷言：listener 數量維持不變；Debug 產生的檔案能以獨佔模式重開，Release 則完全不建檔。
        /// Debug 驗證實際 handle 確定釋放，Release 驗證停用時沒有檔案副作用；兩種設定都不依賴 GC。
        /// </remarks>
        [Fact]
        public void Dispose_RemovesListenerFromGlobalCollection()
        {
            var path = NewTempTracePath();
            var before = Trace.Listeners.Count;

            using (var tracer = new FileToolUtilityTracer(path))
            {
                tracer.Write(5, 1, "寫入私有 writer", new StackFrame(0, true));
                Assert.Equal(before, Trace.Listeners.Count);
            }

            Assert.Equal(before, Trace.Listeners.Count);

#if DEBUG
            using var reopened = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.True(reopened.Length > 0, "Dispose 後檔案應可重新開啟且保留輸出");
#else
            Assert.False(File.Exists(path), "Release 的 legacy 追蹤停用契約不得因測試要求而改為寫檔");
#endif
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
        /// 保護 legacy 時間欄位格式及 Release 停用契約。注入一筆虛構訊息；Debug 以 Big5
        /// 讀回並斷言固定毫秒格式，Release 則斷言沒有建立檔案。只驗證格式而不比較系統時間值，
        /// 避免測試依賴執行日期；所有 writer、stream 與暫存檔在本案例結束時確定釋放。
        /// </summary>
        [Fact]
        public void Write_uses_culture_invariant_legacy_timestamp_format()
        {
            var path = NewTempTracePath();
            using (var tracer = new FileToolUtilityTracer(path))
            {
                tracer.Write(5, 1, "固定時間格式測試", new StackFrame(0, true));
            }

#if DEBUG
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var big5 = Encoding.GetEncoding(950);
            var text = File.ReadAllText(path, big5);
            Assert.Matches(
                new Regex(@"Time\s+=\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}", RegexOptions.CultureInvariant),
                text);
#else
            Assert.False(File.Exists(path), "Release 不應建立追蹤檔或輸出任何 legacy 時間欄位");
#endif
        }
    }
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs
// 所屬區塊：ChurchReport Session 隔離與診斷輸出契約的回歸測試。
// 檔案責任：驗證逐步 Session 診斷在預設停用時不進入 Debug 輸出管線，避免 request
//           路徑因同步磁碟 I/O 污染效能量測，或把 session / 用戶端資料留存到 trace。
// 測試生命週期：本測試只讀取 repo 內受版控的 C# 來源，不建立 listener、stream、timer、
//           background task 或靜態 request 狀態，因此沒有跨測試資源需要回收，也不會污染原始 trace。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與最終 CRLF。
// ============================================================================
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ToolUtility.Dataverse.Tests
{
    /// <summary>
    /// 驗證 Session 診斷開關預設關閉與全部輸出呼叫皆受同一入口保護的來源契約。
    /// </summary>
    /// <remarks>
    /// 故障注入方式是直接檢查產品原始碼：若未來有人在四個 Session 方法直接加入
    /// <c>Debug.WriteLine</c>、遺漏其中一個既有呼叫，或把 switch 預設改為 true，測試會失敗。
    /// 決定性斷言是產品檔案只有 helper 內的一個 <c>Debug.WriteLine</c>，而 51 個既有輸出
    /// 全部呼叫 helper；同時 helper 必須檢查開關。這比 .NET 10 無法攔截的 Debug listener
    /// 更能防止「測試看似通過、實際仍同步寫檔」的虛假成功。
    /// </remarks>
    public sealed class SessionDiagnosticsSwitchTests
    {
        /// <summary>
        /// 保護預設關閉時四個既有 Session 診斷區段不會進入未受控 Debug 輸出的契約。
        /// </summary>
        /// <remarks>
        /// 測試從編譯輸出目錄向上尋找 repo root，再讀取受版控來源，而非信任當前工作目錄或
        /// 建置產物。它刻意不寫入任何 trace，避免用測試本身污染 B1 的量測基準。判斷失敗時
        /// 會精確指出是 switch 缺檔、預設值錯誤、編譯防線消失、direct Debug call 增加，或
        /// 51 個既有呼叫未全部走受保護出口，讓維護者能分辨隔離／隱私／效能契約的破壞原因。
        /// </remarks>
        [Fact]
        public void Session_diagnostics_are_default_disabled_and_all_call_sites_are_guarded()
        {
            var repositoryRoot = FindRepositoryRoot();
            var switchPath = Path.Combine(
                repositoryRoot, "SpeechMessageProducts.ChurchReport", "Diagnostics", "SessionDiagnosticsSwitch.cs");
            Assert.True(
                File.Exists(switchPath),
                "SessionDiagnosticsSwitch 必須存在，才能以預設關閉的程序級開關保護 Session 診斷輸出。");

            var switchSource = File.ReadAllText(switchPath);
            var contextSource = File.ReadAllText(Path.Combine(
                repositoryRoot, "SpeechMessageProducts.ChurchReport", "Models", "InMemoryDataContextSmallGroup.cs"));

            Assert.Contains("#if DEBUG", switchSource, StringComparison.Ordinal);
            Assert.Contains("public static volatile bool Enabled = false;", switchSource, StringComparison.Ordinal);
            Assert.Contains("[System.Diagnostics.Conditional(\"DEBUG\")]", contextSource, StringComparison.Ordinal);
            Assert.Equal(1, CountMatches(contextSource, @"System\.Diagnostics\.Debug\.WriteLine\("));
            Assert.Equal(52, CountMatches(contextSource, @"WriteSessionDiagnostic\("));
            Assert.Equal(1, CountMatches(contextSource, @"if \(SessionDiagnosticsSwitch\.Enabled\)"));
            Assert.Equal(21, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[GetCurrentSessionId\\]"));
            Assert.Equal(18, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[GenerateCurrentRequestFingerprint\\]"));
            Assert.Equal(11, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[SetSessionDirtyFlag\\]"));
            Assert.Equal(1, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[InMemoryDataContext\\]"));
        }

        /// <summary>
        /// 從測試輸出目錄向上找到 repo root，避免依賴測試 runner 的目前工作目錄。
        /// </summary>
        /// <returns>同時包含 ChurchReport 與本測試專案的 repository root。</returns>
        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory != null;
                 directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.ChurchReport"))
                    && Directory.Exists(Path.Combine(directory.FullName, "ToolUtility.Dataverse.Tests")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("找不到包含 ChurchReport 原始碼的 repository root。");
        }

        /// <summary>
        /// 以文化無關的正規表示式計算指定來源契約的出現次數。
        /// </summary>
        /// <param name="source">只由 repo 受版控檔案提供的 C# 原始碼。</param>
        /// <param name="pattern">固定的程式碼結構模式，不接受 request 或使用者輸入。</param>
        /// <returns>符合模式的精確次數。</returns>
        private static int CountMatches(string source, string pattern)
            => Regex.Matches(source, pattern, RegexOptions.CultureInvariant).Count;
    }
}

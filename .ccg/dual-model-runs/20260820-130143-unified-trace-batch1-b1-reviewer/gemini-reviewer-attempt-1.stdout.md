# UI/Code Review 報告：unified-trace-batch1-b1

本報告針對目前工作樹中尚未提交的 B1 變更進行審查，重點關注程式碼品質、效能、隔離邊界、資源生命週期以及編譯正確性。

---

## 驗證報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 後端診斷優化，避免高頻 Debug 輸出與同步磁碟 I/O 阻塞主執行緒，間接提升 /Home/ProcessLogin 的響應速度。
Visual Consistency: 20/20 - 此變更不涉及前端 UI 變更，維持既有設計系統一致性。
Accessibility: 20/20 - 此變更不涉及前端 UI 變更，無 a11y 影響。
Performance: 12/20 - 雖然將 AutoFlush 設為 false 並改為批次 Flush 顯著減少了同步 I/O 開銷，但 WriteSessionDiagnostic 在 Release 模式下仍會執行字串插值，造成不必要的記憶體配置與 CPU 損耗。
Browser Compatibility: 20/20 - 後端診斷邏輯，無瀏覽器相容性問題。

TOTAL SCORE: 92/100 (若排除編譯阻礙) -> 實際得分: 60/100 (因 Critical 編譯錯誤阻礙)

ISSUES FOUND:
- [Critical] SessionDiagnosticsSwitchTests.cs 檔案編碼損毀且雙引號被吞掉，導致編譯失敗 (CS1010)。
- [Critical] SessionDiagnosticsSwitch.cs 檔案編碼損毀，繁體中文註解完全變成亂碼。
- [Warning] WriteSessionDiagnostic 呼叫在 Release 模式下仍會執行字串插值，產生效能開銷。

RECOMMENDATION: NEEDS_IMPROVEMENT
```

---

## 審查發現與問題分類

### Critical (嚴重問題)

#### 1. 檔案編碼損毀與語法錯誤導致編譯失敗 (Build Break)
- **檔案路徑**: 
  - `SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs`
  - `ToolUtility.Dataverse.Tests/SessionDiagnosticsSwitchTests.cs`
- **技術理由**: 
  這兩個新建立的檔案在寫入時，繁體中文註解與字串常數被錯誤地以 CP950 (Big5) 解碼後再以 UTF-8 格式寫入，導致檔案內容完全變成亂碼。
  更嚴重的是，在 `SessionDiagnosticsSwitchTests.cs` 第 47 行與第 81 行中，字串常數結尾的雙引號 `"` 與中文字元結合後在解碼過程中被吞掉，變成了 `??);`。這會直接導致 C# 編譯器報 `CS1010: Newline in constant` 錯誤，造成建置失敗。
- **具體修正建議**: 
  必須將這兩個檔案重新以正確的 **UTF-8 without BOM** 編碼重新寫入，並修復所有損毀的繁體中文註解與字串常數，確保雙引號正確閉合。
  - **`SessionDiagnosticsSwitch.cs` 修正後內容**:
    ```csharp
    #if DEBUG
    namespace ChurchReport.Diagnostics
    {
        /// <summary>
        /// 控制 Session 診斷偵錯訊息是否寫入 System.Diagnostics.Debug 輸出管線。
        /// </summary>
        public static class SessionDiagnosticsSwitch
        {
            /// <summary>
            /// 取得或設定是否啟用 Session 診斷輸出。
            /// </summary>
            public static volatile bool Enabled = false;
        }
    }
    #endif
    ```
  - **`SessionDiagnosticsSwitchTests.cs` 修正後內容**:
    ```csharp
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using Xunit;

    namespace ToolUtility.Dataverse.Tests
    {
        /// <summary>
        /// 驗證 Session 診斷開關預設關閉，且所有既有呼叫點均受保護的單元測試。
        /// </summary>
        public sealed class SessionDiagnosticsSwitchTests
        {
            [Fact]
            public void Session_diagnostics_are_default_disabled_and_all_call_sites_are_guarded()
            {
                var repositoryRoot = FindRepositoryRoot();
                var switchPath = Path.Combine(
                    repositoryRoot, "SpeechMessageProducts.ChurchReport", "Diagnostics", "SessionDiagnosticsSwitch.cs");
                Assert.True(
                    File.Exists(switchPath),
                    "SessionDiagnosticsSwitch 檔案必須存在，以作為預設關閉的防護機制。");

                var switchSource = File.ReadAllText(switchPath);
                var contextSource = File.ReadAllText(Path.Combine(
                    repositoryRoot, "SpeechMessageProducts.ChurchReport", "Models", "InMemoryDataContextSmallGroup.cs"));

                Assert.Contains("#if DEBUG", switchSource, StringComparison.Ordinal);
                Assert.Contains("public static volatile bool Enabled = false;", switchSource, StringComparison.Ordinal);
                Assert.Equal(1, CountMatches(contextSource, @"System\.Diagnostics\.Debug\.WriteLine\("));
                Assert.Equal(52, CountMatches(contextSource, @"WriteSessionDiagnostic\("));
                Assert.Equal(1, CountMatches(contextSource, @"if \(SessionDiagnosticsSwitch\.Enabled\)"));
                Assert.Equal(21, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[GetCurrentSessionId\\]"));
                Assert.Equal(18, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[GenerateCurrentRequestFingerprint\\]"));
                Assert.Equal(11, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[SetSessionDirtyFlag\\]"));
                Assert.Equal(1, CountMatches(contextSource, "WriteSessionDiagnostic\\(\\$?\"\\[InMemoryDataContext\\]"));
            }

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

                throw new DirectoryNotFoundException("找不到 ChurchReport 專案根目錄。");
            }

            private static int CountMatches(string source, string pattern)
                => Regex.Matches(source, pattern, RegexOptions.CultureInvariant).Count;
        }
    }
    ```

---

### Warning (警告問題)

#### 2. Release 模式下仍會執行字串插值，造成效能開銷
- **檔案路徑**: 
  - `SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs` (多處呼叫點，如第 170-600 行)
- **技術理由**: 
  `WriteSessionDiagnostic` 方法雖然在內部使用 `#if DEBUG` 包裹了實作，但該方法本身在 Release 模式下依然存在且為空實作。由於呼叫端（如 `WriteSessionDiagnostic($"[GetCurrentSessionId] 📋 Session ID: {sessionId}");`）並未被 `#if DEBUG` 包裹，CLR 在 Release 模式下執行時，仍會先評估參數並執行字串插值（呼叫 `string.Format` 並配置記憶體），隨後才呼叫空方法。這會在高頻的 Session 存取中產生不必要的 CPU 與記憶體（GC）開銷，違反了 B1 修正「避免效能污染」的初衷。
- **具體修正建議**: 
  在 `WriteSessionDiagnostic` 方法上加上 `[System.Diagnostics.Conditional("DEBUG")]` 屬性。這樣編譯器在 Release 模式下會完全移除所有對該方法的呼叫及其參數評估，徹底消除字串插值的效能開銷。
  ```csharp
  [System.Diagnostics.Conditional("DEBUG")]
  private static void WriteSessionDiagnostic(string message)
  {
      if (SessionDiagnosticsSwitch.Enabled)
      {
          System.Diagnostics.Debug.WriteLine(message);
      }
  }
  ```

---

### Info (建議事項)

#### 3. 既有檔案 `Program.cs` 的歷史亂碼清理
- **檔案路徑**: 
  - `SpeechMessageProducts.ChurchReport/Program.cs`
- **技術理由**: 
  雖然本次 B1 變更在 `Program.cs` 中新增的註解是正常的，但該檔案原本就存在的歷史註解（如第 1-12 行、第 38-44 行等）也是亂碼。這不影響本次變更的正確性，但會降低程式碼的可讀性與維護性。
- **具體修正建議**: 
  建議在後續的重構或清理任務中，將 `Program.cs` 檔案重新以正確的 UTF-8 編碼儲存，並還原歷史註解的繁體中文內容。

---

## 優秀設計點 (Positive Notes)

1. **嚴格的隔離邊界**:
   `SessionDiagnosticsSwitch` 僅包含一個 `volatile bool`，完全沒有保存任何 request、Session、使用者或租戶狀態，完美符合設計要求，避免了狀態洩漏與跨請求干擾。
2. **決定性的資源清理**:
   `Program.cs` 中對全域 Trace listener 的生命週期管理非常嚴謹。在正常停止時會先解除訂閱 `UnhandledException` 事件，再進行 Flush 與 Dispose，且使用了 `lock` 保護，有效防止了競態條件與資源洩漏。
3. **非同步 I/O 優化**:
   將 `StreamWriter.AutoFlush` 與 `Trace.AutoFlush` 設為 `false`，並在請求結束、正常停止與未處理例外三處進行批次 Flush，這能大幅降低高頻偵錯輸出對磁碟 I/O 的壓力，保護了 `/Home/ProcessLogin` 的效能量測準確性。

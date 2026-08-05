Two backends now have usable output for this run (`gemini-analyzer-attempt-1.stdout.md` and the new `claude-analyzer-attempt-1.stdout.md`), preserved under `.ccg/dual-model-runs/20260805-083516-base-church-controller-error-recovery-analyzer/`. No toolchain repair was needed — this session ran directly as the Claude analyzer leg.

## Analysis: BaseChurchController.HandleError 錯誤恢復與 TempData 遮蔽問題

**方法**：直接讀取 `BaseChurchController.cs`（1247 行全讀）、`HomeController.cs` 的 `DisplayErrorView`、`Startup.cs` 的 MVC/Session 註冊、`DisplayErrorView.cshtml`；用 `git blame`／`git log --follow` 追溯亂碼註解的起源提交；對整個 `Controllers/` 目錄做全文檢索排除 fire-and-forget／null-exception 等替代假設；確認 `ChurchReport.Tests` 目前完全沒有覆蓋這條路徑。

### 🔴 Critical
1. **`HandleError` 目前本身就會把 `exception.Message` 原文回傳給使用者**（第 357 行 AJAX、第 365 行非 AJAX），直接牴觸任務約束「never leak exception details」——這是獨立於 NRE 之外、更根本的合規缺陷。
2. **第 365 行 `TempData["ErrorMessage"] = exception.Message;` 是整個方法中唯一沒有 `try/catch` 保護的外部狀態存取**（同方法內 tracing、AJAX 判斷都有防護），一旦 `TempData` getter 內部解析失敗即拋出新例外，完全蓋掉原始例外，違反方法自身「統一錯誤處理」的設計初衷。

### ⚠️ Warning
3. `HomeController.cs:752` 讀取端同樣無防護，是同一缺陷的鏡像，若不修就會二次崩潰在錯誤頁本身。
4. `Startup.cs:1230` 仍保留舊版 `{ErrorMessage}` 路由樣板，保留了「無界路由值」攻擊面（風險由 Razor 自動編碼緩解）。
5. 第 190 行 `new HttpContext` 隱藏（非覆寫）基底屬性——已確認 `ControllerBase.HttpContext` 非 `virtual`，故可排除是本次 NRE 成因，但屬潛在維護風險。

### ℹ️ Info
6. 亂碼註解已用 `git blame`/`git show` 定位：早在 `270e7dea`（首次加入這批註解的提交）當下就已是 `?` 亂碼，屬**當時寫入即損毀**（非後續編碼另存造成），位元組層級已不可逆，只能依代碼行為重寫，不能重新解碼還原。
7. 全目錄檢索排除了 fire-and-forget／`async void`／`HandleError(null...)` 等替代根因假設。

**根因**：`ControllerBase.TempData` getter 內部經 `HttpContext.RequestServices.GetRequiredService<ITempDataDictionaryFactory>()`，若 `ControllerContext.HttpContext` 在呼叫當下實際為 `null`，下一步 `.RequestServices` 存取即拋出 NRE——外觀與回報現象一致。這是「有根據的最佳假設」，建議下次事故時保留完整 stack trace以驗證。

**最小安全修復設計**：用既有的 `GetStableHash` 手法算出短關聯碼（不新增任何靜態快取），把使用者可見訊息改成「通用訊息＋關聯碼」（同時滿足「不洩漏」與「TempData 不可用時安全降級」兩個約束），並把非 AJAX 分支的 TempData 寫入包 `try/catch`、`RedirectToAction` 移出 try 區塊無條件執行；`HomeController.DisplayErrorView` 讀取端同樣包一層 try/catch 但沿用既有 fallback 鏈。診斷豐富的 `errorMessage` 完全不動，仍原樣送往 `TraceByLevel`／LINE 管理通知（後台通道，不受約束限制）。

**測試計畫**：`ChurchReport.Tests` 目前零覆蓋，需新增 `HandleError_NonAjax_NeverLeaksExceptionMessage`、`HandleError_WhenTempDataThrows_StillRedirectsAndDoesNotThrow`、`HandleError_Ajax_ReturnsJsonWithoutExceptionDetails`、`HandleError_AlwaysTracesOriginalExceptionBeforeAnyTempDataAttempt`、`DisplayErrorView_WhenTempDataThrows_FallsBackSafely`。

**生命週期風險**：修復不新增 `IDisposable`、不改動 `Dispose()`／連線池／`InMemoryContext`，風險僅侷限於訊息內容與控制流，屬低風險。

完整報告（含程式碼草案、逐行引用、風險緩解）已寫入 `claude-analyzer-attempt-1.stdout.md`。此為分析階段產出，尚未修改任何原始碼。

---
SESSION_ID: 573846e6-6d6f-4912-b469-97dc903079af

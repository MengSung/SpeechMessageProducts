## P7.2 Continuation 服務轉發與隔離審查報告

審查完成，成果已存於 `.ccg/dual-model-runs/20260812-094007-p7-2-continuation-service-forwarding-review-2-reviewer/claude-reviewer-attempt-1.stdout.md`（與同目錄下既有 Gemini 產出配對）。未連線 CRM、未修改任何原始碼、未輸出密鑰或原始 ID。

### Critical
- **`DownloadIntegrateData.Core.cs:109-134` + `ListManager.cs:232-256`**：`SetupIntegrateData` 完全沒有 `IOrganizationService` 參數，其 `IdentityConverter` 直接讀 `m_ToolUtilityClass.m_Crm2011OrganizationService`（`ToolUtilityFactory` 的 process-wide static 單例）。呼叫端 `ListManager.SetupIntegrateData` 也未傳任何 service。**確認 `DownloadIntegrateData`／Factory ToolUtility 仍是 P7.4/P7.5 的明確 blocker**，本次 diff 未觸及此路徑。

### Warning
- `DownloadListManager.GetListManager` 的 `organizationService` 參數目前**沒有任何生產呼叫端**（BaseChurchController、SmallGroupController、MemberInfoController、AuthenticationController、ListManagerCacheExtensions 全部只傳 3 個位置參數）實際供應。本次修正移除了「借用 service 寫回共用單例」的反模式，這是真實改善，但 fallback 仍會落到 Factory 共用欄位——隔離效果目前主要停留在測試層，尚未在生產路徑成立。

### Info
1. 舊版寫回共用單例的反模式已被移除（`git diff` 確認），並由 `DownloadListManagerIsolationTests.cs` 驗證兩個交錯操作後共用欄位保持 null。
2. `ToolUtilityFacade.cs` 的 dynamic-list overload 系列正確只轉傳 caller 提供的 service，底層 `ListService` 在 service 為 null 時回傳空集合而非回退，符合 fail-closed；4 個 overload 測試以呼叫計數驗證。
3. `P72ContinuationLocalOnlyCatalog.cs` 的輸入過濾在 type-init 時即生效，`CeExecutorEnabled`/`ConsumerEnabled` 硬編碼 false，`accessToken`/`organizationAlias`/`profileAlias` 皆會 fail closed（測試已驗證）。
4. 錯誤傳遞已從 `throw e;` 改為 `throw;`，保留原始 stack trace，測試已驗證。
5. **實測驗證**：本機執行 `dotnet test` — SpeechMessage.Dynamics.Tests 的 P72Continuation 8 個測試、ChurchReport.MemberInfo.Tests 的 DownloadListManagerIsolationTests 7 個測試，全數通過。

**結論**：本次 diff 對 `DownloadListManager`/`ToolUtilityFacade` 路徑的隔離修正是真實且有測試覆蓋的，但 `DownloadIntegrateData` 仍是明確的 P7.4/P7.5 blocker，且已完成的隔離改動尚未被任何生產呼叫端接線啟用。

---
SESSION_ID: 1b20b118-5320-42a9-827d-884ec7899742

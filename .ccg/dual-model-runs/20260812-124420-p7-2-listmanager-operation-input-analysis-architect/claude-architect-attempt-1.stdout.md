## 判斷結果：此最小修正**正確**（無 Critical 發現）

已交叉核對 `SpeechMessageProducts.ChurchReport/Models/ListManager.cs:281-291`、對照組 `DownloadIntegrateData.Core.cs:163-202`、回歸測試 `DownloadIntegrateDataIsolationTests.cs` 與 `DownloadIntegrateDataOperationServiceIntegrationTests.cs`，以及 `.trellis/tasks/08-12-p7-2-continuation-release-candidate/design.md` 第 21-37 行的既定契約範圍，並確認目前所有正式呼叫端（Controllers、`ListManagerCacheExtensions`）皆只使用單參數 `SetupIntegrateData(string)`，無人呼叫 service-aware 二參數 overload。

## Critical
無。

## Warning

1. **`InvalidOperationException` 可能與其他商業例外語意混淆（跨層防禦深度不足）**
   `ListManager.cs:289` 使用泛用 `InvalidOperationException`，目前雖已全域掃描確認 Controllers 沒有 `catch (InvalidOperationException)` 會吞掉此訊號，但這只是「目前沒有」而非「架構上不可能」。由於此 overload 尚無呼叫端，未來第一個呼叫端很可能是在既有 controller 的既有 try/catch 區塊中新增，若該區塊剛好以 `catch (InvalidOperationException)` 處理其他商業邏輯錯誤（例如驗證失敗），會把這個刻意的 fail-closed 硬停止語意錯誤分類為可重試的一般錯誤。
   - **可能失效情境**：未來開發者在某個 Controller 加入呼叫此 overload 的程式碼，且該 Controller 既有 `catch (InvalidOperationException) { /* 顯示驗證錯誤，continue */ }`；fail-closed 訊號被吞掉，使用者看到「請重新輸入」而非「此 API 尚未就緒」，掩蓋了真正需要修正的呼叫端誤用。
   - **建議**：非必要變更，但可考慮定義專屬例外型別（如 `OperationContextNotReadyException : InvalidOperationException`），讓未來呼叫端若要特別處理可以精準 catch，同時保留 `is InvalidOperationException` 的既有測試相容性。

## Info

1. **Fail-closed 邊界確實在任何 instance 欄位讀取或 CRM I/O 之前** — `ListManager.cs:283-290` 只做 `ArgumentNullException.ThrowIfNull(organizationService)` 後立即無條件 `throw`，未讀取 `m_Account`、`m_Password`、`LoginType`、`m_SelectDate`、`ActiveListId` 或 `m_ListSmallGroupWeeklyReport`，也未存取 `ListEntityId` 參數本身。`DownloadIntegrateDataIsolationTests.cs:45-47` 以 `RuntimeHelpers.GetUninitializedObject` 繞過建構式與欄位初始化式即可安全通過測試，間接證明了這一點——若方法曾意外讀取任何未初始化欄位會直接 NullReferenceException 而非預期的 `InvalidOperationException`。

2. **跨使用者 isolation 與 service ownership 已有充分證據** — `AssertDoesNotRetainBorrowedService`（`DownloadIntegrateDataIsolationTests.cs:107-115`）以反射掃描 instance 的 public/non-public 欄位，確認 A、B 兩個 marker service 皆未被寫入 `ListManager` 或 `DownloadIntegrateData`；`DisposeCount`／`CallCount` 均為 0，證明方法既未觸發 CRM I/O，也未越權 Dispose 呼叫端借用的資源，所有權留在外層 lease owner，與 XML 文件宣告一致。

3. **與已完成的 service-aware 對照組行為一致** — `DownloadIntegrateData.Core.cs:163-202` 的真正 operation-local 入口示範了完整模式：建立 `operationReport` 本地物件、只在全部唯讀 SDK 呼叫成功後才做單一 reference assignment 提交（第 201 行），失敗不留半成品。`ListManager` 這個過渡 overload 選擇「連這條路徑都不進入」的更保守子集，方向一致且風險更低，不需要現在就對齊完整實作。

4. **是否有更小的替代方案：不建議刪除此 overload** — `design.md:21-22` 明確將 `ListManager.SetupIntegrateData(string, IOrganizationService)` 定義為未來 service-aware 安全路徑的「公開契約」佔位簽章。若直接刪除，會讓 P7.4/P7.5 路線圖失去可辨識的公開切點；維持簽章、內部 fail closed，是比刪除或標記 `[Obsolete(error: true)]`（會與「未來要實作」的既定方向矛盾）更務實的最小修正。

5. **Fault/timeout cleanup 在此 overload 範圍內是無操作議題** — 因為方法同步、無條件在任何非同步或 I/O 路徑之前 throw，不存在需要清理的中繼狀態；真正的 fault/timeout eviction 責任仍完全落在呼叫端 lease owner，這與文件聲明一致，也超出本 overload 的責任範圍。

6. **範圍外但相關的既知缺口（design.md 已追蹤，非本次新發現）**：同一 session 快取 `ListManager` 的平行呼叫併發語意（`design.md:37`）尚未有 request-local 證據；此為既有的 P7.4/P7.5 blocker，與本次二參數 overload 的 fail-closed 修正無直接關聯，不影響本次判斷。

---
SESSION_ID: b312fa0c-effe-4a5a-a822-4b12ca44caa2

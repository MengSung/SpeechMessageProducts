# CCG Analyzer 報告：P7.2 continuation — Slice C 根因與跨使用者隔離分析

稽核依據：直接讀取本機原始碼（`DownloadListManager.cs`、`ListManager.cs`、`ToolUtilityFactory.cs`、`ToolUtilityClass.Core.cs`、`ListManagerCacheExtensions.cs`）與既有測試前例（`ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStore*.cs`、`SpeechMessage.Dynamics.Tests`）。未執行 CE、未修改任何檔案，未輸出 credential／endpoint／CRM ID／原始例外內容。

---

## 審查點 1：跨 request/profile 洩漏風險與最低風險修正

**Critical**
- `ToolUtilityFactory.GetInstance()/GetInstance(string)` 是 process-wide 真單例（static `_instance` + double-check lock，`ToolUtilityFactory.cs:28,50-95`）：無論傳入哪個 `discoveryServiceType`，只有第一次呼叫會建立實例，之後所有呼叫共用同一物件，直到程式重啟或呼叫僅供測試用的 `ResetInstance()`。`DownloadListManager` 每個 request 都用 `ToolUtilityFactory.GetInstance("DYNAMICS365-9.0")`（`DownloadListManager.cs:45`）拿到「同一個」`ToolUtilityClass`，其 `m_Crm2011OrganizationService`／`m_OrganizationService` 天生是跨 request、跨使用者共享的可變狀態。
- `ToolUtilityClass` 建構式（`ToolUtilityClass.Core.cs:97-118`）在建立當下就呼叫 `InitializeCrmConnection()`，用單一固定服務帳號建立連線並寫入 `m_Crm2011OrganizationService`（`Core.cs:156-163`）。因此 `GetListManager` 的 write-back 判斷式執行時，該欄位幾乎必然已非 null。
- 結論：`GetListManager` 內「only-if-null」寫回邏輯（`DownloadListManager.cs:108-123`）在目前正式呼叫路徑上是**死碼**——repo 內唯一的正式呼叫（`ListManagerCacheExtensions.cs:51,73`）呼叫 `SetupListManager(account, password, selectDate)`，完全未傳入 `organizationService`，該參數恆為 null。目前**沒有作用中的洩漏事件**，但此分支從未被任何呼叫路徑或測試驗證過。
- 一旦未來有呼叫端開始傳入非 null 的 `organizationService`（例如 Slice D-H 的 per-request/pooled 連線），這不是機率性 race，而是**確定性**問題：(a) 第一個呼叫者的 service 會被永久釘死在此單例欄位上，之後所有呼叫者傳入的 `organizationService` 一律被靜默忽略——因為 `GetSmallGroupMemberNumber` 等下游方法一律直接讀 `m_ToolUtilityClass.m_Crm2011OrganizationService`/`m_OrganizationService`（`DownloadListManager.cs:354-361, 379-409`），從不使用 `GetListManager` 收到的本地參數；(b) 若第一個寫入的 service 屬於特定租約且之後失效，所有後續請求會靜默沿用錯誤或已失效的連線，且外部無法察覺。

**Warning**
- 即使不考慮身份洩漏，process-wide 單例代表所有並行 request 共用同一個 CRM 連線／channel，在中高併發下容易把單一連線失敗放大為全站中斷，而非單一 request 失敗。

**最低風險 operation-local 修正**：不動 `ToolUtilityFactory` 既有單例語意（影響面過大）。在 `DownloadListManager` 內新增「本次呼叫專用」區域變數（`organizationService ?? m_ToolUtilityClass.m_Crm2011OrganizationService`），把 `GetListManager`／`GetSmallGroupMemberNumber` 中對單例欄位的直接讀取全部改讀此區域變數，並移除寫回單例欄位的邏輯。

**Info**：此死碼分支「看起來已支援 per-request service」卻被單例語意架空，屬於高信心的誤導性程式碼，建議在 Slice D-H 動工前處理，而非等 CE cycle 才發現。

---

## 審查點 2：child-to-parent 受控診斷欄位

**Info（可用的固定分類欄位）**
- `ErrorCategory`：`ConnectionUnavailable` / `IdentityMismatch` / `Timeout` / `ObjectDisposed` / `StackTraceReset` / `Unknown`
- `OperationStage`：對應原始碼中已公開的方法名，如 `GetListManager` / `FindLoginUser` / `FindListCollection` / `ProcessListEntity` / `GetSmallGroupMemberNumber`
- `IsRecoverable`（bool）
- `OccurredAtUtc`（`DateTimeOffset`，改用 UTC；現有 catch block 用 `DateTime.Now.ToString()` 跨時區比對不可靠）
- `EvidenceId`（純內部關聯序號，不含任何業務 GUID）

**Warning**：現有 catch block（`DownloadListManager.cs:229, 267, 343` 等）組出的 `ErrorString` 直接內嵌 `e.ToString()`（含完整 stack trace 與 message）。這些字串目前建立後未被使用/拋出，若未來接上 child-to-parent 診斷通道，**絕不能**直接上傳這個 `ErrorString`，否則會夾帶原始例外內容。

---

## 審查點 3：必須先寫的最小 TDD 測試與回歸測試

**Critical（隔離測試，預期先紅燈以鎖定現況）**
- 測試 A：以兩個假 `IOrganizationService`（fakeA/fakeB）依序驅動兩個 `DownloadListManager` 執行個體，斷言第二次呼叫實際使用 fakeB 而非 fakeA。依現有 only-if-null 寫回＋單例邏輯，此測試預期紅燈。
- 測試 B：驗證 `GetSmallGroupMemberNumber` 只使用本次呼叫的 `organizationService`，不讀共用單例欄位；用兩個 mock 分別注入並斷言呼叫次數（仿照 `P72Data8ListManagementFixtureStoreTests.cs` 既有的 recording-service 模式）。

**Warning（exception stack trace 回歸）**
- 測試 C：讓下游拋出標記例外，驗證外層捕捉到的 `StackTrace` 仍含原始拋出點 frame。目前 `DownloadListManager.cs:231,269,345,448,488,570,654` 與 `ListManager.cs:78` 的所有 `throw e;`/`throw Exception;` 都會讓此測試紅燈；修法一律改為 `throw;`。

**Info（Timeout／Dispose 回歸）**
- 測試 D：模擬底層 `Retrieve` 逾時，驗證不會被誤判為「查無資料」而回傳空集合（現行邏輯僅在 `serviceToUse == null` 時丟例外，其餘底層例外會直接往外傳，屬 fail-closed，應以測試鎖定，避免未來重構誤吞逾時）。
- 測試 E：多次呼叫 `ToolUtilityFactory.ResetInstance()` 應冪等、不拋 `ObjectDisposedException`（`Core.cs:173-176` 已對各資源包 try/catch，但無對應自動化測試）。

---

## 審查點 4：Slice D-H 本機 capability 與 CE evidence gate 分離

**Info**
- Repo 內已存在更嚴謹的 operation-local 參考實作，可作為 Slice D-H 設計基準：`ChurchReport.MemberInfo.Tests/P72Data8ListManagementFixtureStore.cs`（建構子接手單一 service 的 Dispose ownership、禁止跨 test/request 重用、固定 allowlist 查詢）；`SpeechMessage.Dynamics.Tests` 亦已有 `OrganizationAdmissionLeaseLifecycleTests`、`OnPremiseClientDisposalTests` 涵蓋 lease 生命週期。建議 Slice D-H 沿用此既有 lease/ownership 慣例，而非繼續往 `ToolUtilityFactory` 單例疊加功能。
- 分離機制：Slice D-H 本機驗證（TDD、隔離、Release build）應完全在不依賴真實 CRM 連線的假物件/recording-service 層完成；CE evidence gate 僅在本機驗證全部通過後才觸發新 cycle（沿用「舊 cycle 不得重試」前提，新 cycle 需獨立 id）。在本機驗證完成前，不應變更、停用或移除任何現有 `ToolUtility` 呼叫路徑——production 目前唯一呼叫路徑（`ListManagerCacheExtensions.cs:51,73`）應維持不變，直到新路徑通過本機與 CE 雙重驗證，避免提早切流。

---
SESSION_ID: 8ff37eea-e823-4d6a-8427-a6f5a355b70a

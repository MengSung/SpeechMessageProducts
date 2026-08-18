# 產品 A 完整實現 Dataverse 連線架構圖（v1）

## 目標

讓「四大產品 Dataverse 連線架構 — 合併終版 v2」這張圖，**在產品 A（ChurchReport）上完整成立**，
並且成型為可直接套用到產品 B / C / D 的第一版基座。

不是「消除共用連線」（那是前一個任務，已完成）。本任務的目標是**把圖上的每一個元件真的建出來**：
Gateway、ConnectionManager、Keyed Bounded Pool、Lease、狀態機、Pool Key、Metrics。

## 為什麼上一輪只做到一半

前置任務 `08-17-toolutility-scoped-lifetime` 的 PRD 把目標定為「消除 Worker Process 共用一條連線」，
並在 A 類收斂。那條終點線對它自己的目標是對的，但**不等於這張圖**。

結果是：地基清了一半，圖上三個核心新元件（Gateway / ConnectionManager / Lease）一個都沒建。
本任務接手把圖建完。

## 已完成的基礎（可直接採信，皆已驗證）

| 項目 | 狀態 |
|---|---|
| `ToolUtilityClass` 生命週期 | 已是 Scoped，連線由 DI 注入 |
| `CrmConnectionPool`（586 行） | 已有 Semaphore、ConcurrentBag、cleanup Timer、Stats、健康檢查、MarkConnectionFaulted |
| `PooledOrganizationService` | 已有 per-request 取得／歸還／faulted 銷毀 |
| `OnPremiseClient` | 已實作 `IOrganizationService`，且**已具備 `CallerId` 與 `Timeout`** |
| 追蹤資源 | 已抽離為 Singleton `FileToolUtilityTracer` |
| 死碼 | 已刪除 7 個型別與 2 個死建構式 |
| 測試基線 | ToolUtility 63、Dataverse 13、MemberInfo 22 失敗／305 通過 |

## 設計成敗的四個關鍵事實（本任務規劃前已逐一在程式碼上驗證）

這四點決定了本任務能否收斂。**它們不是假設，是查證結果。**

**F1 — `IOrganizationService` 是現成的接縫。**
`ToolUtilityFacade` 的 19 個 lazy 子服務全部以 `IOrganizationService` 建構
（`ToolUtilityFacade.cs:148-168`）。在這一層插入代理，上層 3126 次方法呼叫、
19 個子服務、52 處 `m_Crm2011OrganizationService` 參照**全部零改動**。

**F2 — 全專案只有一處把 `IOrganizationService` 轉型成具體型別。**
`ToolUtility/Adapters/LegacyOrganizationServiceAdapter.cs:69` 的 `as OrganizationServiceProxy`，
是 null-safe 轉型；今天傳入 `PooledOrganizationService` 時本來就得到 null。插入代理不造成回歸。

**F3 — `ToolUtilityClass.m_OrganizationService`（`OrganizationServiceProxy` 公開欄位）從未被指派，恆為 null。**
全專案查無任何 `m_OrganizationService =` 指派。約 24 處 `if (... != null)` 分支是死碼，
`ref` 傳遞傳的是 null。圖 ⑨ 的第二條技術債因此可以極低成本清除。

**F4 — `ICrmConnectionPool` 雖注入 16 個 Controller，實際只有一處在用。**
`BaseChurchController.cs:1063` 的 `GetStats()`。抽換底層實作不會波及那 16 個建構式。

## 需求

### 架構元件（對應圖上編號）

- **R1** 建立 `IDataverseGateway`（Scoped），為應用程式的唯一 CRM 入口；
  提供 `Execute` / `Execute<T>`，**per-operation 取得 lease**（圖 ⑦ 採用的方案）。
- **R2** Gateway 必須實作 **Reentrant Lease**（圖 ⑥）：巢狀 `Execute` 共用同一條 lease，
  以深度計數管理；Gateway 是 Scoped，因此計數不跨 request。
- **R3** 建立 `IDataverseConnectionManager`（Singleton）作為統一入口：
  擁有 Pool Key、租借、服務身分；負責 Create / Health / Fault / Dispose；
  Timeout / Metrics / Shutdown Cleanup。**應用程式不得再看見 raw client。**
- **R4** 建立 `IClientLease`，實作圖 ③ 的三條鐵則：
  只接受自己建立的 client、拒絕重複釋放、拒絕並行共用同一條 client。
- **R5** 建立 `IBoundedClientPool`（Singleton，**Keyed**），實作圖 ④ 的完整狀態機：
  Idle → Leased → Faulted → Disposed，含淘汰路徑（IdleTimeout、應用程式關閉、Faulted 一律不回池）。
- **R6** Pool Key = `Product + Environment + OrganizationUrl + EffectiveIdentity`（圖 ⑤ / ⑩）。
  今天 `EffectiveIdentity` 恆為服務帳號 → 恆 1 個子池；**但 key 結構今天就要存在**。
- **R7** 健康檢查以 `WhoAmI` 實作（圖 ④）；Faulted 一律不回池。
- **R8** 五個參數全部可組態（圖 ⑧）：`MinSize`、`MaxN`、`AcquireTimeout`、`IdleTimeout`、`HealthInterval`。
- **R9** Metrics 可讀取（圖 ③ Manager 職責）：至少 Idle / Leased / Faulted / 等待中 / Acquire 逾時次數。
- **R10** Shutdown Cleanup：應用程式關閉時由 DI 釋放 Manager 與 Pool 的所有 client。

### 技術債清除（圖 ⑨）

- **R11** 刪除 `ToolUtilityClass` 自行建立連線的 legacy 路徑
  （`InitializeCrmConnection()` / `CreateOnPremiseClient()` 呼叫）。
- **R12** `m_Crm2011OrganizationService` 不得再持有 raw client；改為持有 gateway 支撐的代理。
- **R13** 刪除恆為 null 的 `m_OrganizationService` 欄位與其死分支（依 F3）。

### 相容性（不可違反）

- **R14** `ToolUtilityClass` 的公開 API 不變 → 3126 次呼叫零改動。
- **R15** `TraceByLevel` / `TraceByLevelStatic` 簽章不變 → 160 個呼叫點零改動。
- **R16** `ICrmConnectionPool` 的消費端不變 → 16 個 Controller 建構式零改動（依 F4）。
- **R17** **不修改 `InMemoryDataContextSmallGroup` 的 13 個 session 鍵快取。**
  那 20 個 legacy 持有者改以 ambient 解析取得當前 request 的 gateway（見 design.md §7），
  因此不需要動快取，也不會跨 request 持有連線。
- **R18** 任一 Run 結束時系統都必須可建置、可登入。

## 驗收標準

| # | 判定方式 |
|---|---|
| A1 | `grep -rn "CreateOnPremiseClient" --include=*.cs SpeechMessageProducts.ChurchReport ToolUtility` 僅命中 Pool 內部建立路徑與其介面定義；`ToolUtilityClass` 為 0 |
| A2 | `grep -rn "m_OrganizationService" --include=*.cs .` 排除註解後為 0 行 |
| A3 | `grep -rn "PooledOrganizationService" --include=*.cs .` 排除註解後為 0 行（已由 Gateway 取代） |
| A4 | `IDataverseGateway`、`IDataverseConnectionManager`、`IBoundedClientPool`、`IClientLease`、`DataverseConnectionKey` 五個型別皆存在且有測試 |
| A5 | 測試：巢狀 `Execute` 三層只取得 **一條** lease（Reentrant，圖 ⑥） |
| A6 | 測試：同一條 client 不可能同時被兩個 lease 持有（圖 ③ 鐵則三） |
| A7 | 測試：Faulted 的 client 不回池，且池大小正確遞減（圖 ④） |
| A8 | 測試：Pool Key 不同 → 不同子池；今天服務帳號恆定 → 子池數恆為 1（圖 ⑤ / ⑩） |
| A9 | 測試：`Acquire` 超過 `MaxN` 時在 `AcquireTimeout` 內擲出明確逾時例外，且 Metrics 的逾時計數 +1 |
| A10 | 測試：request scope 結束後，該 request 取得過的 lease 全部已歸還（Leased 歸零） |
| A11 | 測試：ambient 解析在無 HttpContext 時自建 scope，工作結束即釋放（R17 的安全性） |
| A12 | 五個參數皆可由 `appsettings` 覆寫，且有測試證明覆寫生效 |
| A13 | `dotnet build SpeechMessageProducts.sln -c Debug` 0 錯誤 0 警告 |
| A14 | `ToolUtility.Tests` 63 全綠；`ToolUtility.Dataverse.Tests` 全綠（含本任務新增）；`ChurchReport.MemberInfo.Tests` 失敗 ≤ 22 且通過 ≥ 305 |
| A15 | 人工回歸（沿用 `08-17` 任務的 `regression-checklist.md`）—— **不列為 agent 完成條件** |

### Run E 實際驗收證據

| # | 狀態 | 實際證據 |
|---|---|---|
| A1 | 達成 | Run D 的非註解等價 grep 輸出為 `NO OUTPUT`（`ToolUtilityClass` 及其 partials 無 `CreateOnPremiseClient`）；唯一建立位置是 `DataverseConnectionManager.CreateClient`。 |
| A2 | 達成 | Run D 的 `m_OrganizationService` 非註解等價 grep 輸出為 `NO OUTPUT`。 |
| A3 | 達成 | Run C 的 `PooledOrganizationService` 非註解 grep 輸出為空；Run C 已刪除型別及其測試。 |
| A4 | 達成 | `ToolUtility/Dataverse/` 包含五個契約型別，Run A/B 的 `PoolArchitectureTests` 與 `GatewayArchitectureTests` 已通過。 |
| A5 | 達成 | `GatewayArchitectureTests` 的三層巢狀 Execute 測試通過，斷言只取得一條 lease。 |
| A6 | 達成 | `PoolArchitectureTests` 的同 client 不可同時由兩個 lease 持有測試通過。 |
| A7 | 達成 | `PoolArchitectureTests` 的 `MarkFaulted` client 不回池且計數遞減測試通過。 |
| A8 | 達成 | `PoolArchitectureTests` 的相同／不同 `DataverseConnectionKey` 子池分割測試通過；Manager 的 EffectiveIdentity 取服務帳號。 |
| A9 | 達成 | `PoolArchitectureTests` 的 MaxN timeout 測試通過，並斷言 `AcquireTimeouts` 增加。 |
| A10 | 達成 | Run C `RunCServiceGraphTests` C2 通過：三個並行 scope 結束後 Leased 為 0 且 pool 大小不超過 MaxN。 |
| A11 | 達成 | Run B ambient fallback 測試及 Run D D2 通過：無 HttpContext 時建立一個 scope、操作後已釋放且 Leased 為 0。 |
| A12 | 達成 | Run C `RunCServiceGraphTests` 的五參數組態覆寫測試通過；Run E 已將五項完整寫入 `Dataverse:Pool` 的 base、Development、Production 組態。 |
| A13 | 達成 | Run E 最終 `dotnet build SpeechMessageProducts.sln -c Debug` 輸出為 0 warnings／0 errors；原文已記入 notes.md。 |
| A14 | 達成 | Run E 最終輸出：ToolUtility 63 passed、Dataverse 24 passed、MemberInfo 22 failed／305 passed，符合既定基線；原文已記入 notes.md。 |
| A15 | 等待人工回歸 | 非 agent 完成條件；沿用 `.trellis/tasks/08-17-toolutility-scoped-lifetime/regression-checklist.md`。 |

## 不在範圍（明確排除，不得中途擴大）

- **產品 B / C / D 的實作。** 本任務只做 A，但所有新型別必須放在可被 B/C/D 直接引用的組件
  （`ToolUtility` / `PowerPlatform.Dataverse.Client`），不得寫進 `SpeechMessageProducts.ChurchReport`。
- **`InMemoryDataContextSmallGroup` 的 13 個 session 鍵快取重新設計。**（R17 已給出不需動它的解法）
- **per-user impersonation 的啟用。** Pool Key 的 `EffectiveIdentity` 今天恆為服務帳號；
  結構要有，但不開啟 impersonation。
- 明文密碼與憑證輪替。
- `ToolUtilityClass` 的公開 API 重新設計。
- 那 22 個既有失敗的 Payments 命名測試。

## 收斂承諾

本任務為 **5 個 Run，固定不變**（見 `implement.md`）。

**執行期間不得新增 Run、不得擴大範圍。** 若發現阻礙，寫進 `notes.md` 並停止回報，
由使用者決定，不得自行衍生子任務。這是上一輪發散的直接教訓。

規劃前已完成 F1～F4 四項程式碼查證，正是為了避免「動手才發現假設錯了」。

# P7.4 legacy Gateway admission boundary

## 目標

補足 ChurchReport P7.4 的 aggregate-capacity / non-overlap 安全缺口，使未來
`DynamicsAccess:Package01FeeReadsEnabled` 僅能在「所有同一 Dynamics Organization 的
legacy ToolUtility 外送工作已確定 drain，且 Gateway/Data8 已由 deployment-owned durable
admission authority 接管」的可驗證條件下啟用。

本 child 的交付不是立即啟用功能旗標，也不是把同步 CRM 呼叫包一層 permit 後宣稱安全；它必須建立
可稽核的程式契約、測試、deployment runbook 與 fail-closed 判斷，讓實機 owner 能完成一次
可讀回的 non-overlap 演練。若 repository 無法證明外部 binding，gate 必須保持 `false`。

## 已確認事實

- `DonationFeeQueryService` 在 Package01 關閉時，直接呼叫
  `ToolUtilityClass.RetrieveDedicationFeeByDateFetchXml`；該呼叫最終對長壽命
  `IOrganizationService` 直接執行 `RetrieveMultiple`。
- `ToolUtilityFactory` 是 process-wide singleton，且 ToolUtility 同時保有 mutable CRM service、
  facade 與 process-global trace listener；fee path 不經 ChurchReport DI 的
  `CrmConnectionPool`。
- 現有 ChurchReport Embedded / Dedicated Data8 runtime 建立 local in-memory admission；
  Gateway 的 SQL coordinator unit tests 不等於 ChurchReport + ToolUtility 已採用同一個
  durable coordinator。
- 單一 Package01 fee adapter 即使能限制該讀取，也不能取消同步 SDK in-flight call，且不能約束
  同一 ToolUtility instance 的其他 legacy Dynamics call；因此不能單獨作為 feature-gate enablement
  證據。

## 需求

1. 在不接受 caller-selected profile、endpoint、credential、connector、Organization 或 routing 的前提下，
   定義由 deployment composition root 唯一擁有的 legacy-to-Gateway admission / drain boundary。
2. 該設計必須明確區分兩個事實：
   - operation-level metering：可限制已包裝的單一 legacy call；
   - organization-level safe enablement：必須證明所有 legacy outbound work 已停止、drain 完成，
     並且 Gateway/Data8 以同一 canonical Organization、namespace、epoch、configuration digest
     與 durable SQL coordinator 接管。
3. 在未能證明全域 durable authority 前，提供 deployment-owned drain-first / non-overlap
   control-plane contract：停止新 legacy intake、追蹤受控範圍內 active work、等待固定期限、
   驗證 no active legacy work，再允許 Gateway preflight；任何逾時、未知工作、lease loss、
   cleanup failure 或 read-back 不符都必須 fail closed。
4. repository-side runbook、validator 與 diagnostics 只輸出固定分類；不得記錄 CRM ID、名稱、
   endpoint、credential、token、cookie、原始 CRM response 或例外。
5. 需設計並實作 TDD 覆蓋：A/B isolation、server-owned routing、capacity contention、取消、
   deadline、permit / registration 釋放、drain、shutdown、lease loss、unknown synchronous transport
   與資源基線。
6. 不得啟用 `Package01FeeReadsEnabled`、切換 ChurchReport 流量、執行 CE 寫入，或將 local
   unit / integration tests 描述為 actual deployment evidence。

## 驗收條件

- [x] 有明確且完整的 design，證明為何單一 adapter 不足，並選定可安全推進的最小實作邊界。
- [x] 新的程式 contract 只有 deployment-owned immutable input；不保存 request、session、CRM entity、
      credential 或可變 profile state。
- [x] 可重複的本機測試證明 controlled legacy work 的 admit / reject / drain / disposal 生命週期，
      以及不跨 A/B workload 或 profile 洩漏。
- [x] runbook 與 validator 明確要求 external deployment owner 提供同一 canonical durable binding、
      完成 bounded drain、Gateway readiness、rollback 與去識別化 read-back。
- [x] 在 external evidence 缺失或不確定時，validator 產生 no-go，feature gate 維持 `false`。
- [x] 完成 focused tests、Release build、encoding / CRLF、scope、`git diff --check` 與 CCG review
      或明確記錄雙模型降級狀態。

## 非目標

- 不重寫所有 ToolUtility CRM API，不修改 P7.2 已關閉的 CE cycle，也不進行 P7.5 移除。
- 不將 `CrmConnectionPool`、`ToolUtilityFactory` 或新的 in-memory manager 當成 shared durable authority。
- 不在此 child 假裝完成 actual deployment drill、P7.5 或 P8。

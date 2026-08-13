# P7.4 奉獻能力對應與隔離稽核

## 目標

釐清權威 matrix 的 `ORG-CALL-00059`、`ORG-CALL-00060` 與已存在的 Package01 typed capability 之間是否為同一個能力，並將可證明的去重結果或精確的 source-only local design no-go 寫入任務紀錄。此 child 只產出來源分析與任務持久化；不改動產品 runtime、CE、feature gate、流量、P7.5 或 P8。

## 已確認事實

- `ORG-CALL-00059` 是 `ToolUtility/QueryOperations/FetchXmlQueryService.cs` 中依 contact 讀取 active `new_dedication_booking` 的固定 FetchXML；原始 phase-0 matrix 明確記錄它「應與 ORG-CALL-00041 的 product service row 去重」。
- `ORG-CALL-00041` 已有固定 operation `payments.dedication.retrieve.by.contact`、registry、Data8 executor、typed ProductClient，以及 disabled-by-default 的本機 consumer boundary；它不是 CE、流量或 ToolUtility removal 證據。
- `ORG-CALL-00060` 是 `DonationDedicationFeeFormService` 為表單組裝以 Line ID 或 caller-provided contact GUID 讀取 `contact` 的 legacy path。它與認獻單讀取不是同一 response contract；既有 auth-contact typed read 也尚未接入登入或此 consumer。
- 現況的 `DonationPaymentManager`、`DonationPaymentFormModel`、`ToolUtility` CRM `Entity` 和 fee refresh lock 屬 legacy state chain。不得將它們、Session、Line ID、browser GUID、profile、connector、endpoint、credential 或 CRM query 當作新 Gateway capability 的 authorization 或 routing authority。
- checked-in feature gates 必須保持 `false`；歷史 P7.2 Slice C 為已 cleanup 的 `write-not-committed` no-go，不得重播或復用舊 cycle。

## 需求

- 證明或否證 `00059` 與 `00041` 的資料語意、固定模板、輸入、輸出與 consumer boundary 是否完全相同；只有完全相同才可標記為去重，且不得把 local-only evidence 升級為 consumer、CE、host、cutover、P7.5 或 P8 evidence。
- 追蹤 `00060` 從 controller/manager/service 到 CRM read 的完整授權、狀態與 resource chain，判定是否已有 server-derived immutable authorization boundary 可安全建立 DTO-only child。
- 若不存在該邊界，記錄精確 no-go 與恢復條件；不得以 partial Church-only branch、caller-provided locator、legacy Entity bridge、request-time fallback 或 shared mutable manager state 假裝完成遷移。
- 依 AGENTS.md、Trellis、CCG 與 backend isolation contract 產出可追溯任務紀錄；外部 Gemini/Claude 分析與 review 每次最多等候 45 秒，未完成時記錄降級並採本機驗證。

## 驗收條件

- [x] task record 明確列出 `00059` 對應 `00041` 的證據，以及不應新增重複 registry、executor、ProductClient 或 consumer 的結論。
- [x] task record 明確列出 `00060` 的 caller、authorization、mutable state、CRM SDK 與 response-boundary 風險，以及 fail-closed 恢復前置條件。
- [ ] 不存在 `.cs`、`.cshtml`、`.csproj`、設定、feature gate、matrix、CE、traffic、P7.5 或 P8 runtime 變更。
- [ ] 產出 design、implement、context manifests、CCG task、check 與雙模型降級狀態；任務檔通過 JSON、UTF-8 無 BOM、CRLF、final CRLF、`git diff --check` 與 scope 檢查。

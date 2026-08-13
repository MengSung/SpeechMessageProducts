# P7.4 奉獻能力對應與隔離稽核需求

## 目標

只依目前工作樹來源與既有任務證據，判定 `ORG-CALL-00059` 是否應與既有 `ORG-CALL-00041` 去重，並判定 `ORG-CALL-00060` 是否具備安全建立 DTO-only Gateway child 的先決條件。

## 不可違反條件

- 僅能修改本 CCG／Trellis child 與直接 parent 任務紀錄。
- 不得改動 runtime、權威 matrix、feature gate、CE、流量、P7.5、P8 或歷史 Slice C artifacts。
- 00059 的去重不得被描述為 consumer migration、ToolUtility removal、實機 CE 或 cutover。
- 00060 必須在 server-derived immutable authorization scope 缺失時 fail closed；不可把 Session、InMemoryContext、CRM Entity、mutable payment form、browser target 或 Line ID 當成 authorization authority。
- 每一項結論都必須列出可重現的 source／task evidence，以及不得升級的 evidence 類型。

## 驗收條件

- 已在 `audit.md` 記錄 00059 的 legacy caller、typed query／DTO coverage、去重結論與未完成 evidence。
- 已在 `audit.md` 記錄 00060 的 caller-to-CRM trace、mutable state／authorization risk、no-go 與最小恢復條件。
- 已記錄 45 秒雙模型降級狀態。
- 所有 task artifacts 通過 JSON、UTF-8 無 BOM、CRLF、final CRLF、scope 與 `git diff --check` 檢查。

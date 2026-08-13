# P7.4 MemberInfo basic-info consumer boundary Check 紀錄

## 結論

`ORG-CALL-00030` 的 consumer migration 是 **no-go**。這不是 registry、Data8 executor 或 ProductClient
缺少二欄 contact contract；原因是既有 `MemberInfoController.UpdateContactInfo` 在同一個 legacy
`Entity("contact")` mutation 可更新 phone、address、membership status 與 spiritual identity 四欄，
而 typed/Data8 operation 僅允許前兩欄並只對前兩欄 read-back。

若只替換二欄，單一 request 會同時進行 Gateway 和 ToolUtility/SDK 寫入，沒有共同 transaction、deadline、
全欄 read-back/reconciliation、reverse-order cleanup 或 single rollback owner。若 gate=true 改為拒絕或忽略
兩個 OptionSet，則會無聲縮小既有 action 語意。因此維持 `temporary-legacy`、不接線、不開 gate、不操作 CE。

## 證據與範圍

- 權威 matrix：00030 為 registry/executor/ProductClient implemented，但 consumer `not-migrated`，CE/host
  evidence pending。
- runtime source：`MemberInfoController.cs` 的 `UpdateContactInfo` 同時寫入四個欄位。
- typed source：`IPackage02ContactBasicInfoUpdateClient.cs` 只提供 phone/address；
  `Package02Data8ContactBasicInfoWriteOperations.cs` 只允許三個 input scalar 並只 retrieve/read-back 兩欄。
- 本 child 僅修改 Trellis/CCG task artifacts；沒有改動 ChurchReport runtime、settings、feature flag、
  ToolUtility、CRM SDK、CE fixture 或外部資料。
- P7.2 歷史 Slice C 維持封存，沒有重試、讀寫或復用其中任何資產。

## 審查與驗證

- CCG self-healing runner 健康檢查成功；本工作在 45 秒等待上限停止等待，當時不將未完成的外部
  review 視為通過。其後 runner 保存的 artifacts 顯示 Gemini 與 Claude 都完成並提供可用輸出：Gemini
  確認四欄對二欄差異禁止 partial migration；Claude 的唯一 Warning 是本 task 原先把等待逾時誤寫成
  「沒有 backend output」。此紀錄與 parent 已於封存前更正，因此最終 review 沒有未修正的 Critical／Warning。
- context JSONL schema validation：通過。
- source-only 判定：runtime/configuration/CE/fixture mutations = 0。
- task artifacts UTF-8 無 BOM、CRLF-only、final CRLF：通過。
- `git diff --check`：通過。

## 恢復條件

只有新的 P7.2 child 先提供完整四欄、server-authorized、DTO-only fixed write family，並證明 OptionSet
valid-value policy、idempotency、逐欄 read-back/reconciliation、fresh task-owned fixture cleanup、single rollback
owner 與 CE/host/parity evidence，才可重新評估。任何 timeout、ambiguous、partial、read-back mismatch 或
cleanup uncertainty 都停止該 family，絕不重試。

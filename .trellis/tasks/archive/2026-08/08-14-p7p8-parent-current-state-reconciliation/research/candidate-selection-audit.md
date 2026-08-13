# P7.4 候選能力選擇稽核（2026-08-14）

## 範圍與方法

本稽核只讀取 authoritative 70-row matrix、封存 P7.4 task、目前 ChurchReport call chain、
ProductClient 與 Data8 registry。目的是防止重做已封存的 disabled path，或為了加速而把帶有
寫入、副作用、credential/session 或 special-resource ownership 的 legacy consumer 誤接到 typed read。
沒有 CE request、fixture、feature gate、traffic、matrix row rewrite 或 production C# 變更。

## 已完成且不可重做的候選

| Matrix row | Operation | 已有 evidence | 本輪結論 |
| --- | --- | --- | --- |
| ORG-CALL-00005 | `fee.dedication.retrieve.by.contact` | 封存的 server-authorized disabled P7.4 read boundary | 不重做。matrix consumer 仍非 traffic/CE cutover 證據。 |
| ORG-CALL-00066 | `fees.editor.load.by.disciplelesson` | 封存的 DTO-only fee-editor JSON endpoint、A/B、cancellation、immutable result tests | 不重做；不可接入 `FeeList.FeeDataList`、`UpdateFeeData` 或 `SaveBatch`。 |
| ORG-CALL-00014 / 00065 | app-named list catalog reads | 封存 fixed-query registry/Data8/ProductClient local capability | 不把 shared legacy `EntityCollection` consumer 接入；仍 temporary-legacy。 |
| ORG-CALL-00026 / 00028 | MemberInfo present／contact image reads | 封存 disabled local typed response boundaries | 不誤列為 CE、host、traffic 或 ToolUtility-removal evidence。 |

## 仍不可直接接入 P7.4 read child 的候選

| Matrix row／family | 原因 | 要求的下一 family 設計 |
| --- | --- | --- |
| ORG-CALL-00064 fee by dedication period | 付款回傳流程相鄰於 recurring payment writer，不能 read-new/write-legacy 混接 | 獨立 P7.2 payment writer family，具 server authorization、idempotency、exact read-back/reconcile、fresh fixture、cleanup、no-replay。 |
| ORG-CALL-00055 / 00056 authentication contact lookup | typed read 不含 credential，不能替代 legacy account/password 或 LINE session initialization | 獨立 credential-safe authentication/session family；不可 DTO-to-Entity rehydration 或 typed-to-legacy fallback。 |
| ORG-CALL-00063 weekly meeting statistics | `paging-result` special-resource requirement，且 current reporting graph 使用 mutable legacy models | 先建立 P7.3/P7.4 bounded paging DTO、authorization、retention、cancellation、resource baseline 與 no-write-adjacency design。 |
| list membership read/action rows | shared legacy `EntityCollection`、list-management actions／state change 相鄰 | 對 action/write family 另建 full mutation governance，不可偽裝成 read-only consumer。 |
| ORG-CALL-00030 contact basic info | legacy composite 有四個欄位，typed contract 只覆蓋部分欄位 | 先補完整 four-field DTO、OptionSet policy、read-back、reconciliation、idempotency、cleanup 與 rollback。 |

## 選擇結果

截至本輪 source audit，沒有尚未封存、同時符合「DTO-only、server-authorized、無 shared
Entity/EntityCollection bridge、無 write adjacency、無 credential/session ambiguity、無 special-resource
ownership gap」的低風險 P7.4 consumer 候選。

這不是 P7 的終止或 P7.5/P8 的 blocker 新增；它只表示下一工作不得重複 endpoint。最合適的下一個
獨立 child 是先為 `ORG-CALL-00063` 規劃 bounded weekly-meeting paging read family，或為一條 P7.2
write/action family 建立完整治理設計。選定前必須重新完成該 family 的 source audit、PRD、design、
implementation plan 與測試策略。

## 不變量

- 歷史 Slice C 不重試；任何 future CE writer 都是新 family，不能復用歷史 fixture/evidence。
- 所有 checked-in feature gate 仍為 false；沒有 CE／traffic／P7.5／P8 授權。
- matrix 的 `consumer=not-migrated`／`temporary-legacy` 保持權威；local boundary 不會自行改寫它。

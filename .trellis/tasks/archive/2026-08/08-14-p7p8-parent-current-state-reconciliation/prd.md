# P7/P8 Parent 現況校正

## 目標與使用者價值

以目前工作樹、封存 task 與 authoritative 70-row gap matrix 為唯一證據來源，將 P7/P8 parent
文件校正為可安全續作的現況。這可讓後續 agent 不會重做已封存 child、不會把本機 disabled
path 誤宣稱為 consumer cutover／CE evidence，也不會在 P7.5 或 P8 的硬性前置條件尚未成立時提前啟動。

## 已確認事實

1. P3～P6、P7.0、P7.1、P7.2、P7.3 的既有 task 都是唯讀封存證據；P6 Official Worker
   live compatibility 仍為 `evidence-pending`，不阻擋 Data8-first 本機工作。
2. 歷史 P7.2 Slice C 的最後 CE cycle 為 `write-not-committed` no-go 且 exact cleanup 已完成。
   它的 nonce、ledger、fixture、descriptor 與 evidence 均不可重試或復用。
3. `08-14-p72-governed-recurring-payment-return-write-family` 已封存新的 local-only payment
   fresh-fixture control plane；它保持 `CeDispatchAllowed=false` 與 `ProductConsumerAllowed=false`，
   不構成 CE 寫入成功、consumer cutover 或 P7.5/P8 evidence。
4. P7.4 parent 有 15 個封存 child，包含 disabled DTO-only local paths、read-boundary assessments、
   local admission boundary 與 action/write consumer no-go。所有 checked-in feature gate 仍為 false；
   這些成果不自動改變 legacy consumer 的 matrix status。
5. authoritative matrix 仍有 70 個 `temporary-legacy` row、67 個 `consumer=not-migrated` row；
   P7.5 prerequisite report 為 deterministic `no-go`。P7.5 removal 及 P8.0～P8.4 不能建立或啟動。
6. 本輪 source audit 證實 ORG-CALL-00066 已由封存 child
   `08-13-p74-fee-editor-read-boundary` 完成本機 disabled DTO-only endpoint；不得重複建立相同
   endpoint 或將其接入可寫的 `FeeList.FeeDataList`／`SaveBatch` legacy chain。

## 需求

1. 以最小範圍更新 `08-05-gateway-purpose-and-positioning` 的 PRD、design、implement、roadmap 與
   task metadata，使其 current baseline、next action 及 P7.5/P8 gate 與上述事實一致。
2. 明確區分下列證據強度，不得互相升格：registry／executor／ProductClient、本機 disabled boundary、
   legacy consumer migrated-disabled、CE 8.2/9.1、Embedded/Dedicated/Central host、traffic cutover。
3. 在文件中記錄本輪可安全候選盤點：已具 typed read 的候選若已封存則不得重做；若仍與
   `EntityCollection`、credential/session、special resource、payment 或其他 write family 相鄰，則維持
   temporary-legacy，必須先另立對應 family design。
4. 下一步必須是 matrix-backed、可獨立驗證的 capability-family planning／implementation child；它必須
   有 bounded DTO、server authorization、隔離、生命周期、rollback owner 與相稱測試。不得為了進度
   而啟用 gate、發 CE request、切流、移除 ToolUtility 或建立 P8。
5. 執行限時 CCG dual-model 文件審查：每個 backend 最多等待 45 秒。若未完成，只能記錄
   「雙模型未完成」並改採本機 evidence；不得反覆等待或將降級結果稱為完整雙模型審查。

## 驗收條件

- [ ] parent 文件清楚標示已完成／封存的 P3～P7.3 baseline、歷史 Slice C non-replay，以及 P7.2
      新 local-only payment control plane 的證據界線。
- [ ] parent 文件列出 P7.4 的實際進度，但不將 disabled local endpoint 或 local-only contract
      誤寫成 consumer／CE／traffic migration 完成。
- [ ] parent next action 指向下一個 matrix-backed、獨立 capability family；不重做 ORG-CALL-00066 或
      其他已封存 child。
- [ ] P7.5 的 70 temporary-legacy／zero-reference／CE-host-parity-soak-drain-rollback prerequisites 與
      P8 的 immutable-handoff／外部 deployment prerequisites 均仍為 no-go。
- [ ] 未修改任何 production C#、appsettings feature gate、CE／fixture／traffic、P7.5 removal 或 P8
      deployment material；git diff 僅包含 task-owned documentation、metadata 與 review records。
- [ ] task artifacts、targeted documentation validation、encoding/line-ending checks、`git diff --check` 及
      限時 CCG review 結果都持久化記錄。

## 明確排除

- 不重新開啟或修改封存的 P3～P7.3 task，不重試歷史 P7.2 Slice C。
- 不改 authoritative matrix 的 row status；它是 baseline，任何 consumer state 升級都必須由其 own
  child 的 direct evidence 支持。
- 不修改 product code、CRM schema、fixture、CE data、secret、identity、feature flag、traffic、P7.5 或 P8。

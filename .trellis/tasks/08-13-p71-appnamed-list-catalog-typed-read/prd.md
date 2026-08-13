# P7.1 App-named 名單目錄強型別讀取能力

## 目標

完成權威 matrix 的 `ORG-CALL-00014`：建立一項零 caller parameter、有界、server-owned、DTO-only 的
`list.catalog.retrieve.app.named` Data8 / ProductClient 讀取能力。它提供未來受權 ChurchReport consumer
可以使用的 app-named 小組名單 catalog 資料層，但本 child 絕不切換 consumer、絕不開啟 feature gate、
絕不對 CE 發送 request、絕不進行 ToolUtility removal、P7.5 或 P8。

## 已確認事實與邊界

- `ORG-CALL-00014` 的唯一 legacy source 是 `ToolUtility/ListOperations/ListService.RetrieveLists()`。
  靜態 FetchXML 只投影 list 的 `listname`、`createdfromcode`、`lastusedon`、`purpose`、`listid`，且固定
  `statuscode=0`、`purpose=小組名單`、`new_app_named=1`，以 `listname` descending 排序。
- `ORG-CALL-00014` 沒有 typed parameter。產品不可傳入 FetchXML、QueryExpression、entity/attribute 名稱、
  sort、filter、list ID、owner、profile、connector、endpoint、credential、CE version 或 cache key。
- `ORG-CALL-00065` 是不同 operation/template：其 filter 額外排除測試名、投影 leader lookup，現有
  ChurchReport consumer 亦保留 shared `EntityCollection` cache。它不在此 child，不能共用 operation ID、
  response branch 或 consumer cutover。
- 歷史 P7.2 Slice C 是 immutable `write-not-committed` no-go；不得讀取、修改或復用其 nonce、ledger、
  fixture、descriptor 或資料。P7.5 prerequisite report 仍為 deterministic no-go，P8 未建立。

## 需求

1. 新增固定 operation ID `list.catalog.retrieve.app.named`、固定 `fetchxml` template ID
   `list.catalog.appnamed.v1`、固定 response discriminator，並使原始 Phase 0 matrix、schema 與 compiled
   registry 一致。registry 參數必須為空，並維持四頁、單頁 64 KiB、累積 256 KiB、4096 rows 的既有安全上限。
2. Data8 connector 必須使用單一固定的 `QueryExpression` 讀取 list，固定 ColumnSet、filters、排序與
   PageInfo；所有 CRM `Entity`、paging cookie 與 query 只存在 lease scope，結果僅投影為 immutable wire record。
   任一 null page、錯 entity/ID、缺少必填 list ID、超頁/超列/超 byte、型別不符或 pagination contract 不符，
   都必須 fail closed 且不得發布 partial records。
3. wire record 與 ProductClient DTO 只可公開 list ID、名稱、created-from option value、last-used UTC time 與
   purpose scalar。不得公開 `Entity`、`EntityCollection`、lookup graph、raw formatted-value dictionary、
   OData annotation、Query、endpoint、credential、cookie 或原始 exception。
4. ProductClient interface 只能接收 deployment-owned `profileAlias`、`workloadSubjectId` 和原樣傳遞的
   `CancellationToken`；它必須驗證 response operation ID、discriminator 和唯一 branch，再防禦性複製成
   request-local readonly DTO collection。不得 cache、retry、fallback、DTO-to-Entity rehydration 或保存
   profile/workload/list/response/transport state。
5. executor 需在 pool/connector allocation 前拒絕非空 parameter map 或不支援 operation，並維持既有
   cancellation、timeout、fault eviction、lease/permit release 生命週期。不能新增 global mutable cache、
   timer、background work、同步阻塞或額外 connector。
6. 以 TDD 建立 registry/matrix agreement、wire union、Data8 query/projection、ProductClient mapping、
   cancellation、wrong operation/branch、source-mutation 與 A/B interleaving tests。測試僅使用 fake executor
   或 fake Data8 service，不能連線或呼叫真實 CE。
7. 每個新增或實質修改的 C# 檔必須有完整繁體中文 XML/lifecycle 文件，UTF-8 without BOM、CRLF、final CRLF。
   child 結案前須通過 targeted tests、Dynamics Release tests、solution Release build、encoding/line ending、
   `git diff --check`、scope/forbidden-pattern scan 與限時 CCG review。

## 非目標

- 不改動任何 ChurchReport caller、shared cache、Controller、WebServiceConnector 或 ToolUtility legacy path。
- 不把 `ORG-CALL-00065`、`00015`～`00020`、membership action 或任何 write/action/function 合併進此 read。
- 不執行 CE 8.2/9.1、Official Worker、fixture、write、traffic、feature enablement、P7.4 cutover、P7.5、P8、
  push 或 PR。

## 驗收標準

- [ ] `ORG-CALL-00014` 在 matrix、registry、Data8 executor、closed response envelope 與 ProductClient
      均是同一固定 operation/template/bounded policy，且零 caller parameter 得到測試證明。
- [ ] Data8 path 只做有界 `RetrieveMultiple`，不將 `Entity`／`EntityCollection`／paging cookie 跨出 connector，
      不做 N+1 `Retrieve`；ProductClient 不公開 CRM/OData/connection/credential/response stream。
- [ ] null/錯 branch/錯 operation/未知 parameter/cancel/fault/超限與 source mutation 均 fail closed；A/B
      interleaving 證明沒有跨 profile/workload/request 的 record 或 DTO reuse。
- [ ] 不修改 consumer 或 feature flag，沒有 CE request/mutation、fixture、traffic、P7.5 或 P8；matrix 只如實
      記錄 registry/executor/client 的 local completion，不能升格為 consumer/CE/host evidence。
- [ ] Trellis/CCG planning、check、scope-only commit 與 archive 完整，並記錄外部 review 在 45 秒內未完成。

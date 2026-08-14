# P7.4 點名名單成員唯讀資料平面

## 目標與使用者價值

為 authoritative matrix 的 `ORG-CALL-00057`／
`list.membership.retrieve.appnamed.by.contact` 新增可重複驗證的本機資料平面：以固定 CE 9.1/Data8 query
將指定 contact 的啟用點名名單投影為有界、不可變、DTO-only 結果。這是日後安全取代 ToolUtility
`EntityCollection` 的必要基礎，但本 child 不會把任何既有 ChurchReport consumer 接至新能力。

## 已確認事實

- Legacy `RelationshipQueryService.QueryListOfContactManyToMany` 對 `list`／`listmember` 執行 `AllColumns`
  查詢，僅固定 `new_app_named=true`、`statecode=0` 與 contact relationship。
- `ContactService.GetContactCurrentGroup`、`NewPerson.DoesContactAlreadyInASmallGroup` 與
  `DownloadListManager` 都將這個 read 混入登入、建立或轉移成員、出席、週報或通知等 legacy flow；它們
  沒有可直接重用的 immutable server authorization scope。本 child 不得修改它們。
- 既有 `ORG-CALL-00052` current-group child 已封存 source-only no-go；本 child 不可將它的 first-match
  行為、mutable `Entity` input 或 write-adjacent caller 視為 consumer migration。
- Historical P7.2 Slice C 已 `write-not-committed` no-go 且 exact cleanup；舊 cycle 永不可 replay。P7.5
  prerequisite 仍為 no-go，P8 尚未開始。

## 需求

1. 定義獨立 operation ID、compiled registry definition、固定 `QueryExpression`、closed response branch、
   wire record、ProductClient DTO/interface/client 與 DI registration。它們只允許 server/deployment
   composition 提供的 profile/workload，並只接受已在上游 server authorization 後取得的 non-empty contact GUID。
2. 固定投影只能含 `listid` 與 nullable `listname`；固定 active/app-named filters、contact relationship、
   deterministic sort、單頁 row/byte bounds 與 duplicate-ID rejection 都由 registry/connector 擁有。不得
   接受 FetchXML、filter、sort、paging cookie、endpoint、credential、connector、owner 或 raw CRM object。
3. Data8 connector 必須在 lease/request scope 內將 CRM `Entity` 投影成 immutable scalar record；超限、
   MoreRecords、malformed row、duplicate、fault、timeout 或 cancellation 都不得發佈 partial collection 或
   reuse uncertain transport。
4. ProductClient 必須在 outbound I/O 前驗證 routing/contact input，精確驗證 response operation/kind/branch，
   defensive-copy 每一筆 DTO 並發佈不可寫 collection；不可 cache、retry、fallback、留存 request state 或
   rehydrate `Entity`。
5. 本 child 僅提供 default-disabled local capability evidence。不得修改 ChurchReport/ToolUtility runtime
   consumer、feature setting、traffic、CE fixture/mutation、P7.5、P8 或 immutable archived matrix。

## 驗收條件

- [ ] registry、wire branch、executor、ProductClient 與 DI 對 `ORG-CALL-00057` 具有獨立 operation/template/
      response kind，且不重用 current-group 或 app-named catalog 的語意。
- [ ] fixed query/projection/order/bounds、invalid input zero-I/O、malformed response、duplicate、more-page、
      cancellation/fault、source mutation 與 A/B profile/workload isolation 有 targeted RED/GREEN tests。
- [ ] 新能力沒有 ChurchReport consumer、ToolUtility bridge、session/cache、feature gate、CE request 或資料變更；
      既有 legacy routes 保持原樣。
- [ ] 完成 Trellis／CCG task records、bounded dual-model analysis/review（或「雙模型未完成」降級紀錄）、
      targeted tests、full solution Release tests/build、encoding/CRLF、`git diff --check`、scope-only commit/archive。

## 非目標

- 不建立 browser endpoint、server authorization resolver、consumer cutover、shadow read、legacy fallback、
  dual-write 或 current-group first-match API。
- 不執行 CE、fixture、feature enablement、traffic switch、P7.5 ToolUtility removal 或 P8 deployment。

# P7.3 ChurchReport 特殊資源能力遷移：品質檢查

## 範圍

審查僅涵蓋 ORG-CALL-00028、ORG-CALL-00029、ORG-CALL-00034、ORG-CALL-00040 與
ORG-CALL-00063 的本機 typed capability contract。沒有執行 CE mutation、fixture、feature flag、
ChurchReport traffic switch、Official Worker、P7.4/P7.5/P8 或雲端操作。

## 跨層資料流結論

`ProductClient request → IDynamicsOperationExecutor → Data8ProfileOperationExecutor → generation-owned
lease → fixed Data8 projection → OperationResponseData closed union → ProductClient result` 已維持封閉。
ProductClient、Gateway normalizer 與 Abstractions 不傳遞 CRM SDK type、`Entity`、raw stream、
`FetchXML`、paging cookie、endpoint、credential、token、raw exception 或 raw response。

metadata cache 僅保存 immutable option pure values，key 含 server-resolved profile/generation/target/locale，
無法證實 locale 時不快取。weekly paging cookie 與 projected page 均只存在一次 connector lease；所有
取消、未成功 connector result、無效 response 與 paging failure 均令 lease faulted 或由例外 fault/dispose，
不回傳 partial response。

## 已修正的 release-blocker

`Data8ProfileOperationExecutor` 曾在 `ConnectorOperationResult.Succeeded=false` 時直接回傳 bounded
failure，沒有標記 lease faulted。這不能證明 Data8/WCF transport 或 session 可安全重用。已以
`Execute_async_evicts_client_when_special_resource_connector_reports_unsuccessful_result` 建立故障注入，
證實修正後 client 在 request 結束前 dispose、permit exactly-once release，且不可回到 idle pool。

## 證據

- `dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore`：通過。
- `dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore`：0 warnings、0 errors。
- `.cs` byte-level validation：23 個 task-owned 檔案皆 UTF-8 無 BOM、CRLF-only、final CRLF。
- `git diff --check`：通過。
- CCG external reviewer：在核准 45 秒上限內未產生可用 backend output；記錄為「雙模型未完成」，
  不視為雙模型審查通過。

## 結論

P7.3 的本機 contract／lifecycle／quality gate 已完成；CE evidence、consumer migration、feature-gate
enablement、ToolUtility removal 與任何 deployment evidence 均仍未完成，必須由後續獨立 task 取得。

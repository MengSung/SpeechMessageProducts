# P7.1 App-named 小組名單目錄強型別讀取檢查紀錄

## 本機能力與邊界

`ORG-CALL-00065`／`list.catalog.retrieve.appnamed.smallgroups` 已完成獨立的 registry、Phase 0
matrix/schema、Data8 固定 `QueryExpression`、closed response union、immutable ProductClient DTO 與 DI。
固定查詢只投影 list ID、名稱、created-from option、UTC last-used、purpose、race leader 與 family leader
contact GUID；條件固定為 active、`purpose = 小組名單`、`new_app_named = true` 與名稱不含既有「已退出」
pattern，排序固定為名稱遞減、list ID 遞增。

此 operation 不接受 caller parameter。參數非空時，executor 在 profile router、pool、connector 或 CRM I/O
之前回傳 `operation.invalid-parameters`。Data8 對 null page、`MoreRecords`、paging cookie、result/page/byte
超限、identity/type/UTC/schema/leader lookup 漂移皆 fail closed；未通過所有 row 驗證前不會發佈 partial response。
leader lookup 在 connector scope 立即縮減為 nullable contact GUID，不保留 `EntityReference.Name`、CRM entity、
formatted values、cookie、profile、session、cache 或 transport state。

ProductClient 僅接受已由 deployment/server 擁有的 profile 與 workload，建立 request-local immutable DTO
snapshot。它不建立 cache、retry、fallback、timer、subscription、background work、Entity rehydration 或第二條
transport；connector、lease、permit、fault eviction 與 cancellation cleanup 仍由 executor/pool 的既有單一 owner 負責。
A/B interleaving、source mutation、invalid routing zero-I/O、wrong operation/kind/branch、cancellation 與 published
collection immutability 都有 focused regression tests。

## 新鮮品質證據

| 檢查 | 結果 |
| --- | --- |
| `AppNamedSmallGroupListCatalog` focused suite | 20 passed、0 failed |
| Dynamics Release suite | 810 passed、7 個既有受控 live-SQL skips、0 failed |
| Solution Release suite | Dynamics 810 passed／7 skips；ChurchReport 617 passed／14 個既有受控 live-environment skips；0 failed |
| `dotnet build SpeechMessageProducts.sln -c Release --no-restore` | 0 warnings、0 errors |
| Trellis context validation | `implement.jsonl` 6 entries、`check.jsonl` 5 entries，通過 |
| 編碼與 scope | 15 個 changed/new C# 均為 UTF-8 無 BOM、CRLF-only、final CRLF；`git diff --check` 通過；目標 capability 沒有接入 ChurchReport legacy shared consumer 或 ToolUtility consumer |

## 如實保留的未完成證據

權威 matrix 只將本 row 的 registry、Data8 executor、ProductClient 標為本機 `declared`／`implemented`，並指定
P7.4 capability owner 作為未來 rollback/rollout owner。consumer 仍是 `not-migrated`，CE 8.2／9.1 與
Embedded／Dedicated evidence 仍為 `evidence-pending`，`temporaryLegacy` 仍存在，`p75RemovalBlocker` 仍為
`consumer-not-migrated`。本 child 沒有執行 CE request、fixture、feature enablement、traffic cutover、P7.5
ToolUtility removal 或 P8 Central Gateway deployment。

## 雙模型狀態

CCG self-healing reviewer 依授權以 45 秒界限啟動；Gemini 在期限內 timeout，Claude 為 provider session-limit，沒有 accepted usable output。
此結果記錄為「雙模型未完成」，不將本機完整品質證據表述為完整雙模型審查。Gemini timeout 時留下的未完成草稿未被採納為 completed reviewer result；僅在本機重新核對後確認它沒有提出 Critical／Warning finding。

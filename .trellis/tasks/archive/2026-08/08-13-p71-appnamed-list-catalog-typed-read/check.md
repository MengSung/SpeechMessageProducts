# P7.1 App-named 名單目錄型別化讀取檢查紀錄

## 完成本機能力

`ORG-CALL-00014`／`list.catalog.retrieve.app.named` 已完成 registry、Phase 0 matrix/schema、Data8 fixed
`QueryExpression`、closed response union、immutable ProductClient DTO／DI 與測試。查詢固定為 active
`list.statuscode = 0`、`purpose = 小組名單`、`new_app_named = true`、`listname` descending、`listid` ascending；
它不接受 caller parameter、entity、filter、order、owner、profile、connector、endpoint 或 credential routing。

Data8 僅在現有 lease scope 內執行 bounded `RetrieveMultiple` 並立即投影純 scalar wire record。null page、entity
identity/type、UTC date、paging cookie、row/page/byte budget 與 response branch 不符都 fail closed，不會發布
partial result。ProductClient 以 request-local defensive copies 發布不可變 DTO；A/B interleaving、source mutation、
invalid routing zero-I/O、cancellation、wrong operation/branch 與 published collection immutability 均有回歸測試。

## 品質證據

| 檢查 | 結果 |
| --- | --- |
| focused query/registry/Data8 tests | 98 passed、0 failed |
| Dynamics Release tests | 786 passed、7 controlled live-SQL skips、0 failed |
| solution Release tests | exit code 0；所有 live-environment skips 依既有 gating 維持 skip |
| solution Release build（non-incremental） | 0 warnings、0 errors |
| rebaseline tests | 13 passed |
| authoritative matrix validator | `outcome=valid` |
| task context validation | `implement.jsonl` 6 entries、`check.jsonl` 5 entries，通過 |
| 編碼／diff | changed C# UTF-8 無 BOM、CRLF-only、final CRLF；`git diff --check` 通過 |

## 狀態邊界

權威 matrix 如實記錄 `ORG-CALL-00014` 為 registry／Data8 executor／ProductClient `implemented`，但 consumer
仍是 `not-migrated`，CE 8.2／9.1 與 Embedded／Dedicated evidence 都是 `evidence-pending`，`temporaryLegacy`
仍存在。此 local completion 不啟用 feature gate、不產生 CE evidence、不切換 ChurchReport traffic、不移除
ToolUtility，亦不開始 P7.5 或 P8。

## 雙模型狀態

final reviewer 依授權僅等待 45 秒。Gemini 沒有產生 usable reviewer output，Claude 在期限內未完成並已停止；
記錄為「雙模型未完成」。本機檢查完整通過，但不得將此降級狀態描述為完整雙模型審查。

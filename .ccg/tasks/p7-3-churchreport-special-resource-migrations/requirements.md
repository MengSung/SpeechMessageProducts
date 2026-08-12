# P7.3 ChurchReport 特殊資源能力遷移：需求

## 目標

將權威矩陣中五個特殊資源呼叫改造成可由 `Embedded + Data8` 與
`DedicatedGateway + Data8` 共用的 bounded typed capability contract；本任務只交付
本機實作、測試與明確 evidence-pending 記錄，不執行 CE 寫入、產品切換或雲端部署。

## 權威範圍

| Matrix row | Capability | 類別 |
| --- | --- | --- |
| ORG-CALL-00028 | `memberinfo.contact.retrieve.image` | image stream read |
| ORG-CALL-00029 | `memberinfo.contact.update.image` | image write |
| ORG-CALL-00034 | `newperson.contact.update.image` | image write |
| ORG-CALL-00040 | `metadata.optionset.retrieve.by.attribute` | metadata cache |
| ORG-CALL-00063 | `stats.meeting.retrieve.by.sunday` | bounded paging result |

## 不可變條件

- 產品、ProductClient、Gateway 與一般測試不得傳遞或保存 CRM SDK type、
  `IOrganizationService`、raw stream、FetchXML、endpoint、credential、token、cookie、
  raw exception、raw response 或 caller-selected entity/schema。
- Image payload 必須有 input/output byte、格式、像素與 dimension hard limit；每一份
  request／response byte array 必須 defensive copy；stream、image decoder、buffer 與
  connector lease 皆有唯一 owner 與取消／fault 後確定釋放。
- Metadata cache 必須以 server-derived `(ProfileAlias, GenerationId)` 隔離、具 bounded
  size／TTL／invalidation／eviction，且只保存 bounded immutable option value/label DTO。
- Weekly statistic 必須以固定 server-owned query、固定 page／result／cumulative-byte
  上限讀取；page cookie 只活在單一 connector lease/request scope；任何 page failure、
  over-limit 或 cancellation 不得回傳 partial success。
- 寫入 capability 僅實作 local contract；任何未來 CE 寫入都要另外擁有新 ledger、
  task-owned fixture、preflight、read-back、reconcile、cleanup 與 no-retry rule。
- 所有修改的 C# 使用完整繁體中文文件、UTF-8 無 BOM、CRLF、final CRLF。

## 驗收

- 五個 operation 有固定 Operation ID、registry policy、bounded typed request／response
  discriminator、Data8 executor dispatch、ProductClient 與 focused local tests。
- Tests 明確驗證 image defensive-copy/size/format/dimension/cancellation/cleanup、metadata
  isolation/TTL/eviction/no raw SDK retention、paging limit/cookie lifetime/no partial success、
  executor fail-closed 及 ProductClient response discriminator 驗證。
- 不改動既有 ChurchReport consumer 的 feature gate 或 legacy runtime path；matrix status
  與 task record 必須仍標示 consumer-not-migrated／CE evidence-pending。
- 完成 targeted tests、完整 Release solution tests/build、encoding/CRLF、`git diff --check`
  與 scope-only review；通過後才可提交、封存 P7.3，並由後續 gate 決定能否建立 P7.4。

# P7.3 ChurchReport 特殊資源能力遷移

## 目標與使用者價值

將權威 70-row gap matrix 中尚未具備受控資源界限的五個 ChurchReport 特殊資源能力，
實作成可供 `Embedded + Data8` 與 `DedicatedGateway + Data8` 共用的本機 typed capability。
成果必須讓後續 P7.4 能逐 capability 遷移 consumer，而不把影像串流、CRM SDK 型別、
metadata graph、FetchXML、paging cookie 或可變 session 狀態帶進產品邊界。

## 已確認事實

- P3、P4、P5、P6、P7.0、P7.1 與 P7.2 均為唯讀封存前置；不得重做或變更其 task。
- P7.2 Slice C 的最後 CE cycle 為 `write-not-committed / no-go-closed`，已完成 cleanup；
  本任務不重試該 cycle，也不復用任何 historical nonce、ledger、fixture 或 descriptor。
- authoritative matrix 指定本 task 唯一的五列：
  `ORG-CALL-00028`、`ORG-CALL-00029`、`ORG-CALL-00034`、`ORG-CALL-00040`、
  `ORG-CALL-00063`。
- 目前 metadata operation 雖已在 registry 宣告，但 response 為 `Unsupported`；其餘四項尚未
  宣告完整 registry/executor/ProductClient contract。上述事實不表示 consumer 已遷移或具備 CE evidence。
- 現有 Gateway request body 預設為 64 KiB；P7.3 的影像 wire contract 必須低於此 deployment
  cap，且同時維持 code hard limit。legacy 5 MiB upload 行為不構成本 task 的 Gateway contract 授權。

## 功能需求

1. 為下列固定 operation 建立 registry policy、封閉 request/response discriminator、Data8
   executor dispatch 與 typed ProductClient：
   - `memberinfo.contact.retrieve.image`
   - `memberinfo.contact.update.image`
   - `newperson.contact.update.image`
   - `metadata.optionset.retrieve.by.attribute`
   - `stats.meeting.retrieve.by.sunday`
2. 影像讀取與寫入只能處理固定 `contact.entityimage` capability。影像輸入、輸出、格式、像素、
   dimension 與 payload bytes 都必須有 hard limit；不得跨 ProductClient、Gateway 或 connector
   傳遞 live `Stream`、`IFormFile`、`JsonElement`、SDK `Entity` 或 caller-selected schema。
3. 寫入成功僅能在固定 image update 已完成 read-back 並確實相符時回傳；timeout、取消、
   fault、讀回不符或清理不確定都必須 fail closed，不能自動重送或宣稱成功。
4. metadata 僅能讀取 server allowlist 的 entity/attribute 組合，並回傳有界、不可變的
   `(value, label, configuredOrder)` 純值序列。任何可共享 cache 必須完整以 server-resolved
   `(ProfileAlias, GenerationId, entity, attribute, locale)` 分區，具有上限、TTL、eviction 和
   generation retirement；不得保留 SDK metadata、例外、request 或 identity。
5. weekly meeting statistics 只能使用 server-owned 的固定 query 與 projection；page count、
   page bytes、cumulative bytes、result rows 都必須有限。cookie 只活在單次 connector request，
   取消、缺 cookie、page overflow、schema 不符或任一頁失敗時不得回傳 partial success。
6. 所有新增或變更的 `.cs` 檔案必須使用完整維護得當的繁體中文文件、UTF-8 無 BOM、CRLF
   與 final CRLF；不得引入跨使用者、跨 profile、跨 generation 或跨 product 的 session、
   cache、buffer、stream、lease 或 resource leakage。

## 非功能與安全需求

- 每一個 ProductClient 都是 stateless singleton，只能保存 DI-owned executor/logger/明確分區的
  immutable cache；不得保存 request、image bytes、paging cookie、CRM Entity、credential、token、
  connector lease、timer、CTS 或 background task。
- 所有請求必須於取得 connector lease 前完成 allowlist、型別、長度、idempotency 與 bounded-size
  驗證；connector 回應 discriminator 或 projection 不符時必須 fault client，避免未知 session 回池。
- 本 task 的寫入僅交付本機 code/test contract。不得進行 CE mutation、feature flag 變更、
  ChurchReport 流量切換、CE 8.2 操作、Official Worker 操作或 P7.4/P7.5/P8 work。
- 雙模型結果為「雙模型未完成」：前次依限時規則的 architect run 沒有取得可用輸出，後續採
  本機審查與驗證；不得將此狀態描述成完整雙模型審查。

## 驗收標準

- [ ] 五個 operation 都有固定 ID、registry 定義、有限 policy、response discriminator、
  executor allowlist/dispatch、connector projection 與 typed ProductClient。
- [ ] focused tests 證明 image defensive copy、wire/format/dimension/pixel limit、取消/fault cleanup；
  metadata cache 的 profile/generation isolation、TTL/eviction；以及 meeting paging 的 cookie
  scope、size/page/result limit 和 no-partial-success。
- [ ] focused tests 證明 executor 在取得 lease 前拒絕非 allowlisted request，且對 response
  operation ID、CE version、discriminator 和 branch 進行 fail-closed 驗證。
- [ ] 所有既有 ChurchReport consumer、legacy runtime path 與 feature gate 維持不變；matrix/task
  evidence 明確標示 `consumer-not-migrated` 與 `CE-evidence-pending`。
- [ ] 通過相稱 targeted tests、完整 Release tests/build、encoding/CRLF、`git diff --check`、
  scope review 與 isolation/lifecycle 檢查後，才可 scope-only commit 與 archive。

## 不在範圍內

- 變更 ChurchReport Controller、Service、ToolUtility 或既有 feature gate。
- CE live mutation/read-back evidence、production/Lenovo traffic cutover、Central Gateway deployment。
- 任何 generic CRUD、caller-selected entity/field/query、raw metadata/FetchXML/SDK object transport。

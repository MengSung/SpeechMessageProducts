# P7.4 認證聯絡人唯讀安全邊界

## 目標

為 authoritative matrix 的 `ORG-CALL-00055`（帳號登入聯絡人查詢）與
`ORG-CALL-00056`（LINE 使用者識別碼聯絡人查詢）建立可獨立驗收的
ProductClient/Data8 typed read boundary。此 child 只提供 disabled-by-default、
local-only、DTO-only 的安全契約與本機測試，不改變既有登入流程、不執行 CE、
不啟用 feature gate，也不宣稱 P7.5 或 P8 已完成。

## 已確認事實

- 兩個 matrix row 的 registry、Data8 executor 與 ProductClient 目前皆為
  `not-implemented`，CE 8.2/9.1 與 Embedded/Dedicated evidence 皆為
  `evidence-pending`。
- 舊路徑由 `ToolUtilityClass` 查詢 contact；帳號路徑還會在程序記憶體中比較
  `new_app_pass` 明文，屬 credential-or-secret 高風險資料。
- LINE 路徑是以 `new_lineid` 與 active `statecode` 查詢 contact，屬個人資料。
- 現有 ChurchReport 登入與 QR/付款流程廣泛使用 SDK `Entity` 與可變模型；本 child
  不得直接接回這些流程，避免把新 DTO 契約偽裝成完整 cutover。

## 功能需求

1. 宣告兩個 server-owned operation ID 與固定查詢模板；caller 不得選擇 profile、
   connector、endpoint、organization、credential 或任意 FetchXML。
2. 建立 immutable wire record、Data8 DTO projection、typed ProductClient API，
   只允許回傳非敏感的 contact locator/display 欄位；絕不回傳密碼、password hash、
   token、cookie、原始 CRM Entity 或原始例外。
3. 帳號登入查詢必須將帳號與密碼驗證邊界拆開：查詢輸入只作 server-side lookup，
   密碼比對不得由 browser 或 ProductClient 以明文回傳；若現有 schema 無法在不輸出
   secret 的情況下完成驗證，typed boundary 必須 fail closed。
4. LINE 查詢必須固定 `new_lineid` + active contact 條件，對空白、格式不符、
   多筆或無結果採固定去識別化分類；不得從多筆中猜選一筆。
5. 所有 API 均 async、支援 cancellation，request-local、不使用 static/session
   可變共享狀態，不把 credential 或 contact DTO 放入共用 cache。
6. 既有 gate 預設 false；false 時不得建立 client/pool/handler、解析 host、
   發出 CE I/O 或呼叫 legacy fallback。此 child 不得變更正式設定。

## 非目標

- 不修改既有 AuthenticationController、WeeklyReportManager、QR code、付款或
  onboarding 的 legacy consumer 行為。
- 不執行 CE 8.2/9.1 request、Create/Update/Assign/Delete/Associate/Disassociate、
  fixture、ledger、traffic switch、Official Worker 或 Central Gateway 操作。
- 不移除 ToolUtility；不啟動 P7.5 或 P8；不創建 P9/P10。

## 驗收條件

- [ ] PRD、design、implement 與 task record 完成並符合 Trellis。
- [ ] Registry、wire、Data8 executor、ProductClient 與 disabled bootstrap 的
      focused tests 通過；涵蓋空白/多筆/secret-redaction/cancellation/A-B isolation。
- [ ] source contract test 證明 false gate 先於 profile/client/handler/CE I/O，且
      沒有 legacy fallback、sync-over-async 或 raw `Entity` 洩漏。
- [ ] 受影響 solution tests、Release build、UTF-8 no BOM/CRLF/final CRLF、
      `git diff --check` 與 scope check 通過。
- [ ] 任務紀錄明確標示：這是 local-only evidence；CE/host parity 尚待後續 owner task。
# P7.4 認證聯絡人唯讀安全邊界

## 目標

為 authoritative matrix 的 `ORG-CALL-00055`（帳號登入聯絡人查詢）與
`ORG-CALL-00056`（LINE 使用者識別碼聯絡人查詢）建立可獨立驗收的
ProductClient/Data8 typed read boundary。此 child 只提供 disabled-by-default、
local-only、DTO-only 的安全契約與本機測試，不改變既有登入流程、不執行 CE、
不啟用 feature gate，也不宣稱 P7.5 或 P8 已完成。

## 已確認事實

- 兩個 matrix row 的 registry、Data8 executor 與 ProductClient 目前皆為
  `not-implemented`，CE 8.2/9.1 與 Embedded/Dedicated evidence 皆為
  `evidence-pending`。
- 舊路徑由 `ToolUtilityClass` 查詢 contact；帳號路徑還會在程序記憶體中比較
  `new_app_pass` 明文，屬 credential-or-secret 高風險資料。
- LINE 路徑是以 `new_lineid` 與 active `statecode` 查詢 contact，屬個人資料。
- 現有 ChurchReport 登入與 QR/付款流程廣泛使用 SDK `Entity` 與可變模型；本 child
  不得直接接回這些流程，避免把新 DTO 契約偽裝成完整 cutover。

## 功能需求

1. 宣告兩個 server-owned operation ID 與固定查詢模板；caller 不得選擇 profile、
   connector、endpoint、organization、credential 或任意 FetchXML。
2. 建立 immutable wire record、Data8 DTO projection、typed ProductClient API，
   只允許回傳非敏感的 contact locator/display 欄位；絕不回傳密碼、password hash、
   token、cookie、原始 CRM Entity 或原始例外。
3. 帳號登入查詢必須將帳號與密碼驗證邊界拆開：查詢輸入只作 server-side lookup，
   密碼比對不得由 browser 或 ProductClient 以明文回傳；若現有 schema 無法在不輸出
   secret 的情況下完成驗證，typed boundary 必須 fail closed。
4. LINE 查詢必須固定 `new_lineid` + active contact 條件，對空白、格式不符、
   多筆或無結果採固定去識別化分類；不得從多筆中猜選一筆。
5. 所有 API 均 async、支援 cancellation，request-local、不使用 static/session
   可變共享狀態，不把 credential 或 contact DTO 放入共用 cache。
6. 既有 gate 預設 false；false 時不得建立 client/pool/handler、解析 host、
   發出 CE I/O 或呼叫 legacy fallback。此 child 不得變更正式設定。

## 非目標

- 不修改既有 AuthenticationController、WeeklyReportManager、QR code、付款或
  onboarding 的 legacy consumer 行為。
- 不執行 CE 8.2/9.1 request、Create/Update/Assign/Delete/Associate/Disassociate、
  fixture、ledger、traffic switch、Official Worker 或 Central Gateway 操作。
- 不移除 ToolUtility；不啟動 P7.5 或 P8；不創建 P9/P10。

## 驗收條件

- [ ] PRD、design、implement 與 task record 完成並符合 Trellis。
- [ ] Registry、wire、Data8 executor、ProductClient 與 disabled bootstrap 的
      focused tests 通過；涵蓋空白/多筆/secret-redaction/cancellation/A-B isolation。
- [ ] source contract test 證明 false gate 先於 profile/client/handler/CE I/O，且
      沒有 legacy fallback、sync-over-async 或 raw `Entity` 洩漏。
- [ ] 受影響 solution tests、Release build、UTF-8 no BOM/CRLF/final CRLF、
      `git diff --check` 與 scope check 通過。
- [ ] 任務紀錄明確標示：這是 local-only evidence；CE/host parity 尚待後續 owner task。

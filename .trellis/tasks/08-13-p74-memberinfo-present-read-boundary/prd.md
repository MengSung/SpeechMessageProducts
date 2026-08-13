# P7.4 MemberInfo present-record typed read boundary

## Goal

將 ORG-CALL-00026 以 server-authorized、disabled-by-default、DTO-only ProductClient boundary 遷移；僅本機實作與驗證，無 CE、切流、P7.5 或 P8 操作。

## 已確認事實

- 權威 matrix 將 `ORG-CALL-00026` 定義為 `churchreport.attendance` 的唯讀
  `memberinfo.present.retrieve.by.contact`。目前 registry、Data8 executor、ProductClient 與 consumer
  均未實作，且該 row 仍是 `temporary-legacy`、CE 8.2/9.1 與 Embedded/Dedicated 都是
  `evidence-pending`。
- 既有 `MemberInfoController.LoadContactPresentRecords` 在 `EnsureCorrectUserData()` 後，先以
  `CanViewContact(contactGuid)` 授權，再由 ToolUtility/CRM SDK 讀取 contact fullname，並以固定
  `new_present_record` projection、contact lookup filter 與 `new_sunday_date desc` 排序建立
  `ContactPresentRecordRow`。
- Browser 的 `contactId` 僅能作為目標 locator；它不得選擇 profile、workload、endpoint、connector、
  CE version、organization、owner 或 credential。完整 target authorization 必須在任何 typed dispatch 前完成。
- 既有 Package02 contact-profile client 同時擁有 LINE profile write 與未分組承諾 aggregate，將此
  個人出席 read 併入其中會混合 capability、rollout 與 rollback owner。因此本 child 必須建立獨立
  DTO-only client/interface，不改動或擴張既有 mutation capability。
- 已確認 `Package02ContactProfileOperationsEnabled` 是既有 Package02 deployment base gate，checked-in
  settings 均為 false。新的 present-read sub-gate 必須同樣預設 false，且兩個 gate 未同時啟用時，
  controller 不得建立 typed client、process host、Data8 pool、credential graph 或 outbound I/O。
- P7.2 歷史 Slice C 已 `write-not-committed` 且完成 cleanup；本 child 是 local-only read migration，
  不重試、復用或修改歷史 CE cycle、nonce、ledger、fixture 或 descriptor。

## 需求

1. 建立唯一 server-owned operation ID `memberinfo.present.retrieve.by.contact`，以及固定的
   registry、Data8、wire、ProductClient 與 ChurchReport DTO contract；禁止 SDK `Entity`、
   `EntityCollection`、`QueryBase`、`OrganizationRequest`、endpoint、credential、owner 或 caller
   routing crossing product boundary。
2. typed request 只能含已驗證的 deployment `ProfileAlias`、固定 workload 與已由 controller 授權的
   non-empty contact GUID。Data8 connector 必須使用固定 entity、五欄 projection、固定 contact lookup
   filter、descending Sunday-date order、單一頁面與 row/text/response byte hard limits；
   `MoreRecords`、schema mismatch、duplicate/empty record ID、錯誤 lookup/date/scalar 型別或任何 limit
   超限都必須在發布前 fail closed，不得回傳 partial result。
3. typed response 必須只含 bounded scalar 出席列與顯示名稱的純值投影。每一層都必須 defensive-copy；
   不得保存 request、profile、token、client、response、exception、contact name、cache、timer、lease、
   stream 或 background work。Date/legacy display 轉換必須明確保留既有 `DateTime?` 行為，不得未驗證地
   改變 UTC/Unspecified 之日期。
4. 新增 deployment-owned
   `DynamicsAccess:Package02MemberInfoPresentReadEnabled`。base/sub gate 都為 explicit true/1 才能進入
   typed path；兩份 checked-in appsettings 都維持 false。false gate 保留完整 legacy route；true gate
   的 client unavailable、fault、timeout、cancellation 或 malformed response 不得 retry、fallback 或
   publish partial results。
5. controller 的 typed branch 必須先取得 deployment configuration/gate，再完成既有 user/session scope、
   parse locator 與 `CanViewContact` authorization，才可組成 typed client 並以
   `HttpContext.RequestAborted` dispatch。所有 `OperationCanceledException` 必須原樣離開 generic error
   handler；`DataSourceLoader` 只能消費完成的 action-local typed view-model list。
6. 以 TDD 證明 registry/wire union、Data8 query bounds、ProductClient validation、bootstrap base/sub gate
   和 profile-before-host validation、service A/B isolation/defensive copies/cancellation、controller source
   contract、settings false gate 及 legacy compatibility。任何本機測試均不得呼叫 CE、改變 feature gate 或
   建立 fixture。

## 驗收條件

- [ ] registry、operation executor、wire union、ProductClient 與 fixed Data8 query 對同一 operation
      有一致的 operation ID、CE 9.1 read-only policy、response discriminator 與 bounded scalar contract。
- [ ] connector 對 fixed filter/projection/order、exactly-one page、row/text/byte bounds、日期與 scalar
      schema 建立完整 fail-closed tests；錯誤資料絕不發布 partial records。
- [ ] typed client/service 固定 deployment profile/workload，不能被 browser 取代，並對 upstream/result
      collection 建立 defensive copies；A/B interleaved requests、fault 與 cancellation 不會跨 user/profile
      污染或造成 retry/fallback。
- [ ] `LoadContactPresentRecords` 在 false gate 維持既有 legacy 行為；true gate 保持 server authorization
      順序、只使用 typed DTO path 與 `RequestAborted`，且 generic error handling 不吞掉 cancellation。
- [ ] checked-in production/development settings 的 base/sub gates 都是 false，rollback 只需把 present-read
      sub-gate 保持或設回 false；沒有 CE、traffic、P7.5、P8、push 或 PR 操作。
- [ ] focused tests、相關 project/solution Release tests、Release build、UTF-8 no-BOM/CRLF/final-CRLF、
      `git diff --check`、scope/isolation/lifecycle scan、Trellis Check 與限時 CCG review 均有任務紀錄。
      外部模型未在 45 秒內完成時，必須標示「雙模型未完成」並改用本機驗證，不得阻塞工作。

## 不在範圍

- 啟用 feature gate、CE 讀寫、fixture、流量切換、Embedded/Dedicated parity/soak、rollback 實機演練、
  ToolUtility removal、P7.5、P8、push 或 PR。
- 改寫其他 MemberInfo action、weekly report/attendance write、週報資料、legacy ToolUtility 行為，或
  將 typed DTO 回填為 CRM SDK graph。

# P7.4 MemberInfo 承諾類型 metadata 讀取邊界

## 目標

為 `ORG-CALL-00040` 的 `contact.customertypecode` 建立一個獨立、預設關閉、
僅供本機驗證的 Package03 typed metadata consumer boundary。它必須讓 MemberInfo
在 gate=true 的單一 request 中只使用既有的 bounded Package03 option-set DTO，
在 gate=false 時完整維持原有的 `IOrganizationService` metadata provider 行為。

本 child 的交付是 disabled-by-default 程式、測試、rollback 設定與任務證據；
不是 CE 證據、實機切流、ToolUtility removal、P7.5 或 P8 完成聲明。

## 已確認事實

- `IPackage03SpecialResourceClient.RetrieveOptionSetAsync` 已提供固定
  `MetadataOptionSetTarget.ContactCustomerTypeCode` 的 pure DTO contract。
- Data8 runtime 已擁有以 profile/generation/target/locale 分隔且有界的 metadata cache；
  ChurchReport 不得建立第二個 shared metadata cache。
- legacy `MemberInfoCommitmentTypeMetadataProvider` 的 1028、2052、
  `UserLocalizedLabel` label fallback 與單一 process-global cache key 是既有相容行為，
  只能在本 child 的 false gate 使用。
- `SearchDistrictTree`、`LoadGroupMembers`、`LoadUngroupedMembers` 分別使用 metadata
  進行文字比對、configured-order segment 與 member-row label projection。
- `Package03SpecialResourcesEnabled` 已是關閉預設的 Package03 base gate；metadata
  必須另設可獨立 rollback 的 sub-gate，不能啟用既有圖片 route。

## 需求

1. 新增 deployment-owned `DynamicsAccess:Package03MemberInfoCommitmentMetadataReadEnabled`；
   base 與 sub-gate 都明確為 true/1 才可進入 typed path，兩份 checked-in 設定均為 false。
2. gate=false 必須在 process host、typed client、Data8 pool、metadata DTO 或 outbound I/O 前
   short-circuit 至既有 legacy path；不得改變既有路由、排序、搜尋或 label 語意。
3. gate=true 時，每個受影響 action 只取得一份 request-local metadata snapshot，並以固定的
   deployment ProfileAlias、固定 workload、
   `MetadataOptionSetTarget.ContactCustomerTypeCode` 與 `HttpContext.RequestAborted`
   呼叫唯一 typed operation。
4. typed snapshot 必須 defensive-copy 且驗證 response：非 null、最多 1,024 筆、value 唯一、
   configured order 唯一且精確為 0..N-1、label 非空且最多 512 字元。任何違反均在發布前
   fail closed。
5. typed branch 的 metadata fault、timeout、cancellation、client unavailable 或 response mismatch
   不得 retry、不 fallback legacy、不發布 partial result。`OperationCanceledException` 必須原樣
   傳遞給 ASP.NET Core 和下游 lease owner。
6. controller 不得保留 DTO、profile、token、client、exception、authorization result 或
   `HttpContext`；它只在目前 request 將 immutable option snapshot 傳給搜尋、排序與 row projection。
7. gate=true 時，「結案」的 raw choice value 也必須由同一份 request-local typed snapshot 以
   精確且唯一的標籤解析；缺少或重複皆 fail closed，且不得再讀 legacy OptionSet service、
   provider 或 shared cache。
8. false-gate legacy path、base/sub gate composition、profile 驗證順序、固定 request、
   malformed DTO、A/B profile isolation、cancellation、controller source contract、UTF-8/CRLF
   均需由本機測試與檢查證明。

## 驗收條件

- [x] Package03 metadata base/sub gate 與 factory 在 host resolution 前 fail closed，且 direct
      lifecycle tests 覆蓋 false、base-only、both-gates、空白 ProfileAlias。
- [x] 新 service 只發出固定 typed metadata operation，建立 request-local immutable copy，並拒絕
      malformed response、retry、fallback 或 shared ChurchReport metadata cache。
- [x] 三個 MemberInfo metadata consumer 在 true gate 使用同一份 request snapshot；false gate 保留
      legacy provider 和既有文字比對行為；true gate 的「結案」值只由該 snapshot 唯一解析。
- [x] cancellation 不被 controller generic catch 吞掉；typed fault 不會改走 legacy metadata。
- [x] 所有新增/修改 C# 與 task artifact 為 UTF-8 無 BOM、CRLF、final CRLF，完整繁體中文文件，
      且沒有 Session、Memory、Resource 或 cross-profile leakage。
- [x] focused tests、完整 solution tests、Release build、encoding/scope/diff 檢查、限時 CCG review
      與 Trellis Check 均有任務紀錄；若雙模型未在 45 秒完成，明確記錄降級而不延遲工作。

## 不在範圍

- 啟用任何 feature gate、CE 操作或 mutation、traffic cutover、parity/soak/rollback 實機證據。
- 修改 legacy metadata provider、其 cache key、圖片路由、weekly statistics、出席/週報流程、
  ToolUtility removal、P7.5、P8、push 或 PR。
- 將 typed metadata DTO 回填成 CRM `Entity`、接受 caller 指定 profile/workload/target/locale，
  或在 request-time 使用 legacy fallback。

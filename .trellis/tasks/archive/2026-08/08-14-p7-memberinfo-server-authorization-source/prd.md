# P7 MemberInfo 伺服器擁有的指派證據來源

## 目標

建立可獨立驗證的、伺服器擁有的 MemberInfo 指派證據 read capability。它只以已驗證
Cookie subject 建立的 `P7GatewayRequestScope` 為身份起點，從固定的 CRM contact/list
欄位產生 bounded、immutable、request-local 的 Church-wide 或 Shepherd assigned-list evidence，
使 ORG-CALL-00031、00032、00033 未來能在不讀寫 Session、`InMemoryContext` 或 legacy
`ListManager` 的條件下重新評估。

本 child 的交付是 data-plane 與 ChurchReport security adapter；不接線 `MemberInfoController`，
不切換 feature gate、流量或正式資料，也不把本機契約宣稱為 CE、consumer、P7.5 或 P8 evidence。

## 已確認事實

1. `P7GatewayRequestScopeResolver` 只安全投影唯一 Cookie 的 contact GUID 與封閉 login kind；
   它刻意不保存 account、password key、Session、principal、profile 或 CRM entity。
2. legacy `MemberInfoController.GetAccess()` 先讀 Session，再以 `InMemoryContext` contact 的
   `new_church_jobtitle` 及 legacy `ListManager.LoginType` 取得 access；Shepherd branch 會在資料
   缺失時以保存的帳密重新載入 `ListManager`。這不是可接受的 Gateway authorization source。
3. `DownloadListManager.FindListCollection()` 以 subject contact 在六個 list lookup 的任一符合為
   Shepherd assignment，去重後只保留 `new_app_named=true` 且有效日區間包含 server local now 的 list：
   `new_contact_list_vice_family_leader`、`new_contact_family_leader_list`、
   `new_contact_co_race_leager_list`、`new_contact_race_leager_list`、
   `new_contact_list_arealeader`、`new_contact_list_co_arealeader`。
4. `MemberInfoAccessResolver` 對 job title 包含「牧師傳道」、「牧養主任」或
   「檢視全教會照片資訊」者優先給 Church-wide；非 Church subject 僅在可見 assigned list
   存在時才屬 Shepherd。
5. 既有 `list.catalog.retrieve.appnamed.smallgroups` 只投影兩個 leader GUID，沒有四個 assignment
   lookup 或有效日欄位，故不得當作完整 authorization source。
6. 最新 70-row matrix 沒有其他可直接 consumer cutover 的安全候選；所有 70 rows 仍是
   `temporary-legacy`，P7.5/P8 保持 fail closed。

## 需求

1. 新 capability 使用固定 server-owned operation ID
   `memberinfo.authorization.assignment.resolve.by.subject`；它不得接受 browser list ID、role、
   profile、endpoint、credential、FetchXML、排序、日期或任何 caller-controlled authority。
2. 唯一 subject identity 必須由 `P7GatewayRequestScope` 提供。ChurchReport adapter 在 locator parse、
   cache、legacy manager、profile/client composition 或 CRM I/O 前建立 scope；失敗時只回傳固定、
   去識別化 failure。
3. Data8 execution 僅可：直接讀取 subject contact 的 `new_church_jobtitle`；若不是 Church-wide，
   以固定 schema 查詢 list 的六個 assignment lookup、`new_app_named`、`statecode`、`purpose`、
   `new_happy_start_date`、`new_happy_end_date`。只接受 active、purpose 為「小組名單」、app-named、
   有效日區間符合 server-owned current local time 的 list。
4. 查詢與回應必須 bounded：最多 512 unique list IDs；使用 top count 513／MoreRecords 作為 overflow
   sentinel；null、空 GUID、duplicate、錯 logical name/型別、paging 或不完整 response 一律 fail closed。
5. Church-wide 決定優先於 Shepherd assignments。非 Church subject 的零筆有效 assignment 是固定 denied；
   不以 legacy login type、個人 list、browser 值或 fallback 推導權限。
6. 每層只傳遞 immutable scalar/readonly collection；不得暴露 `Entity`、`EntityReference`、
   `IOrganizationService`、credential、Session、Cookie、principal、cache entry、raw exception 或 connector。
   connector/lease/permit 是 executor 的唯一 owner；取消、fault、timeout 或不確定 response 不重試且
   依既有 lease contract 釋放。
7. 實作必須有 A/B subject/profile interleaving、invalid source、overflow、duplicate、cancellation、
   wrong-operation/branch 與 zero-I/O-before-authorization tests；所有新/修改 C# 區域須具完整繁體中文
   XML 文件，UTF-8 無 BOM、CRLF、final CRLF。

## 驗收條件

- [ ] operation registry、Data8 executor、ProductClient 與 ChurchReport adapter 形成唯一固定資料流，
      並禁止 generic CRM bridge、request-time fallback、Session/legacy manager 或 caller-selected routing。
- [ ] Church-wide、assigned-list、無 assignment、job-title/list ambiguity、所有 malformed/overflow
      投影皆有 deterministic fail-closed 測試與 bounded error category。
- [ ] A/B subject 與 profile 的交錯測試證明不共享 DTO、list IDs、routing、cache、client 或保留 state；
      cancellation/fault 後 executor 仍依 lease owner 釋放資源。
- [ ] `MemberInfoController` 與現有 legacy behavior 不被改動；matrix consumer、feature gate、CE、traffic、
      P7.5、P8 均不因本 child 升格。
- [ ] 完成 focused tests、Dynamics/ChurchReport solution tests、Release build、encoding/CRLF、
      `git diff --check`、scope check 與限時 CCG review；雙模型逾時須如實記為「雙模型未完成」。

## 不在範圍

- 不重播歷史 P7.2 Slice C，不使用舊 nonce、ledger、fixture 或 descriptor。
- 不執行 CE mutation、feature enablement、traffic cutover、Official Worker、P7.5 ToolUtility removal 或 P8。
- 不將 server-owned evidence adapter 接到 `MemberInfoController` 或其他 consumer；後續 tree/membership/
  relation-goal capability 各自需要新的 read-back、CE、rollback 與 consumer child。

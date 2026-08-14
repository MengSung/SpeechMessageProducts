# ORG-CALL-00057 來源稽核

## 權威矩陣

`ORG-CALL-00057` 是 `list.membership.retrieve.appnamed.by.contact`，來源為
`ToolUtility/QueryOperations/RelationshipQueryService.cs:QueryListOfContactManyToMany`。它目前是 read、
`mapped-pending-evidence`；本 child 已將 registry、Data8 executor 與 ProductClient 實作成 default-disabled local-only
data plane，consumer、CE、host 與 traffic evidence 仍皆 pending。現行 matrix 已同步為 server-owned
`queryexpression` template `list.membership.appnamed.by.contact.v1`，input 僅為 `contactId`，並鎖定單頁 32 rows／32 KiB
closed response policy。

## Legacy query 與可安全保留的語意

legacy query 對 `list` 固定篩選 `new_app_named=true` 與 `statecode=0`，並以 `listmember.entityid=contactId`
限制 relationship。唯一必要投影可收斂成 `listid` 與 `listname`；`AllColumns=true`、formatted values、
Entity graph、paging state 與 raw exception 都不屬於新 contract。新 query 將加上 deterministic order、single-page
bound、byte budget、duplicate-ID 與 MoreRecords fail-closed policy。

## Consumer graph 與禁止接線

1. `ContactService.GetContactCurrentGroup` 對 collection 做 first-match，並位於 member transfer、attendance、
   contact update、owner assignment 與 LINE notification composite。`08-14-08-14-p74-contact-current-group-read-boundary`
   已證實它沒有 immutable authorization 或 duplicate policy，絕不可接線。
2. `NewPerson.DoesContactAlreadyInASmallGroup` 也是新增 contact/list management flow 的一部分，接受 mutable
   `Entity` 並回傳 mutable `Entity`；不得以此做 Data8 consumer 或 authorization source。
3. `DownloadListManager` 將結果保存到 mutable field，接著讀週報、更新 attendance projection 與 list counts；
   它不能成為本 child 的 cache、routing 或 consumer。

因此可以安全交付的範圍只有 data-plane registry/executor/ProductClient。不建立 ChurchReport route，才能確保
`contactId` 不會由 browser/session/legacy workflow 越過 server authorization 進入 executor。

## CCG 分析狀態

已透過 `Start-CcgDualModelRun.ps1` 啟動一次 architect run，參數為 `TimeoutSeconds=45`、`MaxAttempts=1`。
工具等待窗結束前僅產生 health 與 Gemini prompt artifact，沒有任何 Gemini 或 Claude usable finding，亦沒有
summary。依使用者 45 秒上限不重送、不等待；本 child 記錄為「雙模型未完成，採本機 source validation」。

## 結論

**local-only data plane = go；consumer cutover/CE/traffic = no-go。** 未來 consumer 的恢復條件是：在 CRM I/O
之前建立 principal-derived immutable authorization scope，針對 target contact 做 server authorization，並另行完成
composite write isolation、read-back/reconciliation、capacity/parity/rollback evidence。

## 2026-08-14 bounded review 結果

本 child 完成後再啟動一次 `reviewer` self-healing run，指定 `TimeoutSeconds=45`、`MaxAttempts=1`。在使用者設定的
等待上限內，runner 只產生 health 與 Gemini prompt artifact，沒有 Gemini 或 Claude finding、stdout/stderr result 或
`summary.json`；等待已停止且不重送。因此此輪是「雙模型未完成」，不可宣稱完成雙模型審查。本機人工審查、契約測試、
full Dynamics/solution test 與 Release build 是本 child 的可追溯降級證據；任何未來正式 enablement 仍須重新取得其
自身治理所要求的 evidence，不能用此本機 review 取代。

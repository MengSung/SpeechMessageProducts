# P7.4 fee editor read boundary

## 目標

將權威 matrix 的 `ORG-CALL-00066`（`fees.editor.load.by.disciplelesson`）新增為一條
獨立、唯讀、DTO-only、預設關閉的 ChurchReport 查詢入口。此入口只提供目前登入者已由
伺服器端課程快照授權的課程摘要；它不是既有繳費／點名編輯器的切換，不能修改
`FeeList`、`Fee`、出席、付款或 CRM 資料。

## 已確認事實

1. `ORG-CALL-00066` 的 registry、Data8 executor、typed ProductClient、CE 9.1 及 Embedded
   唯讀 evidence 已存在；Dedicated evidence 仍為 `evidence-pending`，consumer 尚未遷移。
2. 既有 `FeeManagementController` 的 `Fee`、`Present`、`GetFeeData`、`UpdateFeeData` 及
   `SaveBatch` 共享 session-cached、可變的 `FeeList.FeeDataList`。它會經 ToolUtility／CRM Entity
   建立資料，且可接續寫入，因此不能當成本 child 的 response model 或 fallback。
3. 現有 `FeeList.LessonList` 是依目前 session login account/password 由既有伺服器流程建立的
   課程清單，並能以 `IsLessonListLoadedFor` 驗證目前登入身分。只要尚未載入、登入不符、
   目標不在清單或含無效 ID，新的唯讀端點都必須拒絕；不得為此呼叫 `SetupLessonList`、
   `EnsureLessonListLoaded`、ToolUtility 或 CRM。
4. `Package01FeeReadsEnabled` 及本 child 新增的 `Package01FeeEditorReadEnabled` 都是
   deployment-owned gate，兩者在所有 checked-in 設定預設 `false`。false gate 只能回傳固定
   去識別化拒絕結果，且不得解析 browser locator、讀取 `FeeList`、建立 ProductClient 或發出 I/O。

## 需求

1. 新增獨立 route，僅在兩個 deployment-owned feature gate 都為 true 時才可進入後續處理；
   false、缺設定或不一致設定均在任何 locator parsing、session snapshot 或 client composition 前拒絕。
2. true gate 時，先以目前 session 的 account/password 對 `FeeList` 做無 I/O 的 scope check；
   只接受已載入且仍屬於相同登入者的 lesson snapshot。瀏覽器提供的 `discipleLessonId` 只是
   locator，不是身分、授權、profile、connector、owner 或組織選擇器。
3. authorization 成功後才可 parse locator；target 必須精確存在於 request-local 複製的伺服器
   lesson snapshot。任何空白、重複、無效或 snapshot 外 target 均固定拒絕，且不做 CRM lookup。
4. 成功路徑只能使用既有
   `IPackage01FeeReadClient.RetrieveFeeEditorRowsByDiscipleLessonAsync(profileAlias, "church-report-service", target, cancellationToken)`。
   profile、workload subject 與 operation 都必須由 server composition 固定；不得接受 caller 指定值，
   不得 fallback、dual-read、retry 或使用 legacy `RetrieveEntity`。
5. service 必須在發佈 JSON 前完整驗證每個上游 row 的 `DiscipleLessonId` 與 authorized target
   相同；null、mismatch、fault 或取消時不得發佈 partial response。`OperationCanceledException`
   必須原樣向上傳遞，不能落入一般 catch。
6. response 只可包含 immutable、allowlisted scalar projection，且以 defensive copy 與不可寫
   wrapper 發佈。它不得包含 CRM `Entity`、`EntityCollection`、`Fee`、`FeeList`、profile、端點、
   credential、raw exception 或任何可跨 request 留存的物件。
7. 既有 editable Fee／Present UI、更新／儲存／建立／指派流程及其 legacy semantic 完全保持不動。
   此 child 不執行 CE request／mutation、feature enablement、traffic cutover、P7.5、P8、push 或 PR。
8. 所有 C# 變更遵守 AGENTS.md：完整維護得當的繁體中文文件、UTF-8 無 BOM、CRLF、final CRLF，
   並以測試證明 false-gate zero-work、授權排序、exact operation、row match、取消、A/B 隔離與
   deterministic response ownership。

## 驗收條件

- [ ] `Package01FeeEditorReadEnabled=false` 或基礎 Package01 gate=false 時，route 不會 parse GUID、
      讀取 `FeeList`、建立 client、呼叫 ToolUtility 或 outbound I/O。
- [ ] true gate 只接受 server-loaded/current-login-matched lesson snapshot；未載入、登入切換、
      invalid、duplicate 或 snapshot 外的 lesson 一律在 dispatch 前拒絕。
- [ ] true gate 只呼叫精確的 `fees.editor.load.by.disciplelesson` typed operation，固定 server-owned
      profile/workload，且原樣傳遞 request cancellation。
- [ ] 上游 response 的每列皆與 authorized lesson 精確 match；任何 mismatch/null/fault/cancellation
      不會部分發佈資料，也不會 fallback。
- [ ] A/B 交錯請求取得不同 immutable response collection，沒有 session、cache、DTO、授權結果或
      resource ownership 共用。
- [ ] 新 route 未觸及既有 editable Fee／Present Grid、`FeeList.FeeDataList`、`UpdateFeeData`、
      `SaveBatch`、Create 或 Assign。
- [ ] focused tests、相稱 Release test/build、encoding/CRLF、`git diff --check`、scope check 與
      CCG review 都完成並留下準確記錄；deployment gates 仍為 false。

## 明確排除與停止條件

- 若現有 `FeeList.LessonList` 無法被測試證明為目前登入者的已載入 snapshot，或不能在不做 legacy
  I/O 的條件下取得 request-local target allowlist，本 child 為 no-go；不得自行重新查 CRM、掃描課程、
  猜選權限或改接 editable path。
- 完成本機 disabled path 不會升級 Dedicated／CE／cutover evidence，也不解除 P7.5 或 P8 gate。
- 先前 CCG architecture run 於 45 秒限時內沒有可用雙模型輸出，並因 repository path 編碼錯誤不可靠；
  本 child 將以本機 evidence 完成設計，且不重試等待該 run。任何後續 review 仍最多等待 45 秒並如實記錄。

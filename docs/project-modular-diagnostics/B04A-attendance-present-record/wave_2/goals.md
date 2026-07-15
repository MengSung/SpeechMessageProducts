# B04A Wave 2 完成目標

CONTRACT_STATUS: WAVE_PLAN_APPROVED

## 審查核准紀錄

- Claude-only review runner `20260715-093908-wave2-b04a-contract-reviewer` 兩次皆為 `no-usable-output`，故不是 Claude approval。
- 恰有一次 workflow 允許的唯讀 Codex fallback re-review 已核准：`APPROVED`，Critical=`None`、Warning=`None`；其確認五個唯一 create declaration A/B1/B2/C1/C2 和 Q0 no-data branch 必須移除對 A 的 query reachability。
- 此狀態只核准 Wave 2 plan；不表示產品 repair、local proof、staging proof 或 runtime proof 已完成。

## 不可擴張的完成範圍

完成僅表示 `B04A-SEC-001` 與 `B04A-SEC-002` 達標。它不表示處理 B04A logging、performance、extraction、未選 issue，亦不表示處理 B01、B02、B04B、B04C、X05Q。不得以這兩項安全修復改變已列 three CRUD routes、加入 batch route、改動 upload/weekly-report processor，或改變 authorized attendance flow 的 response shape。

## B04A-SEC-001 成功準則

1. 三個 exact routes 的匿名、stale-session、invalid anti-forgery、self、cross-member、cross-list、inactive case 必須逐一取得 `measurements.md` 固定的 status/reason，allowed/rejected count 與 13 個 named side-effect counters；所有 rejection counter 都是零。
2. `Staff-A1 -> A1` 的 insert、update、delete 分開通過；其 shared state、CRM/notification counts 必須精確等於量測表。特別是 delete 的四個 projection remove、一次 CRM membership remove、一次 CRM delete、兩次 notification dispatch 不可被模糊 aggregate 掩蓋。
3. `AuthorizedAttendanceMutationContext` 必須在任何 shared state/CRM/notification 前由 server-side principal、session、anti-forgery、role/list/record ownership 與 active-state 建立。client ID/name/key/query 值的竄改不得改變拒絕結果。
4. 已授權 flow 的 canonical route、HTTP verb、success response shape 與 target state semantics 維持不變。

## B04A-SEC-002 成功準則

1. `GetAllMemeberDataList` -> `GetAllMemberDataFromPresentRecordOptimized` -> `GetPresentRecordByLoginType` 的每個讀取入口，先完成 server-side read guard；固定 fixture outcome 必須完全符合量測表。
2. 授權 hit/no-match 僅可組裝新建 request-local `ListSmallGroupWeeklyReport` response snapshot。它不得是、不得成為、也不得 merge 回 shared manager/cache/projection。
3. query graph 從 `GetAllMemeberDataList` 起不得 direct 或 indirect reach 任一 `CreatePresentRecordList` inventory symbol、`ExecuteAuthorizedPresentRecordCreate`、CRM create/update/delete/assign、marketing-list mutation、notification 或 background enqueue。no-match 只能回 `200 PRESENT_RECORD_QUERY_EMPTY`。
4. authorized hit 與 no-match 各 N=10 次的 result hash 固定；每一次 shared manager/cache/projection/session/CRM-log/notification-queue `beforeHash == afterHash == baselineHash`，13 個 named shared mutation counter 都為零。
5. former create-on-read 僅能由 `ExecuteAuthorizedPresentRecordCreate(AuthorizedAttendanceMutationContext, PresentRecordCreateRequest, PresentRecordCreateIdempotencyKey)` command boundary 執行；它的 created/already-exists 結果必須以 canonical IDs/idempotency key 決定，query 無權呼叫。

## Local proof 與 staging proof 的分離

本 wave 的成功前提是 local build 和全部 synthetic contract tests 通過，且所有輸出保存為匿名 artifact。Local proof 只證明 fake provider、controller guard、query boundary 與 source graph；它不能證明實際部署的 middleware/session/anti-forgery/role wiring。

staging proof 是後續、獨立的 deployment gate：需以隔離環境、相同合成 fixture、無真人資料重跑所有 route/query matrix，並確認 deployed effective routes、authentication/session/anti-forgery/role middleware 與 local evidence 相同。在 staging proof 完成前，結果只能標示 `local-proof-only`，不能宣稱部署安全完成。

## Wave 失敗與回滾

下列任一情況即為 security failure：拒絕 case 有任一 named side effect、authorized count/counter 不符、query no-match 產生 record、query repeat 改變任一 shared snapshot hash、query graph 可達 create command、route/response regression、local build/test failure，或後續 staging 與 local proof 不一致。

失敗時僅回滾 `plans.md` allowlist 所定義的 future repair：CRUD guard、query/local snapshot/create-command boundary 與新增 contract test；保留失敗計數、hash、graph scan 和 review artifacts。不得放寬 status/reason、counter、hash 或 scope 以改寫失敗為成功。Wave plan 已獲流程允許的審查核准；產品 repair 仍須先通過所有 local goals，之後才可進入獨立 staging/runtime proof gate。

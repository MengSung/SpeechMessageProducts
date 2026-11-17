# Deployment CLI Contract

## Scope and connection boundary

P0-12 is an offline implementation and test package. No D365 or LINE connection
is permitted until the user reports goodshepherdback cleanup complete. Earlier P0-13 guidance reserved writes for the user. On 2026-11-04 the user
explicitly authorized Codex to finish this goodshepherdback trace acceptance,
including smoke, the reviewed disable plan/apply, verification and restoration of
exception-only tracing. This authorization is scoped to that acceptance and does
not authorize other organizations or future LINE pushes. Credentials come only from the Deployment User
Secrets provider; Windows Credential Manager is a future option. Do not modify
ToolUtility or accept passwords on the command line.

The connector validates the sole allowed HTTPS host and Organization.svc path,
then uses CrmConnectionService.CreateOnPremiseClient directly, without a shared
pool. One command owns one OrganizationLease. Dispose releases the client and
clears retained references, including on failure or caller cancellation.

## Metadata, output and mutation

The registration manifest contains templates only. Plan and Markdown never
resolve secrets. Export contains managed assembly content and metadata summaries;
settings use presence bits or hashes. Never export trace text, token or userId.
Report real exceptions through the shared diagnostic owner, which writes only
safe type/stack metadata; never print raw exception messages or CLI values.

P0-13 diagnostic correction (2026-11-04): after reporting the original exception
once, the CLI may print only `ErrorCode` from a typed
`FaultException<OrganizationServiceFault>` and its SDK `InnerFault` chain. Format
each Int32 as eight uppercase hexadecimal digits with invariant culture, and
iterate at most four nodes including the outer detail, even for a cyclic chain.
A missing detail prints no codes. Never read or serialize Message, Reason,
TraceText, ErrorDetails, Data or raw stack for this supplemental output. Its
failure must not prevent original reporting or change exit 1. Keep command-local
lease disposal and caller-cancellation classification unchanged. Offline fault
injection must cover secret-bearing fields, reporter failure, deep/cyclic chains,
missing detail, release-before-report and actual shared-log redaction.

Plan hashes cover manifest, DLL and current organization fingerprint. Apply
revalidates these, exports and flushes the current state before writing. Stable
step identities use type/message/entity/stage/mode/rank, and duplicate database
IDs fail closed. Existing step configuration changes update the original ID;
trace/push transitions retain idempotency, parse secrets only for writes, and
remove obsolete secure configuration records. Plan does not compare secret
values and therefore is not a credential-rotation tool. Both CLI and Markdown
must disclose this scope whenever placeholders exist. If metadata matches but
secret placeholders exist, verify returns 3 (incomplete), never full success.
Metadata differences still return 1; trace-only complete equality returns 0.
Do not add secret resolution, hashes of actual secrets, new rotation commands
or raw exports to obtain a successful verification result. Secret-value
changes require separately authorized rotation work and user test delivery.

A bounded OS file-handle lease serializes same-organization write commands on
this machine; release it after the connection is disposed. Smoke only creates
its own marked task, polls its correlated async/trace metadata for up to three
minutes and deletes the task in finally. Caller cancellation is not notified.

## Step secure-configuration lookup (P0-13 correction, 2026-11-04)

Use the official `sdkmessageprocessingstepsecureconfigid` logical attribute on
`sdkmessageprocessingstep`; `secureconfigid` is not an alias. Queries, creates,
updates, presence checks, export and fingerprints share one product constant.
The separate secure-configuration entity still uses `secureconfig` for its
content; do not retrieve that content during plan, verify or export.

The offline SDK double must independently validate step column names and filter
attributes, plus create/update attribute keys, against a fixed official schema.
Never derive this schema from product constants. Exercise a fresh registration
followed by verification, existing secure lookup export/cleanup and trace/push
transitions. A first-install-only test misses queries that start only after a
Plugin type exists. The real CLI lifecycle test uses a unique temporary solution,
a generated unsigned net462 PE and an exclusively scheduled working-directory
scope restored in finally; every command releases its injected fake lease.

Primary references: Microsoft Learn `sdkmessageprocessingstep` entity reference
(`SdkMessageProcessingStepSecureConfigId`) and web-service error codes
(`QueryBuilderNoAttribute`, `0x80041103`). A live retry requires explicit user authorization; successful
offline checks do not establish the current organization's state.

## Offline verification

Keep all original 17 test methods and every subsequently added case; 17 is the original contract list, not the current suite total. L1 uses operation-local in-memory SDK services;
L2 emits an unsigned net462 PE into the test's unique temporary folder and reads
metadata without Assembly.Load. A test runner may itself target net48, but the
generated deployment fixture must exercise the net462 Plugin metadata contract.
Dispose readers, timers, locks and temp fixtures.
CI includes the Deployment project and a separate >=80% coverage report.
Actual WCF/WS-Trust connectivity and D365 sandbox behavior remain P0-13 user
acceptance, not a claim made by the offline suite.

## Minimal P0-13 acceptance (user approved 2026-11-04)

P0-13 is user-approved trace-only sandbox acceptance; the user explicitly
authorized Codex to finish the remaining live operations on 2026-11-04. The Plugin deployment
assembly targets .NET Framework 4.6.2 per the user's 2026-11-04 D21
authorization. Registration, trace smoke and disabled-state verification on the
target D365 host were confirmed on 2026-11-04. The smoke field `FrameworkFileVersion=4.8.x` describes
the server's loaded `mscorlib` runtime file, not the Plugin target framework.
Only OrganizationUrl,
Username and Password under the Deployment organization User Secrets are needed;
no test LINE identity or token is required. Plugins and Deployment have no direct
or transitive ImageSharp dependency, so these project builds need no Six Labors
license. A manifest explicitly enabling goodshepherdback creates the new step
in enabled state; one plan/apply/verify=0 registration pass is sufficient.
Always disable the step with a new plan/apply/verify=0, restore exception-only
tracing and confirm smoke task cleanup afterwards.

Sandbox push acceptance moves to the prerequisite of P2-04. Own Plugin FileVersion
tracing and update/rollback acceptance move to phase 5 before the first production
Plugin is deployed to goodshepherdback. P0 live same-event retry is replaced by
the source-record-ID retry-key unit contract and LINE's 24-hour retry-key validity
contract, as accepted by the user; this is not a claim of observed delivery or
an assurance beyond the retry-key validity window. Transitional CI scope is
accepted; prioritize DevExpress source resolution once phase 1 begins and retain
full-solution local checks. The 2026-11-04 live run passed all five trace checks, reported runtime
FrameworkFileVersion 4.8.4795.0, left zero run-scoped smoke tasks, disabled the
diagnostic step with a single UpdateStepState plan and verify=0, and restored
exception-only tracing (value 1). Keep only fixed results, counts and validated
versions as evidence; never persist or print the raw message block. The temporary
audit uses the existing fixed-endpoint connector, owns and disposes its
configuration and connection, and queries only the exact diagnostic type, run
time window and correlation. Old LINE data was retained under the user's explicit
P0-only exception; do not describe it as cleared or extend that exception to later
phases. This acceptance did not exercise LINE delivery or prove hosted CI success.

## P2-04 前置失敗後的原況核對（2026-11-08）

- `export.Assembly.Content` 是 Base64；必須解碼後比較 DLL 位元組數與 SHA-256。
  字串 964608 字元與解碼後 723456 bytes 是同一份內容，不能因此誤判部分更新。
- `apply` 回 `0x8004852E` 時先停止寫入，核對 live assembly、同一 step ID、
  trace／secure lookup／enabled 與 pre-apply export；不盲目 rollback 或重跑。
  Microsoft 官方名稱是 `SqlEncryptionSymmetricKeyPasswordDoesNotExistInConfigDB`。
- 本次 goodshepherdback 唯讀 `IsDataEncryptionActive` 為 false，
  `RetrieveDataEncryptionKey` 回空；這不是 LINE token 的格式錯誤。
  未取得原有組織金鑰或可回復的核准方案前，不生成替代鍵、不重設加密、不讀寫正式組織。
- 本次已核對原 DLL hash 未變、步驟仍停用／trace／無 secure，trace 2→1 並讀回。
  D34 已清理的非測試主 LINE ID 不因部署失敗而恢復；備份保留在 repo 外 DPAPI。
  這些結果不等於 smoke、TLS、整體 verify=0 或 P2-04 前置通過。

## P2-04 加密解除後的接續證據（2026-11-08）

前節 false 是先前失敗時的歷史狀態，不是目前狀態。SDK `IsDataEncryptionActive` 的回應欄位為 `Results["IsActive"]`；先驗 Contains 與 bool 型別再使用，缺欄位不能當 false。此次已為 true，未讀取／輸出組織加密金鑰。

兩輪推播均獲真實 LINE 200，但 smoke 的外部診斷契約仍未達成。收尾 verify=0、追蹤=1、同一步驟 trace／停用／空 secure，依精確 trace ID＋correlation 與 owning step 備份後只清掉自己的兩個 trace／已完成作業；不得清其他作業或平台步驟。手機收訊與 HTTPS 成功不等於獨立量測 OS TLS 協定版本，也不能取代 smoke=0。

本機診斷腳本使用 PowerShell 對 SDK 查詢時，管線 `object[]` 可能含 PSObject 包裝，導致 DataContract 序列化失敗。對 GUID 的 In 條件建立 `ConditionExpression`，逐一 `Values.Add(Guid.Parse(...))`，先離線序列化確認；不要把此失敗誤判為伺服器資料錯誤。live CLI 與會改寫其 bin 的建置不可並行；改用獨立輸出或等 lease／程序釋放後重建。

## P5-03a 命令接線與同步追蹤（2026-11-14，實作中）

`shadow` 與 `apply` 共用組織 OS 寫鎖及單一 lease；只讀固定模組 Golden 目錄。整批情境、權威 Shadow ID、部署 PE／清單及即時註冊核對成功後才可寫 trace 或 seed。現在 new／reading／legacy-golden 的 CRUD 新輪已由真 CLI 測試；未接通的 live 舊輪盤點與推播等必須拒絕，不能改基準假稱通過。

runner 的 readLegacy 委派同時用於 finally 停用，不能捕捉已取消的 caller token；清理明確使用 CancellationToken.None，正常流程仍在各界面檢查 caller 取消。同步 step 由當下註冊 mode 提供，trigger 用獨立 RequestId 的 SDK request；追蹤查詢同時限制 requestid、pluginstepid、typename、mode=0 與開始時間，不等待同步步驟不存在的 asyncoperation。這只有離線證據，05A §8 的實機識別查證仍待完成。

安全報告放在 artifacts/shadow 的唯一操作子目錄，每份仍保留日期-module.md，不覆寫同日既有證據。情境的秘密樣式掃描先獨立檢查 LINE ID、長憑證及 Bearer，再對點分候選驗實際 JSON JWT header；不能把合法完整 CLR 名稱誤擋，也不能因點分候選吞掉內含憑證。不對 type／entrypoint 開略過掃描例外。

## P5-03a 關聯列觀察及清理（2026-11-14，離線證據）

observe 可選填 `related: {attribute, alias}`，alias 必須是本輪 owned seed，不可指向共用教會／會友。factory 驗正式 lookup Targets、唯一 selector、Create 期望相同父參照及有序建立／刪除，CLI 同時載入 observe 的 metadata。舊輪以精確父 ID 查回並核對每列 lookup；新輪以 Create 意圖而非最後快照配對，避免 Create 後 Delete 丟失別名。runner 在觀察／追蹤完成後才重新讀取動態 alias 映射。

trigger 送達後失敗或 caller 取消也要在停用後發現子列，先刪子再刪 seed；只對本輪父 ID 的有界完整回應登錄所有子 ID。查詢分頁、錯父 lookup、重複 ID 或子列刪除失敗必須失敗並保留父列作重試範圍，不能假稱零殘留。每個 selector 最多讀 64 列，超量不取部分；同 selector 多列可清理但不可任取第一筆配對。影子意圖 GUID 只供比較，永不加入遠端刪除清冊。這些測試不代表已執行實機舊輪。

## P5-03a 推播期望的 CLI 接線（2026-11-15，離線證據）

expected.json 的 lineCalls 可選填 shadow 物件，但一旦提供就必須完整列明
`templateKey`、`retryKey`、`contentSha256`，不得從訊息正文猜測。範本使用最多
120 字元的 ASCII 英數及 `._:-`；內容摘要為 64 字元小寫 SHA-256；Retry Key
是本情境明列的 owned 記錄別名。reader 共用結構驗證；factory 再驗唯一收件者
是權威共享 contact、端點為單人 push、訊息數大於零及 Retry Key 不屬共享列。
舊離線黃金沒有 shadow 仍可讀，但實機比較不可將其當成完整推播期望。

factory 按正式 writer 的 CRUD 後 Push 順序產生完整意圖；Push 的 Entity 中繼
資料沿用 parser 的空字串契約，身分放 contactId，不可填 contact 破壞重新解析
一致性。收件者與 Retry Key 的 GUID 僅依明確別名正規化。真 CLI 合成測試驗證
吻合與內容摘要不同兩種出口、停用還原、seed 排空及報告不包含真實身分；這些
離線測試不代表舊輪訊息內容已可觀測，也不能取代實機追蹤及推播送達證據。

## P5-03a 公開 legacy switch 受控第一輪（2026-11-15，離線證據）

`switch --org goodshepherdback --module <代號> --to legacy` 保留既有語法，
從該模組固定 Golden 目錄讀取 liveShadow 情境，不接受外部路徑、記錄 ID
或秘密參數。只接受 legacy-live／legacy-live-only；reading 等基準不能用來
宣稱執行舊輪。命令共用 shadow 的完整部署、權威身分、D34 與固定受審 token
盤點前置，全部情境準備成功後才寫 trace 或 seed。

runner 的 RunLegacyOnly 與原兩輪共用 RunCore／RunRound，第一輪仍在
SwitchModule 的 D5-18 回呼內注入測試 token、啟用、觸發、讀回。成功、失敗
或取消都停用新舊、清除 token、清理自建列、核對並還原原 trace，SDK lease
由 CLI 最後歸還。CLI 不留下供另一程序使用的啟用狀態。無 token 依賴只能
來自明確盤點，不能因空欄位或缺少 TestChannelToken 自行推定。

報告明標 scope=legacy-round-only，Passed 僅判定舊輪觀察欄位對期望相符；
新輪、完整意圖與兩輪互比均須不可比且沒有差異。報告明文不代表完整影子
或 LINE 內容／送達驗收，不能用此結果取代 shadow 命令的兩輪證據。
缺固定情境時讀檔失敗回一、零連線；非法組織或語法仍回二、零連線。

## P5-03a 平台映像情境接線（2026-11-15，離線證據）

trigger.images 不注入 SDK context；factory 只驗其可由明列 seed／trigger 重現。
CLI 的 StepImages 來源是已核對的新步驟清單及本次正式舊映像匯出，不能由情境
宣告授權。舊映像使用 EntityAlias，不能使用 Name 顯示文字代替；Both 類型拆為
同別名前後映像。實機舊基準對全部實際舊／新步驟核對，缺任一步驟映像即拒絕。

每個宣告映像須具有唯一別名、正確前後種類及註冊涵蓋的明列欄位；Create 前像、
Delete 後像、不符 seed／覆蓋後 trigger 的值或型別一律拒絕。只支援能由可寫欄位
重現的值，不從系統預設或前置 Plugin 猜值；名稱前綴及真正 GUID 仍由 runtime
統一處理。這是執行前的可重現性檢查，不能冒稱讀到了實機 Plugin context。

## P5-03a 受審外部既有列摘要（2026-11-15，離線證據）

固定入口盤點必填 externalWrites，逐項列明實體與指向 account 的教會 lookup。
CLI 以正式 metadata 核對 lookup、主鍵及 BigInt versionnumber，整批準備通過後
才可寫入。空陣列表示來源審查未宣告外部範圍，缺欄位則拒絕，不能補成空集合。

單輪 observer 在 seed 前及舊輪完成後查權威測試教會範圍，只取主鍵、教會及版本。
最多十六表、每表六十四列；分頁、超量、錯教會、重複主鍵或缺版本拒絕完整結果。
摘要只比較基線已有列的版本變化或離開範圍，不還原、不刪除既有列；owned 及 related
清理仍由原清冊處理。快照於 round Dispose 清除，沒有背景工作或跨命令快取。

報告只含實體與筆數，區分 not-executed、unavailable、none-declared、reviewed-scope。
舊輪缺摘要證據不能 Passed；未執行舊輪不可附造出的摘要。版本變動可能受並行寫入
影響，不能宣稱全域稽核或唯一因果。真 CLI 測試驗證變動傳遞、既有列保留、盤點／
metadata 前置零寫入與前後快照分頁失敗時的還原清理；全部是合成離線證據。

## P7 註冊清單live分級（2026-11-17）

依07A §4.2/4.3維持schemaVersion=2；每個非diagnostics步驟與每個活動都必須有live，
包含tier（live/dormant）、reason、evidence（file/sha256/at/result）。只有步驟另有必填
布林tokenKey；活動出現該欄位一律由未知欄位規約拒絕，不因值false或null放行。
diagnostics沒有live區塊，永不參與組織層級切換。

loader對磁碟與程式內候選都驗證完整live資料，不能為沿用舊fixture而放寬產品檢查。
dormant理由必須以P<階段>-PROP-<三位數>開頭；result只能空字串或pass，pass必有非空
檔名、64位十六進位SHA-256及YYYY-MM-DD HH:mm Asia/Taipei (UTC+08:00)時間。
空結果表示未驗收；欄位宣告不等於讀取證據或已通過實機，後續切換仍需完整plan關卡。
資料只活在當次命令，沒有SDK/秘密/跨使用者可變狀態；1MiB與JSON深度32限制不變。

前置B只可把預設live降為dormant，對每筆保留來源/欄位/通知與產品其他入口的盤點證據；
不確定亦依D7-13降級。不更改enabledByOrganization、unsecureConfig或secureConfig來
假裝已切換。tokenKey按外殼實際讀取判定，包含dormant的MemberLanguageChangedPlugin。

ManifestLiveTests的四指定方法以合法控制組再注入故障，防止未知欄位或新缺欄遮蔽原故障。
舊測試只補新必填合成live資料，不改斷言或期望值。詳前置B的wp-reports盤點表；
這些離線格式測試不能宣稱goodshepherdback的影子或Live證據。

### P7 格式錯誤與斷言矩陣

| 輸入 | loader結果 | 決定性測試 |
| --- | --- | --- |
| 非診斷缺live、非法tier、活動含tokenKey | InvalidDataException，無SDK | Manifest_LiveBlockMissingOrInvalid_Rejected |
| dormant理由空白/非提案前綴 | InvalidDataException | Manifest_DormantWithoutReason_Rejected |
| 空result/完整pass | 合法控制組可讀 | Manifest_EvidenceResultOtherThanPass_Rejected |
| 其他result、pass缺file/hash/時間 | InvalidDataException | 同上故障組 |
| diagnostics省略live/帶區塊 | 前者可讀、後者拒絕 | Manifest_DiagnosticsHasNoLiveBlock |

正例：步驟live含tokenKey=false、空evidence表示未驗收；基本例：dormant理由P7-PROP-006加說明。
錯例：為保住舊fixture而讓程式內候選免驗live，或把活動tokenKey=false靜默忽略。
正確：LoadDocument與Validate均驗完整候選；只補測試輸入，原故障/斷言與清理不變。

## P7-01a 組織catalog與正式關卡（2026-11-17）
以上P0～P5單組織敘述是歷史契約；現在Deployment組織唯一來源為d365/registration/organizations.json。
OrganizationCatalog嚴格有界解析、未知／重複欄位、重複名稱／主機、非法kind/state與決定拒絕；唯一test仍為goodshepherdback，repo現只此一筆。
ProductionGuard位於所有Channel/Schema/一般分流之前；production所有命令須--production，test禁止該旗標。
正式寫入先由既有ChannelSecretInput讀一行canonical名稱、Ordinal相等、最多256字元及30秒總deadline；與Channel兩行秘密共用reader，無Task.Run或背景讀取。
正式禁止smoke/shadow/module switch/live-check/ui-export；export-legacy在production只報啟用counts，test保留啟用即回1。所有命令原parser仍拒未知選項。
SDK連線每命令獨占lease，secret鍵按canonical名稱讀取；URL必須核定host、HTTPS預設埠及固定Organization.svc，production先RetrieveCurrentOrganization核正式ID/UniqueName。
所有資料與catalog只屬命令，不建立ambient mutable授權或全域credential；內部engine亦查白名單，影子/module switch再要求test種類。
十三指定方法在OrganizationsTests；另驗Program鍵前綴、四分流零連線、正式Channel同reader與schema匯出成功、server名稱錯配租約清理。
Schema plan原hash已包含server OrganizationId，合成同名不同ID仍拒絕，不改歷史hash格式；此為離線證據，P7-01不連任何D365。

## P7-01b 計畫切換與回復（2026-11-17）
新型別在Switching/。plan --switch-to off|shadow|live為唯讀；switch --plan --plan-sha256要求完整檔SHA，
同一buffer嚴格解析。共同ProductionGuard／路徑／寫鎖先行；鎖內重讀WhoAmI ID、全部metadata指紋、來源及兩DLL。
manifest/catalog的唯讀handle持有整個命令，兩DLL每次重新PE檢查；未部署、hash差異或註冊缺漏先拒絕。
SwitchTarget為唯一五列表，型別override > 模組override > manifest；diagnostics不參與。
原註冊plan/apply與diagnostics-only verify沿舊0/1/3契約；P7非診斷verify依catalog.switchState，
verify --plan依目標，僅核secure有無並明示不比秘密，不宣稱金鑰正確／輪替或實機推播通過。
正式Live所有有效live項目須pass證據。新舊同模組互斥：原況衝突即拒絕；KeepLegacy啟用列會阻擋同模組啟新。
舊step依正式ID/type/assembly對照；dormant或未對應列KeepLegacy。流程含dormant／未移植活動（含
JoinMainSmallGroupList.JoinMainSmallGroupListPlugIn）整個不動，即使草稿也列KeepLegacy。
流程超1MiB／暫停列Manual；原export-legacy維持超限拒絕，只在switch專用盤點保守列候選。
WorkflowRepointer由ActivityProbe抽共用，支援帶版本qualified name與CLR節點、assembly大小寫；
DTD／外部資源／深度>128拒絕，不以全文Replace。rollback用log原字串而非重序列化結果。
SDK前先完整export與legacy export，再write-ahead flush attempted；完成後再flush completed。
secure候選GUID在Create前記錄，送達後throw亦精確清孤兒；清lookup後只刪無引用secure列，不讀值。
故障與取消反序回復所有attempted，讀寫回復使用None token；原失敗保留，cleanup失敗Aggregate回報。
本機switch-log有界128MiB、strict JSON／hash、原子flush替換；不進版控，原XAML總量16MiB。
來源／server／正式ID不符拒絕回復；只碰本次log列出的項目。固定資源沿SharedText與既有產生器。
指定測試在SwitchTargetTests、SwitchPlanTests、WorkflowRepointerTests、SwitchExecutorTests；
另以SwitchSnapshotReaderTests與SwitchCliTests驗真SDK讀取及離線完整循環，均不能當作實機證據。

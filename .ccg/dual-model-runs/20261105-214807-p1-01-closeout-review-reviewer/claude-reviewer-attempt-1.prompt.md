ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\reviewer.md
<TASK>
# CCG reviewer Task: p1-01-closeout-review

## Repository
C:\Users\Administrator\.codex\worktrees\p1-01-closeout\1.0.0.11.Integration.P1.Worktree

## Request
Read-only final review of P1-01 closeout documentation and evidence. Do not modify files or run builds. Both backends requested; quota fallback must be explicit. Review correctness of scope, counts, user decision and completion claim; do not expand product scope. No product code changed. Fixed product snapshot03c8aa28; only two metadata final CRLF added in a3b5b749. Full build/test were freshly executed, no hosted CI claim. User explicitly keeps current DevExpress, no upgrade, no subscription renewal, no UI replacement; source/hosted restoration deferred and no longer P1-01 blocker. Main implementation owns shared progress page; this worktree owns the report. Formal P1-01 requires orphan removal without references, 15 tests retained with Performance trait, ToolUtility.Tests in sln, before/after duplicates/build/test; local section8 plus architecture and offline plugin checks. Return concise Critical/Warning/Info findings supported by provided evidence. Flag only actual defects, not speculative new scope. Detailed inputs follow.

## wp-reports/P1-01-acceptance.md
# P1-01 清理孤兒專案與檔案：正式驗收

日期：2026-11-05（Asia/Taipei）。收尾負責對話：`01a1abe3-d126-7b63-a2be-8acc7ce2b5e3`。

## 結論與範圍

P1-01 的產品清理及測試整合驗證已通過；本報告的最終審查結果見下方「審查與交付」。
固定驗證提交為 `03c8aa28bc8ede00b59ec769737a8ed0ca5d06b8`，在獨立 worktree 從乾淨輸出重新建置與測試，未使用主實作仍在變動的 Activity/Core 輸出。
本輪沒有修改任何產品 C#、Razor、專案、方案、套件版本或 CI workflow；不代表 P1-04 或整個 P1 完成。

使用者已明確決定：目前 DevExpress 足夠，繼續使用現有版本，不升級、不續訂，也不替換現有 UI 元件。
新套件來源與 hosted 完整 CI 恢復暫緩，不再作為本次 P1-01 的阻擋；此為使用者核定的範圍調整，不是宣稱雲端驗證成功。
現行中央版本 `DevExtreme.AspNet.Core 23.1.5`、`DevExtreme.AspNet.Data 5.1.0` 均未改變。

## 正式需求逐項核對

依據 `文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md` 的 P1-01，以及 `00_總覽與執行規則.md` §8。

| 要求 | 本輪實際核對 | 結果 |
| --- | --- | --- |
| Trace 無有效引用才移除 | 刪除前提交 `f5c69cde^` 與驗證提交均檢查專案檔名及兩個專用型別；排除候選本身、參考方案與其他 worktree，0 個有效引用 | 通過 |
| 刪除兩個冗餘 `_Net10.csproj` | `Line.Messaging_Net10.csproj`、`LineMessagingProcessor_Net10.csproj` 已刪，沒有有效引用 | 通過 |
| D17 不移植 LINE Pay | `LinePayCSharp` 36 個追蹤檔已刪，沒有有效引用 | 通過 |
| 四個 Crm82/91Worker 目錄只剩 lock 才刪 | Git 刪除前樹逐一確認各只有 `packages.lock.json`；沒有 csproj 或原始碼 | 通過 |
| 原效能案例搬入正式專案 | 舊路徑已無檔，新路徑為 `ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs`；原 15 個測試名稱完整保留 | 通過 |
| Performance 分類及可執行 | 類別具有 `Trait("Category", "Performance")`；原 15 案加 1 個並行發布回歸，共 16 案在本輪 TRX 全數實際通過 | 通過 |
| ToolUtility.Tests 加入方案 | 專案在 `.sln`，全部 15 組方案組態均有 ActiveCfg／Build.0；本輪兩個測試宿主均執行 | 通過 |
| 完整清單與無人參考證據 | 51 個稽核路徑與刪除提交比對一致，現存 0；本輪掃描 1,129 個追蹤來源／專案檔，引用命中 0 | 通過 |

完整 51 檔清單保留於 `wp-reports/P1-01-reference-audit.md`；重新核對的路徑、搜尋 token、15→16 測試名稱、30 個方案映射與 SHA-256 存於收尾任務 `evidence/source-audit.json`。
刪除僅針對已核實追蹤檔，沒有把別人的 untracked 檔案遞迴清掉。逐項清單實算為 `Trace` 9 檔、LINE Pay 36 檔、冗餘專案 2 檔、worker lock 4 檔，合計 51；不採用歷史外審將 LINE Pay 誤記為 35 檔的摘要。

## 新鮮驗證證據

所有命令均在獨立工作樹執行；建置／還原只使用既有開發環境，測試不連 D365、不發 LINE。

| 檢查 | 命令／證據 | 結果 |
| --- | --- | --- |
| 完整 Release 建置 | `dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605 -m:1 -nr:false` | 成功，0 錯誤、21 個既有 warning；未新增 NoWarn 或降低檢查 |
| 完整 Release 測試 | `dotnet test SpeechMessageProducts.sln -c Release --no-build --logger "trx;LogFilePrefix=p101"`，在本輪實際建置後執行 | 16 組件／目標，2,278／2,278，0 失敗、0 略過 |
| ChurchReport | 完整方案中的 `ChurchReport.MemberInfo.Tests` | 623／623，內含效能 16／16 |
| ToolUtility | 完整方案中的 net48／net10.0 測試宿主 | 413／413、518／518；net48 是測試宿主，產品仍為 net462 |
| 架構守門 | `SpeechMessage.Localization.Tests` net10 的方案／MSBuild 守門 | 該組件 67／67，其中包含原 Localization 案例；不把全部 67 案說成架構專項 |
| Debug tracer | 正式 §8 filter，另用 `Test-TrxExecution.ps1 -ReportPath .../tracer.trx` 核對正執行 | 7／7，TRX 計數與結果列一致 |
| 重複程式 | `Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json` | 28 專案／938 檔，groups/new/stale 都為 0，未重設基準 |
| 編碼 | `Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf` | 既有快照補兩個檔尾 CRLF 後 791 檔、758 raw 排除、0 問題；最終含本輪新檔結果另記 evidence |
| 國際化 | `dotnet run --project tools/I18nCheck -- verify` | CJK ratchet 與 2 個 generated resource 驗證通過 |
| Plugin 打包 | `Test-PluginBuild.Tests.ps1 -UseEphemeralTestKey`、`Test-PluginAssembly.Tests.ps1` | 6／6 建置案例、10／10 metadata 案例；一次性測試金鑰，由既有 finally 清理，無正式部署 |
| Plugin 檢查工具 | `dotnet test tools/PluginPackaging.Tests -c Release` | 27／27 |

原始 logs／TRX 位於此 worktree 的 ignored `artifacts/p1-01-closeout/`；不把 TRX、DLL 或金鑰加入 Git。
可提交的摘要、每個目標的測試計數與原始檔 SHA-256 位於獨立收尾任務的 `evidence/`。
本輪不新增產品模組或行為，因此不為文檔收尾重做新模組 coverage；原有門檻與主線各模組覆蓋率責任不變。

## 前後比較與誤判修正

- 刪除前保存的基準為 26 專案／879 來源檔、0 duplicate groups；全方案 1,125 案通過，方案外 ToolUtility 另 63 案通過。原效能 15 案當時沒有正式測試專案。
- 本輪為 28 專案／938 來源檔、0 duplicate groups；專案與檔數增加來自後續 P1-02～04，不等於孤兒未刪。2,278 是整個固定提交的回歸數，不宣稱全由 P1-01 新增。
- 原 15 個案例均保留，另增並行 fixture 的 1 案；假 SDK 只驗查詢契約、取消、結果隔離及有界呼叫，不能當成實機吞吐提升證據。
- 第一次稽核腳本假設 `.sln` 只有 Debug／Release 四條映射而拒絕；實際存在 15 組（含自訂／x86／x64），已改用真實方案組態集合精確比對 30 條映射，沒有修改方案或放寬產品測試。
- 初次完整編碼守門發現 `.ccg/tasks/p013-apply-fault-investigation/.turns.json` 與 `.trellis/workspace/codex/index.md` 缺 final CRLF。只在本獨立 worktree 附加兩個 bytes；原有 bytes 全數不變，沒有改任務狀態或日誌內容。前後 SHA 保存於 `baseline-format-fixes.json`。
- Debug tracer 已 7／7，但第一次正執行檢查誤傳 `-TrxPath`；依實際工具改用 `-ReportPath` 重新驗證。沒有以錯誤參數命令冒充通過。

## 審查與交付

分析 run `20261105-213802-p1-01-closeout-analysis-analyzer` 嘗試 Gemini＋Claude；Claude 週額度阻擋，Gemini 成功，機器摘要為 `dualModelComplete=false`、`quotaBlocked=true`、`degradedFallback=true`。這是單模型降級分析。
Gemini 指出的缺驗收報告已由本報告補齊；要求確認 untracked 清除及 Plugin net48 的建議不採納：不得刪未核實檔，產品 Framework 必須 net462，net48 僅作測試宿主。
最終主會話審查／外審與提交結果會在收尾 task `review.md` 保存；未通過前不預先修改主線進度。

主實作對話仍單一擁有進度 HTML 與主 P1 task；本收尾不封存主 P1，不將 P1-04 進行中的測試與缺陷冒充已驗收。
正式 hosted full CI、遠端新來源、實機 D365／LINE 均未驗證；依使用者決定暫緩的 DevExpress 事項不再阻擋 P1-01。


## .trellis/spec/backend/solution-ci-contract.md
# Solution CI Contract

## Scope

P0-11 runs on Windows with the SDK from global.json and full Git history.
All builds and tests are offline with respect to D365 and LINE. Hosted Actions
are not executed by local verification and must never be reported as such.

## Commands and evidence

The authoritative command sequence is .github/workflows/solution-ci.yml.
Run every applicable run block locally before committing a change to that
sequence. Preserve sanitized summaries in the active WP evidence directory;
raw coverage, build logs, temporary scripts and DLLs belong under ignored
artifacts only. Never commit TRX or signing keys.

## Coverage gate

Test-Coverage.ps1 -ReportPath <coverage.cobertura.xml> -Module <assembly>
requires exactly one ordinal matching package and a finite invariant decimal
line-rate in [0,1], at least 0.8 without rounding. Missing reports, missing or
duplicate modules, malformed XML and external entities fail closed. The XML
reader is bounded to 32 MiB characters, has no resolver, and is disposed before
returning. The tool does not load product assemblies or contact any service.

Collect each new module and each target framework separately into a fresh
directory; require exactly one report there. Do not select the best result from
multiple runs or let a net10 report satisfy net48. The complete solution test
run remains mandatory; targeted reruns provide isolated coverage for new code.
Include I18nCheck.Tests and PluginPackaging.Tests, which are outside the solution.
Add new modules to the explicit coverage matrix as they enter future WPs.

### P1 Core 納入與證據邊界（2026-11-05）

新增 `ToolUtility.Core` 時，必須同步將 Core 與完整 `ToolUtility.Tests` 納入過渡
solution filter，並在 coverage matrix 分別使用 net48（實載 net462 產品）及 net10.0。
`Test-CiTransition` 必須比較真實方案的完整 eligible 集合，不能繼續用 P0 的固定 23
掩蓋新專案遺漏；原核定三項 DevExtreme 排除、完整差集及重複拒絕仍須保留。

Core strict 全域已啟用 Nullable；沒有逐檔 `#nullable enable` 不表示 nullable-oblivious。
若審查質疑介面契約，應核對各目標的 evaluated Nullable 值與真實中繼資料，而不是
單靠檔頭 pragma 作結論。

TRX logger 可能把 coverage attachment 複製到第二個結果路徑，使「恰好一份 coverage」
守門正確拒絕。不要刪失敗報告、挑較佳結果或放寬斷言；將 TRX 執行證據與獨立 coverage
run 分開，各用新目錄，coverage run 不另啟 TRX logger。

每次抽取新增型別或測試後，先實際建置兩個測試目標，再從新 TRX 核對執行數及
Framework 產品反射斷言，才使用該輸出收集覆蓋率。`--no-build` 不代表目前來源
已被編譯；若發現案例數少於來源預期，必須保留該輪失敗證據、重建後另開新目錄，
不得拿過期 DLL 的高覆蓋率或成功結束碼當成目前抽取的驗收。

跨 assembly 搬移的相容性補證，應包含真實已編譯的代表業務方法與公開 Facade
委派鏈，只替代外部 SDK I/O。若既有建構式會讀憑證或建立與該方法無關的外部
client，隔離測試可明載略過該建構式、僅注入方法實際使用的依賴；此證據只保護
方法及委派，不代表建構、DI、授權或實機整合。不得為測這條路徑而讀真實秘密、
連線 D365、發 LINE，或將一個代表方法說成全部業務分支都已執行。

## Signing

Ordinary Release builds receive no key and do not package. The workflow reads
PLUGIN_SNK_BASE64 only in its optional preparation step. With no secret, it writes
an explicit skipped summary and does not execute the package target. Do not create
a GitHub secret or request an upload without separate user authorization.

A configured secret is decoded only into runner temp; clear the byte buffer,
never print its content, and always delete the fixed temporary key in a separate
if: always() step. Only the signed packaging step sets the process variable
SPEECHMESSAGE_PLUGIN_KEYFILE and explicitly opts into SpeechMessagePackagePlugin.
Only coverage XML is uploaded, never runner temp, DLLs or keys.

If `PLUGIN_SNK_BASE64` is configured in the future, it must be a GitHub
Environment secret restricted to protected branches and requiring approval.
Never place it in repository secrets: this workflow runs on pushes to all
branches, so a repository secret would be available to any branch workflow.
Whether to create that Environment secret remains a user decision.

Offline build contract samples use -UseEphemeralTestKey explicitly. This mode
creates a disposable RSA fixture in a unique temp directory, never reads the
formal key contents, restores the caller environment, and deletes the fixture.
Without the switch, P0-10's formal process-environment requirement remains intact.

## Tests

Keep coverage rejection cases and both signed/unsigned metadata samples.
Run Pester 3.4.0 for the existing encoding and duplicate tests, the relocated
Test-P002Settings.ps1, progress rendering, CCG summary semantics, analyzer probes,
central package guard, Debug tracer, I18n verification and plugin fixture tests.
Do not treat zero discovered tests or an arbitrary failure as a successful guard.

## Six Labors general license (user approved 2026-11-03)

SIXLABORS_LICENSE is a required, user-configured repository secret for this
workflow. This specific authorization is separate from the future Environment
restriction on the Plugin signing key. Pass it only through the preparation
step environment, never by interpolating its value into run script text.
Missing or whitespace content fails with a clear message before building.

The preparation block creates a unique speechmessage-sixlabors-<guid>.lic in
RUNNER_TEMP with CreateNew and UTF-8 without BOM. Dispose the writer and stream
before publishing only its path through GITHUB_ENV as SixLaborsLicenseFile.
Clear the preparation process secret in finally; do not print its content or
write it to summaries, GITHUB_ENV, or artifacts. A preparation/publication fault
removes the file it created and emits a fixed diagnostic without raw messages.

A separate if: always() block deletes the published file after success or
failure. Validate the direct runner directory and workflow-owned filename
before deleting; never follow an arbitrary environment path or delete another
file. Cleanup is idempotent and clears the local process path. Only coverage
XML is uploaded. Local tests execute these actual workflow blocks with synthetic
license text, including missing secret, publication failure and unowned-path
rejection. They do not read or copy the real local license.

Local product builds may use the user-level SixLaborsLicenseFile path only in
the current process, after Test-Path confirms existence. Do not commit that
path, inspect license contents, alter persistent environment settings, or claim
hosted Actions execution from local verification.

## Tracer execution evidence

The Debug tracer block writes a fresh tracer.trx, then Test-TrxExecution.ps1
requires positive executed count, all passed, no failed results and agreement
with actual result rows. Reject empty discovery even if dotnet exits zero. XML
is bounded to 4 MiB with DTD and external resolution disabled; dispose readers
on every path and never print raw XML or exception messages. Synthetic tests
execute the real workflow block with only dotnet replaced. Keep them active
even while the tracer step is temporarily disabled for a dependency source.

## Transitional DevExtreme dependency scope (user approved 2026-11-04)

SpeechMessageProducts.CI.slnf retains 23 of the solution's 26 projects. Exclude
SpeechMessageProducts.ChurchReport, ChurchReport.MemberInfo.Tests and
ToolUtility.Dataverse.Tests because they directly or transitively require
DevExtreme.AspNet.Core 23.1.5, whose approved package source is still pending.
Restore, build and Release test use this filter. The Debug tracer step keeps
its run block and positive-TRX guard but is temporarily disabled; synthetic
tracer checks stay active. The always-run scope summary derives the exclusions
from the filter and must state that this is not full solution verification.

Test-CiTransition.Tests.ps1 checks the real dependency closure and executes
actual command/summary blocks with only dotnet replaced. No unrelated project
may be silently omitted. Per-module coverage and offline JavaScript checks
remain enabled. Local 00 section 8 validation still uses the full solution,
including ChurchReport and Debug tracer.

The obsolete machine-specific DevExpress 19.1 source is removed from the
ChurchReport NuGet.config. It inherits user-level package sources. On a new
computer, install the licensed DevExpress 23.1 tools, locate their local
DevExtreme AspNetCore package folder, and add that folder to the user's NuGet
package sources (Visual Studio package-source settings or user-level NuGet
configuration). Keep the actual machine path outside the repository. Existing
machines may restore from their populated cache. Do not add nupkg files,
private source URLs or credentials to the repository, upgrade DevExtreme, or
try an unapproved remote feed.

After user approval of a DevExtreme source, restore the full solution commands
and tracer, then prove the complete hosted CI passes. Stage 0 cannot be closed
on this transitional CI alone without explicit user acceptance. Per the user's
hosted audit, Six Labors preparation/always-cleanup have succeeded, while past
runs stopped at Restore before builds/tests. Local checks do not establish a
subsequent hosted result.

User approval on 2026-11-04 explicitly accepts the 23/26 transitional CI scope
for phase 0 completion. Resolve the DevExpress package source as a priority after
phase 1 starts; this scope decision is not evidence of a successful hosted run.
Full-solution local section 8 checks remain mandatory.

## Existing DevExpress version retained (user decision 2026-11-05)

The user explicitly considers the existing DevExpress functionality sufficient:
keep the current versions and UI components; do not upgrade, renew a subscription,
or start a replacement project. This decision supersedes the earlier priority to
resolve a new package source during phase 1. Defer that source work and hosted
full-solution CI restoration; neither blocks P1-01 orphan-cleanup acceptance.

Keep the existing local package/cache setup and dependency versions unchanged.
Do not infer any license entitlement or hosted restore capability from a local
build. P1-01 still requires the complete local section 8 checks, actual test
execution, architecture and offline packaging checks. Report hosted CI as
unverified/deferred, never passed, and keep its existing transitional scope
explicit until a later separately authorized change.


## .ccg/tasks/p1-01-closeout/evidence/source-audit.json
{
  "verifiedCommit": "03c8aa28bc8ede00b59ec769737a8ed0ca5d06b8",
  "deletedCount": 51,
  "remaining": 0,
  "deletedPaths": [
    "Line.Messaging/Line.Messaging_Net10.csproj",
    "LineMessagingProcessor/LineMessagingProcessor_Net10.csproj",
    "LinePayCSharp/LinePayCSharp.csproj",
    "LinePayCSharp/LinePayClient.cs",
    "LinePayCSharp/Models/AuthorizationInfo.cs",
    "LinePayCSharp/Models/AuthorizationResponse.cs",
    "LinePayCSharp/Models/Capture.cs",
    "LinePayCSharp/Models/CaptureInfo.cs",
    "LinePayCSharp/Models/CaptureResponse.cs",
    "LinePayCSharp/Models/Confirm.cs",
    "LinePayCSharp/Models/ConfirmInfo.cs",
    "LinePayCSharp/Models/ConfirmPayInfo.cs",
    "LinePayCSharp/Models/ConfirmResponse.cs",
    "LinePayCSharp/Models/ConfirmUrlType.cs",
    "LinePayCSharp/Models/CreditCardBrand.cs",
    "LinePayCSharp/Models/Currency.cs",
    "LinePayCSharp/Models/LanguageCode.cs",
    "LinePayCSharp/Models/PayInfo.cs",
    "LinePayCSharp/Models/PayMethod.cs",
    "LinePayCSharp/Models/PayStatus.cs",
    "LinePayCSharp/Models/PayType.cs",
    "LinePayCSharp/Models/PaymentInfo.cs",
    "LinePayCSharp/Models/PaymentResponse.cs",
    "LinePayCSharp/Models/PaymentUrl.cs",
    "LinePayCSharp/Models/PreApprovedPay.cs",
    "LinePayCSharp/Models/PreApprovedPayInfo.cs",
    "LinePayCSharp/Models/PreApprovedPayResponse.cs",
    "LinePayCSharp/Models/Refund.cs",
    "LinePayCSharp/Models/RefundInfo.cs",
    "LinePayCSharp/Models/RefundList.cs",
    "LinePayCSharp/Models/RefundResponse.cs",
    "LinePayCSharp/Models/Reserve.cs",
    "LinePayCSharp/Models/ReserveAddFriend.cs",
    "LinePayCSharp/Models/ReserveExtras.cs",
    "LinePayCSharp/Models/ReserveInfo.cs",
    "LinePayCSharp/Models/ReserveResponse.cs",
    "LinePayCSharp/Models/ResponseBase.cs",
    "LinePayCSharp/Models/TransactionType.cs",
    "SpeechMessage.Dynamics.Crm82Worker.Tests/packages.lock.json",
    "SpeechMessage.Dynamics.Crm82Worker/packages.lock.json",
    "SpeechMessage.Dynamics.Crm91Worker.Tests/packages.lock.json",
    "SpeechMessage.Dynamics.Crm91Worker/packages.lock.json",
    "Trace/AssemblyInfo.cs",
    "Trace/BSUStackTrace.cs",
    "Trace/BSUTextWriterTraceListener.cs",
    "Trace/Trace.csproj",
    "Trace/Trace.csproj.new",
    "Trace/Trace.xml",
    "Trace/Trace_Fixed.csproj",
    "Trace/Trace_Net10.csproj",
    "Trace/packages.config"
  ],
  "activeSourceAndProjectFilesScanned": 1129,
  "referencePatterns": [
    "Trace.csproj",
    "Trace_Fixed.csproj",
    "Trace_Net10.csproj",
    "BSUStackTrace",
    "BSUTextWriterTraceListener",
    "LinePayCSharp",
    "Line.Messaging_Net10.csproj",
    "LineMessagingProcessor_Net10.csproj",
    "SpeechMessage.Dynamics.Crm82Worker",
    "SpeechMessage.Dynamics.Crm91Worker"
  ],
  "referenceHits": [],
  "beforeDeletionReferenceHits": [],
  "oldTestNames": [
    "RetrieveEntityCollectionByFieldAsync_ShouldReturnEntities",
    "RetrieveEntityCollectionByConditionAsync_ShouldReturnEntities",
    "RetrieveEntityCollectionByConditionsAsync_ShouldReturnEntities",
    "QueryWeeklyReportBeforeTowMonthOfSundayAsync_ShouldReturnReports",
    "RetrievePagedEntitiesAsync_FirstPage_ShouldReturnPagedResult",
    "RetrievePagedEntitiesAsync_WithFilter_ShouldReturnFilteredResults",
    "RetrieveBatchByIdsAsync_ShouldReturnAllMatchingEntities",
    "RetrieveBatchByIdsAsync_WithLargeSet_ShouldHandleEfficiently",
    "RetrieveEntityCollectionByFieldAsync_WithCancellation_ShouldThrowOperationCanceledException",
    "RetrievePagedEntitiesAsync_WithCancellation_ShouldThrowOperationCanceledException",
    "CompareAsyncVsSyncPerformance",
    "ConcurrentAsyncQueries_ShouldNotBlock",
    "PagedQuery_MemoryUsage_ShouldBeLow",
    "RetrieveEntityCollectionByFieldAsync_WithInvalidEntityName_ShouldThrowException",
    "RetrieveBatchByIdsAsync_WithEmptyIdList_ShouldReturnEmptyCollection"
  ],
  "currentTestNames": [
    "RetrieveEntityCollectionByFieldAsync_ShouldReturnEntities",
    "RetrieveEntityCollectionByConditionAsync_ShouldReturnEntities",
    "RetrieveEntityCollectionByConditionsAsync_ShouldReturnEntities",
    "QueryWeeklyReportBeforeTowMonthOfSundayAsync_ShouldReturnReports",
    "RetrievePagedEntitiesAsync_FirstPage_ShouldReturnPagedResult",
    "RetrievePagedEntitiesAsync_WithFilter_ShouldReturnFilteredResults",
    "RetrieveBatchByIdsAsync_ShouldReturnAllMatchingEntities",
    "RetrieveBatchByIdsAsync_WithLargeSet_ShouldHandleEfficiently",
    "RetrieveEntityCollectionByFieldAsync_WithCancellation_ShouldThrowOperationCanceledException",
    "RetrievePagedEntitiesAsync_WithCancellation_ShouldThrowOperationCanceledException",
    "CompareAsyncVsSyncPerformance",
    "ConcurrentAsyncQueries_ShouldNotBlock",
    "PagedQuery_MemoryUsage_ShouldBeLow",
    "RetrieveEntityCollectionByFieldAsync_WithInvalidEntityName_ShouldThrowException",
    "RetrieveBatchByIdsAsync_WithEmptyIdList_ShouldReturnEmptyCollection",
    "QueryFixture_LastQuery_ConcurrentCallsPublishCompleteQueryReference"
  ],
  "preservedOriginalTests": 15,
  "addedTests": [
    "QueryFixture_LastQuery_ConcurrentCallsPublishCompleteQueryReference"
  ],
  "performanceTrait": true,
  "solutionConfigurationCount": 15,
  "solutionConfigurations": [
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|Any CPU.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|Any CPU.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x64.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x64.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x86.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug_LearnCrm|x86.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|Any CPU.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x64.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x64.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x86.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Debug|x86.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|Any CPU.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|Any CPU.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x64.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x64.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x86.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.DebugOracleConnector|x86.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|Any CPU.ActiveCfg = Release|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|Any CPU.Build.0 = Release|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x64.ActiveCfg = Release|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x64.Build.0 = Release|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x86.ActiveCfg = Release|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Release|x86.Build.0 = Release|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|Any CPU.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|Any CPU.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x64.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x64.Build.0 = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x86.ActiveCfg = Debug|Any CPU",
    "{753F7C1C-E6C0-43A9-A740-5D4F32F7C480}.Test_Exchange_Service|x86.Build.0 = Debug|Any CPU"
  ],
  "workerBeforeDeletion": {
    "SpeechMessage.Dynamics.Crm82Worker": [
      "SpeechMessage.Dynamics.Crm82Worker/packages.lock.json"
    ],
    "SpeechMessage.Dynamics.Crm82Worker.Tests": [
      "SpeechMessage.Dynamics.Crm82Worker.Tests/packages.lock.json"
    ],
    "SpeechMessage.Dynamics.Crm91Worker": [
      "SpeechMessage.Dynamics.Crm91Worker/packages.lock.json"
    ],
    "SpeechMessage.Dynamics.Crm91Worker.Tests": [
      "SpeechMessage.Dynamics.Crm91Worker.Tests/packages.lock.json"
    ]
  },
  "performanceSourceSha256": "59da075c303aa77397fa0e63b175da86fe884625e0cac478e00b4324feedc386"
}


## .ccg/tasks/p1-01-closeout/evidence/test-summary.json
{
  "sourceCommit": "03c8aa28bc8ede00b59ec769737a8ed0ca5d06b8",
  "targetCount": 16,
  "total": 2278,
  "passed": 2278,
  "failed": 0,
  "skipped": 0,
  "targets": [
    {
      "assembly": "churchreport.memberinfo.tests.dll",
      "target": "net10.0",
      "total": 623,
      "passed": 623,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214020.trx",
      "sha256": "8ef866cfa7eb0ce9853ca16a182548d2b676a446ca124f62e13ffb320c934aa8"
    },
    {
      "assembly": "line.messaging.tests.dll",
      "target": "net10.0",
      "total": 101,
      "passed": 101,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214014.trx",
      "sha256": "aa7dfd050635528b0ac1e6146a124e5276fb10a038ff654c7d0ae0dc489cddac"
    },
    {
      "assembly": "line.messaging.tests.dll",
      "target": "net48",
      "total": 96,
      "passed": 96,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net48_20261105214014.trx",
      "sha256": "3c0a80282fc9ad913ca9d8014bc6b35599793039382b270853113a6b327abff4"
    },
    {
      "assembly": "linemessagingprocessor.aspnetcore.tests.dll",
      "target": "net10.0",
      "total": 4,
      "passed": 4,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214015.trx",
      "sha256": "80f771e75eea69847bf4848873b423378610560a9f1e7408970e21c95607fcfd"
    },
    {
      "assembly": "linemessagingprocessor.richmenus.tests.dll",
      "target": "net10.0",
      "total": 34,
      "passed": 34,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214011.trx",
      "sha256": "135295711147a2f1892e8eab93860b9f4c851ab0bb8533e46ad4d5a041b39a70"
    },
    {
      "assembly": "linemessagingprocessor.tests.dll",
      "target": "net10.0",
      "total": 79,
      "passed": 79,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214016.trx",
      "sha256": "13cd93f0ff92900ffc9cf9d5f5b2ac1ad1a8311917ea23b82a20e97f156034ec"
    },
    {
      "assembly": "linemessagingprocessor.tests.dll",
      "target": "net48",
      "total": 46,
      "passed": 46,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net48_20261105214013.trx",
      "sha256": "d2fe2b1187fb7dad80fcdb02f4fe59111f70ccba542a1d50dcbd8ff0eac90fc4"
    },
    {
      "assembly": "linemessagingprocessor.workflows.tests.dll",
      "target": "net10.0",
      "total": 33,
      "passed": 33,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214013.trx",
      "sha256": "2d0e322ca5774cdb3bc3dfd18167bde33c5d288eaf5c46f2b84c05c3e8aa6212"
    },
    {
      "assembly": "speechmessage.dynamics.deployment.tests.dll",
      "target": "net10.0",
      "total": 62,
      "passed": 62,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214018.trx",
      "sha256": "aa299c5d2005f625d2ea73c56bbcc1cee20cfb620f46d5ca4f3e7cbb22eeb558"
    },
    {
      "assembly": "speechmessage.dynamics.plugins.tests.dll",
      "target": "net48",
      "total": 25,
      "passed": 25,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net48_20261105214015.trx",
      "sha256": "f1ed5c510607ba25497dc845e235f713cd16688214d583dcd765a4c81004bf3e"
    },
    {
      "assembly": "speechmessage.localization.tests.dll",
      "target": "net10.0",
      "total": 67,
      "passed": 67,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214053.trx",
      "sha256": "4450ae825750b6a26accce243b7af11ddca482ae674a81f652aa3dd16bd07a96"
    },
    {
      "assembly": "speechmessage.localization.tests.dll",
      "target": "net48",
      "total": 43,
      "passed": 43,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net48_20261105214012.trx",
      "sha256": "33690a09643f4b5269992d144bc56609323bf5dc431efae3a0cfa8c608fd18fc"
    },
    {
      "assembly": "speechmessage.payments.tests.dll",
      "target": "net10.0",
      "total": 55,
      "passed": 55,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214012.trx",
      "sha256": "ed99fd0a31d0e0728200aa65563ec6fb2f5077ae876319e15deffbee879fd14e"
    },
    {
      "assembly": "toolutility.dataverse.tests.dll",
      "target": "net10.0",
      "total": 79,
      "passed": 79,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214019.trx",
      "sha256": "39e2351a26cfe70863ceb0cb86f9d7fd78cafab4ca76194902c83f32afb7a2f0"
    },
    {
      "assembly": "toolutility.tests.dll",
      "target": "net10.0",
      "total": 518,
      "passed": 518,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net10.0_20261105214017.trx",
      "sha256": "bbe19eb637c7ba0f5b4e0ec24295fcae16283e07b13066b779bdd8168691366f"
    },
    {
      "assembly": "toolutility.tests.dll",
      "target": "net48",
      "total": 413,
      "passed": 413,
      "failed": 0,
      "trx": "artifacts/p1-01-closeout/release-results/p101_net48_20261105214016.trx",
      "sha256": "a9c2799b475e748f227af8e8289c7f4d54b8145096c3bbfbc272a09340918351"
    }
  ],
  "performanceTests": [
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.QueryWeeklyReportBeforeTowMonthOfSundayAsync_ShouldReturnReports",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveEntityCollectionByConditionsAsync_ShouldReturnEntities",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveBatchByIdsAsync_WithLargeSet_ShouldHandleEfficiently",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveBatchByIdsAsync_WithEmptyIdList_ShouldReturnEmptyCollection",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.ConcurrentAsyncQueries_ShouldNotBlock",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveEntityCollectionByConditionAsync_ShouldReturnEntities",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveEntityCollectionByFieldAsync_WithCancellation_ShouldThrowOperationCanceledException",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.PagedQuery_MemoryUsage_ShouldBeLow",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrievePagedEntitiesAsync_WithFilter_ShouldReturnFilteredResults",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrievePagedEntitiesAsync_WithCancellation_ShouldThrowOperationCanceledException",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrievePagedEntitiesAsync_FirstPage_ShouldReturnPagedResult",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveEntityCollectionByFieldAsync_ShouldReturnEntities",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveEntityCollectionByFieldAsync_WithInvalidEntityName_ShouldThrowException",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.CompareAsyncVsSyncPerformance",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.QueryFixture_LastQuery_ConcurrentCallsPublishCompleteQueryReference",
      "outcome": "Passed"
    },
    {
      "name": "ChurchReport.MemberInfo.Tests.Performance.CollectionQueryServiceAsyncTests.RetrieveBatchByIdsAsync_ShouldReturnAllMatchingEntities",
      "outcome": "Passed"
    }
  ]
}


## .ccg/tasks/p1-01-closeout/evidence/validation-summary.json
{
  "freshValidation": {
    "buildExitCode": 0,
    "buildWarnings": 21,
    "buildErrors": 0,
    "releaseTargets": 16,
    "releaseTestsPassed": 2278,
    "performancePassed": 16,
    "debugTracerPassed": 7,
    "duplicates": {
      "projects": 28,
      "files": 938,
      "groups": 0,
      "new": 0,
      "stale": 0
    },
    "encodingBefore": {
      "checked": 791,
      "excluded": 758,
      "issues": 2
    },
    "encodingAfterBaselineFix": {
      "checked": 791,
      "excluded": 758,
      "issues": 0
    },
    "i18nVerify": true,
    "pluginBuildPassed": 6,
    "pluginMetadataPassed": 10,
    "pluginToolPassed": 27
  },
  "historicalBeforeCleanup": {
    "solutionTargets": 13,
    "solutionPassed": 1125,
    "outsideSolutionToolUtilityPassed": 63,
    "duplicateProjects": 26,
    "duplicateFiles": 879,
    "duplicateGroups": 0,
    "historicalLogs": [
      {
        "name": "p0-release-build.log",
        "sha256": "692a4705bbfe4da7e156ed67f2b19484ac8367fcb291d9f695392a60089bdcfe"
      },
      {
        "name": "p0-release-tests.log",
        "sha256": "df2cde8f0d8bc3e8620acbf310f12fefb45b0f1c71225216670d671b121f0358"
      },
      {
        "name": "orphan-toolutility-tests-baseline.log",
        "sha256": "3e9b28e83889559a9df8426fff600e10b32ad520e28815c5733681e0d3a8eca0"
      },
      {
        "name": "duplicates.log",
        "sha256": "b4087c46e588ce8e0f066934276b0e21983d8de48154019d874fbbc598247584"
      }
    ]
  },
  "freshLogManifest": [
    {
      "path": "artifacts/p1-01-closeout/release-build.log",
      "sha256": "c8da04f0aba9148e53fd17ee75917b7abca89413f306cc640e391fc486891124",
      "bytes": 32812
    },
    {
      "path": "artifacts/p1-01-closeout/release-tests.log",
      "sha256": "e3e5b24199d8832904b727f6eaad3a6fc1373ea06dfc13cbb46e903543841666",
      "bytes": 9506
    },
    {
      "path": "artifacts/p1-01-closeout/debug-tracer.log",
      "sha256": "89f532e4482cfa9c8767b2fa26ef99d4bf75850b280f0ab80f87ade79bb68b03",
      "bytes": 5339
    },
    {
      "path": "artifacts/p1-01-closeout/debug-tracer-positive.log",
      "sha256": "94279616002627609bc9483374df50a394f0bbf2d83cfa94716d4a0f07dfffa1",
      "bytes": 35
    },
    {
      "path": "artifacts/p1-01-closeout/duplicates.log",
      "sha256": "190769034a3d920f5325b37ca3f087218a8db360a16763d7930cb0b69b092481",
      "bytes": 84
    },
    {
      "path": "artifacts/p1-01-closeout/encoding-baseline.log",
      "sha256": "3f631c21e0c5dcf640a7bb81fc34342868024278b0cab319235e29ddc5070561",
      "bytes": 209
    },
    {
      "path": "artifacts/p1-01-closeout/encoding-fixed-baseline.log",
      "sha256": "8e28294fc85b8c6b02b0bd51165ec852cfee5b30a157b426c3a77e7267c5a8d5",
      "bytes": 73
    },
    {
      "path": "artifacts/p1-01-closeout/i18n.log",
      "sha256": "88a630c621fad9160bb1cdcfa1d6df50b33a8cf17c51f6dbc12709405e7cb0cd",
      "bytes": 64604
    },
    {
      "path": "artifacts/p1-01-closeout/plugin-build.log",
      "sha256": "b5f3f9a873e56163dca2051ab911a434e8e7034716d09ed6087c0b7680e7042c",
      "bytes": 227
    },
    {
      "path": "artifacts/p1-01-closeout/plugin-metadata.log",
      "sha256": "4bdf69dda3675c169b05136d234741c2ef393b118de8c5bd00eee3cbfb0632f6",
      "bytes": 962
    },
    {
      "path": "artifacts/p1-01-closeout/plugin-tool.log",
      "sha256": "897db4d571aca37cf18d46c0b3406534045bb454243a99e5834207ee2a94fe94",
      "bytes": 1239
    }
  ],
  "hostedCi": "deferred, not verified",
  "productSourceChanges": false
}


## .ccg/tasks/p1-01-closeout/evidence/baseline-format-fixes.json
[
  {
    "path": ".ccg/tasks/p013-apply-fault-investigation/.turns.json",
    "change": "append final CRLF only",
    "beforeSha256": "19a63b5262bde9fa4b84ddcc9f30938d75572c744fc0e865d0936f3fb6f31d29",
    "afterSha256": "e54e64a18c33fc5f5351bdb14186893d3a8f400533625659b3a3726d49eacfa4",
    "allOriginalBytesUnchanged": true
  },
  {
    "path": ".trellis/workspace/codex/index.md",
    "change": "append final CRLF only",
    "beforeSha256": "1f1051d8e1d478dd4f7a6e7c8a7b5a885ab84a988b61c452d0d247c4c74face1",
    "afterSha256": "05fd104e683d0cdac1f519a83974e2867d2f472be27e391cdf6a5a998cf2ecee",
    "allOriginalBytesUnchanged": true
  }
]


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
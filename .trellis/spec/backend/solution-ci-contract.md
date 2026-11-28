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

P2-01 `SpeechMessage.Hosting` is a net10-only module and therefore has one
independent `SpeechMessage.Hosting.Tests` / `net10.0` coverage case. Its
architecture sources are owned by `Hosting.Tests`; removing the old linked
Compile from `SpeechMessage.Localization.Tests` must be accompanied by both
the solution filter entry and this coverage case. A passing Localization run
alone is not evidence that the moved architecture checks executed.

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

## 架構來源守門與未通過差距契約（P2-01）

### 1. 範圍與觸發

Hosting.Tests/Architecture 是 R2／R3／R4／R9 唯一來源。新增目標、條件參考或來源
語法時，必須同步正反例及兩種組態，不由 Localization.Tests 重複編譯。

### 2. 介面

`ReadSolution(configuration)`／`ReadProjects(root, paths, verifyRestoredAssets, configuration)`
只接受 Debug、Release。`ValidateSource(projects, observePublicType = null)` 回傳規則、專案、目標、組態、來源
與完整型別／依賴身分；不回傳來源本文、token或設定。

### 3. 契約

- Roslyn 使用 DefineConstants、Compile 及已評估 Using 建立同組態語法／語意；禁止
  以 Regex 自行模擬 C# 註解、raw string、可見性、別名或繼承。
- Plugin 入口使用真 SDK IPlugin／CodeActivity 組件身分；相似名稱、來源假介面不合格。
- R3 比對解碼後實際值，白名單固定完整專案／方案相對路徑／值，理由及移除條件必填。
- R9 不依賴 Compile 非空；檢查普通Reference、FrameworkReference、已還原相依及
  目前目標相依閉包。HttpClient雙目標例外只有Line.Messaging、LineMessagingProcessor.Core。
- SDK隱含可用Framework組件不等於輸出AssemblyRef；忽略時須同時匹配真正SDK定義及
  implicit metadata，並檢查Debug／Release Plugin產物的實際組件參考。
- 單來源16MiB、每目標64MiB、同次唯一文字256MiB；嚴格UTF-8、拒跨工作樹與reparse。
  metadata僅由單次掃描持有且finally Dispose，不執行產品DLL、不保存跨呼叫可變快照。

### 4. 拒絕矩陣

語法錯誤回SourceSyntax；錯誤UTF-8或來源超預算明確失敗；Plugin無Compile仍拒絕
Payments／System.Drawing相依。global using不可漏掉HttpClient，raw文字不能冒充宣告。

### 5. 正常／既有／錯誤

真正SDK別名／繼承入口合法；ChurchReport只允許框架入口、真正MVC控制器／檢視元件、
由其公開簽章展開的必要型別，以及附重現理由與證據的明列例外。其餘公開型別、新來源、
同名偽入口、白名單路徑拼接或任何新增R3／R9必須紅燈；不得恢復清冊豁免。

### 6. 必要斷言

合成語法反例、真正XML條件來源、大小／解碼故障、完整方案兩組態R4及來源嚴格零違規、
Plugin產物AssemblyRef與程序drain。完整方案每次同一快照同時驗R4及來源；清冊必須resolved且空。

### 7. 錯誤與正確

錯誤：`Assert.All(violations, x => x.Project == ChurchReport)`使整個產品任意違規綠燈。
正確：`unresolved-r2-inventory.json`固定為resolved且entries空陣列，僅保留歷史用途；
`Solution_EvaluatedGraphAndSource_MatchContractsAndUnresolvedInventory`保名，但同時要求
Validate及ValidateSource零違規。`R2_Inventory_IsEmptyAndResolved`與雙組態
`R2_ChurchReport_PublicTypes_AreOnlyEntryControllerOrSignatureRequired`獨立防回歸。
公開表面統計使用同次Roslyn分類的operation-local接收器，不維護人工允許大清單、不保存語意模型。

## F7 Assets 精確架構分類（2026-11-28）

`SpeechMessage.Assets` 是 net10 圖片共用引擎，精確登錄於既有第50層，
和 Scheduling、Access、ASP.NET Core adapters 同層。產品主機可向下消費，
同層互參、基礎層反向、共用層參考產品、雙目標核心引用net10單目標仍拒絕。
不得以 Assets 名稱前綴、Tests 後綴或共用公開契約授予主機R2豁免。
`SpeechMessage.Assets.Tests` 只由實際 `IsTestProject` 評估為測試，移除標記仍須拒絕。
線A維護唯一架構來源與合成圖正反例；線B保留圖片程式與真方案評估所有權。
完整守門不可和使用同一輸出目錄的實機部署並行：SDK載入的DLL會被Windows鎖住，
此時建置失敗須保留，部署排空後完整重跑，不得列環境限制或刪除載入中的檔案。

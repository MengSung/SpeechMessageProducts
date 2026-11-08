# Plugin Packaging Contract

## 1. Scope / Trigger

P0-10 originally built the net48 DiagnosticsPingPlugin shell. Per the user's
2026-11-04 D21 authorization, the deployment Plugin and every input merged into
it now target .NET Framework 4.6.2 (`net462`). The separate deployment DLL is
validated offline. No package test or build connects to D365 or LINE.

## 2. Signatures

```powershell
dotnet build SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true -p:SpeechMessageBuildNumber=37
powershell -NoProfile -File tools/PluginPackaging/Test-PluginAssembly.ps1
powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
# CI sample mode (temporary test key only):
powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1 -UseEphemeralTestKey
dotnet test tools/PluginPackaging.Tests -c Release
```

The checker optionally accepts `-AssemblyPath <file-or-local-wildcard>`.
It emits one JSON result per DLL and fails the whole run if any DLL fails.

## 3. Contracts

- `ILRepackTargetsFile` points to `build/PluginMerge.targets`. The package's
  default target merges every bin DLL, overwrites the primary assembly, and
  deletes dependencies; that breaks test type identity and can embed SDK DLLs.
- Merge exactly the primary DLL, Localization, Core, and Newtonsoft.Json.
  Internalize dependencies, leave Xrm SDK external, and write only the dedicated
  `artifacts/plugins` deployment output. Never overwrite the original test DLL.
- The standalone .NET 10 metadata validator must be built with the ordinary
  `dotnet build` restore/build flow and a forced restore when invoked by the
  packaging target. Disable static-graph restore for this nested tool build so
  it cannot accidentally depend on another project's `obj/project.assets.json`
  or intermediate-path calculation. Do not invoke nested `MSBuild` targets as
  `Restore;Build` from the active solution build.
- The primary Plugin, Localization, Core, and Newtonsoft.Json merge inputs must
  each declare a .NET Framework target no higher than v4.6.2; the merged
  deployment assembly must declare v4.6.2. The metadata checker reads PE
  metadata without loading the files. Do not merge .NET Standard facades,
  including `System.Runtime`. The common PE checker rejects both facade assembly
  identities and facade references before merging and when validating the final
  DLL, through a separate `checks.noNetStandardFacade` field. The bounded,
  immutable name set consists of the Facades directory names from the fixed
  net462 ReferenceAssemblies 1.0.3 package plus `netstandard`; compare exact
  assembly simple names case-insensitively, without blocking all `System.*`
  references or consulting the runtime machine. Keep the original allowed-reference
  and other checks independently active.
- `net48` may remain a test-host target, but tests that reference product
  projects must resolve and verify their actual `net462` product outputs.
- Read the formal key only from process environment
  `SPEECHMESSAGE_PLUGIN_KEYFILE`. Do not read, copy, print, or record its contents.
  Never generate a replacement formal key.
- Packaging is opt-in (user approval 2026-11-03): only
  `SpeechMessagePackagePlugin=true` executes the merge target. Ordinary Debug
  and Release builds never enter it, read the signing key, or create artifacts.
  CI without a signing secret must build and test normally, then report packaging
  as skipped. CI with a secret restores it only to runner temp and always cleans
  it in a separate `if: always()` step; never print or upload the key.
- Missing key: Release packaging fails with SMP0001; Debug emits SMP0002 and an
  unsigned output. A configured missing file fails with SMP0003.
- AssemblyVersion remains 1.0.0.0. FileVersion and InformationalVersion carry
  `SpeechMessageBuildNumber`, default 1, supplied by the build when publishing.
- The .NET 10 helper uses System.Reflection.Metadata/PEReader, never loads or
  executes the target DLL. It closes its stream before returning.
- All request/context/result state stays local to Execute. Only immutable step
  configuration is retained on the reusable plugin instance. Core alone owns
  HTTP and its ten-second operation timeout.
- Retryable failures preserve the original transport exception as InnerException;
  unexpected failures trace only the type and rethrow the same exception.
  Stage 0-4 monitoring uses system jobs and plugin traces; no notifier or file
  logging is added to the sandbox.

## 4. Validation & Error Matrix

| Input / condition | Required result |
| --- | --- |
| Signed net462 public DiagnosticsPingPlugin, allowed references, under 16 MiB | JSON ok=true, exit 0 |
| Signed net48 public DiagnosticsPingPlugin | targetFramework=false, exit 1 |
| No public key/token or no StrongNameSigned flag | strongName=false, exit 1 |
| Wrong/missing TargetFrameworkAttribute | targetFramework=false, exit 1 |
| Reference outside mscorlib, System, System.*, Xrm SDK/Proxy | allowedReferences=false, exit 1 |
| System.Drawing or a child reference | noDrawing=false, exit 1 |
| Framework facade identity or reference, including System.Runtime, System.Collections or netstandard | noNetStandardFacade=false, exit 1 in both entry points |
| Missing/private diagnostic type | diagnosticsPlugin=false, exit 1 |
| Length at least 16 MiB, corrupt PE, unavailable input | Fail closed with fixed reason, no original path/message |
| Mixed directory containing valid and invalid DLLs | Inspect every DLL; aggregate exit 1 |

## 5. Good / Base / Bad Cases

- Good: merged deployment DLL passes metadata checks while unit tests keep the
  original bin assembly and dependency identities.
- Base: a trace-only invocation sends no HTTP and writes both diagnostic cultures.
- Bad: treating an arbitrary compile/restore failure as a functional red test,
  executing target assemblies during inspection, or merging bin/*.dll.

## 6. Tests Required

Preserve the seven planned plugin methods and two approved exception methods.
Add bounded A/B registration reuse with a shared HTTP client, drain counters,
configuration rejection, and synchronization-context tests.
Keep ordinary Debug/Release-without-key builds, opt-in missing/invalid-key
failures, and signed/unsigned samples and independently inject framework, dependency,
visibility, drawing, size, image and mixed-directory faults. Temporary test
keys belong to a unique fixture, are never formal keys, and must be deleted.
The signed positive fixture targets net462; a signed net48 fixture is a
required negative case so the previous target cannot return unnoticed.
The in-process helper suite must also prove immediate exclusive file reopening;
its writer is operation-owned, avoiding global Console replacement in tests.

## 7. Wrong vs Correct

Wrong: ILRepack takes every bin DLL and writes back into the plugin test output;
the test process sees two assemblies claiming the same referenced Core types.

Correct: an explicit four-input merge writes to artifacts, while the unmerged
bin DLL and SDK references remain unchanged for the normal build/test graph.

## P2-04 前置的診斷字串契約（2026-11-08）

`DiagnosticsPingPlugin.Execute` 將 Core 的 `LinePushOutcome.Success` 映射為外部追蹤 `Outcome=Accepted`；其他分類原樣保留。公開 enum 與 HTTP 分類不改，不能讓 409、429 或永久失敗冒充首次受理。

部署 smoke 嚴格消費 `Accepted`＋`Status=200`，一頁實機證據另查有效 RequestId、無敏感值與殘留 0。測試 `Execute_PushMode_EmitsAcceptedForSmokeContract` 必須走真實 Core＋假 HTTP 200，斷言這段追蹤、禁止 token／收件者／內文，並確認回應 Dispose；既有 Retryable／Permanent、隔離與 drain 案例保留。

錯誤：直接格式化 `result.Outcome` 導致輸出 `Success`，或放寬 smoke 只看 200。正確：只在診斷邊界明確轉譯，保持 Core ABI 與驗收條件。

live DLL hash 等於本機、合併 IL 含新字串，不代表沙箱已執行新碼。2026-11-08 兩輪 smoke 均仍見舊字串；這是觀察，不是已證實的快取根因。停止重送、保存安全證據與收尾，未核對主機／服務隔離前不重啟共用服務，也不擅改版本契約。

## P2-04 前置已通過的後續事實（2026-11-08）

12:46 唯讀核對同一固定 step→plugintype→pluginassembly，唯一型別、Sandbox(2)、Asynchronous(1)、PostOperation(40)；live FileVersion仍1.0.0.1。使用者確認已重啟D365-9.1 Sandbox／Async／IIS並接受共用組織短暫影響（原重啟時間未提供），本次依既定SpeechMessageBuildNumber=2將FileVersion遞增至1.0.0.2，AssemblyVersion仍1.0.0.0。只跑一次plan/apply/smoke後，smoke=0、Accepted／200／有效RequestId、敏感值0，收尾verify=0、trace=1、disabled／無secure，自己task／trace／job殘留0。

本次不啟用舊字串降級路線。服務重啟與FileVersion遞增在同一驗證窗內，不能聲稱已隔離出唯一沙箱快取根因；後續部署仍以實際執行追蹤驗新碼，不以資料庫DLL hash單獨推定生效，不更動固定AssemblyVersion。

## P2-12：部分信任 Flex 序列化（2026-11-09）

- 適用／介面：`FlexTemplateEngine.Fill` 的私有容器進入 `LinePushClient.FreezeMessages`；`CreateContainerConverter()` 只供 Core 當次 serializer 使用，不改公開 API、enum 或全域 JSON 設定。
- 契約：不能假設 D365 沙箱允許反射存取非公開容器。Core 明確加入無狀態、只寫入且精確匹配 TemplateContainer 的 converter，直接寫 type 與已驗 JSON 節點，writer 仍受原256Ki容量限制；不公開可變 JToken、不快取內容。
- 故障矩陣：沒有私有反射權限的 Framework AppDomain，舊版會在 TemplateContainer.Type getter 產生 JsonSerializationException／MethodAccessException；修正版須經真 Core 把 bubble／carousel、文字與 server sender 完整交給合成 HTTP handler。既有一般 SDK 型別及 FullTrust JSON 相容保留。
- 正常／基本／錯誤：明確 converter 不需提高權限；一般 SDK 訊息沿原 serializer；禁止放寬 ReflectionPermission、公開私有投影或放寬 R2／metadata 來消除錯誤。
- 測試：`MemberFlex_WithoutPrivateReflection_SendsCompleteSnapshot` 兩案，僅 Execution 與測試輸出唯讀；無網路權限、真引擎／Core及合成 handler，finally 卸載 domain，using 排空並 Dispose HTTP 資源。
- 反例／正例：錯誤為把 FullTrust 合併六案當成沙箱權限驗收；正確為保留合併六案及 metadata，另有部分信任回歸，再由核定後實機證明。離線重現同型錯誤仍不等於已讀到原 trace 的完整根因；本次原 trace 僅備份 metadata，不能宣稱根因已由實機堆疊證實。

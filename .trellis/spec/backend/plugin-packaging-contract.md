# Plugin Packaging Contract

## 1. Scope / Trigger

P0-10 builds the net48 DiagnosticsPingPlugin shell and a separate deployment DLL.
All verification is offline. No package test or build connects to D365 or LINE.

## 2. Signatures

```powershell
dotnet build SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessageBuildNumber=37
powershell -NoProfile -File tools/PluginPackaging/Test-PluginAssembly.ps1
powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginAssembly.Tests.ps1
powershell -NoProfile -File tools/PluginPackaging/Tests/Test-PluginBuild.Tests.ps1
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
- Read the formal key only from process environment
  `SPEECHMESSAGE_PLUGIN_KEYFILE`. Do not read, copy, print, or record its contents.
  Never generate a replacement formal key.
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
| Signed net48 public DiagnosticsPingPlugin, allowed references, under 16 MiB | JSON ok=true, exit 0 |
| No public key/token or no StrongNameSigned flag | strongName=false, exit 1 |
| Wrong/missing TargetFrameworkAttribute | targetFramework=false, exit 1 |
| Reference outside mscorlib, System, System.*, Xrm SDK/Proxy | allowedReferences=false, exit 1 |
| System.Drawing or a child reference | noDrawing=false, exit 1 |
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
Keep signed/unsigned samples and independently inject framework, dependency,
visibility, drawing, size, image and mixed-directory faults. Temporary test
keys belong to a unique fixture, are never formal keys, and must be deleted.
The in-process helper suite must also prove immediate exclusive file reopening;
its writer is operation-owned, avoiding global Console replacement in tests.

## 7. Wrong vs Correct

Wrong: ILRepack takes every bin DLL and writes back into the plugin test output;
the test process sees two assemblies claiming the same referenced Core types.

Correct: an explicit four-input merge writes to artifacts, while the unmerged
bin DLL and SDK references remain unchanged for the normal build/test graph.

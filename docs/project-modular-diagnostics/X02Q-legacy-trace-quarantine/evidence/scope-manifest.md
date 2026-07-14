# X02Q Scope Manifest

Module: X02Q
Workspace: `docs/project-modular-diagnostics/X02Q-legacy-trace-quarantine/`
Mode: DIAGNOSIS_ONLY
Gate status: QUARANTINE
Nested agent count: 0

## Owner Boundary

X02Q owns only the legacy `Trace/**` quarantine boundary. The module map assigns `Trace/Trace.csproj`, `Trace/Trace_Fixed.csproj`, and `Trace/Trace_Net10.csproj` to X02Q, marks all three as not included in the solution, and limits this workspace to responsibility discovery, consumer proof, canonical project decisions, and safe quarantine/ownership boundaries.

## Repository Evidence

Read-only inventory found these files under `Trace/**`: `AssemblyInfo.cs`, `BSUStackTrace.cs`, `BSUTextWriterTraceListener.cs`, `packages.config`, `SpeechMessageCrmKey.snk`, `Trace.csproj`, `Trace.csproj.new`, `Trace.xml`, `Trace_Fixed.csproj`, and `Trace_Net10.csproj`.

The active solution has no `Trace` project reference. Repository symbol scans found no product-code consumer outside `Trace/**`; matches outside `Trace/**` are documentation or historical upgrade notes.

## Project Metadata

All three Trace project files declare `TargetFramework` `net10.0`, `RootNamespace` `TraceNameSpace`, `AssemblyName` `Trace`, signing enabled, and `AssemblyOriginatorKeyFile` `SpeechMessageCrmKey.snk`.

`Trace/packages.config` still lists `Newtonsoft.Json` `13.0.4` with `targetFramework="net452"`; this is treated as historical metadata, not active runtime dependency proof.

## Consumers

Confirmed product consumers: none found.

Historical/documentation consumers include `ToolUtility/文件/ToolUtilityClass.cs_原始檔案.md`, `SpeechMessageProducts.ChurchReport/文件/升級ToolUtility/**`, `SpeechMessageProducts.ChurchReport/文件/升級Trace/**`, `SpeechMessageProducts.ChurchReport/文件/效能優化計畫/**`, and `SpeechMessageProducts.ChurchReport/文件/記憶體優化/**`.

## Ownership Boundary

Safe next decisions are F01A build-governance retain/retire/canonical-project decisions and possible X02B observability handoff only after executable tests and a real consumer are proven. No Trace source optimization is approved in X02Q.

# Logging Guidelines

> How logging is done in this project.

---

## Overview

<!--
Document your project's logging conventions here.

Questions to answer:
- What logging library do you use?
- What are the log levels and when to use each?
- What should be logged?
- What should NOT be logged (PII, secrets)?
-->

(To be filled by the team)

---

## Log Levels

<!-- When to use each level: debug, info, warn, error -->

(To be filled by the team)

---

## Structured Logging

<!-- Log format, required fields -->

(To be filled by the team)

---

## What to Log

<!-- Important events to log -->

(To be filled by the team)

---

## What NOT to Log

<!-- Sensitive data, PII, secrets -->

(To be filled by the team)

## Unified ChurchReport Diagnostic Trace Contract

正式錯誤紀錄是獨立契約：所有組態都先寫入並 flush `Logs/Exception.log`，再排入 LINE。
下方 Release 禁寫規則只涵蓋既有三個 Trace 檔，不適用 `Exception.log`，也不得用
`DiagnosticsTrace:Enabled` 停用錯誤紀錄。詳見 [Error Handling](./error-handling.md)。

`Exception.log` 使用 UTF-8 JSONL，每筆含安全的例外型別、程式位置、UTC 與 IncidentId。
正常上限為 5 MiB 並保留五份備份；外部讀取鎖阻止輪替時可有界附加至 10 MiB，
解鎖後恢復輪替。讀取工具須允許 `FileShare.ReadWrite | FileShare.Delete`。
達硬上限、磁碟滿或權限失敗時只輸出固定 stderr 狀態，不得先發送 LINE。
LINE queue 滿載／發送失敗以同 IncidentId 追加本地狀態，不再觸發通知。

### 1. Scope / Trigger

This contract applies when ChurchReport diagnostics write any of the three files:
`dataverse-trace.jsonl`, `Trace.log`, and `CHURCH_REPORT_TRACE.TXT`.
The feature crosses configuration, the ASP.NET composition root, ToolUtility,
Dataverse pooling, and the read-only PowerShell analyzer.

### 2. Signatures

- `DiagnosticTraceOptions.FromConfiguration(IConfiguration, string contentRootPath, bool allowEnabled)` is the only product-level configuration resolver.
- `DiagnosticTraceOptions.CreateDisabled(string contentRootPath)` is the Release composition-root path and always returns `Enabled == false`.
- `DataverseTraceOptions.FromDiagnosticOptions(DiagnosticTraceOptions)` derives the JSONL writer state and path; it does not read another configuration section.
- `Analyze-ChurchReportTraces.ps1 -TraceDirectory <dir> [-DataverseTracePath <path>] [-ApplicationTracePath <path>] [-ToolUtilityTracePath <path>] [-ReportPath <path>]` analyzes all three files.

### 3. Contracts

- The only normal operator setting is:

  ```json
  "DiagnosticsTrace": {
    "Enabled": true,
    "Directory": "D:\\除錯追蹤"
  }
  ```

- File names are code contracts: `dataverse-trace.jsonl`, `Trace.log`, and `CHURCH_REPORT_TRACE.TXT`.
- Debug reads `DiagnosticsTrace:Enabled`; Release constructs disabled options and does not compile or register file listeners/providers.
- Relative directories resolve from the trusted content root. Request, Session, tenant, identity, and user input are never path sources.
- `Program` is the only owner of the process-global `Trace.log` listener. `FileToolUtilityTracer` and legacy `TraceLogger` use private writers and never add global listeners.
- Input traces are opened read-only with `FileShare.ReadWrite | FileShare.Delete`; the analyzer streams lines and writes a UTF-8-without-BOM Markdown report.
- Analyzer exit codes are `0` for PASS/WARN report, `1` for analyzer/report-generation failure, and `2` for a successfully generated report containing FAIL evidence.

### 4. Validation & Error Matrix

| Condition | Required result |
|---|---|
| Release setting or environment variable says `Enabled=true` | Keep all three file writers disabled; no trace directory/file side effect |
| Debug `Enabled=false` | Do not create or append any of the three files |
| Invalid diagnostic directory configuration | Fail closed to disabled options; do not fall back to a hidden hard-coded user path |
| JSONL parse error | Report FAIL and exit 2; never include the raw invalid line |
| Missing request/lease pair | Report FAIL and identify counts only |
| Missing input file or no events | Report WARN; never report a complete PASS |
| Sensitive-pattern hit | Report FAIL with pattern counts only; never copy the raw value |
| Queue/endpoint/category aggregation limit exceeded | Report WARN and state that evidence is partial |
| Listener initialization or disposal exception | Release owned stream/listener resources and keep the application path alive |

### 5. Good / Base / Bad Cases

- Good: Debug `DiagnosticsTrace:Enabled=true` creates all enabled writers under one trusted directory; disposing the application releases every handle.
- Base: A trace file is still being appended while the analyzer reads it; the analyzer reports the readable snapshot and does not lock or mutate the source.
- Bad: `FileToolUtilityTracer` adds a `TextWriterTraceListener` per request, or a public logger constructor silently defaults to a second hard-coded path.
- Bad: Release honors `DiagnosticsTrace__Enabled=true` and creates a file writer.
- Bad: A report prints a complete legacy trace line to explain a sensitive-data match.

### 6. Tests Required

- `DiagnosticTraceOptionsTests` asserts fixed filenames, one directory, disabled no-op behavior, Dataverse derivation, and legacy logger no side effect.
- `FileToolUtilityTracerTests` asserts repeated writes do not grow `Trace.Listeners`, low-level writes remain lazy, and disposal permits reopening the file.
- `DataverseTraceTests` asserts request/lease pairing, pseudonym format, pool isolation, fault eviction, bounded queue behavior, and deterministic drain/dispose.
- PowerShell fixtures must cover valid three-file input, invalid JSONL, missing files, unpaired leases, `[Perf]`/N+1/Gap/Startup, Big5 legacy input, sensitive scan suppression, and exit codes `0`/`2`.
- Debug smoke must prove disabled creates zero files and enabled creates `Trace.log`; Release smoke must prove an injected `DiagnosticsTrace__Enabled=true` creates zero trace files.

### 7. Wrong vs Correct

Wrong:

```csharp
var enabled = configuration.GetValue<bool>("EnableTrace", false);
services.AddSingleton<FileToolUtilityTracer>();
```

Correct:

```csharp
var options = DiagnosticTraceOptions.FromConfiguration(configuration, contentRoot, allowEnabled: true);
services.AddSingleton(options);
services.AddSingleton<IToolUtilityTracer>(options.Enabled
    ? new FileToolUtilityTracer(options)
    : new NullToolUtilityTracer());
```

The Release composition root must instead pass `allowEnabled: false` or call
`CreateDisabled`, so deployment configuration cannot bypass the compile-time
fail-closed boundary.

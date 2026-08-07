# P7.1 Implementation Plan

## 檔案邊界

- Modify: `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs`
- Modify/Test: `SpeechMessage.Dynamics.Tests/Data8ProfileOperationExecutorTests.cs`
- Preserve/Test: `SpeechMessage.Dynamics.Tests/Package01OperationRegistryTests.cs`、`Package01FeeReadClientTests.cs`
- Modify only when a contract gap is proven: `SpeechMessage.Dynamics.Abstractions/Operations/*`、
  `SpeechMessage.Dynamics.ProductClient/FeeReads/*`、`ChurchReport.MemberInfo.Tests/*`
- Task artifacts and a sanitized operator handoff are owned by this task. No Registry/consumer flag/ChurchReport
  production change is permitted unless a failing test proves it is needed for the six operation contracts.

## 順序

- [ ] 以 TDD 新增六個 Data8 executor tests：valid typed requests success、unknown/parameter mismatch fails before
      Pool、connector failure preserves fixed error、invalid scalar projection faults lease、cancellation/timeout
      leaves no active lease/permit。
- [ ] 執行 focused test，確認目前 WhoAmI-only implementation 如預期失敗。
- [ ] 實作最小封閉 operation map、parameter validation、bounded connector template request 與 strict DTO
      projection；不新增 generic request path。
- [ ] 補足 Embedded/Dedicated composition/DI tests，證明同一 ProductClient contract 可被選取，且
      `Package01FeeReadsEnabled=false` 不建立 product traffic 或改變 legacy response。
- [ ] 執行 focused Dynamics/ChurchReport tests、Release build、P7.0 validator、encoding/CRLF、diff/scope check；
      視 credible lifecycle risk 執行既有 soak/lease diagnostics。
- [x] 在 repository gate 綠後建立 PowerShell 5.1-compatible sanitized Data8 read evidence handoff；
      handoff 固定使用 `sunnyvalechback` CE 9.1 的 Embedded + Data8，缺 fixture/evidence 時只停該 gate，
      不開旗標、不重建 P6 profile。操作說明保存在 `p7.1-data8-read-evidence-handoff.md`。
- [x] 完成 Trellis check 與 spec judgment；task-owned commit/archive 依本最終驗證執行，且不得 push。

## 回滾

P7.1 的 rollback 是保持 `Package01FeeReadsEnabled=false`、不修改 consumer selection，並移除/關閉僅本
task 新增的 executor capability path。不得用切換 connector、CE version 或 request-time fallback 取代 rollback。

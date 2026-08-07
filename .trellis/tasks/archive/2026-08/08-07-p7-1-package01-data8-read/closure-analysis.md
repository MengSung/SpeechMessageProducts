# P7.1 Closure Analysis

## Live evidence result

The Lenovo CE 9.1 read-only gate returned `outcome=go` on 2026-08-07: all six
allowlisted operations reported `status=succeeded`. The sanitized evidence is
recorded in `p7.1-data8-read-evidence.md`; it retains no fixture GUID, user,
endpoint, credential target, password, token, cookie, raw CRM payload, or raw
exception.

## Additional spec update

The Windows PowerShell P7.1 evidence handoff now reads its fixed local Generic
Credential only after repository and fixture validation, injects the secret
only into its short-lived process for the child test, and restores the prior
environment in `finally`. `CredRead` owns one native handle and must pair with
`CredFree`; unreadable credentials fail closed before `dotnet` or CE work.
This reusable lifecycle contract is recorded in the backend hosting-and-routing
specification.

The post-review correction makes the lifecycle rules executable: the handoff
captures all overridden process variables before any validation can exit, and
best-effort temporary-directory deletion cannot interrupt credential clearing
or environment restoration. Separately, both Data8 projection branches now
enforce `MaximumPageBytes` for each page before adding to the cumulative
response budget; the offline regression injects an over-budget fee and
stor-lesson page and proves both fail closed with deterministic disposal.

## Scope result

P7.1 的離線實作、測試與 repository quality gate 已完成。六項固定
Package01 Data8 read operation 均由 connector 內部擁有 query/projection；
ProductClient 只看 typed DTO，Embedded 與 Dedicated 的 contract parity 仍由
既有離線測試保護，`Package01FeeReadsEnabled` 沒有改變。

Official Worker 與 P6.2 startup 沒有執行。ChurchReport 的 CE 9.1 Data8 真機
唯讀 evidence 已由 Lenovo operator handoff 的最後一行 sanitized JSON 證實為 `go`；
這項結論依據六個 allowlisted operation 都回傳 `status=succeeded`，不是由 registry、
unit test、build 或 browser 登入推論而得。

## Offline verification record

截至 2026-08-07，P7.1 的 repository 內 gate 已重新完整驗證：

- `SpeechMessage.Dynamics.Tests`（Release）為 475 passed、7 skipped；
- `ChurchReport.MemberInfo.Tests`（Release）為 395 passed、2 skipped，兩個 skip
  均為未提供 explicit opt-in live evidence 的測試；
- `SpeechMessageProducts.sln` Release build 為 0 warnings、0 errors；
- P7.0 archived coverage validator 的 7 個 contract tests 與 `--build`/normal
  validation 都通過；其 archive-path root discovery 已改為 `.trellis/tasks`
  structural anchor，避免封存後錯把 `.trellis/tasks` 視為 repository root；
- P7.1 handoff PowerShell tests、byte-level UTF-8 no-BOM/CRLF/final-CRLF scan、
  與 `git diff --check` 均通過。

上述離線檢查保護 contract、lifecycle 與 handoff safety；另由記錄的 sanitized `go`
evidence 證實六項固定 CE read operation 已執行成功。這不代表 ChurchReport consumer
流量已切換：`Package01FeeReadsEnabled` 維持 `false`，Official Worker/P6.2 未啟動。

## Spec update judgment

本次有新的、可重複使用的跨層契約，因此 **有 spec update 必要**。新增的規則是：

1. P7.1 live evidence 只能使用固定 `sunnyvalechback` CE 9.1 Data8 Embedded
   lane；Dedicated listener evidence 留在 P7.4。
2. PowerShell handoff 必須以 `SPEECHMESSAGE_P7_1_LIVE` 加上六個 bounded
   `P7_1_*` fixture variables opt-in；固定 Windows Generic Credential 的密碼只能
   暫時注入 child process，不可成為 command-line、持久化環境變數或輸出欄位。
3. handoff 必須以有界 child process、TRX marker、固定 operation allowlist、
   runtime dispose 後的一行 sanitized JSON 判定結果；skip、missing marker、
   timeout 與 malformed marker 都不可視為 `go`。

上述 executable contract 已寫入：

`.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的
`P7.1 Data8 read evidence handoff` 章節。

## Remaining external gate

P7.1 沒有剩餘外部 gate。其完成不會倒退或重新開啟 P6，也不會啟動 Official Worker；
後續 P7.2 必須另立 task，並在隔離 CE 環境準備可清理的寫入測試資料後才可開始。

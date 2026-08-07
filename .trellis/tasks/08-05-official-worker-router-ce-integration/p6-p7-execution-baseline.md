# P6/P7 Execution Baseline（歷史 checkpoint）

Recorded: 2026-08-06

## Scoped planning baseline

- Commit: `b098887efbdfbe3c952c94fac2e878b0c0e6d9e3` (`docs: rebaseline P6 P7 execution plan`)
- Scope: 15 reviewed P6/P7 planning, task, roadmap, and gateway-routing documents.
- Excluded: `.ccg/tasks/harden-churchreport-error-recovery/.turns.json`. It was unrelated and uncommitted when this baseline was recorded, and was never staged in a P6/P7 change set. It was subsequently committed independently as `a1cd7213e`.
- Verification before the commit: the scoped documents were strict UTF-8 without BOM, CRLF-only with a final CRLF, had no trailing whitespace, and passed `git diff --check`.

## P6.2 readiness checkpoint

- Sanitized operator evidence: `p6.2-lenovo-inventory-readiness.json`.
- Focused offline readiness-probe tests passed on 2026-08-06.
- Current local-material outcome: `go`.
- Both `crm82` and `crm91` have present same-user Credential Manager targets; deployment material
  and offline identity-chain validation are complete.
- Live startup outcome: `no-go` because the Official Worker did not publish a READY frame before
  the Gateway startup deadline. No CE request was executed and no process/listener remained.
- Repeating the bridge after the approved canonical URI values were written produced the same
  `gateway-startup-failed-before-ready` result. An isolated named-pipe handshake reached both
  Workers, but each exited before READY with `ClientNotReady` (exit code `10`).
- Non-findings: manifest, executable hash, package-lock, canonical URI, profile-input shape,
  named-pipe reachability and local listener setup are not the current blocker; the sanitized
  startup result cannot distinguish credential/IFD/Organization-authorization/runtime causes and
  must not be guessed.

## Boundary and next gate（已由 2026-08-07 重校取代）

上述 P6.2 readiness／startup 結果只描述 Official Worker live-compatibility 的歷史
checkpoint。它不是 Data8、Embedded 或 Dedicated Gateway 的失敗證據，也不再要求操作者
重建 Credential Manager target、修改 URI／home realm 或重跑相同 startup。

目前的下一個 gate 是：把 P6.1 離線 Router／Pool／Lease 擴充點完成文件一致性、quality
與 spec 判斷，保留 Official Worker 為 `evidence-pending`，然後在取得必要的 task-owned
結案提交與封存後啟動既有 P7.0。P7 的 Lenovo 路線固定保留 `Embedded + Data8` 與
`DedicatedGateway + Data8`；P8 另行以 `CentralGateway + Data8` 評估。

Credential values, tokens, cookies, connection strings, private keys, Organization IDs,
and personal data must not be stored in this file, task artifacts, source control, or console
evidence. Future Official Worker live work must use a new, independently authorized task.

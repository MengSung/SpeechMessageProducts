# 稽核 Claude 效能修改並安全加速

## Goal

稽核 Claude 依效能計畫所做的程式修改，確認正確性、跨使用者 Session Isolation、Memory/Resource Lifecycle 與實際效能，並在驗證安全前提下實作可行加速。

## Confirmed facts

- Claude's work is committed in `e5b7a0544`, changing 16 product/build files and adding the performance record.
- Local inspection confirms a functional regression in `DonationPaymentProcessor.Utilities.cs`: `MoneyToChinese` maps 3 to `?`, 5/6/8 to `壹`, and several positional units to `壹`.
- The existing audit runner completed Gemini output but Claude produced no usable output; this is an incomplete degraded external review, not a dual-model pass.
- The repository requires zero cross-user/session, memory, and resource leakage, Traditional Chinese documentation for changed C# files, UTF-8 without BOM, CRLF, deterministic cleanup, and evidence-backed performance claims.

## Requirements

- Audit the full `HEAD^..HEAD` change set, not only the performance record.
- Verify Session, identity, tenant, cache, static singleton, timer, stream, HTTP client, and background-resource isolation and cleanup.
- Correct any release-blocking regression found in the changed code, beginning with `MoneyToChinese`.
- Add focused regression/isolation/lifecycle tests where the existing test structure permits; do not weaken existing contracts.
- Identify further acceleration opportunities, prioritizing bounded CRM field selection (`ColumnSet(true)`), while avoiding speculative broad refactors.
- Run build, focused tests, relevant existing test suites, encoding/line-ending checks, and repository trace/invariant checks.
- Re-run the approved CCG self-healing reviewer entrypoint; report if Claude remains unavailable.

## Acceptance Criteria

- [ ] All Critical findings are fixed or explicitly blocked with reproducible evidence.
- [ ] `MoneyToChinese` returns correct Traditional Chinese financial numerals for zero, integer, decimal, negative, invalid, and representative large values.
- [ ] No changed path introduces cross-user/session/tenant state sharing or unbounded resource retention.
- [ ] `dotnet build -c Release` succeeds with no new warnings attributable to this task.
- [ ] Focused tests and applicable existing suites pass; pre-existing failures are separated by baseline comparison.
- [ ] Modified `.cs`/`.cshtml` files are UTF-8 without BOM, CRLF, and end in CRLF.
- [ ] Final report distinguishes local verification, Gemini-only fallback, and any incomplete Claude review.

## Notes

- Out of scope: changing public payment/API contracts, replacing Newtonsoft globally, changing legacy routing, or deleting source assets without evidence.
- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.

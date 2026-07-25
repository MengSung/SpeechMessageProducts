# Phase 3 Tier A Legacy Baseline Capture

Date: 2026-07-25  
Source: operator IIS Express session + `SpeechMessageProducts.ChurchReport/Logs/Trace.log`

## Baseline (Package01 OFF = legacy)

| Field | Value |
| --- | --- |
| App | ChurchReport on `http://localhost:43371` |
| Login account | `zz` |
| Contact full name | 胡夢嵩 |
| Route | `GET /Dedication/DedicationFeeViewWeb` |
| Auth state | Authenticated |
| Feature flag | `DynamicsAccess:Package01FeeReadsEnabled = false` |
| Query path marker | `[DEDQUERY-LEGACY]` |
| QueryStartDate | 2026-01-01 |
| QueryEndDate | 2026-07-25 |
| Returned row count | **56** |
| Trace evidence | `Trace.log` line containing `[DEDQUERY-LEGACY] FullName=胡夢嵩 Start=2026-01-01 End=2026-07-25 Returned=56` |

## Judgment so far

| Gate | Status |
| --- | --- |
| App up + login + CRM connectivity | PASS |
| Authenticated open of 奉獻收費清單 | PASS |
| Legacy baseline captured | **PASS (56 rows)** |
| Package01 path comparison (`[DEDQUERY-P01]`) | **NOT YET** |
| Tier A full pass | **NOT YET** (waiting Package01 enable + parity) |

## Next step to finish Tier A

1. Keep the same login (`zz` / 胡夢嵩).
2. Set only:
   - `DynamicsAccess:Package01FeeReadsEnabled = true`
   - Prefer `ExecutionMode = Embedded` for this VS local test
3. Restart / F5 IIS Express ChurchReport.
4. Login again, open **奉獻收費清單** with the same date range if needed.
5. Expect log:
   - `[DEDQUERY-P01] FullName=胡夢嵩 Start=2026-01-01 End=2026-07-25 Returned=56`
6. Pass criteria: same returned count (56) and UI totals match legacy baseline.

## Rollback

Set `Package01FeeReadsEnabled` back to `false` and restart.

## UI screenshot confirmation

Operator screenshot of **奉獻收費清單**:

- Header shows contact 胡夢嵩
- Start date: 2026/1/1
- End date: 2026/7/25
- Table shows multiple fee rows (十一奉獻 / 感恩奉獻, card/cash amounts)
- Matches Trace.log baseline: `[DEDQUERY-LEGACY] FullName=胡夢嵩 Start=2026-01-01 End=2026-07-25 Returned=56`

## Package01 enable flip (awaiting restart)

At 2026-07-25, `SpeechMessageProducts.ChurchReport/appsettings.json` was changed for Tier A comparison:

- `DynamicsAccess:Package01FeeReadsEnabled` = `true`
- `DynamicsAccess:ExecutionMode` = `Embedded`
- Profile remains `jesus-prod`
- Web API root remains `https://jesus.speechmessage.com.tw/api/data/v9.1/`

Operator must **restart IIS Express / F5**, login as `zz`, reopen **奉獻收費清單** with 2026-01-01..2026-07-25, then agent checks Trace.log for:

`[DEDQUERY-P01] FullName=胡夢嵩 Start=2026-01-01 End=2026-07-25 Returned=56`

Pass only if Returned=56 (or UI-equivalent parity). Rollback: set Package01FeeReadsEnabled back to false.

## CeVersion 8.2 correction + auth fix (2026-07-25)

Operator confirmed **jesus is Dynamics 365 CE 8.2**, not 9.1.

Config corrected to:

- `OrganizationWebApiBaseUri = https://jesus.speechmessage.com.tw/api/data/v8.2/`
- `CeVersion = 8.2`
- Embedded `CredentialSource = SecretReference`
- Local-dev bridge maps secret names to `CrmConnection` Username/Password/Domain (values not stored in DynamicsAccess JSON)

Also fixed error page:

- long exception message uses TempData
- `HomeController.DisplayErrorView` restored/added

Retry Tier A after F5: expect `[DEDQUERY-P01] ... Returned=56`.

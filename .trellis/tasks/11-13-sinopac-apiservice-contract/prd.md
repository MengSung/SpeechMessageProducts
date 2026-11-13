# 修正永豐 APIService 外層服務名稱

## Goal

修復 P4-03 將 Sinopac APIService enum 數字送至永豐的回歸；依服務名稱字串序列化、增加封包契約測試，通過固定 guard 後依使用者授權執行一次 Sandbox 撥款查詢補驗。

## Requirements

- Correct the P4-03 regression in the outer Sinopac QPay envelope: serialize `APIService` as the documented service-name string, never as the enum's numeric value.
- Map exactly `OrderCreate` to `"OrderCreate"`, `OrderPayQuery` to `"OrderPayQuery"`, and `AllotQuery` to `"AllotQuery"` using an explicit switch. Unknown enum values must throw `ArgumentOutOfRangeException` before HTTP retry classification.
- Replace the misleading numeric-protocol comment with a Traditional Chinese explanation citing QPay specification V2.6 §5.1.1 and the legacy `QPayToolkit` `apiService.ToString()` behavior.
- Correct both `"7"` envelope expectations in `SinopacAllotQueryTests`; do not change Sign, Message, inner JSON, encryption/signing code, error classifications, credentials, or User Secrets.
- Add `Envelope_ApiService_UsesDocumentedServiceName` covering all three services with a fake HTTP handler that captures the second request body, and `Envelope_ApiService_NeverNumeric` covering every enum member.
- Run `SinopacProviderTests`, then `SinopacAllotQueryTests`, then the complete fixed guard at `C:\smv\guard`.
- Only after all offline checks pass, run exactly one `Category=PaymentsLive` case filtered to `Live_AllotQuery_SandboxRoundTrip`, without rerunning the synthetic writeback or idempotency cases.
- Follow the user's A-E result handling exactly. Only classification A completes the requested supplement; B allows one specified second run after an offline fix and full guard; C-E stop with the specified P4-06/P4-PROP-008 handling.
- Do not access production organizations, production stores, production LINE channels, or real payments. Do not start phase 5.

## Acceptance Criteria

- [ ] A test-first regression fails on current behavior because the captured Order request has a numeric `APIService`.
- [ ] Captured requests serialize the three exact documented service names; all defined enum values map to nonnumeric strings; unsupported values throw `ArgumentOutOfRangeException`.
- [ ] The two AllotQuery envelope fixtures expect `"AllotQuery"`; Sign, Message, and inner JSON remain unchanged.
- [ ] `SinopacProviderTests` and `SinopacAllotQueryTests` pass in the prescribed order.
- [ ] Fixed full guard passes after implementation.
- [ ] One authorized Sandbox-only query is classified A-E and handled without exceeding the permitted retry count.
- [ ] P4-06, P4-PROP-008/009, progress DATA, encoding checks, task evidence, commit, push, and remote SHA are updated only as allowed by the resulting classification.
- [ ] Traditional Chinese documentation remains accurate; modified C# files are UTF-8 without BOM, CRLF, and final CRLF.
- [ ] No secret, response body, headers, nonce, signature, or encrypted content is written or reported.

## Notes

- Keep `prd.md` focused on requirements, constraints, and acceptance criteria.
- Lightweight tasks can remain PRD-only.
- For complex tasks, add `design.md` for technical design and `implement.md` for execution planning before `task.py start`.

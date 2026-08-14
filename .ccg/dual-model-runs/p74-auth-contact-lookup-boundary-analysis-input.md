# P7.4 authentication contact lookup boundary analysis

Review the proposed local-only design for authoritative matrix ORG-CALL-00055
(`auth.contact.retrieve.by.account`) and ORG-CALL-00056
(`auth.contact.retrieve.by.lineid`).

Constraints:
- Existing legacy account lookup exposes a plaintext password comparison risk.
- New path must be disabled-by-default, DTO-only, asynchronous and request-local.
- No CE request/mutation, feature enablement, traffic change, P7.5 or P8 work.
- Do not connect the new typed API to legacy login, QR, payment, or session flows.
- No password, hash, token, cookie, Entity, raw exception, endpoint, credential,
  caller-selected profile, connector or FetchXML may cross the new API boundary.
- Gate=false must do no host/client/pool/handler construction or outbound I/O.
- Empty/multiple/malformed results must fail closed with a fixed classification.

Assess only: operation/wire/DTO shape, fixed-query validation, disabled bootstrap,
cancellation, A/B isolation, resource lifetime, testing, and migration risks.
OUTPUT: Critical/Warning/Info findings plus concrete local-only recommendations.

# Diagnostic Run Ledger

Concurrency limit: 2
Agent topology: one Diagnostic Subagent per workspace, nested agents prohibited

| Order | Module | Workspace | Status | Agent | CCG | Verification |
|---:|---|---|---|---|---|---|
| 1 | F01A | `F01A-solution-build-ci-governance/` | HUMAN_DECISION_REQUIRED | `019f4b88-2f37-7c01-b79c-73c428d67628` | Historical APPROVED_DEGRADED findings preserved; recovery review provider-blocked with no usable backend | STRUCTURE_PASS; ORIGINAL_CCG_RESTORE_WRITE; RECOVERY_SCOPE_PASS; PROVIDER_BLOCKED_NO_USABLE_BACKEND |
| 2 | F01B | `F01B-ai-agent-workflow-governance/` | APPROVED_DEGRADED | `019f4b88-2fb9-76a2-934f-fcb4f60eb199` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 3 | F01C | `F01C-documentation-tooling-history/` | APPROVED_DEGRADED | `019f4bc6-2d5d-7222-adc7-b3b897308a80` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 4 | F01D | `F01D-shared-test-harness-governance/` | APPROVED_DEGRADED | `019f4bc6-2e38-7c43-9ebf-bfe948ff7909` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 5 | F02 | `F02-dataverse-connection-foundation/` | DEGRADED_REVIEW_PENDING | `019f4bec-eb82-74d3-807f-92e3a3fb08fb` | R1 Claude KEEP; R2 no usable backend | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 6 | F03A | `F03A-crm-operations-library/` | DEGRADED_REVIEW_PENDING | `019f4bec-ec4d-7aa3-987f-14f107bdde11` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 7 | F03B | `F03B-toolutility-line-adapter/` | APPROVED_DEGRADED | `019f4c1a-6c7b-7d70-8e79-d1b569a0e6c2` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 8 | F03Q | `F03Q-toolutility-mixed-facade-quarantine/` | DEGRADED_REVIEW_PENDING | `019f4c1a-6d50-79a2-ba5f-7dd19c45a3d2` | No usable backend | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 9 | F04 | `F04-line-messaging-sdk/` | APPROVED_DEGRADED | `019f4c45-4f6e-7841-a882-53887958e9af` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 10 | F05A | `F05A-line-processor-core/` | APPROVED_DEGRADED | `019f4c45-5028-74e0-862d-a6f2e0af8b9c` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 11 | F05B | `F05B-line-aspnetcore-composition-adapter/` | APPROVED_DEGRADED | `019f4c76-b46a-78f0-8331-a456f64bac46` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 12 | F06 | `F06-line-notification-reply-workflows/` | APPROVED_DEGRADED | `019f4c76-b56c-7952-9d9e-c3293454e657` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 13 | F07 | `F07-line-richmenu-engine/` | APPROVED_DEGRADED | `019f4f29-2ca2-78c1-b88f-d434553211ad` | Claude KEEP/WARNINGS APPLIED; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE; CCG_REPOPATH_MAIN_SAME_HEAD |
| 14 | F08 | `F08-payment-provider-core/` | APPROVED_DEGRADED | `019f4f29-2d4f-78c0-bd85-f4225cd94e16` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE; CCG_REPOPATH_MAIN_SAME_HEAD |
| 15 | F09 | `F09-payment-workflows-host-adapter/` | APPROVED_DEGRADED | `019f4f53-c028-7e91-bd0a-60502679af4f` | Claude KEEP; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 16 | B01 | `B01-identity-session-access-control/` | APPROVED_DEGRADED | `019f4f54-60cf-7032-acfc-912e5bd2f432` | R1/R2 Claude KEEP after requested rewrites; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 17 | B02 | `B02-member-contact-profile-onboarding/` | DEGRADED_REVIEW_PENDING | `019f4f7d-dd74-7c63-878c-0ce4c33add0c` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 18 | B03 | `B03-small-group-hierarchy-reporting/` | DEGRADED_REVIEW_PENDING | `019f4f7e-8189-7a02-ab9d-5add99972076` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 19 | B04A | `B04A-attendance-present-record/` | DEGRADED_REVIEW_PENDING | `019f54ab-4d48-7b43-9edd-c8e1fe51807c` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE; RECOVERY_EXCEPTION_ACCEPTED; ACCEPTED_AUTHOR=019f54ab-4d48-7b43-9edd-c8e1fe51807c; SUPERSEDED_EMPTY=019f5040-41ca-73d1-a17a-f20593a0e7ce; NO_OVERLAP; NESTED_AGENT_COUNT=0 |
| 20 | B04B | `B04B-appointment-equipment/` | DEGRADED_REVIEW_PENDING | `019f5040-4325-7e62-916c-ba37beaf0196` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 21 | B04C | `B04C-scheduling-qr/` | DEGRADED_REVIEW_PENDING | `019f54b0-ec39-7260-91a3-87f741b0c69c` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE; RECOVERY_EXCEPTION_ACCEPTED; ACCEPTED_AUTHOR=019f54b0-ec39-7260-91a3-87f741b0c69c; SUPERSEDED_EMPTY=019f504b-1e81-7291-80e1-6bb88ab94b71; NO_OVERLAP; NESTED_AGENT_COUNT=0 |
| 22 | B05 | `B05-donation-product-payment/` | DEGRADED_REVIEW_PENDING | `019f504b-1f63-7210-9c2a-a428c987b7f5` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 23 | B06A | `B06A-list-reference-data/` | RUNTIME_VALIDATION_PENDING | `019f5052-4d6c-7dc0-90c5-714b8004128f` | Claude usable degraded fallback; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 24 | B06B | `B06B-fee-management/` | RUNTIME_VALIDATION_PENDING | `019f505a-bdf9-7311-abf1-25b3bf4c0378` | Claude usable degraded fallback; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 25 | B06C | `B06C-church-hierarchy-register/` | RUNTIME_VALIDATION_PENDING | `019f5066-7189-7640-a73c-63df524b0f07` | Claude usable degraded fallback; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 26 | B07 | `B07-churchreport-line-integration/` | DEGRADED_REVIEW_PENDING | `019f5073-c524-7f23-b263-0e345474c447` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 27 | X01 | `X01-host-composition-routes-lifetimes/` | DEGRADED_REVIEW_PENDING | `019f507a-9b0c-7451-a6ce-eae21d6079c7` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 28 | X02A | `X02A-shared-cache-foundation/` | DEGRADED_REVIEW_PENDING | `019f5080-a434-7793-8a79-bdaeca8a647c` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 29 | X02B | `X02B-observability-health-logging/` | DEGRADED_REVIEW_PENDING | `019f508a-2aae-7ed2-aaaf-770d7715f2a6` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 30 | X02C | `X02C-performance-profiling/` | DEGRADED_REVIEW_PENDING | `019f5092-ba51-7d02-8597-043e6c8671ab` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 31 | X02Q | `X02Q-legacy-trace-quarantine/` | DEGRADED_REVIEW_PENDING | `019f509d-f33b-7221-840c-27b5a596dd99` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 32 | X03 | `X03-shared-web-ui-assets/` | DEGRADED_REVIEW_PENDING | `019f50a5-bd58-7ed1-8f35-691d881222b3` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE |
| 33 | X04A | `X04A-runtime-configuration-secrets/` | APPROVED_DEGRADED | `019f54d8-825b-77c0-8246-5b8d2c91b022` | Claude usable degraded fallback; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE; RECOVERY_EXCEPTION_ACCEPTED; ACCEPTED_AUTHOR=019f54d8-825b-77c0-8246-5b8d2c91b022; SUPERSEDED_EMPTY=019f50ac-be25-74c1-9d16-369fff8457a2,019f54b9-aa97-7650-a639-67e3eedde072; DISPATCH_FAILED_MODEL_UNAVAILABLE=019f54d7-432f-72c2-9e5a-c82362c6c1fb; NO_OVERLAP; NESTED_AGENT_COUNT=0 |
| 34 | X04B | `X04B-deployment-package-sources/` | DEGRADED_REVIEW_PENDING | `019f54e7-601a-7ce2-915c-35dabcdeeb03` | No usable backend: Gemini quota; Claude session limit | STRUCTURE_PASS; VALID_WRITE_SCOPE; RECOVERY_EXCEPTION_ACCEPTED; ACCEPTED_AUTHOR=019f54e7-601a-7ce2-915c-35dabcdeeb03; SUPERSEDED_EMPTY=019f5489-0fe7-7622-a123-d2f4ce20548b,019f54bf-6758-78a0-b312-398d6795aefd; NO_OVERLAP; NESTED_AGENT_COUNT=0 |
| 35 | X05Q | `X05Q-churchreport-legacy-boundary-quarantine/` | RUNTIME_VALIDATION_PENDING | `019f54c6-af81-7931-a39a-78a67f4bdb4e` | Claude usable degraded review with runtime-validation verdicts; Gemini quota | STRUCTURE_PASS; VALID_WRITE_SCOPE; RECOVERY_EXCEPTION_ACCEPTED; ACCEPTED_AUTHOR=019f54c6-af81-7931-a39a-78a67f4bdb4e; SUPERSEDED_EMPTY=019f548f-56b0-7af1-a5b3-f1fd5bce16b5; NO_OVERLAP; NESTED_AGENT_COUNT=0 |

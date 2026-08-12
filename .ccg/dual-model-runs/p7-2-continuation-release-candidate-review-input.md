# P7.2 continuation release candidate review

Review the current working-tree changes for the P7.2 continuation task. This is
a high-risk isolation and resource-lifecycle change. Review only evidence in the
repository; do not request or perform CE operations.

## Required invariants

1. An operation-scoped `IOrganizationService` must never be written to shared
   `ToolUtility`, singleton, static cache, or later request state.
2. Fault, timeout, cancellation, uncertain transport, child-process failure,
   and cleanup failure must not result in unsafe retry, client reuse, or loss of
   the original root-cause stack.
3. Slice D-H local-only definitions must never enable Data8/CE executor,
   product consumer, feature traffic, CE 8.2, Official Worker, or P7.4/P7.5.
4. D-H operations must fail closed before profile/router/admission/lease/client
   acquisition, and must not accept Owner, entity, endpoint, credential, token,
   organization, profile, or FetchXML routing authority.
5. H policy must treat zero active weekly report as unlinked attendance,
   exactly-one as exact link, and duplicate/unavailable as no-go.
6. New or modified C# must retain UTF-8 without BOM, CRLF, Traditional Chinese
   lifecycle/isolation documentation, and deterministic resource ownership.

## Expected output

Return Critical / Warning / Info findings with exact file references. Verify
each finding from the repository. Do not infer live CE completion from tests.

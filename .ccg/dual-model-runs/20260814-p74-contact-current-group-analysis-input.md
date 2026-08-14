# P7.4 ORG-CALL-00052 source-boundary analysis

Analyze only this repository source path:

- Matrix: `ORG-CALL-00052`, `contact.current.group.retrieve`.
- Legacy method: `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs`,
  `GetContactCurrentGroup`.
- Production caller: `AddContactToListAsync` in the same class.

Determine whether this exact current source can safely become an independent,
disabled-by-default Gateway/ProductClient DTO-only read boundary now. Check:

1. whether authorization is server-derived and request-local before lookup;
2. whether caller-provided CRM `Entity`, ToolUtility, query cardinality or first-match behavior
   violates isolation or deterministic semantics;
3. whether membership changes, attendance creation, contact update, Owner assignment and LINE
   notification are write adjacency that forbids partial read cutover;
4. exact no-go conditions and the minimal safe recovery design.

Constraints: do not recommend CE work, gates, traffic, fallback, retries, raw SDK bridge,
static/shared authorization state, a static-only partial shortcut or a write implementation.
Return only `GO_LOCAL_DESIGN` or `SOURCE_ONLY_LOCAL_DESIGN_NO_GO`, followed by concise,
repository-verifiable evidence and any Critical/Warning findings.

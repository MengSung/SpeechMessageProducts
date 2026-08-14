# CCG architect Task: p74-contact-current-group-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
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


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
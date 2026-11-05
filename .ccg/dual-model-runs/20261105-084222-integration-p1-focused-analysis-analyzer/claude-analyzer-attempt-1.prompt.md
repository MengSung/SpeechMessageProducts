ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: integration-p1-focused-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P1.Worktree

## Request
# P1 architecture risk analysis (self-contained; no tools)
You are analyzer. Do not read repo, invoke tools, delegate, or connect services. Analyze ONLY this supplied design and return concise Critical/Warning/Info with precise corrective recommendations (max 900 words). Previous broad Claude run timed out; this retry deliberately supplies complete context.

User authorizes autonomous completion of P1, no workflow migrations, no LINE sends, no production data changes. Products net462 and net10, Framework tests run net48 but assert loaded product target is net462. R4 validates actual per-target evaluated project/package graph, forbids Extensions/ASP.NET/Dataverse in dual libraries except ToolUtility.Core net10 Dataverse package for Xrm types, forbids host-host/upwards/tool references. SDK golden JSON locks six existing types before retarget; add optional Sender (1..20 name, HTTPS icon), Background gradient, webhook id/redelivery, transport-independent HMAC-SHA256 verification with fixed-time digest equality. HMAC instance disposed per call.
ToolUtility.Core selectively moves only pure IOrganizationService operations with original namespace and characterization tests; connection/logging/Facade adapters remain net10. Framework SDK CoreAssemblies, modern SDK Dataverse.Client, PrivateAssets=all; host projects explicitly own their SDK package dependency. Four fee/entity queries use XDocument-escaped values. Product parameter DI overload retains obsolete ChurchReport default.
Localization immutable TextContext has validated server church/org/product scope; must never treat incoming church ID as auth. WithCultures preserves scope. SystemTextTable P0 prefilled zh-TW fallback remains backward compatible, new raw-presence method permits exact override->wildcard override->exact system->parent/alias->zh-TW. Named placeholder exact-set validation, invariant comparisons and format culture per call. Released cultures default zh-TW; en only keywords until released, pseudo opt-in nonproduction; production pseudo config rejects startup. Clock IClock UTC; modern TimeProvider adapter, net462 pinned CLDR IANA-Windows map; UTC Kind explicit, local invalid/ambiguous DST rejects, birthdays date-only. Money ISO identity whitelist7, TWD 0 display policy (ISO TWD minor2), other2; locale never changes currency.
Tenancy net10 shared resolver consolidates all existing token fallback chains; keep Jesus fallback safe warning no secrets. Credential ToString redacts. Text snapshots contain only public church labels, not user data, tokens, Entity, clients, HttpContext. D365 source exact authorized church condition, stable database ID/paging validation. Cache keyed complete org/product/church scope + change version, max2000, idle30min/maxreload10min. Bounded inflight admission and per-scope singleflight; caller cancellation detaches waiter, owner token manages load. Failed flight removed; candidate duplicate IDs/key+culture/unknown keys/placeholders/length reject before atomic publication and preserve previous snapshot, report original Exception via notifier. Generation fences prevent invalidated load from republishing. Dispose stops admission/cancels owner/drains then releases cache and registrations, counters baseline.
Deployment schema explicit mode coexists with P0 registration CLI. Sole organization goodshepherdback, fixed existing HTTPS endpoint and UserSecrets lease. Export metadata-only, no business records/config secrets. Plan bound to schemahash,current fingerprint and all operations; apply verifies hash and live state under existing same-org OS lock, exports before mutations, only additive create and unmanaged solution membership. Existing type/date/length/security/ownership conflicts stop, never alter existing field. Alternate keys Active before verify success; two applies second0writes. Appendix account field count contradictory7vs5: follow authoritative appendix field definitions, phase-tag Sinopac P4, no guessing P8 fee entity names. Live acceptance after offline gates/review creates only synthetic duplicate-key test, deletes only its own row IDs in finally. No secrets in artifacts.

Identify concrete design omissions and fixes, not hypothetical claims of existing bugs. Include Framework dependency selection, bounded task shutdown, stale generation races, timezone portability, additive-schema integrity. No broad repo reading. Output the report now.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.
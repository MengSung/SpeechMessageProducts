# Deploy MemberInfo Portable Kit

## Goal

Migrate the complete, authoritative MemberInfo feature contract from the user-provided portable kit into branch `1.0.0.1.WorkTreeMemberInfo`, preserving this branch's existing security, LINE workflow, popup upload, project-path, and payment-neutralization changes.

## Confirmed Facts

- Repository root: `D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.WorkTreeMemberInfo`.
- Branch: `1.0.0.1.WorkTreeMemberInfo`; base commit: `1c68743a1da360ac2e8385cf424cf47f0e6abfcf`.
- The portable package verifier passed: 73 files, strict UTF-8, SHA-256, and 290 relative Markdown links.
- The ZIP SHA-256 is `AE26C46885500CE45F6FBF3DC9134B513C71566E8BFE9222D39EE1255140618A`.
- No executable is present. The only runnable artifact is the inspected, unsigned `verify-package.ps1`; its default mode is read-only.
- Target framework is `.NET 10`; server package `DevExtreme.AspNet.Core` is `23.1.5`; the page actually loads DevExtreme client `22.1.6` from `wwwroot/js/devextreme/dx.all.js`.
- MVC uses Newtonsoft.Json with `DefaultContractResolver`, preserving PascalCase DTO properties.
- Existing authorization derives Church/Shepherd access from CRM `new_church_jobtitle` and `ListManager.LoginType`, with server-side current-contact checks and batch contact authorization.
- Existing avatar priority, protected image endpoints, ImageSharp processing, LINE fallback/resync, and `IMemoryCache` behavior are already present.
- The current UI is still the legacy flat MemberInfo DataGrid. Tree APIs, tree DTOs, tree/search services, relation formatting, responsive tree UI, and metadata-rank sorting are absent.
- The untouched test baseline is 212 passed and 23 failed. The failures are inherited path/payment-neutralization assumptions unrelated to this migration and must remain separately identified.
- External Gemini/Claude review is waived by the owner because both providers have no quota. No external reviewer will be invoked.

## Requirements

1. Follow `docs/portable/member-info-portable-kit/01-INTEGRATED-SPEC.md` as the final behavior contract and the Prompt 2-9 ordering in `04-PROMPT-PLAYBOOK.md`.
2. Treat all six host patches as evidence only. Adapt changes to the current renamed project and current branch behavior; do not run `git apply` against the worktree.
3. Preserve the existing `LineMessagingProcessorClass` profile lookup and do not reintroduce the older direct `Line.MessagingClient` integration.
4. Preserve the existing MemberInfo detail-popup upload toolbar and request-race protection.
5. Add the district -> group -> member tree, Church-only ungrouped paging, authorized search, loading/cancel/restore states, detail gender/birthdate, relation-goal formatting, group count/time/place metadata, responsive fixed columns, widget resizing, single-column sorting, and 22.1.6-scoped touch bridge.
6. Use exact visible column order: `ContactId`, `FullName`, `Phone`, `BirthDate`, `Address`, `SpiritualIdentity`, `MembershipStatus`, `RelationGoals`, `Gender`.
7. Keep `ContactId` at 72px fixed-left and non-resizable/non-sortable; keep `FullName` at 62px fixed-left without an application `minWidth`.
8. Use Dynamics `contact.customertypecode` `OptionSet.Options` collection order as the only membership-status ordering authority. Keep Configured -> Unknown -> Empty at the end rules in both directions.
9. Keep authorization fail-closed, validate requested list/contact IDs server-side, batch CRM work, and prevent user-specific Shepherd data from entering shared cache.
10. Implement with TDD: add or adapt a focused failing test before each production behavior, verify the expected failure, then implement the minimum passing change.
11. Run focused tests after each layer, then full MemberInfo tests, affected builds, JavaScript syntax checks, strict UTF-8/U+FFFD scans, privacy/secret scans, and diff checks.
12. Perform an inline zero-trust review of the final diff and record the external-review waiver truthfully.
13. Commit coherent batches with Traditional Chinese subjects and bodies, push the feature branch, then archive Trellis and CCG task records.

## Acceptance Criteria

- [ ] Tree skeleton contains PascalCase `Districts`, Church-only `Ungrouped`, and `Scope`, with complete `GroupCount` and trimmed `GroupTime`/`GroupPlace` from the existing single list query.
- [ ] Church and Shepherd requested list IDs are checked against authoritative visible-list sets; malformed/unauthorized IDs fail closed.
- [ ] Group, search, ungrouped, detail, and avatar data never include unauthorized contacts and do not use per-row authorization queries.
- [ ] Initial load fetches only the non-personal tree skeleton; group members and avatars load only when a node is expanded.
- [ ] Search supports multiple/single/zero results, cancellation, stale-response suppression, error recovery, and restoration of the previous browse state.
- [ ] Detail shows gender and nullable birth date safely; Year <= 1 is treated as unset; lists expose only one `RelationGoals` column.
- [ ] All three grids share the exact nine-column factory and fixed/resizing/sorting/remote-guard contract.
- [ ] Membership status sorts by metadata rank locally and by segmented remote paging for ungrouped rows, with Unknown and Empty retained and placed last in both directions.
- [ ] DevExtreme 22.1.6 touch behavior is scoped to fixed data-row overlays, preserves vertical scrolling, suppresses post-swipe clicks, and does not intercept headers.
- [ ] Focused new MemberInfo tests pass. Full test output clearly distinguishes inherited failures from migration regressions.
- [ ] Affected application and test projects build with zero errors; JavaScript parses; strict UTF-8, U+FFFD, secrets, privacy, `git diff --check`, and scope checks pass.
- [ ] Local browser checks are completed where the available non-production environment permits; unavailable real CRM/role/mobile evidence is reported as remaining user verification, never fabricated.
- [ ] Final commits are pushed to `1.0.0.1.WorkTreeMemberInfo`, and task records are archived without deleting the user-provided portable artifacts.

## Out Of Scope

- Production publish/deployment, production CRM mutation, or secret/configuration replacement.
- Unrelated payment, RichMenu, or historical path-test repairs.
- Rewriting the user-provided portable kit, ZIP, specifications, plans, or evidence patches.
- Replacing current security/session controls or current LINE workflow with the Sunny reference implementation.

## Open Questions

No code-planning blocker remains. Runtime acceptance that depends on real Dynamics metadata, role-specific accounts, LINE provider state, or physical mobile devices will be recorded as an explicit user-verification gate if the local environment cannot supply it safely.

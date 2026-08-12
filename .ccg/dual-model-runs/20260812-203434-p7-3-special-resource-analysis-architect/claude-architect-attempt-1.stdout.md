I completed the P7.3 architecture analysis for the five special-resource capabilities, grounded in the actual repository code rather than generic assumptions.

Key findings from reading the real source (not just following the Gemini draft that had already run):

- Of the five operations, only `metadata.optionset.retrieve.by.attribute` currently has an `OperationIds` constant and a registry row (registered with `responseKind: Unsupported` as a placeholder) — the other four don't exist yet anywhere in `OperationIds.cs`/`Package01OperationRegistry.cs`.
- `Data8ProfileOperationExecutor.IsData8SupportedOperation` is a separate, independent allowlist gate — even a completed registry entry won't dispatch until this switch is also updated, so "registry done" ≠ "executor done."
- `stats.meeting.retrieve.by.sunday` can directly reuse the existing paging pattern in `Package01Data8ReadOperations.cs` (fail-closed on any page overrun/missing cookie, no partial results) rather than needing a new design.
- Migrating `MetadataOptionSetByAttribute` from `Unsupported` to a real response kind changes its `TemplateHash`, which will break `OperationRegistryAgreementTests` unless the matrix JSON is updated in lockstep — a sharper risk than a plain "new capability" addition.
- Two image-write capabilities (`memberinfo`/`newperson`) should get independent response discriminators, mirroring the existing pattern where structurally identical P7.2 mutation branches (`StaticListMembershipMutation`, `ContactOwnerAssignment`, etc.) are still kept separate to prevent cross-capability data reuse.

I wrote the full Traditional-Chinese analysis (typed contracts, image stream defensive-copy/cancellation/no-retry rules, metadata cache partitioning by `(ProfileAlias, GenerationId)`, meeting paging limits, and prioritized TDD tests) to `.ccg/dual-model-runs/20260812-203434-p7-3-special-resource-analysis-architect/claude-architect-attempt-1.stdout.md`, following the same run-directory convention already used for the Gemini attempt. No repository source files were modified, per the task's "不要修改檔案" constraint.

---
SESSION_ID: be94763f-5e51-47b3-adcb-a5bb2a7e1abc

ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dynamics-gateway-discussion-guide

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Review request: Dynamics Gateway discussion guide update

Review only the newly added or changed content in:

`docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

Use the repository diff and compare the document against:

- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`

The user asked to preserve the complete discussion in the Traditional Chinese explanation manual. Check:

1. Technical correctness for D365 CE 8.2 IFD and CE 9.1.
2. Clear distinction between the checked-in Data8 `PowerPlatform.Dataverse.Client` project and Microsoft's official `Microsoft.PowerPlatform.Dataverse.Client` NuGet package.
3. Whether the document fairly explains that legacy CRM SDK syntax is not inherently worse, while still justifying Gateway centralization.
4. Correct roles of Central Gateway, Local Gateway, and deferred Embedded mode.
5. Correct configuration split between product JSON, Gateway profile/registry, and secret provider.
6. Correct connection-pool ownership versus organization-wide admission coordination.
7. Correct Phase 4/5/6 preservation and Data8 removal gates.
8. Session isolation, memory/resource lifecycle, and safe sustained-performance requirements.
9. Traditional Chinese clarity, internal consistency, and misleading or overly absolute wording.

Do not modify files. Return a Critical / Warning / Info review. Cite exact headings or phrases for every actionable finding. If there are no Critical or Warning findings, state that explicitly.


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
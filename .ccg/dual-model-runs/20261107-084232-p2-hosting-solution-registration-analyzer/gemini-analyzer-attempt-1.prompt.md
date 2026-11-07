ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\analyzer.md
<TASK>
# CCG analyzer Task: p2-hosting-solution-registration

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P2.Worktree

## Request
READ-ONLY analysis limited to missing solution entries. User reports SpeechMessage.Hosting/SpeechMessage.Hosting.csproj exists but SpeechMessageProducts.sln contains only the original 30 projects. Verified dotnet sln list has neither Hosting nor Hosting.Tests. Proposed correction: use dotnet sln SpeechMessageProducts.sln add SpeechMessage.Hosting/SpeechMessage.Hosting.csproj SpeechMessage.Hosting.Tests/SpeechMessage.Hosting.Tests.csproj, preserve all existing entries and all configuration mappings, then verify each entry exactly once, original 30 preserved, both project builds, and record integration still incomplete (do not claim all tests/P2 pass). Inspect only these csproj and the sln as needed. Do not read any settings, secrets, credentials, environment values, unrelated code or change files. Output Critical/Warning/Info for risks of this narrow patch only.

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
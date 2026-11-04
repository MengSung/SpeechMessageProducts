# CCG analyzer Task: line-token-config-separation-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.10.IntegrateWebhook_SDK.worktree\.worktrees\1.0.0.11.Integration.P0.Worktree

## Request
ROLE_FILE: ~/.claude/.ccg/prompts/{backend}/analyzer.md
<TASK>
Analyze the proposed ChurchReport LINE credential separation change before implementation. Do not read, print, request, infer, or reproduce any credential values.

Repository context:
- The tracked ChurchReport base appsettings.json has two nonempty LINE channel token values; Development JSON already clears both production alias values.
- Development app configuration uses base JSON, Development JSON, then User Secrets. A startup guard compares Development values against nonempty baseline values and restricts the default alias to DevTest.
- Eleven legacy ChurchReport configuration builders call AddChurchReportDevelopmentOverrides. For non-Development, that helper currently adds no provider.
- WebApplication.CreateBuilder already loads standard environment variables. The shared LineMessagingProcessor default path also adds standard environment variables and can resolve aliases or LINE_CHANNEL_ACCESS_TOKEN.
- The approved design is to clear only the two tracked base token values; retain Development User Secrets for DevTest; reject nonempty production aliases and generic LINE_CHANNEL_ACCESS_TOKEN in Development; verify any nonempty DevTest value is supplied by the host User Secrets JSON provider; add a `LINE__`-prefixed environment provider only in non-Development, including to the host before validation, legacy builders, and shared default resolver; use environment names `LINE__LineMessaging__Jesus__ChannelAccessToken` and `LINE__LineMessaging__JesusBack__ChannelAccessToken`.
- Do not change other settings, other credentials, User Secrets contents, deployment secrets, GitHub secrets, or external systems. No LINE or D365 connections.

Please identify concrete risks, likely .NET configuration provider ordering pitfalls, whether verifying User Secrets provider provenance via provider source/path is reliable, and focused tests needed. Keep the analysis scoped; do not implement. Do not suggest broad environment-provider changes or printing configuration values.
</TASK>
OUTPUT: Concise findings ranked Critical/High/Medium/Low, with exact files or test contracts to inspect and any required design corrections.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.

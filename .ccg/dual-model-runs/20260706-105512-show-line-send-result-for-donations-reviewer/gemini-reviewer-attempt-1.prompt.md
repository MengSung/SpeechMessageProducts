ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: show-line-send-result-for-donations

## Repository
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.8.WorktreeFabelSecurityScan

## Request
# Review Task: Show LINE send result for donations and add ATM copy button

User requirements:
1. ATM/匯款奉獻 must show LINE send result to the user, including success or failure reason.
2. 輸入奉獻 must show LINE send result to the user, including success or failure reason.
3. ATM/匯款 virtual account result information must include a copy-to-clipboard button so donors can copy the ATM/transfer virtual account result info.

Verification already run locally:
- dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DonationPaymentProcessorKeyInNotificationTests" --no-restore : passed 6/6
- dotnet build .\ChurchReport\ChurchReport.csproj --no-restore : passed 0 warnings / 0 errors
- Modified files checked UTF-8 without BOM and CRLF-only line endings.

Review the git diff in this file for correctness, regressions, security, UX, and missing tests:
D:\網頁APP雲端線上版本\DevExpressDevExtreme-21.2.7版本\音訊產品版本\ChurchReport\.worktrees\Jesus_5.1.8.WorktreeFabelSecurityScan\.ccg\dual-model-runs\show-line-send-result-for-donations-review-diff.patch

Output Critical/Warning/Info findings.


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
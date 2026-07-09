ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: dual-model-review-smoke-test-20260707-review-fix

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# CCG dual-model review smoke test

Review this intentionally small sample change for health-check purposes only:

```diff
diff --git a/sample.txt b/sample.txt
index 1111111..2222222 100644
--- a/sample.txt
+++ b/sample.txt
@@ -1 +1 @@
-old value
+new value
```

Please respond with a short Critical / Warning / Info review report and confirm the backend name you are running under, if available. Do not inspect or modify repository files.


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
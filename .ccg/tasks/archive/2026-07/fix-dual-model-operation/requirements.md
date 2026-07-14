# Requirements

修復 CCG 雙模型 analysis 與 review 正常運作。

## Acceptance Criteria
- `Start-CcgDualModelRun.ps1 -Role analyzer` can produce accepted output from both Gemini and Claude, or clearly classify an unavoidable provider quota/session blocker without masking it as success.
- `Start-CcgDualModelRun.ps1 -Role reviewer` can produce accepted output from both Gemini and Claude, or clearly classify an unavoidable provider quota/session blocker without masking it as success.
- Gemini 403 / garbled diagnostic output is investigated to root cause, with actionable diagnostics or a fix.
- Claude session-limit handling is verified and reported distinctly from local toolchain failures.
- Runner artifacts remain under `.ccg/dual-model-runs/`.

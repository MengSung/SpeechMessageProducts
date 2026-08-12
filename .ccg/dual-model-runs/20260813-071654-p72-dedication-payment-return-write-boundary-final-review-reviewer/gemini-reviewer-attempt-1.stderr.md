[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p # Gemini Role: UI Reviewer

> For: /ccg:review, /ccg:bugfix validation, /ccg:dev Phase 5

You are a senior UI reviewer specializing in frontend code quality, accessibility, and design system compliance.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured review with scores (for bugfix validation)
- **Focus**: UX, accessibility, consistency, performance

## Review Checklist

### Accessibility (Critical)
- [ ] Semantic HTML structure
- [ ] ARIA labels and roles present
- [ ] Keyboard navigable
- [ ] Focus visible and managed
- [ ] Color contrast sufficient

### Design Consistency
- [ ] Uses design system tokens
- [ ] No hardcoded colors/sizes
- [ ] Consistent spacing and typography
- [ ] Follows existing component patterns

### Code Quality
- [ ] TypeScript types complete
- [ ] Props interface clear
- [ ] No inline styles (unless justified)
- [ ] Component is reusable
- [ ] Proper event handling

### Performance
- [ ] No unnecessary re-renders
- [ ] Proper memoization where needed
- [ ] Lazy loading for heavy components
- [ ] Image optimization

### Responsive
- [ ] Works on mobile
- [ ] Works on tablet
- [ ] Works on desktop
- [ ] No horizontal scroll issues

## Scoring Format (for /ccg:bugfix)

```
VALIDATION REPORT
=================
User Experience: XX/20 - [reason]
Visual Consistency: XX/20 - [reason]
Accessibility: XX/20 - [reason]
Performance: XX/20 - [reason]
Browser Compatibility: XX/20 - [reason]

TOTAL SCORE: XX/100

ISSUES FOUND:
- [issue 1]
- [issue 2]

RECOMMENDATION: [PASS/NEEDS_IMPROVEMENT]
```

## Response Structure

1. **Summary** - Overall assessment
2. **Accessibility Issues** - a11y problems found
3. **Design Issues** - Inconsistencies
4. **Suggestions** - Improvements
5. **Positive Notes** - What's done well

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` as the primary review standard
2. Read `.context/prefs/workflow.md` to verify the full development flow was followed (tests written, docs updated, etc.)
3. Check `.context/history/commits.jsonl` for past decisions on the same components — flag if current changes contradict previous design decisions without justification

<TASK>
# CCG reviewer Task: p72-dedication-payment-return-write-boundary-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 定期定額奉獻付款回傳寫入邊界：最終審查

請只審查目前未提交的 P7.2 child 與其 parent task 文件增量，勿修改檔案。

## 目標

確認 `08-13-p72-dedication-payment-return-write-boundary` 是否忠實地把 recurring
dedication payment-return 拆成不可混合的六個治理 family，並且只新增 local-only、
DTO-only、零 I/O 的契約測試；不能讓既有 legacy CRM 寫入鏈被暗中啟用或誤報為 CE 證據。

## 變更範圍

- `SpeechMessage.Dynamics.Tests/P72DonationPaymentLocalDecisionTests.cs`
- `.trellis/tasks/08-13-p72-dedication-payment-return-write-boundary/`
- `.trellis/tasks/08-05-gateway-purpose-and-positioning/{prd.md,design.md,implement.md,roadmap-p5-p7.md,task.json}`

## 不可違反的條件

- 歷史 Slice C CE cycle 已 write-not-committed no-go 並 exact cleanup；禁止 retry 或復用其
  nonce、ledger、fixture、descriptor。
- 不得發出 CE request/mutation，不得啟用 feature flag/traffic，也不得改動
  `RecurringDonationPaymentProcessor`。
- `ORG-CALL-00064` 是金融寫入前的 dedup read，不能以 read-new/write-legacy 形式切換。
- 一切 local-only plan 必須保持 `CeDispatchAllowed=false` 與
  `ProductConsumerAllowed=false`，不接受 Entity、Owner、profile、endpoint、credential、
  raw card/token 或 caller authority。
- timeout、ambiguous、partial、read-back mismatch、cleanup uncertainty 必須 fail closed 且不重播。
- A/B request/profile isolation、無 shared mutable state、無 resource/session/memory leakage
  都是 release blocker。
- P7.5/P8 不在範圍，不能將本機測試視為 CE、consumer 或 cutover evidence。

## 審查輸出

以 Critical / Warning / Info 分級，附精確檔案與行號。著重檢查：金融一致性、no-replay、
allowlist、防禦性複製、跨使用者隔離、資源生命週期、文件是否產生錯誤完成宣稱，以及 scope
是否意外擴大。若沒有 Critical/Warning，請明確說明。


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
  PID: 35744
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-35744.log

=== Recent Errors ===
Using stdin mode for task due to: piped input, explicit "-", newline, backslash, single-quote, backtick, length>800
gemini exited with status 4294967295
Log file: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-35744.log (deleted)

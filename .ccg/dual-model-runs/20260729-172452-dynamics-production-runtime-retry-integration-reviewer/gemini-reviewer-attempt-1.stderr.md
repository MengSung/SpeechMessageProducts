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
# CCG reviewer Task: dynamics-production-runtime-retry-integration

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Production Dynamics Runtime Retry Integration Test 補強審查

## 背景

前一輪完整雙模型 re-review：

```text
20260729-170800-dynamics-multi-profile-runtime-drain-recovery-reviewer
ok=true
degradedFallback=false
completedBackends=[gemini, claude]
```

兩個模型都確認原先 `slot.Draining` 永久鎖死的 Critical 已修正。Claude 留下一項 Warning：三個 Manager regression 使用 `TrackingRuntime`，沒有真正執行 Production `DynamicsProfileRuntime._drainTask` 的快取、cancellation failure 後重設與再次 drain attempt。

## 本次補強

請直接讀取：

- `SpeechMessage.Dynamics.Tests/DynamicsProfileRuntimeFactoryTests.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/phase4-multi-profile-runtime-verification.md`
- `docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md`

新增測試：

```text
Manager_retries_the_real_runtime_after_cancelled_drain_without_allocating_a_third_generation_early
```

測試實際組合：

```text
DynamicsProfileRuntimeManager
→ RecordingRuntimeFactory（只記錄 reference，不建立或 Dispose 資源）
→ DynamicsProfileRuntimeFactory
→ DynamicsProfileRuntime
→ DynamicsHttpTransport
→ AdfsOAuthTokenProvider
→ OrganizationAdmissionRegistration
```

測試流程：

1. 真實 Runtime Generation 1 取得 Execution Lease。
2. 第一次 Replace 發布 Generation 2，Generation 1 進入 Draining。
3. caller cancellation 讓第一次 Production `DrainAttemptAsync` failure。
4. 第二次 Replace 必須重試同一個真實 Runtime；舊 Lease 釋放前 Factory CreateCount 必須維持 2。
5. Lease 釋放後才建立 Generation 3，Generation 1 成為 Disposed。
6. Manager Dispose 後 Registry EntryCount 必須回到 0。

RED 證據：刻意移除 `DynamicsProfileRuntime.DrainAttemptAsync` catch 內的 `_drainTask = null` 後，測試失敗；Manager 第二次 Replace／最終 Dispose 持續取得同一個已取消 Task，最後以 AggregateException＋TaskCanceledException 回報。

GREEN 證據：恢復 Production `_drainTask` 失敗後重設邏輯，單一整合測試通過。

最新完整驗證：

```text
Focused MultiProfile／Registry／Factory／Readiness／Phase4 Soak：36 passed
SpeechMessage.Dynamics.Tests：159 passed
SpeechMessageProducts.sln Release Build：0 warnings / 0 errors
Changed WebApi／Gateway／Tests scoped dotnet format：通過
```

## 審查問題

1. 這個測試是否真正使用 Production Factory／Runtime，而不是再次以 Fake 複製 `_drainTask` 行為？
2. Recording decorator 是否只觀測 reference，沒有形成第二個 Dispose owner、重複 cleanup 或跨測試 static state？
3. RED 證據是否能實際防止 `_drainTask` cancellation failure 永久快取的 regression？
4. 測試的 try/finally、Lease release、Manager／Registry dispose 順序是否 deterministic，無測試自己造成的 resource leak？
5. 是否已充分處理前一輪 Warning，或仍有 Critical／Warning？
6. 新增程式是否有完整、深入的繁體中文註解，說明 ownership、併發、錯誤與 cleanup 順序？

## 輸出格式

輸出 Critical／Warning／Info 分級報告。若前一輪 Warning 已充分解決且沒有新阻斷問題，明確寫 `PASS`。



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
  PID: 47680
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-47680.log

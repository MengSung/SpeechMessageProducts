[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree -p # Gemini Role: Design Analyst

> For: /ccg:think, /ccg:analyze, /ccg:dev Phase 2

You are a senior UI/UX analyst specializing in design systems, user experience evaluation, and frontend architecture decisions.

## CRITICAL CONSTRAINTS

- **ZERO file system write permission** - READ-ONLY sandbox
- **OUTPUT FORMAT**: Structured analysis report
- **NO code changes** - Focus on analysis and recommendations

## Core Expertise

- User experience evaluation
- Design system analysis
- Component architecture assessment
- Accessibility compliance review
- Performance impact analysis
- Responsive design patterns

## Analysis Framework

### 1. User Impact Assessment
- How does this affect user experience?
- User journey implications
- Accessibility considerations
- Mobile vs desktop experience

### 2. Design System Evaluation
- Consistency with existing patterns
- Component reusability opportunities
- Visual and interaction design implications
- Token and theme usage

### 3. Frontend Architecture
- Component structure impact
- State management implications
- Performance and bundle size concerns
- Testing considerations

### 4. Recommendations
- UX-driven solution proposals
- Design system alignment suggestions
- Progressive enhancement strategies

## Response Structure

1. **UX Analysis** - User impact assessment
2. **Design Evaluation** - Consistency and patterns
3. **Technical Considerations** - Frontend architecture impact
4. **Options** - Alternative approaches with trade-offs
5. **Recommendation** - Preferred approach with rationale

## .context Awareness

If the project has a `.context/` directory:
1. Read `.context/prefs/coding-style.md` and `.context/prefs/workflow.md` before analysis
2. Use rules from prefs/ as evaluation criteria
3. When analyzing, check `.context/history/commits.jsonl` for related past decisions
4. Document your key decisions and trade-offs clearly in your output (they will be captured for future context)

<TASK>
# CCG analyzer Task: dynamics-multi-profile-runtime-drain-recovery

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# Dynamics Multi-Profile Runtime Drain Recovery 分析請求

## 角色與範圍

請以高風險 .NET 非同步生命週期、共享容量與資源隔離架構分析者的角度，分析目前尚未修正的 Critical。只分析下列檔案與行為，不要修改程式：

- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntimeManager.cs`
- `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsProfileRuntime.cs`
- `SpeechMessage.Dynamics.Tests/MultiProfileRuntimeTests.cs`
- `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md`

## 已確認問題

`DynamicsProfileRuntimeManager.ReplaceCoreAsync` 在新 Generation 已發布、舊 Generation 已進入 Draining 後，若 `previous.DrainAndDisposeAsync(...)` 拋出例外，`slot.Draining` 目前不一定會清除。`finally` 只重設 `ReplacementInProgress`，因此後續 `ReplaceAsync` 可能永久在 Factory 前遭拒絕。

必須區分兩種狀態：

1. 舊 Runtime 已完成 `Disposed`，但 Token Provider、Transport 或 Admission Registration 的 cleanup 回報例外。Manager 必須保留並向上回報錯誤，但清除精確的 `slot.Draining` 強引用，使後續 Replace 可再進行。
2. 舊 Runtime 尚未 `Disposed`，例如 caller cancellation、drain timeout 或 active lease 尚未釋放。Manager 不可清掉 reference，也不可建立第三套 Client／Token／Handler graph；後續 Replace 必須先重試清理既有 Draining Runtime，成功後才可呼叫 Factory 建立下一個 Generation。

`DynamicsProfileRuntime.DrainAndDisposeAsync` 目前在未 Disposed 的 drain attempt 失敗時會把 `_drainTask` 重設為 null，因此可以重試；若 cleanup 在狀態已切成 Disposed 後拋錯，Runtime 之後回傳 completed task，避免重複 Dispose。

## 初步修正假設

- `ReplaceAsync` 只在 `ReplacementInProgress=true` 時立即拒絕；若存在 `slot.Draining` 且沒有另一個 replacement owner，允許本次 Replace 取得唯一 lifecycle ownership。
- `ReplaceCoreAsync` 在任何 Factory allocation 前，先等待／重試既有 `slot.Draining`。
- 共用一個 helper 執行 drain，並在 `finally` 中僅於 `runtime.State == Disposed` 且 reference identity 仍等於 `slot.Draining` 時清除 Slot reference；cleanup exception 不可吞掉。
- Pending drain 成功清除後才遞增／配置下一個 Generation，避免失敗重試製造不必要的 Generation gap。
- Manager shutdown 仍等待 lifecycle operation；linked cancellation 只中止這次 Replace owner，最終 `DisposeCoreAsync` 仍接管 Active／Draining cleanup。

## 必要 RED 測試

1. 舊 Runtime 完成 Dispose 後注入 cleanup failure：第一次 Replace 應回報 failure，但快照不再保留舊 Generation；第二次 Replace 可配置下一個 Generation 並成功。
2. 第一次 Replace 在舊 Runtime 尚有 active lease 時由 caller cancellation 中止：舊 Runtime 保持 Draining；第二次 Replace 必須先等待舊 Runtime cleanup，等待期間 Factory CreateCount 不得增加；釋放舊 lease 後才建立下一個 Generation 並完成。

## 強制約束

- 所有新 Production／Test 程式與非顯然分支必須有完整、深入、詳細的繁體中文 XML／實作註解。
- 不可吞掉 cleanup failure，不可 fire-and-forget，不可留下 Runtime／Handler／Token／Permit／Registration 強引用或背景 Task。
- 同一 Alias 任一時間最多一個 Active 與一個 Draining Generation。
- 多個同步 Replace 不得並行建立 Candidate；第三套 Runtime 資源必須在 Factory 前被阻止。
- caller cancellation 不得使尚未 Disposed 的舊 Runtime 從 Catalog 消失。
- 請檢查 lock ordering、task publication、dispose race、exception aggregation、generation numbering 與 shutdown ownership。

## 輸出格式

請輸出：

1. Root cause 與狀態機分析。
2. 對初步修正假設的同意／反對與理由。
3. 最小安全實作步驟。
4. 必要測試與競態案例。
5. Critical／Warning／Info 分級風險。



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
  PID: 25032
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-25032.log

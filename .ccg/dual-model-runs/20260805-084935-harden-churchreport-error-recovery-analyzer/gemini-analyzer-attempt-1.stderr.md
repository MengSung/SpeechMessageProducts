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
# CCG analyzer Task: harden-churchreport-error-recovery

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# ChurchReport 錯誤復原與 CRM 服務生命週期分析

請審查下列已驗證的程式行為，提出最小且安全的修正範圍與必須具備的回歸測試。不得假設未提供的事實。

## 已觀察到的證據

1. `BaseChurchController.HandleError` 在非 AJAX 分支直接存取 `TempData["ErrorMessage"]`；當 Controller 沒有可用 TempData / HTTP context 時，這會以第二個 `NullReferenceException` 或上下文例外遮蔽原始 CRM 錯誤。
2. AJAX 分支直接將 `exception.Message` 回傳給瀏覽器；非 AJAX 分支也把原始訊息放進 TempData，可能洩漏內部資訊。
3. `ToolUtilityFactory.GetInstance()` 保存靜態 singleton `_instance`；`BaseChurchController.Dispose()` 卻呼叫 `ToolUtility?.Dispose()`。Factory 沒有在每個 Controller 結束後重建 singleton，因此後續請求可能重取已 Dispose 的 CRM client。
4. 登入 `SetupSystemData` 從 `ICrmConnectionPool` 借用 `IOrganizationService`，傳入 `ListManager.SetupListManager` / `DownloadListManager.GetListManager`，最後在 `finally` 歸還。
5. `DownloadListManager.GetListManager` 若傳入 service 且 `m_ToolUtilityClass.m_Crm2011OrganizationService` 為 null，會將該傳入 service 寫入共用的 `ToolUtilityClass` 欄位。之後同檔案改從這些欄位取 service。這是將短生命週期 lease 洩漏至長生命週期 shared object 的風險。
6. `HomeController.DisplayErrorView` 直接讀取 `TempData["ErrorMessage"]`。

## 限制

- 不接觸外部 CE / Web API / SQL / IIS / DNS / ADFS。
- 不可引入可跨使用者、跨組織或跨請求保留的可變 CRM service。
- 不可將原始 exception message、credential、token 或 connection detail 回傳瀏覽器。
- 所有修改過的 C# 檔都要 UTF-8 no BOM、CRLF、末尾 CRLF，並使用深入繁中註解。
- 請區分「已證實根因」與「需要另外驗證的風險」，避免過度重構。

## 輸出

請提供：
1. Critical / Warning / Info 分級；
2. 最小修正建議；
3. 要先寫且應先失敗的 xUnit 回歸測試清單；
4. 任何應明確拒絕的建議。


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
  PID: 32152
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-32152.log

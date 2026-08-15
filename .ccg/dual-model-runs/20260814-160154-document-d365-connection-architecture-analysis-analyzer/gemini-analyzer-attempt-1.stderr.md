[codeagent-wrapper]
  Backend: gemini
  Command: gemini -o stream-json -y --include-directories D:\音訊科技產品\系統平台\SpeechMessageProducts -p # Gemini Role: Design Analyst

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
# CCG analyzer Task: document-d365-connection-architecture-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts

## Request
# CCG analyzer：目前 D365 連線路徑、endpoint、憑證來源與架構圖證據

## Repository

`D:\音訊科技產品\系統平台\SpeechMessageProducts`

## 使用者問題

使用者要知道目前分支實際如何建立 D365 connection：是直接使用 `Microsoft.PowerPlatform.Dataverse.Client`、透過 `ToolUtility`，或經過其他 SDK／Gateway；並要取得 API endpoint、帳號／密碼資訊，以及一份列出必要參數的架構圖。

## 分析範圍

1. 以目前分支 source、project/package references、startup/DI、factory/provider、connection service、gateway/product client、deployment/runtime configuration 為證據，追蹤從產品呼叫點到 D365 transport 的完整 runtime chain。
2. 區分：
   - 套件有被 reference；
   - 物件在 runtime 被建構；
   - 實際目前業務路徑呼叫哪個 connector／SDK／HTTP endpoint。
3. 找出 endpoint 與驗證參數的設定來源，包含 organization URL、Web API/Organization Service、ADFS/OAuth/IFD discovery 或 token endpoint（只有程式證據存在才列）。
4. 找出帳號識別設定與密碼／secret 的來源位置。
5. 列出架構圖應包含的元件、箭頭、參數與主要替代分支。

## 安全限制

- 不得執行外部 CE、Web API、ADFS、SQL、IIS 或網路登入。
- 不得在輸出、prompt artifact 或 log 中複製完整密碼、client secret、token、cookie、authorization header 或完整含 secret 的 connection string。
- 可以指出檔案、行號、設定鍵、環境變數、secret store／加密／解密位置、帳號名稱；秘密值一律遮罩。
- 不修改任何產品程式或 runtime configuration。

## 輸出

請用繁體中文提供：

1. 一句話結論：PowerPlatform.Dataverse.Client、ToolUtility、Data8、Gateway／Official Worker 各自在「目前實際路徑」中的角色。
2. 逐段 runtime call chain，附檔案與行號。
3. endpoint 清單與各自用途／來源。
4. 連線參數表：參數、來源、是否敏感、消費者。
5. 帳號與密碼來源說明（秘密值遮罩）。
6. 不確定或只能由部署環境確認的部分。
7. 圖面建議。


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
  PID: 39176
  Log: C:\Users\Administrator\AppData\Local\Temp\codeagent-wrapper-39176.log

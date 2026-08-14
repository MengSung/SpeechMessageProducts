ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\architect.md
<TASK>
# CCG architect Task: p7-server-derived-authorization-boundary-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG architect task: P7 server-derived immutable authorization boundary

請以架構、安全、跨使用者隔離與資源生命週期角度審查下列規劃。只分析，不修改檔案或外部系統。

## 目標

現行 P7 matrix 有 70 個 temporary-legacy rows，且 direct P7.4 safe consumer candidate=0。多個 ChurchReport
legacy consumer path 在可證明的授權前使用 Session、InMemoryContext、saved credential、browser/route locator、
mutable CRM Entity／ListManager 或 stored FetchXML。規劃先建立可重用的 server-derived、immutable、
request-local authorization boundary，讓未來每個 capability 可在自身 gate/evidence 下遷移。

## 不可違反約束

- 不能把 browser/route/Session/InMemoryContext/saved credential/CRM Entity 當 Gateway authority。
- authentication/authorization 必須在 cache、manager、connector allocation、target lookup、stored query 或 CRM I/O 前。
- 不留 HttpContext、ClaimsPrincipal、token、credential、profile、CRM object 或 mutable collection 到 request 外。
- 本 child 不做 consumer、CE、feature gate、traffic、P7.5 removal 或 P8。
- historical P7.2 Slice C 已 no-go-closed + exact cleanup，永不重播。

## 請回答

1. 最小可行的 scope/result contract 應包含哪些 immutable server-derived 欄位，及哪些欄位必須禁止？
2. 如何把 shared prerequisite 與 future capability-specific authorization/DTO/CE/rollback evidence 明確分離？
3. 必要的 A/B isolation、fault/cancellation cleanup、no-I/O-before-authorization 與 disabled/no-fallback tests 為何？
4. 請列出任何 Critical/Warning/Info，特別是仍可能造成 session 或 resource leakage 的設計陷阱。

輸出：繁體中文，Critical/Warning/Info 分級、可執行的設計建議；不含 endpoint、credential、ID、原始資料。


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
ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: p74-dedication-capability-identity-audit-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.4 奉獻能力對應與隔離稽核：架構分析

請只做 source-only architecture/security review，不要修改任何檔案或建議啟用 feature gate、CE、流量、P7.5 或 P8。

## 問題

判定以下兩個 capability 是否應去重，或必須 fail closed：

1. `ORG-CALL-00059`：ToolUtility 的 `RetrieveDedicationBookingByFetchXml`，依 contact 讀取 active `new_dedication_booking`。phase-0 matrix 註記「Keep one capability family with product service row; registry should de-dupe later」。
2. `ORG-CALL-00060`：`DonationDedicationFeeFormService` 以 Line ID 或 browser contact GUID 取得 CRM `contact`，再修改 `DonationPaymentFormModel` 並讀 fee 資料。

## 既有證據

- `ORG-CALL-00041` 已使用固定 operation `payments.dedication.retrieve.by.contact`、template `payments.dedication.by.contact.v1`、immutable booking DTO、Data8 executor 與 disabled-by-default local boundary。
- `00059` 與 `00041` 都是 active dedication booking by contact；需確認是否存在會禁止去重的 input/output/template 差異。
- `00060` 的 browser/Line locator 會進入 `InMemoryContext.DonationPaymentManager`、mutable `DonationPaymentFormModel`、`ToolUtility` CRM Entity 讀取及現有 fee refresh lock。其 matrix 註記應使用 auth.contact capability 而非 ad-hoc `RetrieveEntity`。
- `00060` 目前沒有可證明在 Session、manager state、cache、profile/client composition 或 CRM I/O 前建立的 authenticated-principal、server-derived immutable authorization scope。

## 必答項目

1. 對 `00059`：是否可安全去重為 `00041`？列出必要的證據與任何不應升級的 evidence。
2. 對 `00060`：列出使 DTO-only migration 不安全的具體 boundary 漏洞，以及最小的恢復前置條件。
3. 確認本次只能產出 source-only task record，不能修改 matrix、runtime、CE、gate、traffic、P7.5 或 P8。

輸出格式：Critical / Warning / Info 分級；若無法確定請明確寫 no-go。


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
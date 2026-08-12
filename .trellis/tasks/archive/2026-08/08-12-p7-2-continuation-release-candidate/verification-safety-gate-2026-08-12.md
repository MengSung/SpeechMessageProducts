# P7.2 continuation 候選版安全閘門復核（2026-08-12）

## 復核目的

本紀錄釐清「本機候選版持續開發」不等於放寬四大產品的上線安全條件。它只記錄
Slice D–H 的本機決策、契約與測試證據；不授權 CE 寫入、Gateway 切流或
ToolUtility 移除。

## 已確認的硬性邊界

1. Slice C 最新、獨立的 fresh cycle 已在唯一一次 `ExecuteFixture` 回傳
   `write-not-committed` 後終止；strict ledger cleanup 已回報
   `fresh-fixture-cleaned`。該 CE 寫入家族不得重試。
2. Slice D–H catalog 的每一項 capability 都固定為
   `CeExecutorEnabled=false` 及 `ConsumerEnabled=false`。這些設定不是可由
   呼叫端輸入覆寫的 feature flag。
3. Data8 executor 對全部 D–H operation 必須在取得 admission、lease 或建立
   connector client 前回傳 `operation.not-supported`。因此本機 reducer／plan
   不能意外發出 CRM 寫入或建立可跨要求重用的 CRM connection state。
4. P7.4 Gateway 切流及 P7.5 ToolUtility 移除均仍需獨立的 CE evidence、
   read-back、reconcile、deterministic cleanup 與 rollout gate；本機綠燈不是
   任一 gate 的替代品。

## 本次實測證據

- `P72` local contract、A/B isolation，以及 Data8 executor pre-admission 拒絕：
  **116 passed、0 failed、0 skipped**。
- ChurchReport 的 operation-scoped `IOrganizationService` 傳遞、A/B isolation
  與 dispose 邊界：**15 passed、0 failed、0 skipped**。
- `SpeechMessageProducts.ChurchReport` Release build：**0 warnings、0 errors**。
- 本次 P72 的 20 個 task-owned C# 檔均確認為 UTF-8 無 BOM、CRLF-only 且有 final CRLF；
  `git diff --check` 通過。

## 安全結論

本機候選版可繼續交付 D–H 的純本機邏輯，但只可標示為「本機驗證完成；CE 實證待完成」。
它不能宣稱可切換 Central Gateway、Dedicated Gateway 或任何產品流量，也不能宣稱
可以移除 ToolUtility。若未來缺少所需 CE 證據，P7.4／P7.5 必須繼續 fail closed。

## 審查降級狀態

既有 CCG review 在 45 秒期限內取得 Gemini 可讀輸出；Claude 因 session quota
未回傳可用輸出。本輪狀態為「雙模型未完成；Gemini 輔助輸出加本機驗證」，不是
完整雙模型審查，也不構成解除任何安全閘門的依據。

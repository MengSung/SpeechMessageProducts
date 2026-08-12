# CCG analyzer Task: p7-2-slice-d-payment-decision-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7.2 Slice D：付款結果本機決策契約分析

請以繁體中文，僅做程式碼與契約分析，不建議或執行任何 CE、feature flag、產品流量、CE 8.2、Official Worker 或 ToolUtility 移除。

## 背景

- P7.2 Slice C 的唯一 fresh CE cycle 已 `write-not-committed` no-go 且 exact cleanup 成功；CE 軌道 closed，不得重試。
- Slice D–H 只允許本機 implementation/tests。所有 catalog entry 都是 `CeExecutorEnabled=false`、`ConsumerEnabled=false`，Data8 executor 在 admission/lease/client 前拒絕。
- legacy `DonationFeePaymentProcessor` 對成功回呼只有在「付款訂單尚未記錄」且 `new_pay_status == 100000000` 時才會寫入；否則視為已處理。legacy `DonationPaymentResultHelper` 成功碼包含 `S`、`SUCCESS`、`OK`、`0000`、`S0000`、`S00000`，失敗碼含 `F`、`FAIL`、`FAILED`、`ERROR`、`N`、`DECLINED`。

## 擬定本機契約

在 `SpeechMessage.Dynamics.Abstractions` 建立純同步、零 I/O、零 Session/cache/connector/CRM client 的 Slice D 決策：只接受已正規化 outcome 與兩個去識別化布林觀察（是否已有相同訂單、費用是否仍待付款）。

- complete + succeeded + no matching record + awaiting payment => 可以「準備一次未來受治理 dispatch」；此階段仍不能 dispatch。
- complete + succeeded + matching record 或不再 awaiting => 已處理，無 dispatch。
- complete + failed => 只要求 reconciliation，不能以失敗回呼自動寫入或重播。
- pending/unknown/incomplete/null => fail closed/no-go，絕不重播。
- 決策結果不攜帶 CRM ID、order ID、Owner、profile、endpoint、credential、token、raw provider code/raw exception。
- 專用 plan builder 只能把 fresh-success 決策轉為已固定的 `payments.fee.update.after.payment` local-only plan；其他 disposition 一律不建立 partial plan。

## 請輸出

1. Critical/Warning/Info：上述 semantics 是否保守且符合 legacy 已知行為。
2. 必測邊界：A/B isolation、input mutation、timeout/ambiguous、partial completion/no replay。
3. 未來 CE executor 仍須補上的 evidence/ledger/read-back/cleanup，避免把此本機契約誤宣稱為 CE evidence。


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.

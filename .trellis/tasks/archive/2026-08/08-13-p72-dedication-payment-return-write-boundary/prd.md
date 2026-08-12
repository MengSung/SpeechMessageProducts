# P7.2 定期定額奉獻付款回傳寫入邊界

## 目標

將目前 `RecurringDonationPaymentProcessor.HandlePaymentReturn` 內部混合的 CRM 讀取、費用建立、
聯絡人卡片更新、認獻單更新、Owner 指派與通知流程，收斂為可本機驗證的受治理寫入邊界設計。
本 child 只交付 immutable、DTO-only、零 I/O 的決策、計畫與契約測試增量；它不改接既有付款
consumer，不執行 CE mutation，也不宣稱 P7.4 cutover、P7.5 removal 或 P8 部署已完成。

## 既有事實與範圍

- 歷史 P7.2 Slice C CE cycle 已 `write-not-committed` no-go 並完成 exact cleanup。該 cycle、nonce、
  ledger、fixture 與 descriptor 永遠不可重試或復用。
- `ORG-CALL-00064`（`fees.retrieve.by.dedication.period`）已有 typed Data8 executor、ProductClient 與
  CE 9.1 read evidence，但其 consumer 仍未遷移。它位於下一個金融寫入 family 的前置讀取中，
  不得被誤當成獨立 read-only cutover。
- `ORG-CALL-00036`、`00037`、`00038`、`00042`、`00043`、`00049` 均仍是 local-only rejected、
  consumer-not-migrated、CE not-executed 的 write capability。它們不得因本 child 的本機測試而
  改為 executor implemented、consumer migrated 或 CE evidence succeeded。
- 本 child 僅擁有 recurring dedication payment-return family；一般 fee payment、appointment、
  onboarding、fee editor 與 attendance 仍由各自 capability child 擁有。

## 需求

1. 建立明確的 immutable payment-return observation／decision／local plan 邊界，不接受 CRM ID、
   `Entity`、Owner、profile、endpoint、credential、token、raw payment/card data 或任意欄位 map。
2. 付款成功只有在完整 read-back 已證明「同一付款尚未處理且目標仍可安全處理」時，才可建立
   **local-only** 的未來 governed plan；失敗只可要求 reconciliation；pending、unknown、timeout、
   ambiguous、partial 與 cleanup uncertainty 一律 no-go 且不可 replay。
3. 將以下 mutation family 分開建模，禁止以一個 generic write 或一個 transaction 假裝處理：
   - dedup / fee-period 前置讀取；
   - contact card profile update；
   - fee create；
   - fee owner assignment；
   - dedication booking completion/status update；
   - notification。
4. 建立本 child 的「尚不可實機 dispatch」gate：在沒有新 child、new nonce、new ledger、
   task-owned fresh fixture、read-only preflight、single dispatch、exact read-back/reconcile 與
   deterministic cleanup 前，所有 CE executor 與 product consumer 必須保持關閉。
5. 確保所有新型別都是 request/operation-local：不使用 Session、`HttpContext`、static mutable state、
   cache、background task、connector、lease、CRM SDK、ToolUtility 或 shared mutable collection。
6. 以測試固定：重複 callback 不重播、timeout-after-dispatch 不重播、未知 outcome fail closed、
   A/B interleaving 不交叉污染、allowlist 不接受 routing/identity authority，以及沒有 partial plan。
7. 在 parent 的 PRD、design、implement、roadmap 與 metadata 中記錄新的 P7.2 child、P7.4 現況、
   P7.5/P8 gate，取代已過期的「先重跑 P6/P7.0」敘述；已封存資料只作唯讀證據。

## 不在範圍內

- 不對 CE 8.2、CE 9.1、Official Worker、feature flag、流量或正式資料發出讀寫操作。
- 不修改既有 legacy payment-return chain 的實際 CRM mutation 順序，也不加入 request-time fallback、
  dual-write、auto retry、同步阻塞或 SDK rehydration。
- 不掃描 CRM、猜選 Owner、使用舊 ledger/fixture，或自行製造任意 production-like baseline。
- 不啟動 P7.5 ToolUtility removal 或 P8 Central Gateway。

## 驗收標準

- [ ] child 具備完整 `prd.md`、`design.md`、`implement.md`、research 與 check record；所有重大
  no-go、CE boundary、雙模型降級與下一步都有 task 持久化紀錄。
- [ ] 每個 recurring payment-return mutation family 都有唯一 owner、固定 operation ID、預期
  read-back/reconciliation 與 reverse-known-key cleanup 說明；沒有 generic Entity/attribute/Owner API。
- [ ] 本機 decision/plan 只輸出 bounded、去識別化分類，CE dispatch 與 consumer gate 均為 false。
- [ ] focused unit tests 覆蓋 duplicated callback、complete failure、pending/unknown/incomplete、
  malformed allowlist、A/B interleaving 與 no partial plan；測試不建立 connector、fixture 或外部資源。
- [ ] 所有變更符合 UTF-8 無 BOM、CRLF、final CRLF、完整繁體中文文件、`git diff --check` 與
  cross-user isolation/lifecycle requirements。
- [ ] 若外部模型未在 45 秒內完成，紀錄「雙模型未完成」後繼續本機驗證，但不得宣稱完整雙模型審查。

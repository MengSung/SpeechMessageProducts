# P8-04a 執行清單

1. [ ] 盤點 P6 的登入、fixture、SDK bridge、Host、Edge 與 secrets owner，建立 Shared API 並確認不擴張資料／秘密範圍。
2. [ ] 新增並先跑紅燈 `PseudoAuditRules.Tests.cjs`：CJK 未包覆、已包覆、捲動容器、真正截斷。
3. [ ] 新增並先跑紅燈 `PseudoAuditLifecycle.Tests.cjs`：登入失敗、頁面逾時、清理失敗；無網路、無真實秘密。
4. [ ] 抽取 P6 共用元件，改接 P6 並跑 `node tools/PageAcceptance/P6/Tests/P6Lifecycle.cjs`，維持原有四項回歸全綠。
5. [ ] 實作偽語系頁面稽核、固定四頁 manifest、I6 排除說明及 JSON 伴隨文件；跑兩支 Node 測試。
6. [ ] 實際以 Development ChurchReport＋Edge 開四頁。必要時先確認 User Secrets 租約／測試身份，禁止將機密輸出或保留在 repo；保存實際結果與安全證據。
7. [ ] 若有頁面問題，以 `P8-04 fix:` 修正；重跑稽核直到未包覆中文 0、截斷 0，或逐項記錄可接受例外。
8. [ ] 更新 `wp-reports/P8-04-pseudo.md` 前半、父任務、進度頁 DATA 與決策紀錄；每筆摘要／歷史使用台北日期時間。
9. [ ] 跑 P6Lifecycle、兩支 PseudoAudit 測試、文件說明、ProgressPage、適用位元組檢查與 `git diff --check`；核對 staged paths 只含 P8-04a。提交前只 restore 指定 `.turns.json`。
10. [ ] 提交訊息依 08A 完成條件；在唯一 `C:\smv\guard-p8` 跑適用完整守門並把證據存 `C:\smv\guard-p8-evidence`，完成一頁回報、驗收、推送及 `git ls-remote` 核對。

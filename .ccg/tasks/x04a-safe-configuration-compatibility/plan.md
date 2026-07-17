# X04A 安全設定相容性修復：規劃工作

1. 建立修訂設計，將 X04A-PERF-001 明確加入為 X04A P0 secret 修復的必要
   前置範圍。
2. 將 13 個已盤點的 ad-hoc configuration consumer 寫入修訂後 allowlist，並
   保留所有未選 X04A issue 的排除邊界。
3. 定義 host-initialized compatibility bridge、初始化順序、未初始化失敗行為、
   consumer 遷移規則、測試隔離與回復單位。
4. 將 `plans.md`、`measurements.md`、`goals.md` 修訂為可驗收合同，並同步
   更新 Wave 2 manifest 的 issue count 與 X04A terminal 狀態。
5. 以 Claude-only runner 審核設計與修訂合同；若無可用輸出，由主 session
   執行一次唯讀 Codex fallback。
6. 僅在合同獲得審核通過後，才開始 inline 實作、測試、審核與獨立提交。

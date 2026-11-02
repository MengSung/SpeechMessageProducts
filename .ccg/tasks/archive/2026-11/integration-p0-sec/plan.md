# P0-SEC 執行順序

- [x] 核對 11＋1 設定讀取點，遮罩掃描寫死 token 與繞道路徑；遇停止條件只保存報告。
- [x] 更新 01 的 P0-SEC、03 的 P2-01 與 Trellis 登錄。
- [x] 先寫設定層、來源架構、啟動守門測試並保存紅燈。
- [x] 最小實作 Development 覆寫、UserSecretsId、12 個接點與 Host 守門，保留 Production provider 順序與值。
- [x] 測試轉綠後重構，完整跑 Release 建置／測試、Debug tracer、Changed 編碼與位元組檢查。
- [x] 附錄 F 回報、進度頁與 completedWorkPackages，通過後獨立提交 P0-SEC，再開始 P0-ENV。

外部審查沿用使用者核定，依 3.3 僅 P0-12 後、P0-13 前一次 ClaudeOnly；本包列為階段審查重點。

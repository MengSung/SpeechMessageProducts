# P0-13 數字診斷補修驗證與審查

## 範圍及尚未完成事項

此補修僅新增安全數字碼以診斷使用者的 apply 失敗。真正的 SDK 伺服器拒絕原因尚未知；不得將本機綠燈標為 P0-13 或階段 0 完成。後續由使用者重新 plan、審核再 apply。Codex 未連線 D365／LINE，未讀取機密或其他 worktree；使用者的清單 true 變更保留但不得提交。

## TDD 與本機 00 §8

- 基線 Deployment：46／46。
- 新增 6 例功能紅燈：5 失敗（缺失數字診斷）、1 通過（缺失 Detail 的相容性）。未修改或刪除原測試。
- 補修後 Deployment：52／52；獨立模組覆蓋率 82.62%，覆蓋率守門通過。
- Release 全方案建置：成功，該次增量建置 0 錯誤、0 警告，不能據此宣稱清除使用者先前見到的 CS1572。
- Release 全方案：13 組、1116／1116，0 失敗、0 略過，含 ChurchReport 596／596。
- Debug tracer：7／7，非零案例。
- Changed 編碼（Base 73b6e6cf）：初次 5 份先前 LINE 設定分離工作紀錄違規；僅整理換行後 0 問題。不改守門、不改文件語意。
- 重複程式：projects=26、groups=0、new=0、stale=0。
- I18n：verify 通過，CJK ratchet、生成資源檢查通過。
- C# 來源與新增記錄已做 strict UTF-8 no BOM、CRLF、final CRLF 位元組驗證。

證據保留在本機 artifacts/p013-apply-fault-tests/。基線與紅綠為獨立 TRX；全方案使用固定 TRX 檔名造成檔案覆寫警告，因此其 13 組完整結果以 validation/release-test.log 的各組摘要為依據，不宣稱單一 TRX 包含全方案。未因此重跑已通過的案例。

## 外部分析與修正審查

均經 docs/scripts/Start-CcgDualModelRun.ps1 執行，沒有直接呼叫模型或 wrapper。

| 階段 | runId | reviewMode | dualModelComplete | quotaBlocked | degradedFallback |
| --- | --- | --- | --- | --- | --- |
| 分析 | 20261104-131202-p013-apply-fault-diagnostics-analyzer | dual | true | false | false |
| 審查 | 20261104-132317-p013-apply-fault-diagnostics-reviewer | dual | true | false | false |

雙方審查未發現 Critical／High／Warning。分析時的 Critical 是對未採用危險寫法的預警，已落實強型別讀取、原回報先行、Int32 八位格式與四次硬上限，不當成已存在的漏洞。補修審查獨立於先前階段 0 審查。

Claude 的兩項 Info：
1. 循環樣本在第四層回指，沒有再輸出回指後的節點。保留目前測試：它確實注入循環並斷言四次上限；移除上限會破壞這項保證。數字格式及深度另由五節點樣本保護，不需要改產品邏輯。
2. 文件範例碼不可當成實際伺服器代碼；文件已明確標註，取得使用者新輸出後再同步更新。

原始分析／審查產物留存於上述 runId 對應的 .ccg/dual-model-runs/ 本機目錄；本摘要不含帳密、token、金鑰或授權內容。

# P0-13 數字診斷補修驗證與審查

## 範圍及尚未完成事項

此補修僅新增安全數字碼以診斷使用者的 apply 失敗。真正的 SDK 伺服器拒絕原因尚未知；不得將本機綠燈標為 P0-13 或階段 0 完成。後續由使用者重新 plan、審核再 apply。Codex 未連線 D365／LINE，未讀取機密或其他 worktree；使用者手動提交 `714465fd` 的清單 true 變更已核對保留；驗收收尾須另以新提交保存 false，不改寫歷史。

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

## 伺服器診斷紀錄審查（2026-11-04）

- 自癒入口 runId=`20261104-144816-p013-server-event-record-reviewer`；reviewMode=dual、dualModelComplete=true、quotaBlocked=false、degradedFallback=false。
- 已完整讀取 Gemini 與 Claude 的輸出，兩方均無 Critical／High。Gemini 的 Info/Medium 提醒保留 D21 核定邊界，已遵守。
- Claude Medium 指出 GUI 導覽與篩選細節超出審查提示列舉的七項事實；Low 指出未記錄識別值／未複製 DLL 的自述不在給定事實內。這些不構成產品缺陷；為縮小紀錄至已列舉證據，文件改為只列 GUI 觀察到的事件與檔案版本，刪除上述多餘敘述。
- 審查後另作兩項事實補充：唯讀查看伺服器 mscorlib.dll 詳細資料，檔案／產品版本為 4.8.4795.0；核對使用者手動提交 714465fd 並修正清單收尾條件。這兩項不宣稱已由前述審查涵蓋。
- 本次僅改驗收文件與既有任務紀錄；未重跑產品測試或全方案 §8，前節測試數字屬前次診斷補修。P0-13 仍未通過，尚未執行 verify、smoke 或停用收尾。

### 最終紀錄複審與本機檢查

- runId=`20261104-150013-p013-server-evidence-final-reviewer`；reviewMode=dual、dualModelComplete=true、quotaBlocked=false、degradedFallback=false。已讀取兩方完整輸出，均無 Critical／High；Gemini 無 Warning，Info 提醒遵守 false 的新提交收尾條件。
- Claude Medium 指官方頁面 HTTP 狀態與修訂日期不在本輪提示的已確認事實內。這兩項對故障定位沒有必要，已從 analysis.md 刪除，保留來源頁面連結與支援範圍限制。Low 提醒須將「不再請使用者重找事件」明確連回已記錄證據，已補明。
- 本次四份文件的 strict UTF-8 無 BOM、CRLF／final CRLF、任務 JSON 欄位檢查通過。清單內容與 HEAD 相同，未修改 true；核對內容時統一 Git 與工作目錄的換行表示，未改檔。Changed 編碼守門（Base 73b6e6cf）：checked=499、excluded=284、filesWithIssues=0；git diff --check 通過。

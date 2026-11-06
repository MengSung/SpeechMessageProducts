# P1-05 主會話最終審查

日期：2026-11-06；範圍為本包 14 個產品／測試檔案、生成資源、需求映射及驗證證據。
採主會話直接審查，遵守 inline 與使用者取消外部雙模型的指示。

## 實際檢查

- 完整讀取新增 context、snapshot、catalog、formatter、resolver、keyword 與新增測試；
  核對 tracked 差異及原 43 案保留，未以 git diff 遺漏新 untracked 程式。
- 對照 02 P1-05、90 C.1／E.3：五層、未知 key、具名參數、語言政策、英文空殼及 Production guard 全有實作與執行證據。
- SystemTextTable 保留原完整表 API，新增 raw table；沒有把繁中預展開誤判為 exact 翻譯。
- 教會、組織、產品完整比對；快照候選 formal ID、key/culture、來源 mutation、相同文字不同 key 均測。
- 缺參數先驗證再觸發 IFormattable、單次 replacement、明確文化、不修改 global culture。
- 關鍵字固定表只保存資源，NFKC／空白／case、同／跨文化衝突、明確模式與業務 priority；
  不把 P3 完整路由搬移冒稱為本包完成。
- 並行工作全部 await；provider 真 Exception 上傳 host、可重試；catalog 不保留 context／參數，弱參照回收通過。
- 沒有新 cache、連線、timer、queue、訂閱或 ownerless Task；所有輸入候選有界，net462 不使用不相容 API。
- resx 單一來源、生成檔由正式工具產生，I18nCheck 自身 108 案及 deterministic check 通過。
- 16 份方案 TRX 4004 案逐列核對，coverage 各目標恰一份，非合併或挑選報告。

## 審查中修正與結果

1. 參數缺少時仍可執行另一參數的任意格式化：真紅測試後改成兩階段，雙宿主綠。
2. 重疊前綴不能靠長字優先推測舊業務順序：新增明確穩定 key priority，真紅後雙宿主綠。
3. 新 untracked 檔案不在舊 Changed 掃描範圍：逐檔 strict UTF-8 檢查並無損轉 CRLF；
   最終 staging 後再跑完整 Changed，不能沿用漏新檔的早期 997 檔報告。
4. 進度頁 active count 曾未同步：補齊資料一致性，完整頁面 14 案通過，未放寬測試。

Critical：無。High：無。未處理的本包阻斷項目：無。
方案有 23 個未修改既有測試警告；Localization strict 為零警告、零錯誤，沒有新增抑制。

## 審查方式與證據界線

本包沒有新增外部 reviewer 或獨立第二人審查證據。
歷史自癒分析 `20261106-122201-p105-analysis-analyzer` 兩後端皆無可用輸出，
completedBackends=[]、dualModelComplete=false、degradedFallback=false，不能稱為雙模型分析通過。
本次依使用者取消雙模型，不重試相同供應商額度／403。

驗收：`wp-reports/P1-05-acceptance.md`。
可提交證據：`.ccg/tasks/integration-p1/evidence/p105-final/`。
最終完整 Changed／頁面守門及本地 commit 見 `closeout.json`。
主 P1 尚未完成，不能歸檔；只歸檔本 P1-05 子任務。未 push、未部署、未連 VM／D365、未發 LINE。

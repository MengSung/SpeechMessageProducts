# P1-01 收尾審查

## 範圍與判定

主會話直接審查，未派 implement/check 子代理。P1-01 產品清理在既有提交，這輪只補文件／證據及兩個 metadata 檔尾 CRLF。完整本機門檻通過，無待修的可重現 Critical／High，接受此工作包收尾；不接受整階段、hosted CI 或 P1-04 完成宣稱。

| 檢核 | 結論 |
| --- | --- |
| 51 路徑刪除前後 | Git 前樹確認 worker lock-only，候選外引用 0；現行 1,129 個追蹤來源／專案檔命中 0，51 路徑存在數 0 |
| 測試保留 | 原 15 個方法名稱皆在現行 16 案，Performance trait 存在，TRX 16 案逐一 Passed |
| 方案整合 | 15 個實際方案組態／30 條 ActiveCfg、Build.0 精確集合一致；ToolUtility 兩宿主均在完整方案 TRX |
| 本機回歸 | 16 個唯一組件／目標、2,278／2,278；Debug 7；Plugin build 6、metadata 10、tool 27 |
| 專項守門 | 架構所在組件67、duplicates 0、I18n verify 綠、Changed encoding 修正後綠 |
| 本輪產品改動 | C#／Razor／csproj／props／targets／workflow 差異為空；既有 21 個 build warning 未抑制，0 error |
| 最新使用者決策 | 保留現有 DevExpress，無升級／續訂／UI 替換；新來源／hosted 恢復暫緩，不阻擋 P1-01 |

## 外部模型原始結果與處置

兩輪均經 Start-CcgDualModelRun.ps1，要求 Gemini 與 Claude；不是主動選單模型。
analysis `20261105-213802`、reviewer `20261105-214807` 皆為 Gemini 成功、Claude quota blocked：`dualModelComplete=false`、`quotaBlocked=true`、`degradedFallback=true`。依既有核定 fallback 繼續，不能說完整雙模型審查通過。

- Analysis Critical「缺正式報告」：以 `wp-reports/P1-01-acceptance.md` 和四份來源／測試／執行／格式 JSON 補齊。
- Analysis 建議清除 untracked：不採納，正式清理只允許已驗證的追蹤檔；不存在把其他人的資料當孤兒刪除的授權。
- Analysis 說 Plugin 應 net48：錯誤，產品必須 net462；維持實際 metadata 守門，net48 只作測試宿主。
- Reviewer Critical：0。
- Reviewer Warning 1「記錄外圍格式修正」：報告、SHA-256、原 bytes 完全不變斷言與獨立提交 a3b5b749 已記錄，不是缺少證據。
- Reviewer Warning 2「未來跨目標套件衝突」：未指出目前缺陷；完整雙宿主／架構與 package 邊界已驗，維持現有守門，未新增套件或放寬檢查。
- 不採用外審無依據的 TypeScript／瀏覽器 100 分評價；本次未做瀏覽器驗收，也未把本機驗證冒充 hosted CI。

## 執行過程校正

初次稽核硬編碼四條方案組態失敗，已改成真實組態集合核對；產品方案未修改。Debug 正執行檢查參數由誤傳 TrxPath 修為工具實際 ReportPath，7 筆真實通過。初次 metadata final CRLF 失敗原紀錄保留；兩檔僅附加 CRLF，修後未修改 encoding 守門。Windows Python 的預設 cp950 曾拒絕讀 task JSON，改顯式 UTF-8；驗證摘要已檢查有效 JSON。這些是收尾工具使用修正，不是產品測試被略過。

## 交付責任

本地 commit／archive 只包含本收尾範圍。主線 Activity/Core、進度頁、主 P1 task 與其他 staging 都不由本對話覆寫；通知主實作根據正式報告更新進度。兩個格式修正的來源檔若在主線繼續變動，僅可在其最新內容附加檔尾 CRLF，不可用本快照覆蓋內容。

最終 staged diff check 通過；Changed encoding Base73b6e6cf 為807檔／780份raw排除／0問題。兩輪模型輸出原始 bytes 全數封存於 external-model-originals.zip 並附逐檔SHA；Git鏡像僅移除兩份health JSON的多餘檔尾空行，JSON值相等，其餘原始輸出不改。

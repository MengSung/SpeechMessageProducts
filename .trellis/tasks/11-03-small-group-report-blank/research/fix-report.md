# 小組回報空白修復回報

日期：2026-11-03（Asia/Taipei）。本報告接續第一階段 triage-report.md，取代其中「尚未進入修復」的目前狀態；第一階段的實驗紀錄保留供追溯。

## 結果與已知界線

使用者最新截圖與回報確認「小組牧養」「新人跟進關懷」兩個表格已恢復標題及資料列。截圖含實際個資，只記錄功能結果，不複製圖片或抄錄資料至 repo。

本機已重現並修正 `this.store(...).load(...).done is not a function`。圖表使用獨立 DataSource，不能以表格恢復推論圖表資料及軸標籤已全面正常。Codex 沒有開啟或重新整理實際網站，瀏覽器工具回報本機瀏覽器操作停用；沒有繞過設定，沒有連線 D365／LINE。

## 根因與修正

- WebApi store 原回傳 jQuery Deferred；載入協調器改回原生 Promise 後，遺失 DevExtreme 22.1.6 需要的 `.done/.fail` 與第二個完成參數（totalCount、summary、groupCount），使初始化中斷。
- Grid adapter 明確注入現有 `$.Deferred`；協調器直接觀察原 transport 的 done/fail，回傳受世代保護的唯讀 promise，完整保留多個成功／失敗參數與 context。日期切換仍沿用原生 Promise。
- 清理 active transport 的時機在同步消費者 callback 之前，避免 callback 拋錯或同步重入保留舊傳輸。保留 generation、abort、refresh 合併、WeakMap owner 隔離與 dispose。
- 移除主版面 14 個確實沒有部署來源、且使用者 Console 已呈現 404／MIME 錯誤的舊範本引用；保留實際使用的樣式與腳本，沒有更改 DevExtreme／jQuery 版本。此項不等同已證明圖表根因。
- IntegrateView 的協調器 script 加內容版本，避免一年 immutable 快取混用不同版本。CI 納入實際 Razor adapter 與 repo jQuery Deferred 的 Node 回歸測試。

Git 追溯：c9a1f7db53（2026-09-09）引入協調器原生 Promise；527ae45bb8（同日）接上 Grid adapter。這兩段不是本次 P0 新增，但使用者「2026-11-03 早上正常」的實際執行版本與快取狀態沒有證據，不能推論早晚差異的確切原因，也不能宣稱已排除所有 P0 間接影響。

## 驗證

| 項目 | 實際結果與證據 |
|---|---|
| 前端 TDD | adapter-red.txt：新增 10 個案例全部失敗；修正後 adapter-green.txt：原 5 個＋新 10 個＋同步重入 1 個，共 16／16 通過。執行實際 Razor script 與現有 jQuery Callbacks／Deferred，沒有模擬 Deferred 來掩蓋相容問題。 |
| 隔離與生命週期 | 包含同名不同資料庫 ID 保留、兩個 owner 獨立、晚到成功／失敗拒絕、原始錯誤與 metadata 保留、同步 callback 拋錯、同步重入、200 次 dispose／drain 歸零。 |
| Release 方案建置 | release-build.txt：`dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605` 成功，0 錯誤、37 警告。 |
| Release 方案測試 | release-tests.txt：1049 通過、8 失敗，總計 1057。ChurchReport 為 539／547；8 個失敗均在已提交的 P0-12A ImageUploadCharacterizationTests、personal 路徑（4 格式 × 2 orientation）。本任務沒有修改這些測試或圖片產品程式，不能宣稱全方案全綠。 |
| Debug tracer | 一般命令因執行中的 Debug 網站鎖住 ToolUtility.dll 等輸出而建置失敗，沒有停止使用者網站。另以既有 Debug 組件加 `--no-build` 執行，7／7 通過（debug-tracer-no-build.txt）；不把它當作本次重新建置成功。 |
| 重複掃描 | 26 專案、876 檔案，groups=0、new=0、stale=0。 |
| 多語系 | `dotnet run --project tools/I18nCheck -- verify` 成功；CJK ratchet 通過、實際生成檔檢查 1 檔通過。 |
| 編碼 | `Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf` 通過；另逐位元組檢查本任務文字檔為 UTF-8 無 BOM、CRLF、檔尾 CRLF，無 U+FFFD。時間戳記模型原始輸出不改寫。 |
| 差異 | git diff --check 通過；未混入 P0-12A 圖片修補、設定或金鑰變動。 |

Debug 建置失敗的舊 console capture 含解碼替代字元，已保留於本機 ignored artifacts/small-group-report-blank/debug-tracer-original.log；repo 中 debug-tracer.txt 改為可讀摘要，如實記錄建置失敗。沒有改寫 timestamp 模型原始證據。

## 外部分析與審查

- 分析：20261103-201904-small-group-deferred-analysis-retry-analyzer 的 Gemini／Claude 均完成，dualModelComplete=true；已讀取並核對兩份結果。
- 修正審查：20261103-202623-small-group-deferred-fix-review-reviewer 經自癒入口執行兩次，Gemini 完成、Claude 兩次逾時且沒有可用輸出；ok=false、dualModelComplete=false、quotaBlocked=false、degradedFallback=false。
- Gemini 未提出 Critical／Warning。其 UI／可及性評分屬靜態審查意見，不能作為實際瀏覽器、圖表或跨瀏覽器驗證證據。
- 未完成雙模型修正審查，任務維持 review 階段，不封存、不宣稱完成。最新差異與 SHA256 已準備於 .ccg/dual-model-runs/small-group-report-blank-review-packet.md，供人工轉交或後續同入口重試。
- 這是獨立問題修復的審查，並非階段 0 的 ClaudeOnly 總審查。

## 提交與待決

96487ea2 為使用者在工作期間手動提交，已核對其產品修正與本任務實作一致，保留原歷史；目前補充測試、規格及收尾回報尚未另行提交。沒有推送、合併、reset、rebase 或 amend。

待完成 Claude 修正審查後再決定收尾提交／封存。P0-12A 保留在 d42b6d3f 的特徵測試進度，本任務不修其 8 個失敗；回到該工作包時須先處理並再次確認全方案結果。圖表及早晚執行差異維持明確未驗證狀態，不把表格恢復擴張成整頁完整驗收。

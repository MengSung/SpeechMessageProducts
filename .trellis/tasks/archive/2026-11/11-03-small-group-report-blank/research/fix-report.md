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

96487ea2 為使用者在工作期間手動提交，已核對其產品修正與本任務實作一致；後續使用者手動提交 0f3d6a82 保存規格、驗證及審查包，亦已核對一致並保留。此段提交追溯補註留於工作區，不為補註改寫既有提交。沒有推送、合併、reset、rebase 或 amend。

待完成 Claude 修正審查後，再處理審查修正及收尾／封存。P0-12A 保留在 d42b6d3f 的特徵測試進度，本任務不修其 8 個失敗；回到該工作包時須先處理並再次確認全方案結果。圖表及早晚執行差異維持明確未驗證狀態，不把表格恢復擴張成整頁完整驗收。

## 使用者 2026-11-03 續行核定與風險待決

- 使用者確認點擊小組回報後頁面正常；圖表未逐項驗證。
- Claude 兩次失敗是逾時，不是配額問題，不能視為單模型降級完成。使用者核定改併階段 0 第 ⑧ 批審查；審查通過後才收尾與封存，本任務維持 review 階段。原人工包保留供追溯，不再阻擋 P0-12A。
- 依使用者核對，缺陷來源 c9a1f7db53、527ae45bb8（2026-09-09）也存在於 1.0.0.8、1.0.0.9、1.0.0.10 等正式線分支；修正目前只在 P0 分支。是否移植由使用者決定，本任務不在其他分支或 worktree 修改。
- 主版面移除的 JSZip 原本就不存在；DedicationFeeView、DedicationFeeViewWeb 的 Excel 匯出可能早已無法使用，屬既有問題。只記錄，未實測或修復匯出。

## 2026-11-04 審查通過與收尾

依使用者 2026-11-03／11-04 核定，併入階段 0 第 ⑧ 批及第 09 份修正複審後收尾。
兩份均由同一自癒入口完成：reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。
屬「單模型（使用者指定 ClaudeOnly）」，無未處理 Critical／High；前次 Claude 兩次逾時仍保留為失敗歷史，不冒稱雙模型完成。
第 ⑧ 批為 20261104-081149-integration-p0-stage0-resume-08-reviewer，第 09 份為 20261104-081853-integration-p0-stage0-fixes-09-reviewer。

本輪重新執行實際前端兩檔共 16／16，零失敗／跳過；完整階段驗證在來源 e3f89eb6 為 Release 1104／1104、ChurchReport 590／590、Debug tracer 7／7。
先前 P0-12A 八個 personal 特徵測試失敗已在該工作包處理，本報告的舊失敗／鎖檔證據不改寫。
Medium 以下 Node runner 零測試／skip 下限補強只記錄，本輪不擴大修改。

使用者已確認小組回報頁面恢復；**圖表未逐項驗證**。是否移植至 1.0.0.8／1.0.0.9／1.0.0.10 正式線仍由使用者決定。
JSZip 原本缺失及 DedicationFeeView／DedicationFeeViewWeb Excel 匯出的既有風險只記錄，未修復或驗收。
任務依核定封存於 .trellis/tasks/archive/2026-11/11-03-small-group-report-blank；CCG 對應 .ccg/tasks/archive/2026-11/small-group-report-blank。
沒有連線 D365／LINE，沒有更改 DevExtreme 版本、其他分支或 worktree，沒有推送、合併或改寫歷史。

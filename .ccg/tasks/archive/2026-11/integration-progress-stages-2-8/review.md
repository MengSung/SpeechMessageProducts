# 階段 2～8 進度頁審查與驗收

## 範圍

實作進度 HTML、頁面渲染測試及本次任務紀錄。沒有實作後續產品功能，沒有執行 D365／Hyper-V 或外部推播；付款測試及應用設定的既有修改不屬本次交付。

## 正式計畫對照

| 階段 | 文件與範圍 | 包數 | 頁面狀態 |
|---|---|---:|---|
| 2 | 03：P2-01～P2-12 | 12 | 全部未開始 |
| 3 | 03：P3-01～P3-09 | 9 | 全部未開始 |
| 4 | 03：P4-01～P4-06 | 6 | 全部未開始 |
| 5 | 04：P5-01～P5-13 | 13 | 全部未開始 |
| 6 | 04：P6-01～P6-06 | 6 | 全部未開始 |
| 7 | 04：P7-01～P7-02 | 2 | 全部未開始 |
| 8 | 04：P8-01～P8-05 | 5 | 全部未開始 |

直接從兩份原文擷取唯一 WP ID 與頁面陣列比對，53 對 53、無遺漏或額外項。每包具備工作內容、測試／驗收及前置界線；國際化追加工時已列入階段估時。總計 83 包、20 包已驗收，P1 維持 2/12。

## 本機證據

- `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs`：8/8、0 失敗、0 略過；保留既有 P1-03 獨立驗收檔案斷言。
- `git diff --check`：通過；最終提交前再次檢查。
- Edge／Playwright 真實瀏覽器：1440×1000 明色、390×844 明色、320×800 暗色均通過。每種 7 個階段、53 個 todo、Enter／Space 切換、可見加減號、0 頁面錯誤、0 溢出；每個獨立 context 與 browser 都在 finally 關閉，HTTP 外連全部阻擋。
- 瀏覽器驗收首次抓到既有 P0 長名稱／時間軸使 320px 畫面寬 362；修正最小欄寬後仍有警示長 API 文字造成 322。補換行後 320／390／1440 皆精確回到視窗寬度，未提高容許值或隱藏內容。
- 本機重跑程式、JSON 及畫面：`artifacts/progress-stages-2-8-browser/verify.cjs`、`result.json`、`{desktop,mobile,narrow-dark}.png`。頁面測試紀錄：`artifacts/progress-stages-2-8-tests.log`。
- 提交前再次重跑：頁面測試仍為 8/8、0 失敗、0 略過；三組瀏覽器結果均通過。正式 03／04 原文 ID 再次比對為 53 對 53、唯一數 53，缺漏與額外項皆為空。
- 為避免 ignored 產物隨工作樹清理遺失，本任務 `evidence/` 保存測試紀錄及 `browser/` 的重跑程式、結果 JSON、三張截圖。瀏覽器重跑須在工作樹根目錄設定 `NODE_PATH=C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules`，再執行該 `verify.cjs`；使用既有 Edge 及無登入資料的新 context。
- 暫存檢查曾指出四份 runner 自動產生的 `health-attempt-1.json` 有多餘檔尾空行。原始位元組與 SHA-256 已保存在 `evidence/original-runner-health.zip`，Git 文字複本只整理 CRLF 及檔尾空行，解析後 JSON 值逐一相等；模型 prompt／stdout／stderr 完全未改。整理後 `git diff --cached --check` 通過，沒有放寬檢查。

## CCG 分析與審查

全部使用 `docs/scripts/Start-CcgDualModelRun.ps1`，原始 prompt／stdout／stderr／summary 保存在 `.ccg/dual-model-runs/`。

| Run | 性質 | 結果與處置 |
|---|---|---|
| 20261105-152344…analysis-analyzer | 內容分析 | 雙模型完成、無配額降級；分析時舊頁尚未加入 53 包，不能將過期頁面結論當成現況 |
| 20261105-160221…review-reviewer | 完整內容初審 | 雙模型完成；Gemini title 編碼 Warning 經 UTF-8 位元組與 Claude 核實為誤報；展開提示 Info 已以紅→綠回歸修正 |
| 20261105-161352…final-review-reviewer | 展開提示複審 | 雙模型完成，無 Critical／Warning；FileVersion 關卡歸屬 Info 已澄清為階段 5 共用前提 |
| 20261105-162250…closeout-reviewer | 最後四項增量複核 | 雙模型完成，Gemini／Claude 均無 Critical／Warning，無配額降級 |

最後增量包括窄螢幕換行、P4-05 金流契約、P5-01 真實影子記錄摘要與 P5-03 共同關卡歸屬修正。兩模型均確認與正式規則相符，沒有安全或需求回歸。頁面最終 16:25 快照已同步審查與瀏覽器結果。

## 範圍與剩餘狀態

原正式計畫沒有修改，不需 Word 修正報告。主 P1 任務尚未完成、不得歸檔或標 Goal complete。完成本次文件工作後只歸檔此獨立 CCG 任務；不 push。

規範回饋檢查：本次未引入新的 API、資料契約或相依套件；窄螢幕長文字換行及原生 details 的可見提示已由實際頁面回歸驗證保護，沒有需要追加到跨產品規範的新約定。

本地頁面與驗證提交：`48964ee5`（`docs: 補齊階段2至8工作包與進度頁驗證`）；沒有 push。

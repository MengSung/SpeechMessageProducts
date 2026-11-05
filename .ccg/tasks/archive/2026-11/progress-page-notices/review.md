# 進度頁資訊／注意／錯誤分離驗收紀錄

- 修改的是使用者開啟的原始 D: 工作樹頁面與 tools/RepoQuality/Tests/Test-ProgressPage.cjs。本隔離工作樹的舊進度快照不覆蓋原頁，提交以 focused patch、雜湊與驗證證據保存本次改動。
- 未建立 Trellis 任務；依使用者指示直接 inline 修改，未委派子代理。
- 一般資訊採中性色原生 details，預設收合；注意為黃色、錯誤／阻擋為紅色，當前摘要獨立可見，長細節預設收合。現行歷史內容不自動推論為當前錯誤。
- notices 為獨立資料陣列，level 僅接受 info／warn／error；未知分類以一般資訊顯示，文字全部跳脫。
- 保留提示詞複製功能；複製 API 拒絕後可回退，命令失敗或拋錯皆移除 textarea。
- 所有既有 DATA 欄位／歷史項目逐值比對完全相同，僅新增 notices=[] 與 alert.level=info；P1-01=done、P1-04=active、P1=3/12、整體21/83不變。

## 驗證

- RED：原有8案通過，新增3案因缺少資訊收合／獨立 notices 區塊失敗（red-test-output.txt）。
- GREEN：原始頁面完整11/11測試通過（green-test-output.txt）。
- Edge headless：1440／390／320，各 light／dark 共6組通過；Enter／Space／點擊切換、44px summary、黃色／紅色／中性區分、獨立可見摘要、長字串無水平溢出、無JS例外。
- 每組驗證4條複製路徑：Clipboard API、fallback成功、false、拋錯；每次textarea歸零。每個context／browser均finally關閉，外部網路封鎖，不使用登入資料。
- 人工檢視390 light真實頁面與320 dark合成分類截圖；分類範例只存在瀏覽器測試，未寫入正式進度資料。
- 兩個修改檔均通過UTF-8無BOM、CRLF-only與檔尾CRLF位元組檢查；git diff --check通過。

## 外部分析／審查狀態

兩次均經 Start-CcgDualModelRun.ps1 自癒入口要求 Gemini＋Claude。Gemini皆完成，Claude週額度受限（供應商訊息：2026-11-07 16:00 Asia/Taipei重設）；dualModelComplete=false、degradedFallback=true、quotaBlocked=true，僅為已授權單模型降級，不能宣稱雙模型完成。

Gemini reviewer：Critical=0、Warning=0。Info為白名單、跳脫、原生details、焦點與清理方式的說明，無要求修正項目。審查後另以span取代summary內heading的div，維持合法phrasing content；最終11案及6組瀏覽器重跑通過。實際未執行螢幕閱讀器人工驗證，不推論所有AT均會即時播報。

## 規範回饋

沿用頁面既有native details與主題變數，沒有新套件／新跨產品契約，無需新增spec。

證據 patch 使用 zero-context 格式，僅可在核對 beforeHashes 相符後以 git apply --unidiff-zero 套用；原始完整 context patch 與外部 runner 原始輸出保留於本機 artifacts／dual-model-runs。

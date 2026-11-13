# 階段 4 完整交付自我稽核報告

稽核日期：2026-11-13（Asia/Taipei）

## 結論

階段 4 的程式、離線測試、結案文件、進度 DATA、任務封存、最後守門與分支推送均已完成；P4-06 依使用者核定標記為「已驗收（沙箱連線未驗證）」，不得解讀成永豐撥款查詢已驗收。合成回寫與冪等在 goodshepherdback 各通過 1/1，日期回歸在 GoodShepherd.Core 的 net10 與 net48 各通過 3/3；修正後的沙箱測試則為 0/1 通過，Order 階段 HTTP 400，沒有 APIM 代碼或可驗證的供應商狀態。主要未結事項是 HTTP 400 的來源尚不能歸類為核定的外部原因 C/D/E，也沒有證據能判定為程式錯誤；應先由使用者向永豐查明並完成沙箱補驗，且在補驗前不得開始階段 7 的首次正式組織切換。

## 工作包與證據判定

| 項目 | 核對結果 | 判定 |
|---|---|---|
| P4-00 | 子任務回報記載父任務、前置清單與 3 項 §8.3 輕量檢查完成。前置清單只記鍵名／存在狀態；初次 D365 verify exit 1 與教會識別／時區未驗證有如實記錄，P4-06 後續記錄 schema metadata verify 成功、時區空白時使用核定的 Asia/Taipei fallback。P4-00 未推送符合規則。 | 已驗證完成（回報／後續證據） |
| P4-01 | 五個同名 characterization tests、合成 golden、盤點表、固定 guard 摘要與首次推送 SHA 均列於回報；測試原始 TRX 未在目前工作樹或本次找到的本機 P4 證據目錄內，無法重新驗算回報列出的 TRX hash。 | 已驗證完成；原始測試檔證據目前不可重現 |
| P4-02 | 四個指定測試、Money 模型／provider 幣別宣告、ChurchReport 未修改、完整守門與遠端核對均有回報。原始 TRX 未找到；最終全方案 guard 對目前 HEAD 成功，另有 48 筆既有相容呼叫 CS0618 記錄及一處未保存 stdout 的額外呼叫點，並非本包失敗。 | 已驗證完成；原始測試檔證據目前不可重現 |
| P4-04 | profile 驗證／隔離、schema phase 4 單一欄位 plan、固定 guard 與推送均有回報和摘要 hash；原始 TRX 未找到。單一模型外部審查結果已如實揭露，不影響本包計畫驗收條件。 | 已驗證完成；原始測試檔證據目前不可重現 |
| P4-03 | 19 個指定測試名稱與結果、雙目標測試、scope 釋放、完整 guard、封存提交與遠端 SHA 均有回報。報告指向的本機證據根目錄目前只找到 P4-03 的部分 guard／tests 子目錄，未找到報告列出的全部逐項 TRX 路徑；回報中的摘要 hash 無法逐一重算。 | 已驗證完成；本機逐項原始證據不完整 |
| P4-05 | 六個指定 callback 測試、113 個主機測試、完整 guard、單模型審查與推送核對有回報及摘要 hash；原始 TRX／審查全文在目前可檢位置未找到，故不能獨立重算那些檔案 hash。 | 已驗證完成；原始測試／審查檔證據目前不可重現 |
| P4-06 合成回寫 | `P4-06-live-synthetic-retry-current.trx` 實際解析為 1/1 Passed；檔案 SHA-256 與回報一致。案例使用合成永豐回應。撥款、手續費、日期、撥款編號四欄核對記錄及金額 precision 說明見操作紀錄。 | 已驗證完成：goodshepherdback 合成回寫 |
| P4-06 冪等 | `P4-06-live-idempotency-current.trx` 實際解析為 1/1 Passed；SHA-256 與回報一致。操作紀錄描述同一待辦 ID、attempts 與收費單修改時間不變。 | 已驗證完成：goodshepherdback 實機冪等 |
| P4-06 永豐沙箱 | `P4-06-live-sandbox-post-fix-20261113.trx` 實際解析為 `Live_AllotQuery_SandboxRoundTrip` 0/1 Passed、1 Failed；SHA-256 與回報一致。console 診斷為 `Order:400`，待辦 Failed；沒有 APIM code 或可驗證供應商狀態。無法由現存安全證據確認 HTTP 400 是供應商拒絕、APIM／網路邊界回應或其他原因。 | 核定條件式完成；沙箱未驗證，原因未分類 |
| P4-06 離線日期／provider 回歸 | `AllotDate_UserLocal_WritesChurchMidnightAsUtc`、`PayDate_UserLocal_ReadsChurchCalendarDay`（16:00、23:59:59）在 net10 與 net48 各 3/3 Passed；`SinopacProviderTests` 15/15 Passed。TRX SHA-256 已與 P4-06 回報列值一致。 | 已驗證完成：離線回歸 |
| 階段審查 | 自審報告明確記「未取得外部審查」，Critical 0、High 0。這符合使用者免雙模型審核的明確指示；報告標示範圍到 `bcdaaf95`，其後程式碼相關提交為測試檔修改，最新產品程式提交仍在該範圍內。 | 完成（使用者核定自審；非外部審查） |
| 階段收尾 | 父任務及 P4 子任務封存；P3-02、P3-03、P3-05 及重複 P4-02 已移至封存；P4-06 驗收步驟文件、總回報、proposal 與使用者待辦存在。DATA 記錄 P4-01～06 done、階段 4 actual 約 1.1 天且 active=0；階段 5仍為 todo、沒有 current。最終完整 guard 有 `FULL_GUARD_RESULT=PASS`；本地 HEAD 與遠端 SHA 一致。 | 已驗證完成 |

## 主要發現與後續

1. **沙箱 HTTP 400 原因未能分類。** 已驗證的事實只有呼叫到 Order 階段、HTTP 400、沒有 APIM code／供應商狀態，且待辦 Failed。這不足以證明它符合條件式結案補充所列 C/D/E 的外部原因，也不足以判定是程式錯誤。現在進度頁狀態仍可依核定維持「已驗收（沙箱連線未驗證）」，但這不是供應商驗收。使用者需向永豐確認 HTTP 400 原因、開發機 IP 白名單要求、測試金鑰與測試商店代號是否配套；取得結果後再安排核准的沙箱補驗。P4-PROP-008 明確禁止補驗前進行階段 7 首次正式組織切換。
2. **部分早期原始證據未能從現存位置重現。** P4-01、P4-02、P4-04、P4-05 的回報含摘要及 SHA-256，但其原始 TRX／log 在目前工作樹和已檢查的標準本機 evidence 位置不存在。這是證據保存／可重現性缺口，不是測試失敗；最新 HEAD 的完整 guard 成功可補強目前程式的離線品質證據，不能回溯重建先前每包原始檔。
3. **格式檢查有非阻擋警示。** 對 `43ea38cd..HEAD` 執行唯讀 `git diff --check`，在封存的 P4-06 `prd.md`、`design.md`、`implement.md` 各發現一個新增 EOF 空白行。未影響建置、測試或固定 guard；本稽核遵守只讀邊界，未修改歷史檔。

## 全階段核對

- 工作樹分支：`1.0.0.11.Integration.P4.Worktree`。
- 稽核時 HEAD：`62cc6de51f335f3fc993af153bc9eebb9a18265b`。
- `git ls-remote origin refs/heads/1.0.0.11.Integration.P4.Worktree`：`62cc6de51f335f3fc993af153bc9eebb9a18265b`，與 HEAD 一致。
- 最終 guard 證據：封存 P4-06 evidence 的 `guard-62cc6de5-full.log`，SHA-256 `684348D5B8662DCF2D3DB57E45745EA6114F1103A116B388D120BD2A3B4FDD19`，內含 `FULL_GUARD_RESULT=PASS`。
- 計畫提案：進度頁有 P3-PROP-001～005、P4-PROP-001～008；P4-PROP-005 記錄 UserLocal 及日期讀寫契約，P4-PROP-008 記錄階段 7 切換前的沙箱補驗前置。
- 安全／範圍：本稽核未重跑 `PaymentsLive`，未讀取或修改 User Secrets，未連 D365，未推送，未修改 P4 回報或進度 DATA；階段 5 未啟動。

**稽核總判定：** 行政結案、離線守門及分支同步已完成；P4-06 是使用者核定的條件式完成，但永豐沙箱連線／撥款查詢仍未驗證。進入階段 7 首次正式組織切換前，必須先解除 P4-PROP-008 阻擋並完成補驗；除此之外，本次未發現尚未封存的 P4 任務或未推送的 P4 分支提交。

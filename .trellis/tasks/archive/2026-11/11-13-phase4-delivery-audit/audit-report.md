# 階段 4 完整交付自我稽核報告

稽核日期：2026-11-13（Asia/Taipei）；APIService 補驗收尾更新：2026-11-13 20:19 Asia/Taipei (UTC+08:00)

## 結論

階段 4 的程式、離線測試、結案文件、進度 DATA、任務封存、最後守門與分支推送均已完成。補查找到 Sinopac 外層 `APIService` 將 enum 數字序列化的回歸；修正後 provider 測試 20/20、AllotQuery 測試 11/11、固定 guard §8.1 通過。合成回寫與冪等在 goodshepherdback 各通過 1/1；Sandbox `Live_AllotQuery_SandboxRoundTrip` 補驗通過 1/1，取得已驗簽 provider status=`S`，清理殘留為 0，因此 P4-06 標示「已驗收」，P4-PROP-008 已解除。本次 TRX 未記錄撥款明細筆數，亦未進行真實付款；沒有資料支持宣稱發生撥款。尚待使用者核定的是 P4-PROP-009 的 APIService wire-contract 防回歸建議；階段 5 未啟動。

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
| P4-06 永豐沙箱 | 歷史 `P4-06-live-sandbox-post-fix-20261113.trx` 記錄 Order HTTP 400；APIService 修正後 `Live_AllotQuery_SandboxRoundTrip-20261113.trx` 1/1 Passed，SHA-256 `B05B3A689D82699F2D08ACE521D8FBF60EFE7C8BBF117BC2501F1A1B51428517`。TRX 輸出 provider status=`S`、cleanup Contact/Fee/Outbound Task 各 0。 | 已驗證：Sandbox AllotQuery 成功狀態；不代表有撥款明細或真實付款 |
| P4-06 離線日期／provider 回歸 | `AllotDate_UserLocal_WritesChurchMidnightAsUtc`、`PayDate_UserLocal_ReadsChurchCalendarDay`（16:00、23:59:59）在 net10 與 net48 各 3/3 Passed；`SinopacProviderTests` 15/15 Passed。TRX SHA-256 已與 P4-06 回報列值一致。 | 已驗證完成：離線回歸 |
| 階段審查 | 自審報告明確記「未取得外部審查」，Critical 0、High 0。這符合使用者免雙模型審核的明確指示；報告標示範圍到 `bcdaaf95`，其後程式碼相關提交為測試檔修改，最新產品程式提交仍在該範圍內。 | 完成（使用者核定自審；非外部審查） |
| 階段收尾 | 父任務及 P4 子任務封存；P3-02、P3-03、P3-05 及重複 P4-02 已移至封存；P4-06 驗收步驟文件、總回報、proposal 與使用者待辦存在。DATA 記錄 P4-01～06 done、階段 4 actual 約 1.1 天且 active=0；階段 5仍為 todo、沒有 current。最終完整 guard 有 `FULL_GUARD_RESULT=PASS`；本地 HEAD 與遠端 SHA 一致。 | 已驗證完成 |

## 主要發現與後續

1. **APIService 數字序列化回歸已修正並補驗。** 原始 HTTP 400 發生於外層 APIService 為 enum 數字的版本；修正後唯一一次核准 Sandbox 補驗取得已驗簽狀態 `S`，並通過清理檢查。這使此前 P4-PROP-008 的連線補驗前置阻擋解除。TRX 未列撥款明細筆數，故不宣稱本次取得實際撥款資料或完成付款；仍須遵守階段 7 的正式環境核准邊界。
2. **部分早期原始證據未能從現存位置重現。** P4-01、P4-02、P4-04、P4-05 的回報含摘要及 SHA-256，但其原始 TRX／log 在目前工作樹和已檢查的標準本機 evidence 位置不存在。這是證據保存／可重現性缺口，不是測試失敗；最新 HEAD 的完整 guard 成功可補強目前程式的離線品質證據，不能回溯重建先前每包原始檔。
3. **格式檢查有非阻擋警示。** 對 `43ea38cd..HEAD` 執行唯讀 `git diff --check`，在封存的 P4-06 `prd.md`、`design.md`、`implement.md` 各發現一個新增 EOF 空白行。未影響建置、測試或固定 guard；未修改這些歷史任務檔。
4. **初次稽核後的收尾差異。** 初次稽核時發現 APIService 修正提交尚未推送，且 P4-06 驗收步驟仍把已解除的永豐聯絡事項列為待辦；本次將驗收文件更正、補列 APIService 操作紀錄 SHA-256，並完成此補驗任務的封存、紀錄型檢查、提交及 P4 分支遠端核對。遠端 SHA 以實際交付回覆為準。

## 全階段核對

- 工作樹分支：`1.0.0.11.Integration.P4.Worktree`。
- 初次稽核時 HEAD／遠端 SHA：`62cc6de51f335f3fc993af153bc9eebb9a18265b`；APIService 補驗後新增修正提交及結案紀錄提交，最後遠端 SHA 以本次推送核對結果為準。
- 最終 guard 證據：封存 P4-06 evidence 的 `guard-62cc6de5-full.log`，SHA-256 `684348D5B8662DCF2D3DB57E45745EA6114F1103A116B388D120BD2A3B4FDD19`，內含 `FULL_GUARD_RESULT=PASS`。
- 計畫提案：進度頁有 P3-PROP-001～005、P4-PROP-001～009；P4-PROP-005 記錄 UserLocal 及日期讀寫契約，P4-PROP-008 已解除，P4-PROP-009 記錄 APIService wire-contract 防回歸建議。
- 安全／範圍：本稽核及後續收尾未重跑原合成回寫／冪等案例，未讀取或修改 User Secrets，未碰正式環境或真實付款；只執行一次核准的 Sandbox 補驗與紀錄檢查。階段 5 未啟動。

**稽核總判定：** P4-06 Sandbox AllotQuery 已補驗成功並取得已驗簽狀態 `S`；P4-PROP-008 阻擋解除。撥款明細筆數與真實付款未驗證；P4-PROP-009 待使用者核定。階段 4 已結案，P4 分支推送與遠端 SHA 須以本輪最終提交後的實際 `git ls-remote` 結果為準；階段 5 未啟動。

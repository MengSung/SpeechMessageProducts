# 離線原因碼審查處理

2026-11-19 Asia/Taipei (UTC+08:00)，實際測試時間見本機log時間戳；未連組織。

- 自癒入口20261119-090615-p7-02b-diagnostics-review-reviewer：Claude有可用報告，Gemini兩次503，ok=false、dualModelComplete=false、quotaBlocked=false。這不是完整雙模型審查，也不是額度降級成功；尚未執行最後ClaudeOnly階段審查。
- Critical：Claude未確認任何Critical。僅限五個staged檔案的靜態審查，不代表階段審查通過。
- W1確認：workflow讀取及XML解析原無碼。新增實際RunPreparedLive故障注入兩案；先紅（SDK fault及非法XML皆缺LIVE-PREP-10），窄範圍CapturePreparation包完整樣板讀取／解析／選取後綠。保留原SDK例外及數字碼，沒有改樣板選擇條件。
- W2不適用於目前可達路徑：ProductionGuard.VerifyServer僅kind=production執行，live-check嚴格只允許kind=test，故在本路徑必為no-op。原提議新增ServerIdentity原因碼的測試實際落入14（snapshot）而非這個點；已撤下不成立的測試，不新增正式關卡、不修改ProductionGuard。來源open／write lock／artifact建目錄屬命令基礎設施，既有固定失敗訊息保留；不冒稱這些已被新原因碼覆蓋。
- Info唯一性：補兩個contact及兩個binding的實際準備測試，分別碼07／05，零寫入。
- 流程狀態衝突核對：RunPreparedLive及LiveActivityProbe都要求原樣板statecode=0（停用），不是啟用。新離線對照：停用的合成單活動樣板可通過樣板關卡，故意以unsupported basis在factory碼11停止；啟用流程仍碼02拒絕。兩案零寫入，無矛盾，不放寬舊流程互斥。
- 碼12仍為Capture與安全摘要單元測試，沒有宣稱走到runner的禁止能力callback；其餘01～11走真準備入口，13～15走真CLI邊界。
- 本次24案通過。證據：artifacts/p7/live-prep-review-gaps-red.log與live-prep-review-gaps-green.log。不是實機通過證據，不能降級dormant。

## 後續離線定位（尚待審查）

2026-11-19 09:23 Asia/Taipei (UTC+08:00)：離線重放已定位共同準備拒絕為LIVE-PREP-02。既存switch-log的Before含146啟用平台流程，引用舊活動的啟用流程0、停用流程23，XML解析錯誤0（本機snapshot SHA-256 F90F7A2D11FAAD2361874D40C57C42E1EC902D2F6B1FECE896106B394894841D）。原RunPreparedLive以任何State!=0拒絕，四模組替身／真P7來源均重現碼02；改為完整舊活動清冊assembly/type精確比對後抵達刻意注入的metadata碼08，仍零寫入。這僅證明共同舊流程關卡修正，不代表metadata或整案準備通過。樣板原本即要求停用，無啟用樣板矛盾；啟用舊流程仍碼02拒絕。週報另補沿R4的WhoAmI owner共享繫結，先紅後綠，不建systemuser。診斷30案通過，未提交／全guard／live-check，兩份registration未動。

本機來源／鎖／目錄追加碼16：真CLI以檔案阻擋artifact root，先紅（匿名錯誤）後綠；source handle失敗沿碼14。ProductionGuard無修改。新增碼16與先前撤下的ServerIdentity提議無關。安全樣板讀取碼10範圍含SDK讀取、XML解析與選取；不得將舊版24／25案數冒充目前30案。

## ClaudeOnly 階段審查

2026-11-19 11:49 Asia/Taipei (UTC+08:00)：經`Start-CcgDualModelRun.ps1`完成，`ok=true`、`reviewMode=claude-only`，Claude輸出可用；明確標記「單模型（使用者指定 ClaudeOnly）」，不是雙模型審查。完整輸出與summary保留於`.ccg/dual-model-runs/20261119-114925-p7-02b-claude-only-review-reviewer/`。

- Critical：無發現。
- High：活動情境有明列輸入，但沒有經證實的Workflow XAML參數繫結契約，準備路徑以LIVE-PREP-09 fail-closed。修正：在演練紀錄、手冊常見狀況及P7證據頁明列為未解；不猜參數、不啟用舊流程、不改tier。
- Medium：情境測試先前使用固定數量，未從registration live tier反向核對，也未斷言lookup seed先建。修正：測試現在比較live registration與情境一對一，並逐筆斷言被參照seed排序在引用者之前；指定測試通過。
- Medium：LiveActivityProbe目前沒有CLI呼叫點。修正：檔案整體說明明列此owner目前不可由CLI到達，Live實機路徑仍未驗證。
- Medium：其他欄位存取在strict reader輸出後使用；本次不增加推測性原因碼。ProductionGuard在test目標的分流不因審查建議而擴大修改。
- Low：LIVE-PREP-09說明已明確化；不新增或重編原因碼。

審查未授權組織連線或live-check。本輪live項19、pass 0、既有dormant 14；guard仍被禁改檔案的MissingFinalCrlf阻擋，P7尚未結案。

2026-11-19 12:05 Asia/Taipei (UTC+08:00)：修正後複核仍為Critical／High無發現。補正M1說明：LIVE-PREP-09目前對所有非dormant活動拒絕，不依賴輸入集合是否為空。補正M2：模組整批準備使lessons、qrcodes、care、fees的活動拒絕連帶阻擋同模組Plugin案例；測試僅驗證registration與情境結構，不能解讀為可執行或實機通過。Low效能事項：活動樣板前置Workflow讀取尚未量測，記錄要求R6前重評。最終審查輸出為`.ccg/dual-model-runs/20261119-120548-p7-02b-claude-only-final-review-reviewer/`。

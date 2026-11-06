# P1-06 主會話審查

日期：2026-11-06，Asia/Taipei。實作主會話直接審查，沒有獨立第二人或外部 reviewer。
執行層要求的唯讀 analyzer 曾經自癒入口同時嘗試 Gemini／Claude：
`20261106-135758-p106-analysis-analyzer`，兩者皆無可用輸出，Claude 週額度阻擋；
`dualModelComplete=false`、`degradedFallback=false`，不是雙模型或單模型審查成功。
不反覆重試相同供應商阻擋，不改使用者取消外部 reviewer 的決定。

## 逐項審查

- API／相容：新增正式 IClock 在 Localization，不破壞 legacy ToolUtility 的 DateTime／LocalNow；
  Framework 不引用 TimeProvider，net10 只透過 TimeProvider 取得系統 UTC。
- 時間正確：Kind 明確；DST ambiguous／invalid 拒絕，UTC→local 重複時段可轉；
  ticks 溢位拒絕而不夾值；生日純日期不做 zone conversion。
- 隔離／資源：不保留 caller／教會／profile；沒有動態時區 cache、timer 或 task。
  借用來源不 Dispose；千次 A/B 與百次 local 弱參照通過。生成器所有串流 using、
  三個配置預算、完整候選後原子替換，check 不寫。
- 生成：CLDR 49 原 XML、完整授權與來源 SHA；445 個 ID 逐一由兩宿主獨立 XML oracle 驗證；
  territory 同值重述可接受，異值衝突整份拒絕。DTD 不解析、XXE 拒絕，不下載外部資料。
- 啟動：正式 Word 8.2／E.5 明列實際主機檢查，因此補入 ChurchReport.Main，
  順序為通知 owner→ICU→RunApplication，原 catch 報同一 Exception，finally 排空／釋放；
  真實 NLS 子程序在 Startup／CRM／HTTP 前停止，兩通知輸出關閉、不發 LINE。
- 品質：雙目標 strict 重建 0／0；最終正規化後全方案 17 TRX 4127／4127；
  各產品獨立 coverage 98.11%／98.47%，I18nCheck 96.77%，正式 80% 守門均通過。
  正式 Changed 編碼、duplicates、進度頁、Debug 與打包證據另列驗收紀錄。
- 範圍：無業務搬移、無 VM／D365 操作、無正式部署／push；正式需求未改，不需 Word 修正報告。

## 已修正與保留的真實失敗

1. 時鐘缺型別一案真紅；生成 CLI 首輪 2 紅／5 綠，修後通過。
2. verify 在 CLDR 缺檔時可能跳過：一案真紅後修正，正式 Localization 不得缺來源。
3. 主機原本沒有呼叫 guard：真實來源組合測試一案真紅後接入，不以 helper 測試冒充啟動整合。
4. 首輪完整 XML oracle 將多個空白當空 ID，兩宿主皆紅；依 CLDR 的 ID 清單語法處理空白，
   保留 445 個固定唯一 ID 及逐項值斷言，不刪案例或放寬斷言。
5. CA1510 使用每目標對應 API；CA1861 改每案重用單一分隔陣列，未關規則／新增 NoWarn。
6. 進度測試固定歷史 Constants 文案，改固定合成 renderer fixture；P1-12 的真實安全斷言保留。
7. runtime TRX 守門首輪拼錯參數，被拒絕；改正式 ReportPath，三份各 1 案通過，零案不算成功。

Critical／High／未修 Warning：無已知項目。這是實作主會話自審，不能宣稱獨立審查。

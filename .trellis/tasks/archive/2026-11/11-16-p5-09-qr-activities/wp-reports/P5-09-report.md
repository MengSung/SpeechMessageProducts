# P5-09 QR 活動與規則一頁回報

- 驗收時間：2026-11-16 09:02 Asia/Taipei (UTC+08:00)（本包收尾驗證時間，以實際提交與 guard 證據為準）。
- 基準與實機：三個舊入口以 `basis=reading` 盤點；六案 `scenarios.json`／`expected.json` 使用 reading／simulation。舊 DLL 未執行，goodshepherdback 只做新註冊的停用部署驗證；`live=deferred`、`differences=null`，未宣稱實機影子差異為零，也未重試追蹤管線。
- 交付：Core 完整入列、38 欄掃描與 QR 內容規則；三個薄活動外殼；Host schema2 產圖、bounded stream/staging、CAS 與重送防重；教會與 contact 隔離、正式 meeting binding；註冊 manifest 與離線／CAS／部分信任／生命週期測試。
- 安全收斂：`QrCodes:Qr:Churches:{churchGuid}:MeetingIds` 必須是每教會最多 10000 個、全域唯一的正式 GUID 白名單；缺 binding、錯 meeting、跨教會重複 binding 皆 fail-closed，出席鏈只能作額外一致性驗證。既有部署若未補 binding，worker 在 SDK、renderer、檔案 I/O 前拒絕；P7 前需由部署者填入已核對的正式 meeting IDs，不猜測、不以名稱 fallback。
- 驗證證據：RichMenus QR 測試 28/28；AspNetCore QR 測試 21/21；RichMenus 與 AspNetCore Release build 0 errors；兩 DLL 正式打包與 `Test-PluginAssembly.ps1` 均 `ok=true`（Plugin 1,114,624 bytes；Workflow 1,096,192 bytes）；goodshepherdback `plan=0`、`apply=0`、`verify=0`、舊步驟啟用 0、舊流程啟用 0。
- 固定 guard：待 P5-09 收尾紀錄與進度 DATA 提交後，在乾淨 guard checkout 以八項固定命令重跑；未通過前不推送。
- 待核定／階段末事項：追蹤管線仍 `live=deferred`；P5-PROP-006（需要推播的活動 Live 查詢前拒絕、Shadow 記錄意圖，Host 推播架構 P7 前補）；P5-PROP-001、P5-PROP-003 與 §9 事項於階段末一併登錄。
- 流程偏差：P5-09 包內有早期外部 runner 嘗試，Gemini 未產出、Claude 輸出不完整，未視為成功雙模型審查；包內依 D5-20 不再進行外部審查，完整階段外審只在階段末做一次。


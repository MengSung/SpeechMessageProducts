# P6-03 需求紀錄

2026-11-16 20:24 Asia/Taipei (UTC+08:00)

依 06A §4.1、§4.2、§5 P6-03 移除四個 P2-06 送訊相容殼，保留 ChurchReport 現役行為。所有 ChurchReport 推播都留在 legacy workflow，不改至 `LineMessagingProcessor.Core`。送出文字、SDK 訊息、RetryKey、診斷來源代號、失敗政策、等待方式與 client 的建立／Dispose owner 必須不變。

新增 `internal ChurchReportLineNotifier`，集中承接仍有正式呼叫者的文字 best-effort、多則 best-effort、可靠文字與 multicast 入口。P2-06 黃金檔不可修改，每一案必須且只能列為逐位元組相同重播，或列明 `9b7ac0a0` 無正式呼叫者而隨殼退役的理由。

呼叫面只以暫時改名四個型別後的建置錯誤盤點；搜尋僅用於計畫指定的 `typeof`、`nameof`、字串型別名及 DI 反射核對，不得以搜尋代替編譯探針。依 AGENTS 維持繁中完整文件、資源確定性所有權、跨頻道隔離及位元組契約。

禁止：改 `NoWarn`、`duplicate-baseline.json`、計畫文件、D365 步驟／流程；新增專案；切換 ChurchReport 至 Core；改動任何 LINE 內容或來源代號。

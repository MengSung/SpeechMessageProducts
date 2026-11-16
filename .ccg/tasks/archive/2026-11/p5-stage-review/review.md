# 階段 5 審查處置

2026-11-16 15:43 Asia/Taipei (UTC+08:00)

## 外部審查狀態

階段唯一 reviewer runner 已執行一次。Claude 有可用輸出；Gemini 兩次嘗試均為服務端錯誤、沒有輸出。摘要為 `dualModelComplete=false`、`ok=false`、`degradedFallback=false`，所以本次不是雙模型審查完成，也不符合成功的單模型降級條件。保留原 runner 全套輸出於 `.ccg/dual-model-runs/20261116-150625-p5-stage-final-review-reviewer/`，不重跑外部審查。

Claude 抽查約 15 個風險重點檔案，沒有發現可重現 Critical／High；沒有建置或測試，且未讀多個規則、部署與完整測試檔。主會話以既有包級測試、固定 guard、部署證據和指定同名測試完成交叉核對；不得把抽樣審查描述為全檔逐行獨立審查。

## Critical／High

未發現可重現項目。Claude 對外殼／Workflow Shadow 寫入隔離、資源所有權、通知順序、QR 教會隔離、Fees scope 與 Outbound Letter fail-closed 路徑的抽查未指出 Critical／High。

## Medium：記錄後續，不擴階段 5

- P5-11 待辦格式改為 `{feeId, queryDate}`，舊格式佇列項目會被新 Host 拒絕。P7 切換安排先排空舊待辦、先升級 Host、再啟用新的 D365 步驟；記入階段 5 總回報及進度頁「之後再說」。
- `FailureSummaryWorker` 在讀取封閉窗口前推進 `lastEnd`；若讀取故障，該窗口本次程序不重試。記入進度頁「之後再說」，不在本階段擴修。

## 疑慮核對與 Low

- Claude 提出 Care 與 LessonSigning 的 expected 可能未由規則消費。主會話已核對：Care 由 `CareServiceTests.Care_Scenarios_MatchExpected` 對照；課程簽核由 `LessonSigningShellTests.Lessons_Signing_ReadingBaseline_MatchesExpected` 對照。此項關閉，不列未處理 Medium。
- 其餘 Low 涉及彙總通知跨重啟重複、長停頓不補窗口、`modifiedon` 窗口精度與啟動期讀取；其中不追補／非 exactly-once 符合既定限制，未擴大修正。
- SDK 解析已恢復：工作樹 `global.json` 指定 `10.0.403`，本機 dotnet 選用 `10.0.403`；使用者確認 Visual Studio 不再顯示 SDK 錯誤。

## 邊界

本階段舊程式未在任何組織執行；既有實機部署僅為停用步驟的 plan/apply/verify。追蹤管道維持 `live=deferred`、`differences=null`。不啟動階段 6。

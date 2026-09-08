# 實作計畫

1. 追蹤目前 Controller → ListManager → DownloadIntegrateData → DataSourceLoader 資料流並完成雙模型分析。
2. 建立失敗測試，固定 single-flight、完整候選快照、失敗不發布、合法同名保留與 exact duplicate fail-closed 契約。
3. 由 ListManager 擁有單一載入 gate；每次載入建立 operation-local loader/candidate，完成驗證後才原子發布。
4. 建立 detached read，讓 Controller 在鎖外只序列化純值深複製，不持有 Session 可變集合。
5. 移除 LINE 登入對同一 request state 的 Task.Run/Task.WhenAll 競態，保持依賴順序。
6. 執行測試、建置、隔離／生命週期、UTF-8/CRLF 與差異檢查，再進行雙模型審查。

# 實施計畫

1. 讀完整舊入口與 AppointmentSigningLibrary，建立一頁 `wp-reports/P5-08-inventory.md`，列明觸發、深度、欄位、推播、`new_channel_access_token`、外部寫入與 reading basis。
2. 建立 3～6 個 appointment scenarios 與 reading expected；先寫具名測試紅燈，再沿 P5-07 共用簽核部分實作規則。
3. 建立 `AppointmentPlugin`、`AppointmentSigningActivity` 與必要 manifest／companion 登錄；活動 Live 先拒絕、Shadow 記意圖。
4. 執行具名測試與固定共用回歸；逐目標 Core、Plugins、Workflows、CAS；先打包再做停用部署。
5. 執行文件、編碼、進度與部署驗證；每個綠燈小段以 P5-08 開頭提交，紅燈不提交。
6. 一頁報告、固定 guard、乾淨 guard、遠端差異核對後推送；若 Workflows 部署連續失敗兩次停止，否則接續收尾。

## 回滾點

所有實機步驟保持停用；失敗或取消清理本次操作列並恢復追蹤設定。不得啟用舊步驟、不得跑 Live 新步驟、不得重試 diagnostics／shadow。

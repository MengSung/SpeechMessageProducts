# 階段 4 執行順序

依 04A §5 逐包工作，不把後續工作提前併入目前包。每包均先把該包的精確檔案、測試與完成定義寫入其 Trellis 子任務，再依 00 §7.1 驗收；不得增加計畫外測試或文件。

1. **P4-00**：階段切換、父任務前置檢查及任務歸檔；只跑 §8.3 輕量檢查。提交後不推送。
2. **P4-01**：金流行為盤點與 characterization tests。依 04A 指定測試、golden 檔及 file-purpose companions 完成後跑完整 guard；這是 P4 分支第一次推送，核對 `git ls-remote` SHA。
3. **P4-02 金額改用 Money**：依 04A §5 調整架構層級，允許 `SpeechMessage.Payments` 參考 `SpeechMessage.Localization`；公開模型新增 `Money` 成員，舊 `decimal`／`Currency` 成員保留為 `[Obsolete]` 轉接；由各 provider 宣告支援幣別（永豐、MyPay、台新均為 `TWD`），Gateway 在送出 HTTP 前拒絕不支援幣別且不自動換算。ChurchReport 與 `ChurchReport.MemberInfo.Tests` 不修改。四個同名測試為 `Payments_ToLocalization_IsAllowed`、`Localization_ToPayments_IsRejected`、`Money_TwdRoundTrip_NoDecimalLoss`、`CreateOrder_UnsupportedCurrency_Rejected`；依 04A 指令執行測試、完整 guard、回報及推送核對。
4. **P4-04**：依 04A 完成部署 schema 與 `new_sinopacprofile` 結構；只在 plan 出現其他差異時依授權規則停下；完成指定測試、完整 guard、回報及推送核對。
5. **P4-03**：依 04A 實作永豐撥款查詢及費用領域比對；遵守 D365 教會隔離、待辦契約、資源生命週期和同名測試；完成指定測試、完整 guard、回報及推送核對。
6. **P4-05**：依 D4-8 建立回呼端點、處理管線與三道關卡；沒有產品掛勾時回 503，不實作收費單回寫；登錄 04A §9 指定提案；完成指定測試、完整 guard、回報及推送核對。
7. **P4-06**：在開發機執行 04A 指定的 `Category=PaymentsLive` 測試與 D365 結構核對。測試只使用測試組織及永豐沙箱，不部署 P309、不啟動整台 LineWebhook、不進行真實付款；第二個案例使用合成永豐回應並在回報明確標示。只在結構 plan 超出 `new_sinopacprofile` 或同一實機步驟失敗兩次等核定條件時尋求使用者決定。
8. **階段審查**：依 00 §6.6 完成規定審查與交叉驗證，留本機原始審查 evidence 及一頁摘要；只修復規則要求必修的 Critical／High。完成階段回報、推送並以 `git ls-remote` 核對 SHA，隨即停止，不啟動階段 5。

## 共通執行守則

- 不新增 `.csproj`，不擴充 `tools\LegacyGolden`，不改 ChurchReport `appsettings*.json` 既有金鑰；不接觸正式組織、正式商店或正式 LINE Channel。
- 秘密只由 User Secrets、環境變數或標準輸入取得，不放入命令列參數、repo、log、回報或進度頁。P4-00 的測試金鑰存在性已通過，不代表已完成 P4-06 的移轉或實機驗證。
- 進度頁只改 DATA，逐筆加上 `YYYY-MM-DD HH:mm Asia/Taipei (UTC+08:00)`；每包回報放在其子任務 `wp-reports`，內容限一頁並引用原始 evidence 檔名及 SHA-256。
- 任何超出 04A 的改善只記入進度頁「之後再說」，不在階段 4 實作；04A §9 的四項計畫修正提案在對應工作包完成時記入「計畫修正提案」。

# 階段 4 整合設計

## 依據與邊界

本文件描述跨工作包的整合邊界；各包的檔案、型別、方法、測試及驗收以 04A §5 為準。階段起點是 `43ea38cd`，分支為 `1.0.0.11.Integration.P4.Worktree`。04A §2 的 D4-1～D4-12 已核定，不重新選型。

## 元件責任與資料流

- `SpeechMessage.Payments` 負責永豐 API、簽章及 `Money` 互通；依 D4-3 將架構層級從 0 調整為 8，參考 `SpeechMessage.Localization`，不參考其他專案。
- `GoodShepherd.Core\Fees\` 負責收費單與永豐撥款查詢的比對、授權範圍與回寫規則；`SpeechMessage.Payments.Workflows` 提供 `AllotQueryHandler`；`SpeechMessage.Payments.AspNetCore` 擁有主機註冊與回呼端點。LineWebhook 僅做 04A 指定的註冊及兩行主機整合。
- 出站工作沿用 `IOutboundTaskHandlerFactory.OpenAsync(OutboundWorkItem, ct)` 逐筆建立 scope 的現有契約。Worker 的 D365 候選查詢先限制為自己會處理的待辦類型；handler 依待辦 `ChurchId` 解析教會與 `account.new_sinopacprofile`，不信任 payload 或呼叫端提供的 profile。
- 撥款待辦以收費單 ID 作 `sourceId`，payload 僅含 `feeId`。處理時核對待辦教會、收費單、會友及會友的 `parentcustomerid`，完成授權與比對後才允許外部查詢或發佈回寫結果。
- 回呼端點使用 `Payment:Callbacks:Enabled` 開關與既有三道關卡；目前沒有產品掛勾時回 503。依 D4-8，本階段不新增 ChurchReport 或 LineWebhook 的收費單實際回寫產品掛勾。
- `Payment:Outbound:Enabled`、`Payment:Callbacks:Enabled` 缺省皆為 `false`；沒有 `Payment` 區段時 LineWebhook 必須維持既有啟動行為。

## 隔離、生命週期與失敗

每個待辦的 scope、handler 及其 Dataverse 資源依現有 factory scope 擁有並在逐筆工作結束時釋放；不得把身份、profile、token、回應或可變資料放入 singleton、跨教會快取或背景共用狀態。資料來源必須以伺服器端驗證的 `ChurchId` 及 D365 關聯為準。缺少或無法解析教會 profile、範圍不符、金額／幣別不符或回呼防護失敗時，採失敗關閉；取消、逾時與傳輸狀態依現有 worker 重試契約處理，異常不可跨請求重用。

## 驗證與部署界線

各工作包執行 04A 指定且同名的測試；每個產品程式提交在固定 guard 執行 00 §8.1，紀錄型提交執行 §8.3。P4-06 僅在開發機執行 `Category=PaymentsLive`，連測試組織與永豐沙箱、不送真實付款；不重新部署 `LineWebhookP309`、不在本機啟動整台 LineWebhook。原始測試與審查輸出留本機 evidence，只提交回報摘要及 SHA-256。

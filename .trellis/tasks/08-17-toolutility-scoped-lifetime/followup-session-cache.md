# 後續票草稿：重新設計 session-key IMemoryCache 與 B 類 ToolUtility 持有鏈

## 問題陳述

`Models/InMemoryDataContextSmallGroup.cs` 目前以 `SessionId + 型別名稱` 作為程序級
`IMemoryCache` key，將有狀態的表單模型與 manager 保留 30 分鐘（absolute 與 sliding
expiration）。這些物件再以欄位或傳遞鏈持有 `ToolUtilityClass` 及其 Dataverse 連線，
因此不能在本任務直接改成 request-scoped 服務；否則下一個 request 可能取得已釋放租約，
或看見另一個使用者的可變狀態。

受影響的 13 個 session-key cache holder 是：

1. `ListManager`
2. `SmallGroupDataList`
3. `WeeklyReportData`
4. `NewPersonModel`
5. `PersonalInfomationModel`
6. `HappyGroupDataManager`
7. `ListManagementDataManager`
8. `EquipmentDataManager`
9. `FeeList`
10. `LineBindingViewModel`
11. `AppointmentsListManager`
12. `DonationPaymentManager`
13. `PollManager`

## 20 個 B 類 Factory 呼叫點

下列每一處都由上述 session cache 直接或間接持有，檔案與行號以 Run 3-A 完成後的
機械 grep 為準：

1. `Models/DonationPaymentManager.cs:59` — `DonationPaymentManager`
2. `Models/EquipmentDataManager.cs:51` — `EquipmentDataManager`
3. `Models/ListManagementDataManager.cs:65` — `ListManagementDataManager`
4. `Models/PollManager.cs:53` — `PollManager`
5. `Models/WeeklyReportRecord.cs:53` — `ListManager`
6. `Models/InMemoryDataContextSmallGroup.cs:1290` — legacy static getter
7. `WebServiceConnector/AppointmentsDownUpLoader.cs:47` — `AppointmentsListManager`
8. `WebServiceConnector/ChurchListDataProcessor.cs:46` — `ListManagementDataManager`
9. `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs:139` — `DonationPaymentManager`
10. `WebServiceConnector/DownloadEquipment.cs:41` — `EquipmentDataManager`
11. `WebServiceConnector/DownloadHappyGroup.cs:43` — `HappyGroupDataManager`
12. `WebServiceConnector/DownloadIntegrateData.Core.cs:37` — `ListManager`
13. `WebServiceConnector/DownloadListManager.cs:45` — `ListManager`
14. `WebServiceConnector/FeeDownUpLoader.cs:43` — `FeeList`
15. `WebServiceConnector/LineNotifyUtility.cs:43` — multiple session holders
16. `WebServiceConnector/NewPerson.cs:42` — `NewPersonModel` / `ListManagementDataManager`
17. `WebServiceConnector/PersonalInfomatioManager.cs:44` — `PersonalInfomationModel`
18. `WebServiceConnector/UploadIntegrateData.Core.cs:34` — `ListManager`
19. `WebServiceConnector/WeeklyReportManager.cs:43` — `WeeklyReportData`
20. `ViewModels/GalleryViewModel.cs:47` — `LineBindingViewModel`

## 已評估方向

### 方向 1：方法參數傳遞

移除長命 holder 的 ToolUtility 欄位，從每個 request 入口取得 scoped
`ToolUtilityClass`，沿方法鏈以參數傳入 connector，並在 request 或明確建立的 background
scope 內完成工作。

- 影響面：約 30 個以上檔案，實際會隨編譯器找出的傳遞鏈擴大。
- 優點：保留現有 13 個表單模型的跨 request 狀態與過期行為。
- 代價／風險：參數連鎖修改、`ref` 參數與非同步背景工作容易漏傳；任何捕獲 request
  scope 的工作都可能造成 `ObjectDisposedException`，必須逐鏈補隔離測試。

### 方向 2：移除 13 個 session-key cache

移除 `InMemoryDataContextSmallGroup` 的 13 個 cache entry，改由 request scope 建立模型與
manager；需要以明確 DTO、查詢或 session 資料重建原本跨 request 的表單狀態。

- 影響面：至少 `InMemoryDataContextSmallGroup.cs` 與 13 個模型／manager 建立點，並需
  重新檢查約 20–30 個 action、view model 與測試。
- 優點：直接消除 captive dependency 與程序級可變狀態，生命週期最清楚。
- 代價／風險：未遷移的表單欄位會遺失，並行 request 的可見性改變；若沒有完整狀態矩陣與
  A/B 隔離測試，可能造成登入、奉獻、LINE 綁定及報表流程回歸。

## 本任務不處理的原因

Run 3-A 的終點線是 6 個 per-request A 類呼叫點。20 個 B 類呼叫點依其 session-cache
持有鏈保留 Factory 與 legacy 建構式；本任務不選方向 1 或方向 2，不修改 13 個 cache，
也不在此票實作後續設計。任何 scoped ToolUtility 都不得寫入這些 cache。

# P6-05 逐檔文字清單（待前包核定；尚未驗收）

2026-11-17 02:48 Asia/Taipei (UTC+08:00)：等待P6-PROP-008期間的唯讀準備，未啟動第二個active包，
未改任何產品文字、未寫入基準、未提交或推送。

開工基準 ChurchReport 3,677，當下掃描3,583，減94。
其中本階段已改檔案淨減46（相容殼40、計算旗標6、核定QR格式拒絕分支1、統計搬移增加1）；
另48為未改檔案的開工前基準落差，不宣稱為本階段成果。
全專案唯一增加為通知器0→1的統計文字原樣搬移，符合D6-3第4類。

| 專案 | 原基準 | 當下掃描 | 說明 |
| --- | ---: | ---: | --- |
| Line.Messaging/Line.Messaging.csproj | 3 | 2 | 僅既有落差；來源未改 |
| SpeechMessage.Dynamics.Deployment/SpeechMessage.Dynamics.Deployment.csproj | 91 | 83 | 僅既有落差；來源未改 |
| SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj | 3677 | 3583 | P6淨減46＋既有落差48 |
| ToolUtility/ToolUtility.csproj | 188 | 67 | P6刪PushUtility減37＋既有落差84 |

| 檔案 | 原基準 | 當下掃描 | 原因 |
| --- | ---: | ---: | --- |
| Line.Messaging/Liff/LiffClient.cs | 1 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessage.Dynamics.Deployment/DeploymentCli.cs | 6 | 4 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessage.Dynamics.Deployment/DeploymentEngine.cs | 67 | 63 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessage.Dynamics.Deployment/RegistrationManifestLoader.cs | 3 | 1 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs | 8 | 6 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Services/ChurchReportLineNotifier.cs | 0 | 1 | P6-03：原統計文字搬移；唯一增加（D6-3 第4類） |
| SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs | 15 | 0 | P6-03：刪除無正式呼叫的相容殼 |
| SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs | 53 | 50 | P6-02：計算旗標2筆共用；P6-01：格式錯誤分支1筆依核定改為FormatException |
| SpeechMessageProducts.ChurchReport/Tools/PushUtility.cs | 23 | 0 | P6-03：刪除相容殼；統計文字原樣移至通知器 |
| SpeechMessageProducts.ChurchReport/Tools/ReplyUtility.cs | 2 | 0 | P6-03：刪除無正式呼叫的相容殼 |
| SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs | 25 | 23 | P6-02：計算旗標2筆改用共用取值 |
| SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs | 51 | 49 | P6-02：計算旗標2筆改用共用取值 |
| SpeechMessageProducts.ChurchReport/Views/Dedication/DedicationFeeView.cshtml | 6 | 3 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/Dedication/DedicationFeeViewWeb.cshtml | 7 | 4 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/Dedication/KeyInDedicationFeeView.cshtml | 4 | 2 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/Dedication/KeyInDedicationFeeViewWeb.cshtml | 5 | 3 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/DedicationAudit/DedicationFeeAuditViewLine.cshtml | 8 | 3 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/DedicationAudit/DedicationFeeAuditViewWeb.cshtml | 7 | 4 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/FeeManagement/Fee.cshtml | 8 | 1 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/FeeManagement/Present.cshtml | 13 | 2 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| SpeechMessageProducts.ChurchReport/Views/Home/FeeView.cshtml | 12 | 2 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/ContactOperations/ContactService.cs | 15 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/EntityOperations/EntityAttributeHandler.cs | 2 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/EntityOperations/EntityRepository.cs | 13 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/ListOperations/ListService.cs | 3 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/ListOperations/MarketingListService.cs | 15 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/PushUtility.cs | 37 | 0 | P6-03：刪除相容殼；統計文字原樣移至通知器 |
| ToolUtility/QueryOperations/FetchXmlQueryService.cs | 9 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/QueryOperations/PresentRecordQueryService.cs | 8 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/QueryOperations/QueryService.cs | 13 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |
| ToolUtility/QueryOperations/RelationshipQueryService.cs | 6 | 0 | 開工前既有基準落差；9b7ac0a0至目前此檔無變更，不列P6成果 |

## 共用資源逐項比對

比較P6-01～P6-03實際改過且仍存在的18個ChurchReport C#檔案；
以下8個固定值目前沒有完整字面值相同的剩餘可執行項目，因此沒有新增取值替換或顯示文字遞延項目。
計算旗標6處已於P6-02共用；不把相似或只含子字串的顯示文字當成相同。
SundayQrCodeUtility.cs:230仍有一行已註解的計算旗標呼叫，不屬可執行字面值，保留不動。

| 資源鍵 | 完整字面值命中 | 處理 |
| --- | ---: | --- |
| WeeklyReport.CalculatePrefix | 0 | 不變更 |
| WeeklyReport.ReplyNotification | 0 | 不變更 |
| WeeklyReport.Unfilled | 0 | 不變更 |
| WeeklyReport.CareList | 0 | 不變更 |
| WeeklyReport.HappinessPurpose | 0 | 不變更 |
| Messaging.Failure.NotBound | 0 | 不變更 |
| Messaging.Failure.ChurchMismatch | 0 | 不變更 |
| Messaging.Failure.Blocked | 0 | 不變更 |

比對檔案：

- `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`
- `SpeechMessageProducts.ChurchReport/Services/ChurchReportLineNotifier.cs`
- `SpeechMessageProducts.ChurchReport/Services/DonationBookingService.cs`
- `SpeechMessageProducts.ChurchReport/Startup.cs`
- `SpeechMessageProducts.ChurchReport/Tools/ChurchReportLegacyRichMenuCatalog.cs`
- `SpeechMessageProducts.ChurchReport/Tools/DonationFeePaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/QrScanContent.cs`
- `SpeechMessageProducts.ChurchReport/Tools/RecurringDonationPaymentProcessor.cs`
- `SpeechMessageProducts.ChurchReport/Tools/SmallGroupQrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs`
- `SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.FeeManagement.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.PaymentProcessing.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Utilities.cs`
- `SpeechMessageProducts.ChurchReport/WebServiceConnector/LineNotifyUtility.cs`

證據：`artifacts/p6/p6-05-scan-before.log`（scan --baseline，exit0），
SHA-256 `fea67dbec4a6b32497cd93b48d2d875c56320031dbb20b8bdcfe8aadb0a7f8bb`。
既有差異歸屬以 `git diff --name-only 9b7ac0a0` 核對。
待P6-03核定並驗收後，按06A正式寫入基準、verify、提交與完整guard；本頁不是完成證明。

## P6-03驗收後的正式核對

2026-11-17 07:49 Asia/Taipei (UTC+08:00)：scan-after-p6-03逐檔與原scan一致；正式scan --write-baseline已執行，31筆計數變化皆與上表一致，唯一增加為核定統計文字搬移。產生器另同步386個零計數來源項目的新增／移除，沒有增加字串。verify回0；未修改任何產品程式或顯示文字。下一步提交與固定guard，尚未驗收。

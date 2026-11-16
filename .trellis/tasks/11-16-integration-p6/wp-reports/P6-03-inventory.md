# P6-03 編譯盤點

2026-11-17 01:58 Asia/Taipei (UTC+08:00)：依 06A 逐一暫改四個型別並建置
ChurchReport、ChurchReport.MemberInfo.Tests、ToolUtility.Dataverse.Tests。
原始十二份輸出位於 `artifacts/p6/p6-03-probe-*.log`，產生時間為
2026-11-17 00:34 Asia/Taipei (UTC+08:00)；探針已還原，未重跑守門。

| 舊型別／入口 | 編譯指出的產品引用 | 測試引用／處置 |
| --- | --- | --- |
| ChurchReport PushUtility：文字、多則、可靠文字 | DonationFeePaymentProcessor:95；DonationPaymentManager:150；DonationBookingService:113,188；PersonalQrCodeUtility:53；QrCodeUtility:62；RecurringDonationPaymentProcessor:67；SmallGroupQrCodeUtility:66；SundayQrCodeUtility:52；DonationPaymentProcessor.Core:84,122,138,236 | 產品編譯先擋住測試編譯；修正產品後繼續以刪型別編譯錯誤補齊測試引用，不用搜尋代替 |
| ChurchReport ReplyUtility | DonationFeePaymentProcessor:98；RecurringDonationPaymentProcessor:69；DonationPaymentProcessor.Core:85,123,139；待刪的 LineUtilityClass:59 | 只剩欄位／建構，不是方法呼叫；刪欄位及建立，保留現有 ILineReplyWorkflow 參數 |
| ChurchReport LineUtilityClass | ChurchReport 與 ToolUtility.Dataverse.Tests 建置無型別錯誤 | P206SendUtilityGoldenTests:36；LineUtilityClassWorkflowTests:274,294；專用測試依計畫退役 |
| ToolUtility PushUtility：多播 | LineNotifyUtility:45 的型別別名；後續編譯確認實際建立與多播 | 產品編譯先擋住測試編譯，後續依編譯補記 |

`typeof`、`nameof`、字串型別名稱及 DI 註冊另做指定核對：
沒有產品反射建立／DI 註冊這四殼；產品內的 source tag 保留原文。
測試以 typeof 與 alias 直接引用舊殼，TenancyCallSiteTests 另列舊檔路徑；
ArchitectureRules 的舊例外路徑隨檔案刪除。這些是反射／來源契約核對，
不是以搜尋替代正式呼叫者編譯盤點。

付款 partial 的公開建構式直接收舊殼，刪除後改收公開共用 workflow，
在產品內部組成 internal 通知器；不把通知器升為 public。
既有 client owner、訊息／RetryKey、source tag 與等待方式保持。

黃金檔原始 SHA-256：

- P206-pre-migration.http.golden（67 案）：`efd0704ea3817c84a7190bf0354d9a23dd0ebcc8bda477784a0b34a7d03fa2ff`
- P206-source-replay.http.golden（5 案）：`8c07187be54f850ad2ad919b1cf0a556a18507c83a03a31431fec3b2ef54bc4c`

本表是進行中證據，尚非驗收；正式入口／測試刪除數與逐案退役由完成回報補齊。

## 2026-11-17 02:26 Asia/Taipei (UTC+08:00) 編譯收斂與測試清冊

四殼刪除後產品Release建置通過；測試編譯指出LineUtilityClassWorkflowTests兩個型別引用，依計畫刪專用測試後產品測試可建置。多則正式呼叫本輪未再發現，保留notifier入口與黃金案。LineNotifyUtility七個實際多播呼叫維持原不等待，註解中的舊呼叫不計正式入口。

ChurchReportLegacyRichMenuCatalog暫改名後，編譯指出Startup.cs:571仍有DI註冊，故已還原並保留；探針p6-03-catalog-probe.log。四殼沒有產品反射建立；TenancyCallSiteTests來源清冊刪舊檔，十五→十四。

| 刪除測試檔 | 原執行案例 | 處置 |
| --- | ---: | --- |
| LineSharedWorkflow/PushUtilityWorkflowTests.cs | 12 | 只測舊殼；保留四入口與錯誤政策於指定notifier測試 |
| LineSharedWorkflow/LineUtilityClassWorkflowTests.cs | 8 | 只測已刪殼；未復活舊demo入口 |
| Payments/PushUtilityTests.cs | 3 | 只測已退役RequiredText別名 |
| ReplyUtilityGroupRoomProfileAdapterTests.cs | 6 | 3殼轉接退役，3共用profile／reply案例移入Workflows.Tests.LegacyWorkflowRouteTests |
| P206SendUtilityGoldenTests.cs（改寫） | 原83→9 | 72黃金案=6重播+66逐案退役；另保留legacy雙頻道媒體清理1案，原檔hash與逐案清冊2案 |

R2同步只刪已不存在來源44筆（Debug／Release各22），652→608；不改R2規則、不加例外。LineUtilityClass檔內20個公開資料／樣板型別隨檔刪除，產品與測試編譯沒有剩餘正式引用；P6-07再處理其餘公開型別。

完整產品測試639通過、1失敗：DevelopmentSafetyTests固定11個設定建構器，刪LineUtilityClass後10個，已保留原斷言並登錄P6-PROP-008；尚未驗收、不提交紅燈、不推送。ToolUtility雙目標1411＋1268、Dataverse79、Workflows39與指定十名稱16實例全通過；架構Debug／Release合計82/82通過；重複碼groups=0/new=0、文件1743/0、編碼85/0、進度28/28。完整固定guard尚未開始。

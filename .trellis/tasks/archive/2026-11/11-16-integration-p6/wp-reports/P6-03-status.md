# P6-03 驗收一頁回報

2026-11-17 07:46 Asia/Taipei (UTC+08:00)：程式提交451c7d47已納入06A修訂1、核定清冊下修與通知器誤用先行拒絕。
ChurchReport 641/641通過，指定十一名稱共17實例包含本次誤用紅綠；固定guard八項全通過。
Release總數7,062取完整守門22個測試組件加總，不含另列Debug tracer7；頁面驗收仍在階段末。

| 需求 | 證據 | 狀態 |
| --- | --- | --- |
| 四殼不存在、只改正式四入口 | 既有十二份編譯探針、Source_NoObsoleteSendShellTypes；保留legacy workflow／forwarder | 通過 |
| 原送訊政策、來源、RetryKey與200次雙頻道交錯 | 指定Notifier測試；D6-21錯誤模式四方法拒絕、CRM／HTTP零呼叫 | 通過 |
| P2-06原始位元組與72案歸屬 | 6重播逐位元組相同、66逐案明列退役理由，兩份SHA不變 | 通過 |
| 計數型測試依核定下修 | DevelopmentSafetyTests.SourceBuilders_AllLineConfigurationPaths_UseDevelopmentLayer：11→10，僅同步方法註解，其他斷言不動；TenancyCallSiteTests來源列舉15→14 | 通過 |
| R2清冊 | 652→608，只刪已不存在來源44筆（Debug／Release各22）；LineUtilityClass檔內20個資料／樣板型別隨殼刪除 | 通過 |
| 完整固定guard、五項定義、推送 | 451c7d47完整八項全過；首輪MSB4166中止保留，單節點重跑未改測試範圍 | 五項完成；收尾後推送核對 |

## 刪測試與搬移清單

| 檔案 | 原案例數 | 處置 |
| --- | ---: | --- |
| LineSharedWorkflow/PushUtilityWorkflowTests.cs | 12 | 舊殼專用測試刪除，正式政策由Notifier保護 |
| LineSharedWorkflow/LineUtilityClassWorkflowTests.cs | 8 | 舊殼專用測試刪除 |
| Payments/PushUtilityTests.cs | 3 | 退役RequiredText別名專用測試刪除 |
| ReplyUtilityGroupRoomProfileAdapterTests.cs | 6 | 3殼轉接退役；3共用profile／reply案例搬到LineMessagingProcessor.Workflows.Tests/LegacyWorkflowRouteTests.cs |
| P206SendUtilityGoldenTests.cs（改寫，非刪檔） | 83→9實例 | 72黃金案=6重播＋66退役；另保留共用legacy雙頻道媒體釋放契約 |

6重播案：push.SendMessage(String,String).default、push.SendMessage(String,List-1).default、
push.SendReliableMessageAsync(String,String,String).default、tool.MultiCastTextMessageAsync(IList-1,String).default、
source group-push、source multicast-duplicates。其餘66案逐筆入口與理由由P206測試清單保護。

保留未用參數：DonationFeePaymentProcessor兩個公開多載的lineReplyWorkflow，
RecurringDonationPaymentProcessor公開建構式的lineReplyWorkflow，
DonationPaymentProcessor兩個公開多載的aReplyUtility。保留既有注入簽章位置；留P8-05處理。
ChurchReportLegacyRichMenuCatalog仍被Startup註冊，依編譯證據保留。
原await／不等待／同步等待、client owner、D365值、LINE本文、source tag皆不變。

P6-04 stash 280ab07fd620f02deb3c12dc669480026539ebe7的七個變更檔與0bb9414a零差異；
依SHA找到stash@{0}後僅drop該筆，沒有pop，P3三筆保留。
D34已於02:34獨立程序唯讀通過、writes=0，不代表本包或頁面已做真實LINE驗收。
未新增csproj、未改NoWarn或duplicate-baseline.json、未部署或啟停任何D365步驟／流程，未啟動P7。

## 原始證據（本機artifacts/p6，不進版控）

- p6-03-wrong-construction-red.log：SHA-256 `dec210d3562cbe3de8f895161538a181c936c6799b5bfe3c1605db022539c26d`
- p6-03-churchreport-final-green.log：SHA-256 `9e90028709453276e6dd2bacbe4a2bfaf31534903db6f722fa060641dd6cafd4`
- guard-451c7d47-prepare.log：SHA-256 `b7c6f84a17597b91452dad86465c95f59fe79c7797c353627b78319517ad327d`
- guard-451c7d47-full.log：SHA-256 `fe287d9dc578b4e05d2428b82b9f6996e09a03bc6d1bbcab309a9b026064fbe7`

P2-06原始黃金SHA-256：

- 主檔：`efd0704ea3817c84a7190bf0354d9a23dd0ebcc8bda477784a0b34a7d03fa2ff`
- 來源檔：`8c07187be54f850ad2ad919b1cf0a556a18507c83a03a31431fec3b2ef54bc4c`

2026-11-17 07:46 Asia/Taipei (UTC+08:00)：guard重跑通過，Release7,062＋另列Debug tracer7、編碼89、重複groups0/new0、I18nCheck0、文件1743/0、進度28/28。guard-451c7d47-retry-full.log SHA-256 `cb4c4a944222b088d1dcc4aae703c4cf2ad13d3fab4c61b25ba163b8a9a7a040`。紀錄收尾依§8.3檢查後明確推送；以git ls-remote回傳確認，不能把本地guard當遠端CI。

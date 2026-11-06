# P1-04：ToolUtility.Core 雙目標抽取與四個資料操作

日期：2026-11-06。狀態：最終文件與進度同步驗證中。

正式需求來源為實作計畫 3.3「02」P1-04 及「00」§5、§8；沒有修改正式計畫。
本包只抽取資料層及必要相容轉接，不把 P1-05～12、hosted CI 或實機整合列為本包已完成。
主 P1 任務繼續保留；最新結果以本文件及 `evidence/p104-final/` 為準，
先前各切片報告中的「尚待」是當時檢查點。

## 需求與驗收矩陣

| 正式需求 | 實作位置 | 測試與執行證據 | 結果 |
| --- | --- | --- | --- |
| 逐型別盤點及可搬／不可搬依據 | `wp-reports/P1-04-inventory.md` | 原 62 檔、76 宣告的來源索引；補列拆分後 Contact／Complex 資料型別；最新所有表列路徑存在，無未處理純化候選 | 通過 |
| Core 雙目標、strict、SDK 私有相依 | `ToolUtility.Core/ToolUtility.Core.csproj` | net462／net10.0，兩目標 SDK 均 PrivateAssets=all；Release strict 零警告、零錯誤 | 通過 |
| 原 namespace、單一資料實作、host 向下參考 | `ToolUtility.Core/*Operations/`、`Extensions/`、`Constants/`；host csproj、Contact／Complex adapters | `CoreTargetFrameworkTests` 實際反射產品組件、完整 67 多載 golden、nullable 與 host 指派；原 git mv 歷史保留，host 無 List／Marketing／CrmAsync 副本 | 通過 |
| 每個搬移 Operations 搬前特性先綠 | `ToolUtility.Tests/*Operations/`、`DataOperations/`、`Golden/ToolUtility/` | 各切片原 host golden／結果測試及後續安全紅綠；本輪摘要索引 `red-green-index.json` 保留原 TRX 雜湊與紅測名稱 | 通過 |
| `GetPaidFee` | Core `FeeOperations/FeeService.cs` | `FeeQueryContractTests` 7 個專屬執行案例：完整 golden、同名異 ID、課程名稱不能改 scope、非法 ID、SDK 原故障、重複／錯誤身份 | 通過 |
| `GetNotYetPaidFee` | 同上 | 同測試類別 7 個專屬案例，包含未繳條件與字串型別維持、故障及身份拒絕 | 通過 |
| `RetrieveFeeByFetchXmlOrderNumber` | 同上 | 8 個專屬案例；含 `<`、`&`、`"` 的訂單號解碼逐字相同，卡／ATM 兩條 OR 保留；空輸入／壞身份／SDK 故障拒絕 | 通過 |
| `RetrieveEntityNoCache` | Core `EntityOperations/EntityQueryService.cs` | `EntityQueryContractTests` 8 個專屬案例：同 ID 連讀真的兩次 SDK、caller mutation、錯表／錯 ID、空 ID／非法名稱、原故障保真 | 通過 |
| FetchXML 參數安全建構 | Core `DataOperations/SdkDataQuery.cs` 及各 Operations | XElement／XAttribute；完整 golden、特殊字元、非法結構與 DTD 拒絕；不以 escape 代替 host 授權 | 通過 |
| product 參數與舊多載 | host `DependencyInjection/ServiceCollectionExtensions.cs` | `ProductRegistrationTests` 8 案：真實 pool key 不同 product、舊 ChurchReport、相同註冊冪等、衝突拒絕不污染集合、租約歸零與 DI Dispose；舊多載 Obsolete 註明階段 6 | 通過 |
| 測試雙目標並納入方案 | `ToolUtility.Tests/ToolUtility.Tests.csproj`、sln／CI filter／coverage matrix | net48 測試實載 net462，1214/1214；net10 1345/1345；原 host 測試保留於 net10；CI 閉包契約 6/6 | 通過 |
| ChurchReport 舊功能與架構 | 全方案 Release | 16 份 TRX 共 3906/3906；ChurchReport 623/623、架構／Localization 67/67；實際結果列、Counters、零略過一致 | 通過 |
| 每產品目標行覆蓋率 ≥80% | 既有 `Test-Coverage.ps1` | 獨立新目錄、每目標恰一份；Core net462 95.45%、net10 95.81%；嚴格守門實際通過 | 通過 |
| 隔離、完整發布與資源生命週期 | Core 所有新增／抽取資料操作 | 合法同名異 ID 保留；空／重複／錯表 ID fail closed、後頁故障不發布；多組 1000 次 A/B、caller mutation、取消／Dispose 競爭、active 歸零與弱參照回收 | 通過 |
| 00 §8 與工具守門 | `artifacts/p1-04-final-20261106/` | Debug tracer 7、Plugin metadata 10／build 6、I18n 工具 108／PE 工具 27、I18n verify／generate check、套件 34 專案 158 引用零違規、duplicates 零新增、完整 Changed 編碼及進度頁結果 | 通過；末端編碼與頁面摘要見交付證據 |

四方法的上述數字是逐案解析 net10 TRX 得到的專屬案例數，不把同一參數化測試當成三個 API 都已測。
雙宿主均執行其適用純 Core 測試。精確名稱在
`.ccg/tasks/integration-p1/evidence/p104-final/required-methods-and-product-tests.json`。

## 最後抽取的實作與審查重點

| 範圍 | 相容與安全結果 | 搬前／修補證據 |
| --- | --- | --- |
| FetchXmlQueryService | 七入口保留 Execute、top、欄位及舊固定謂詞，唯一安全 XML helper，不保存 logger | 原 9 綠；安全 17 紅／3 綠、擴充矩陣 51 紅／11 綠；修後搬前 134 綠 |
| RelationshipQueryService | 六入口維持 link、條件 CLR 型別與原資料路線，正式結構名／ID／整頁守門 | `artifacts/p1-04-relationship-20261106/` 原始 TRX 及紅綠索引 |
| PresentRecordQueryService | 八入口保留原 UTC 窗、parent-only 日期 link 及原欄位 | 原 8 綠；63 紅／2 綠；修後搬前 72 綠 |
| QueryService | 五入口與真實 Repository consumer route；只借用 repository，時鐘可注入但保留舊 LocalNow 算法 | 原 5 綠；37 紅／3 綠；修後 Query＋Present＋consumer 131 綠 |
| ComplexQueryDataService／host adapter | 十六入口、weeks GUID 字串、blood TotalRecordCount 門檻、UTC-7 class-end 所在 filter、三條 SDK 路徑皆維持；host 只保留相容 ctor | 原 18 綠；84 紅／1 綠；修後搬前 103 綠，補 1000 A/B、Dispose／GC 與真 adapter 委派 |
| CrmAsyncExtensions | 固定最多 64 worker，5000 輸入／輸出、1000 頁、64Ki cookie；跨表同 Guid 不覆寫，全部 worker drain 才完成，Task closure 不留 SDK | 原 3 綠；有界矩陣 7 紅／1 綠；修後搬前 11 綠，最終兩宿主全量包含擴充生命週期測試 |
| List／MarketingList | List 20 方法、Marketing 7 方法；降冪、不多加 statecode、小組 `%退出%`、原姓名 link／leader 與 Repository route 保留 | 原名單特性 18/18（同輪另含 Complex 生命週期 5 案，共 23/23）；名單安全 93 紅／6 綠，修後搬前 117 綠；三種完整投影排序 3 紅後修復 |

名單完整投影在原排序後加正式主鍵，跨頁先驗完再回 ArrayList；精確成員投影也驗 entityid／contactid，
不能只驗 listmember 列 ID。六個批次入口在排程前複製輸入，最多 5000 人、每批 1～1000，
單一 owner 串行 SDK；保留既有批間 100／200ms 節流。後批故障立即停止，不回部分成功。
ExecuteMultiple 即使 ReturnResponses=false 仍檢查 Fault、回應型別及索引，保存原 OrganizationServiceFault。
寫入可能已部分提交，不做隱藏 retry 或假稱 rollback。

完整回歸曾發現 Marketing 查詢少了舊例外包裝層；已恢復固定訊息並保留原 InnerException，
`RepositoryCallerRouteTests` 12 案重跑通過，沒有放寬斷言。兩個 golden 只正規化 CRLF，
XML 結構與值未變。

## 隔離、資源與告警責任

所有操作借入 host 已授權 scope 的 SDK／repository，不建立連線、不持有 HttpContext／Session／credential，
不把格式正確的 ID 當成權限。無共享查詢結果或跨使用者可變 cache；公開欄位模板回傳 caller 複本。
Contact 舊帳密／LINE／姓名 cache 已停用，不把密碼放進 key。

Dispose 停止新入口、解除借用參照；已開始的同步 SDK 不可被假裝中斷，
caller／host 必須 await、drain 後才處置真正 SDK。六名單批次和 CrmAsync 固定 workers 的取消、
故障、Dispose 競爭、弱參照及保留已完成 Task 的回收均有測試。
caller token 明確取消才是正常取消；SDK 內部取消仍為 faulted，保留真 Exception。

Core 不是最後告警邊界：真正故障直接重拋，或固定訊息包裝但保留原 Exception，
交 host 既有共用通知 owner。沒有新增吞錯回成功的 catch、LINE client、無界 queue 或不接入的 notifier。
未對實機壓力、使用者授權流程或全專案其他 cache 作未驗證保證。

## 實際執行與證據位置

執行根目錄為目前 P1 worktree。完整原始輸出保存在本機 `artifacts/p1-04-final-20261106/`，
可追蹤的去敏摘要／精確測試名稱／SHA 在
`.ccg/tasks/integration-p1/evidence/p104-final/`。
該目錄有 306 份來源雜湊、16 份方案 TRX 計數與結果名稱、換行後兩宿主重建重測、
coverage 模組／類別摘要、工具結果、四方法映射及紅綠索引；不提交原 TRX 或任何金鑰。

主要實際命令：

```text
dotnet build SpeechMessageProducts.sln -c Release -warnaserror:NU1605
dotnet test SpeechMessageProducts.sln -c Release --no-build
dotnet build ToolUtility.Core -c Release --no-restore -warnaserror
dotnet test ToolUtility.Tests -c Release -f net48
dotnet test ToolUtility.Tests -c Release -f net10.0
dotnet test ToolUtility.Dataverse.Tests -c Debug --filter FullyQualifiedName~FileToolUtilityTracerTests
powershell -NoProfile -File tools/RepoQuality/Test-TextEncoding.ps1 -Scope Changed -Base 73b6e6cf
powershell -NoProfile -File tools/RepoQuality/Find-DuplicateCode.ps1 -Baseline tools/RepoQuality/duplicate-baseline.json
dotnet run --project tools/I18nCheck -- verify
dotnet run --project tools/I18nCheck -- generate --check
dotnet test tools/I18nCheck.Tests -c Release
dotnet test tools/PluginPackaging.Tests -c Release
node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs
```

Coverage 另用每目標新目錄及 `--collect:"XPlat Code Coverage"`，不加 TRX logger；
首次 logger 附件造成同 SHA 複本時，恰一份報告守門已拒絕，沒有挑較好報告或降低門檻。
Plugin 正反樣本由既有 scripts 執行，build suite 使用一次性合成測試金鑰並清理；
沒有使用正式金鑰或執行部署。

完整方案建置當輪有四個既有 Obsolete 警告、零錯誤；換行後 net10 測試重新編譯亦顯示既有
StringUtilityTests nullable 警告。Core strict 雙目標均為零警告、零錯誤，沒有新增 NoWarn。
Changed 編碼前置六份已提交歷史紀錄已另建任務、無損轉換並獨立提交 `d99a4c77`，
完整基準與工具規則保持不變。

## 審查、提交及剩餘界線

本輪由主會話逐項直接審查，依使用者「不需要雙模型」不呼叫外部模型。
歷史雙模型／配額降級證據保持原樣；沒有新增雙模型完成或獨立第二審查者證據。
沒有已知本包阻斷性缺陷。

主要程式提交：`6aac1c6f`（名單與最後生命週期）、`57078508`（Complex／CrmAsync）、
`ebe6d0b5`（查詢抽取）；其他早期切片的本地提交見各報告。
工作期間其他操作已提交的歷史不重寫；此次收尾提交只收可分離的驗收／文件／編碼差異。
收尾任務歸檔為 `.ccg/tasks/archive/2026-11/p104-final-delivery/`，主 P1 不封存。

剩餘 P1-04 阻塞：無。證據界線：未執行 hosted CI、VM／D365 實機整合或 LINE；
本地 coverage 使用正式 CI 同一守門，不能稱作 hosted 成功。
通用 SDK 同步呼叫的傳輸逾時與授權仍由 host 擁有，分頁也不是跨頁資料庫交易快照。
主 P1 尚有 P1-05～12；本包完成不代表整階段完成。

# 名單與 SDK Task 抽取契約

## 適用與介面

`ToolUtility.Core/ListOperations/` 的 ListService、MarketingListService、共用
ListMembershipOperations，以及 `Extensions/CrmAsyncExtensions.cs`。
沿用原 namespace、List 20 方法、Marketing 7 方法及原 batchSize=50／maxBatchSize=1000。
核心只借入 IOrganizationService／IEntityRepository；ComplexQuery 的三路只讀轉接
另由 ICrmQueryExecutor 提供，host 保留相容建構式。

## 必須維持

- host 在借入 SDK 前驗證 subject／organization／教會／產品與業務範圍。
  ID 格式、姓名或儲存 Fetch 正確都不代表授權；服務不能跨不相容 scope 共用。
- 單頁保留原欄位、條件 CLR 型別、排序、Execute／RetrieveMultiple／Repository 路線及 metadata；
  不把名稱相同當成同一筆。候選正式 ID 非空且唯一、根表一致，最多 5000 列。
- GetAll 名單完整讀取最多 5000 列／1000 頁／64Ki 字元 cookie，
  原排序後加已知名單根表主鍵，後頁失敗或身份衝突不回部分集合。
  ArrayList 的精確成員投影也驗 entityid／contactid，不能只驗 listmember 列。
- 四種名單 Fetch 維持降冪、不擅加 statecode；List 小組排除的是 `%退出%`，
  不能共用另一服務的 `%測試%` 謂詞。固定中文資料謂詞經 resx／正式生成器保存，不隨 UI 語言翻譯。
- 所有批次在排程前完整 snapshot 並驗 5000 輸入上限、正式唯一 ID、batch 1～1000。
  名單一個 owner 串行 SDK；List 原 100ms／外借批次 200ms 節流保留。
  任一故障停止後批、保留真實例外，不回部分成功、不隱藏 retry 或聲稱 rollback。
- ExecuteMultiple 的 ReturnResponses=false 只省成功回應，不能省 Fault 檢查。
  回應型別、索引範圍與重複亦須拒絕；OrganizationServiceFault 以原 Detail 保存，
  固定診斷訊息不得回顯其私密本文。
- CrmAsync 單次同步 SDK 不逐次 Task.Run；明確 ParallelRetrieve 最多 64 個固定 worker，
  輸入／輸出各 5000。Guid 字典不能容納跨表相同 Guid，故必須在 I/O 前拒絕。
  通用 QueryExpression 沿用 caller 查詢及排序，不臆測異形表 metadata；
  caller 在操作期間獨占查詢物件，完整讀取 finally 恢復原 PageInfo。
- caller 明確 token 取消才是 canceled；SDK 內部 OCE 仍是 faulted，不能漏掉最終共用告警。
  同步 I/O 入場後必須排空，Task.WhenAll 結束才發布或失敗，不留其他 worker 繼續碰 scope。
- Dispose 拒新入口並清借用參照；原 SDK／repository 不由 Core 處置。
  caller／host 必須 await／drain 後關依賴；已完成 Task 的 closure 清 SDK、輸入及 token，
  不讓 caller 長期保留 Task 就延長 credential 或 scope。

## 故障與驗證矩陣

| 注入 | 決定性斷言 |
| --- | --- |
| 同名不同正式 ID | 都保留；caller mutation 不影響後次操作 |
| 空／重複／錯表／超量／錯成員 ID | 整份候選拒絕，不略過、合併或給 draft key |
| 後頁 timeout、重複、壞 cookie | 不回第一頁；明確重試可以成功 |
| 中途批次故障、Fault 回應 | 後批不啟動、原 Exception／Fault Detail 可辨識 |
| 六入口預取消、I/O 中取消與 Dispose | 正確 canceled；已入場必 drain，新入口拒絕、borrowed Dispose=0 |
| 內部 OCE | faulted，保存原物件或 InnerException |
| 三 worker gate 與一批次 owner | active 不超上限，失敗回傳前 active=0 |
| 1000 A/B、服務 Dispose、保留完成 Task | 資料不串 scope；active 歸零、SDK 弱參照可回收 |

測試位置為 `ListCharacterizationTests`、`ListSafetyTests`、`ListLifecycleTests`、
`CrmAsyncExtensionCharacterizationTests`／`CrmAsyncExtensionSafetyTests`、
`ComplexQuery*Tests` 及 `CoreTargetFrameworkTests`。必須真實雙宿主執行，
Framework harness 實載 net462；這些是離線 SDK 契約，不等於 CRM 效能／實機整合。

## 維護禁例

禁止逐列 Task.Run、吞錯回已處理筆數、Guid 字典索引覆寫、按姓名去重、
Dispose 借入 SDK、把內部取消當正常取消、或以高覆蓋率代替原 host 特性先綠。
不要把相同方法名的 Present／Relationship 契約直接替換 Complex：
其 weeks GUID 字串、blood TotalRecordCount 門檻、UTC-7 class-end filter 位置與 SDK route 不同。

## P2-12：對外待辦 CAS 的整秒邊界（2026-11-09）

- 適用／介面：`D365OutboundTaskStore.TryClaim(candidate, utcNow, ct)` 的 `new_claimedon` 寫入及交易內讀回。
- 契約：goodshepherdback D365 9.1 實機讀回只保留整秒；先以 `utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerSecond` 向下量化成 UTC 再寫入，並嚴格比對該值。量化差小於一秒；候選資格仍用原始 utcNow，版本與完整 scope 驗證不變。
- 故障矩陣：含次秒且讀回整秒 → 成功取得新版本；回錯整秒、倒退／未增版本、錯 ID／scope／payload → 仍拒絕。明確版本衝突 → null；其他 SDK 故障原樣上拋。
- 正常／基本／錯誤：含次秒呼叫可取得 Processing owner；原有整秒輸入不變；不能直接比較未量化的 utcNow 而把已成功的資料庫 CAS 誤判失敗，也不能移除時間／版本核對。
- 測試：`Claim_SecondPrecisionReadback_KeepsSuccessfulOwnership` 注入1、1234567、9999999 ticks 及 SDK Unspecified Kind；雙宿主驗證寫入整秒、UTC 讀回、版本11及一次 SDK，並保留 `Claim_InconsistentReadbackRejected` 等原矩陣。
- 反例／正例：錯誤為 `claim.ClaimedOn != utcNow`；正確為與實際寫入的 `claimedOn` 完全相等，不容忍任意一秒時間窗。

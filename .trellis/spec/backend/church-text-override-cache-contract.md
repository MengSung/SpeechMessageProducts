# 教會覆寫來源與快取契約

## 適用與公開入口

P1-08 在 Localization 定義雙目標 `IChurchTextOverrideSource.ReadAsync(TextContext, CancellationToken)`、
`ChurchTextOverrideValidator`；Core 提供 `D365ChurchTextOverrideSource`，Tenancy 提供 net10
`ChurchTextOverrideCache` 及 `AddChurchTextOverrides`。改字輪詢由 P2 Hosting 擁有，不能在本類另建輪詢器。

Host 每次先授權完整 organization/product/church，再建不可變 TextContext。建構context不是授權；
快取命中仍須授權。來源只接完整context，沒有任意Guid查快照的捷徑。語系及格式改變不能改教會scope。

## 來源與發布

- Host 提供 `IChurchTextOverrideServiceFactory.Open(context, token)`，每次取得短命lease，
  實際Context與要求完整比對後才查 `new_localizedtext`，固定 `new_accountid` 等於教會ID。
- 借用SDK沿用Core完整分頁、主鍵排序、正式ID／重複ID／cookie／5000列守門，
  只取account/key/culture/text；全頁完成並轉為純值才回傳。任何後頁錯誤不可回前頁當成功。
- `DataverseChurchTextOverrideServiceFactory` 轉接既有固定部署manager，不能使用request-scoped Gateway。
  組合根須核對manager endpoint與固定組織產品；工廠不取得manager或raw SDK所有權。
- 成功或故障都在I/O排空後using歸還一次lease。來源失敗／取消先MarkFaulted，
  不確定transport不能回池為健康。Host先drain cache，最後才Dispose manager。
- 候選驗證：正式ID、完整教會scope、key/culture唯一、系統key存在、具名參數集合完全相同；
  key/culture/text為200/16/2000 UTF-16字元；最多5000列、總本文100萬字元；非法surrogate拒絕。
- 相同顯示文字但不同正式ID／key保留。不可依名稱／本文刪除、合併或靜默覆蓋。
- Sender展開完整最長名稱後≤20、ChatBar≤14；最大名稱必須與I18n limits一致，不能縮樣本換綠燈。
  所有列驗證後建立既有不可變快照，不保存原清單、SDK Entity或連線。

## 快取與資源擁有權

```csharp
await cache.LoadAsync(authorizedContext, serverTextChangedOnUtc, callerToken);
var snapshot = cache.GetSnapshot(authorizedContext); // 同步純記憶體，冷未載入明確拒絕
cache.Invalidate(authorizedContext, newerUtcStamp);
cache.Disable(authorizedContext);
await cache.DisposeAsync();
```

- scope鍵完整包含組織／產品／教會；entry另保留單調UTC stamp與generation，
  載入中失效的舊flight不得發布。舊stamp不能回退；不同文化共用同scope的原始覆寫表。
- 專有MemoryCache最多2000 scope、閒置30分鐘；成功載入滿10分鐘須重讀。
  失敗保留舊完整快照，不能更新成功時間或stamp；冷失敗保留原Exception並允許下次重試。
- 同scope ConcurrentDictionary/Lazy Task single-flight，最多64個來源flight，沒有無界等待佇列。
  短鎖只保護入場、版本與完整發布；不在鎖內執行I/O、通知或取消callback。
- caller token只取消自己的WaitAsync，不傳給共享來源。flight有獨立owner deadline
  （預設30秒、最高3分鐘）；來源實際transport仍須有界，取消後須drain已入場同步SDK。
- deadline為flight擁有的ITimer；Cancel callback的AggregateException交共用入口，
  清理時await timer.DisposeAsync後Dispose CTS，不能讓壞callback中斷清理或逃至ThreadPool。
- Lazy啟動及timer建立時同步SuppressFlow，不跨await，禁止捕捉caller AsyncLocal。
  ReadAsync及DisposeCoreAsync使用CompletedTask.ConfigureAwait(ForceYielding)，
  既強制離開caller堆疊，又不捕捉caller SynchronizationContext；不得改回Task.Yield。
- Disable移除entry並停止flight；不保存無界tombstone。重新Load只能由Host重新授權啟用。
  清理停止入場→停止flight→取消→排空全部來源→Clear/Dispose MemoryCache，重入共用同一清理Task。
- 結果容器保存原Exception，避免所有caller取消後有未觀測fault。真正失敗交必要共用notifier，
  notifier自身失敗只固定stderr、不遞迴、不覆蓋原錯誤。正常Disable/Dispose不誤報deadline故障。

## DI與故障矩陣

`AddChurchTextOverrides` 要求恰一個singleton來源；缺少、重複、scoped/transient來源或既有衝突owner拒絕。
catalog/provider/cache共用同一owner，容器必須await DisposeAsync。來源及通知委派不可捕捉request。

| 情境 | 必須結果 |
| --- | --- |
| 同scope32位caller | 一次來源，成功者取得完整快照 |
| 組織／產品／教會任一不同 | 不共享快照；錯lease在I/O前拒絕 |
| 重複ID、未知key、參數或字長不符 | 整候選拒絕，保留舊、告警、下次可重試 |
| 來源後頁故障／內部timeout | 原例外交共用入口；faulted lease歸還淘汰 |
| 一位caller取消 | 其他caller來源與結果不受影響 |
| invalidate／disable／dispose後晚回應 | 不得回寫已失效entry；owner工作全部排空 |
| 通知器或cancel callback故障 | 不覆蓋原故障，不阻斷timer／flight歸零 |
| 長生命owner百scope載入停用 | 200個context/snapshot弱參照全部死亡，cache/flight/timer回零 |

## 必跑驗證

來源與validator兩產品宿主都跑；Framework測試必須證明載入net462。真manager adapter為net10 host專屬。
cache須包含32 single-flight、A/B千次、版本競態、容量2000/64、fake time10/30、冷失敗重試、
錯候選保留、caller取消、stop晚回應、壞callback、deadline、AsyncLocal與同步內容隔離、弱參照回收。
真ExceptionDiagnostics整合用SendLine=false，驗原例外重報只一筆且本文／組織不落檔。
各模組每target獨立coverage≥80%、strict及完整方案與00§8守門，不以離線Mock聲稱D365實機通過。

2026-11-06真紅：Task.Yield兩次擷取caller同步內容，百scope有48/200弱參照未釋放。
改owner排程後75/75、回收與排程兩案五輪10/10通過；不得藉延後排程或放寬斷言當作修復。

## P2-10 改字輪詢可執行契約

### 1. 範圍
Hosting輪詢，ToolUtility只轉接固定部署manager；不得以caller路由更換組織。Host先停止服務再Dispose cache／manager。

### 2. 入口
`ITextChangeSource.ReadAsync(DateTime sinceUtc, CancellationToken)`回傳最多5000筆`TextChangedChurch(ChurchId, ChangedOnUtc)`；`TextChangePollingService.PollOnceAsync`及BackgroundService共用同一入場旗標。

### 3. 不變條件
每30秒查嚴格大於上次成功查詢開始時間減2分鐘；僅成功且全部候選驗證後前進watermark。重複／空正式ID或非UTC拒絕整批，先驗候選再Invalidate完整organization/product/church scope；舊快照不清除。組合根必須核對來源manager與輪詢器固定部署範圍相同。SDK完整分頁只取new_textchangedon，按accountid排序，故障／取消標記lease不可重用後歸還。

### 4. 錯誤矩陣
來源故障：原例外交ExceptionReporting，watermark與快照不變，下輪重試；caller取消：排空、不通知；重疊手動呼叫：立即拒絕、不建等待佇列。

### 5. 正常／基本／禁止
正常：A改字只讓A世代前進，B保留同一快照；基本：空結果仍前進成功開始時間；禁止：用完成時間當watermark、故障後清cache或靜默Distinct重複ID。

### 6. 驗證
TextChangePollingServiceTests三個計畫同名測試，加重複ID整批拒絕、正常停止排空及30秒timer Dispose；DataverseTextChangeSourceTests斷言最小ColumnSet、嚴格時間、同時刻不同ID完整保留及原故障淘汰歸還。全部是離線證據。

### 7. 錯誤與正確
錯誤：`last = completedUtc`後立即Invalidate部分頁；正確：完整讀取驗證後Invalidate指定scope，再將last前進至startedUtc。

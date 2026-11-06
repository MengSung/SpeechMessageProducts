# 純契約及優化查詢委派契約

## 1. 適用範圍

ToolUtility.Core 的 Contact／List／Query 純介面與 EntityOptimizedQueryService。
抽取介面不等於 host 實作已完成安全驗證；不能把它作為整包 P1-04 完成證據。

## 2. 介面

保留五介面 67 個多載、namespace、ref count、batchSize=50、maxBatchSize=1000
及選用 CancellationToken。單筆查無回 Entity?，不是非 null 保證。
EntityOptimizedQueryService 保留 ctor(object, IOrganizationService) 與舊入口；
新增 RetrievePagedAsync(string, int pageNumber, ...) 不更改原 cookie 入口。
兩個不同 namespace 的 PagedResult<T> 必須明確限定，不能讓宿主編譯產生歧義。

## 3. 契約

- 單筆／單頁身份委派既有 EntityQueryService；完整批次／單頁續讀委派 CollectionQueryService。
- Batch 沒有 statecode，Paged 缺省也沒有；呼叫 Collection 時傳明確空 FilterExpression，
  不能讓下層 null 的預設啟用條件改掉原上層語義。
- 條件沒有直接 statecode 時，以局部 AND 包原 filter 與啟用條件；不修改 caller 樹，
  尤其不能把 statecode 塞進 OR。caller 執行中獨占輸入，服務不保存 filter／結果。
- top 本地預算1..5000，SDK 超 top 也拒絕；單頁保留 metadata，完整批次沿用下層預算。
- 每個局部 adapter 有 using owner；SDK 不轉移所有權，Dispose 停止新入口並清借用參照。
  已取得局部 SDK 的同步呼叫可排空；host 先 drain 再處置 SDK。
- 不保存 logger、credential 或 user state，不建立 cache／Task.Run／timer／queue。
  caller 取消在 I/O 前後檢查，內部 timeout 保留 fault，交 host 最終共用告警。

## 4. 驗證／錯誤矩陣

| 條件 | 必須結果 |
|---|---|
| 同名異 ID | 兩筆完整保留 |
| 空／重複／錯表／錯單筆 ID／null 回應 | 發布前拒絕 |
| 後頁故障或資料衝突 | 不回部分字典；保留原故障 |
| cookie page=2 | 下一次送第3頁；無cookie續頁用明確多載 |
| OR input 沒有 statecode | 原 OR 整體 AND statecode，輸入不變 |
| caller I/O 中取消／內部取消 | 前者取消、不發布；後者 faulted Task |
| Dispose 與 I/O 競爭 | 舊呼叫排空、新入口拒絕、borrowed Dispose=0 |

## 5. 正例／基本例／反例

正例：1000 次 A/B 呼叫只看到各 SDK scope 的合成記錄，排空 active=0。
基本例：原三個 Query／Retrieve golden 保持欄位、top 與差異條件。
反例：為重用 Collection 直接傳 null filter，暗中增加 statecode 過濾。

## 6. 必要測試

CoreTargetFrameworkTests 的完整 API golden、nullable／host 指派與實載目標；
OptimizedQueryCharacterizationTests 三個原 host 特性；OptimizedQuerySafetyTests
的錯誤身份矩陣、後頁故障、top、OR、取消、1000 A/B、Dispose 競爭與 weak reference。
新目標須各自一份 coverage >=80%，net48 宿主必須實載 net462；Mock 不等於實機。

## 7. 錯誤與正確

錯誤：`filter.Conditions.Add(statecode)`，或 `result[row.Id] = row` 靜默覆寫。
正確：局部 `AND(originalFilter, statecode)`，借用既有完整批次驗證並以正式 ID Add，
任何衝突在發布前失敗。所有 mutable DTO 只供原 caller，不當成跨 scope 共享快照。

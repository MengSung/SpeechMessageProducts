# 活動參與者發布與借用服務契約

## 1. 適用範圍

`ToolUtility.Core/ActivityOperations` 及 ToolUtility 公開活動委派鏈。活動資料及
SDK 的授權／組織身分由 host 先驗證；參與者表名或 GUID 不構成授權。

## 2. 介面

保留 `IActivityService` 六個方法、namespace 及原 ctor。具體
`ActivityService` 新增 `IDisposable`，不強迫既有介面實作者新增成員。
兩個參與者入口仍接收兩個 `ArrayList`，成功時 append、不 Clear。

## 3. 契約

- 消費列識別是 `(logical name, 正式 GUID)`。跨表同 GUID 與同名不同 ID
  都保留；同表同 ID 在既有前綴加新增候選的完整集合內重複即拒絕。
- 缺 `partyid` 的外部地址容器沿用略過，不補造 ID；有 `partyid` 但 null、
  錯型或空 ID 是壞資料，不能當作外部地址略過。
- caller 全程獨占兩個不同的普通 `ArrayList`，不並行修改或列舉；拒絕
  alias／衍生／唯讀／固定清單，避免自訂發布 callback。兩參數不能對違約
  的並行讀者原子交換。所有 I/O、驗證及兩個 Capacity 配置先完成，才以
  預先建好的 `object[]` 追加；不支援強制中止執行緒等程序級故障。
- 來源最多 5000 容器、前綴加新增輸出最多 5000 列，每批 500 個 GUID。
  這是本地配置／讀取預算，不是已查證的 D365 組織限制。
- 一般單頁保留原無排序 AllColumns／IN 查詢。若探測頁有 `MoreRecords`，
  驗證它不是空進度或「全數仍 more」，丟棄該候選，再從第一頁以
  `<logicalname>id` 升冪完整讀取。每頁必有進度、只含本批唯一識別；
  cookie 原樣傳遞，無 cookie 時用有界簡單分頁，最多本批 ID 數個正式頁。
  不混合探測頁與重讀資料。資料變動導致缺列／重複時整體拒絕，由 caller
  明確重試，不宣稱跨頁資料庫快照隔離。
- 共用 `EntityQueryService` 的正式 ID／根表驗頁，再驗頁 metadata、請求
  集合及跨頁唯一性。SDK 已反序列化的 AllColumns bytes 無法事後撤銷，
  host 仍須限制傳輸、單次逾時與連線併發。
- 不保存 logger、輸入或結果。SDK 僅限原授權 scope；Dispose 清除借用
  參照並停止新入口，已取得局部 SDK 的同步 I/O 可完成。host 先 drain，
  再處置自己擁有的 SDK；此服務永不 Dispose 借入 SDK。
- 狀態變更保留活動 1／4、appointment 3／5，只 Execute 一次；空 ID、
  null 或非 `SetStateResponse` 不能回報成功。不自動重試未知寫入。
- 真 SDK timeout／ArgumentException 都以固定英文包裝，保留同一原始
  InnerException 交最終 host 共用告警。不輸出原訊息或另建 notifier。

## 4. 驗證／錯誤矩陣

| 條件 | 結果 |
|---|---|
| 同名異 ID、跨表同 GUID | 全部保留且 type／value 對齊 |
| 前綴衝突、非法參照、錯頁、缺列、重複或超預算 | 兩清單原內容不變，不發布部分結果 |
| SDK 第二組／第二頁故障 | 保留真實 InnerException；明確重試可成功 |
| 缺欄位、null 欄位、空集合 | 不追加、不查 SDK，保留前綴 |
| Dispose 與已開始讀取競爭 | 新入口拒絕，既有操作排空，借入 Dispose=0 |

## 5. 正例／基本例／反例

正例：contact 與 systemuser 使用同一 GUID，回傳各自正確表的資料。
基本例：一次讀 contact／systemuser 兩組，逆序 SDK 回應仍按原參與者順序追加。
反例：`Dictionary<Guid, Entity>` 跨表覆寫，或先追加 types 再呼叫 SDK。

## 6. 必要測試

`ActivityCharacterizationTests` 保留原 host 六案及三份 golden；
`ActivitySafetyTests` 重現原產品 51 個紅案，再驗完整分頁、500／500／1
分批、故障及前綴不變。`ActivityLifecycleTests` 執行 1000 次 A/B 並行、
59 次故障後明確重試、caller mutation、weak-reference 回收及 Dispose/drain。
`ActivityCallerRouteTests` 執行真正常 ctor→公開方法→Facade→Lazy→Core、
required／optional 連續追加、原狀態碼及子服務 Dispose。
net48 宿主必須反射實載 net462 產品；兩目標獨立 coverage，不代替實機。

## 7. 錯誤／正確模式

錯誤：`types.Add(name); resultMap[id] = row;`，既可能部分發布又可能跨表覆寫。

正確：完整驗證前綴與新增 `(name,id)` → 局部批次／分頁候選 → 全部驗證
通過 → 預配置容量 → 在 caller 獨占範圍追加對齊陣列。不 Clear、不靜默去重。

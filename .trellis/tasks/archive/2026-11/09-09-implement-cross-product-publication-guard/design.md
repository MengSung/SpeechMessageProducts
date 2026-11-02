# 跨產品資料唯一 ID 與網路時序防護技術設計

## 1. 設計邊界

本次先完成 ChurchReport 週報初始載入的垂直切片，涵蓋資料候選建立、Session holder 原子發布、API consumer boundary、Razor 初始資料及 DevExtreme Grid 的非同步生命週期。共用元件只承擔不含產品資料的契約責任；不在 singleton、static 或瀏覽器全域陣列保存使用者資料。

## 2. 伺服器端發布契約

新增無狀態、泛型的 `RowPublicationGuard`。它接受 operation-local collection、資料庫 ID selector、consumer 名稱及容量上限，逐列驗證：列不為 null、ID 非空、同一 consumer collection 內 ID 唯一、筆數未超限。它只使用方法區域集合，不保存 Session、`HttpContext`、credential、CRM client、連線或任何 caller graph，因此呼叫完成後即可由 GC 回收。

`ListManager` 繼續擁有每個 Session holder instance 的 single-flight gate。候選完整建立與驗證後才能一次性替換已發布 snapshot；讀取端取得 deep-enough detached copy。Semaphore 的 owner 為 holder，生命週期受 Session holder 限制，不建立 per-key static dictionary，避免 lock entry 無界累積與跨使用者資料混用。

每個 API action 在呼叫 `DataSourceLoader.Load` 前，對「實際將被該 Grid 消費的集合」再執行 `RowPublicationGuard`。這層是 defense in depth：即使候選發布後的 mapping、selection 或未來維護修改意外重複列，仍不會把 duplicate key 交給 UI library。錯誤訊息包含 consumer 名稱與不敏感的衝突 ID，不包含姓名、token 或 credential。

分析已確認 `InsertPresentRecord`、`InsertNewPresentRecord` 與 `HandleSuccessfulNewPersonCreation` 可能在候選發布後直接修改活的 Session 物件圖，其中背景 `Task.Factory.StartNew` 還會延長 Session graph 的保留時間，且例外與結束時間缺乏明確 owner。這三條寫入路徑必須統一經過 `SmallGroupDataList` 既有同步根或改成 request-owned／明確受管的操作；不得另建第二把互不協調的鎖。寫入完成前要驗證 stable ID，重複 ID 必須拒絕，不得把 guard 當成只在輸出端隱藏根因的工具。

初始 Razor 路徑只接收 detached view model。Controller 不把 Session holder 內的可變 list 或 member reference 直接交給 View，確保 Razor 列舉期間不會與另一個 request 同時修改同一物件圖。

## 3. 前端載入協調器

新增 framework-neutral 的 `CollectionLoadCoordinator`，每個 DOM 元件 instance 各自建立一個 owner。owner 保存的狀態只有單調遞增 generation、目前 transport、最多一個 pending refresh 與必要的事件解除函式；不保存跨帳號資料快照。

每次 load／refresh 先增加 generation。callback 只有在 token 等於目前 generation 且 owner 尚未 dispose 時才能 render、顯示錯誤或結束 loading。開始新世代時會 abort 舊 transport，但即使 abort 無效或舊 success 仍晚到，token 檢查仍會拒絕它。

重複 refresh 只合併成一個 pending 意圖，避免慢網路下形成無界 promise／XHR queue。dispose 會先使 generation 失效，再 abort transport、清除 timer／pending flag、解除事件並清掉 closure 參考。這個順序確保晚到 callback 看見 owner 已失效，也避免 callback 在 cleanup 中重新排程。

DevExtreme adapter 繼續使用既有 WebApi data source，不建立第二條平行取數管線。adapter 的責任是確保同一容器只有一個 Grid owner、舊 instance 先 dispose、新 instance 才 mount，以及 refresh 經 coordinator 排程。

## 4. 身份與 scope

- 週報出席列的身份為 `PresentRecordId`。
- 兩筆不同 `PresentRecordId` 即使 `FullName` 相同也必須保留。
- request token、generation、operation idempotency key 不能取代 `PresentRecordId`。
- cache／Session hit 不能取代 server-side authorization；既有 validated user、tenant、報表、日期與認證 scope 必須維持。
- 正式列缺少 `PresentRecordId` 時拒絕發布。若未來 UI 要顯示尚未建立出席記錄的 Contact，必須設計代表 Contact 的明確 DTO 並使用真正 `ContactId`，不能偽造 `PresentRecordId`。

## 5. Manifest 與未來產品

新增 `docs/publication-contracts.json`，以 Solution-relative path 登記每個 collection consumer。第一筆是 ChurchReport `GET /SmallGroup/LoadIntegrate`，identity 為 `PresentRecordId`，adapter 為 DevExtreme，publication mode 為 snapshot replace。未來產品可使用其他資料庫與 UI 框架，但必須提供等價的 stable ID、scope、publication、coordinator 與 lifecycle tests。

## 6. 相容性、錯誤與效能

正常資料路徑只增加 O(n) HashSet 驗證與固定數量狀態欄位。這避免全域鎖與無界掃描；容量上限同時限制意外大量配置。合法同名不同 ID 的資料不受影響。

衝突時採 fail closed。載入錯誤不得沿用不同使用者、日期或認證世代的舊畫面；同 scope 是否保留上一個完整 snapshot 仍由既有 holder 契約控制。前端 error callback 同樣受 generation 驗證，舊錯誤不能覆蓋新成功畫面。

## 7. 回復策略

所有修改保持在目前 Git 工作樹，未部署前可按檔案回復。若某一層驗證造成相容性失敗，先用新增測試確認是資料契約或 adapter 問題；不可移除 stable-ID 驗證、改用姓名去重或引入 static 使用者狀態作為回復方案。

## 8. 驗證策略

以 TDD 完成：先建立會因缺少 consumer-boundary guard、舊 callback 隔離或重複 mount 防護而失敗的測試，確認失敗原因正確，再寫最小實作。完成後執行 targeted tests、完整相關測試、Release build、A/B concurrency、resource drain、manifest validation 與 byte-level encoding／line-ending 檢查，最後執行 CCG 雙模型 review。

# 實作跨產品資料唯一 ID 與網路時序防護

## Goal

在無法進入各教會現場重現問題的前提下，直接強化 ChurchReport 週報初始載入的伺服器發布邊界與瀏覽器非同步生命週期，使慢速 Wi-Fi、防火牆或代理重送、重複 GET、回應亂序、重複元件初始化及同一 Session 併發都不能把同一個資料庫唯一 ID 顯示成兩列；同時建立可供未來採購協會、建設公司及其他產品接入的共同發布契約。

## Confirmed Facts

- 使用者觀察到重複姓名會在剛開啟週報、尚未按任何按鈕時偶發出現，教會 Wi-Fi 較容易發生，5G 較少，但現場網路設備型號與設定未知。
- 後台同一出席事件只有一筆出席記錄；本任務不能假設資料庫存在兩個不同 `PresentRecordId` 才處理。
- 現有後端已具備 operation-local candidate、instance single-flight、完整候選驗證後發布及 detached read 的部分防線，但實際 API consumer boundary 與初始 Razor／前端生命週期仍需端到端驗證與補強。
- 資料列身份只能使用資料庫或權威來源的唯一 ID。不同 ID 即使姓名及所有顯示內容完全相同，也必須保留；同一 consumer collection 內相同非空 ID 重複時必須拒絕發布。
- 使用者要求所有新增或實質修改的 `.cs`、`.cshtml` 程式具有檔案層級、函式層級及非直觀程式區塊的深入繁體中文註解，並以 UTF-8 without BOM、CRLF、final CRLF 儲存。

## Requirements

- ChurchReport 週報的每個重複列必須以伺服器取得的 `PresentRecordId` 作為穩定身份；不得使用姓名、電話、顯示內容、陣列索引、時間或隨機值判定重複。
- 在資料交給 `DataSourceLoader`、JSON serializer、Razor 或 DevExtreme 之前，必須針對該元件實際消費的集合再次驗證非空 ID、唯一 ID 與合理容量；相同 ID 重複時 fail closed，不得 `Distinct`、`GroupBy`、取第一筆或覆蓋字典值。
- 後端只能發布完整、已驗證、與 Session 內可變物件圖分離的快照。載入失敗、逾時或取消不得留下半完成集合，也不得讓下一個 request 取得上一位使用者、上一租戶、上一日期或上一認證世代的資料。
- 初始 Razor model 與 API 回應必須使用 request-owned detached data；不得直接把 Session holder 的可變集合交給 serializer、Grid loader 或背景 callback。
- 每個前端資料元件最多只能有一個 mount owner、一個 active request 與一個 pending refresh。舊 success、error、complete／finally callback 必須以 generation token 判斷，不得修改新世代 UI。
- `abort` 只用於縮短資源生命週期，不能作為正確性邊界；真正的舊回應隔離必須由單調遞增 generation／token 保證。
- dispose 必須解除事件、取消仍在途的 transport、清除有界 timer／pending state 並釋放對 DOM、XHR、Session、HttpContext、credential、連線或大型資料圖的參考。
- 不得新增 static／singleton 使用者狀態、無界 queue、無界 cache、未觀察 task、長生命週期 timer、未釋放 cancellation registration、stream、handle 或 connection。
- 新增 `docs/publication-contracts.json`，登記 ChurchReport 第一個受保護 consumer 的產品、endpoint、view、資料庫 identity、scope、adapter、容量與契約測試。
- 修改必須維持現有路由、頁面操作與合法同名不同 ID 的顯示相容性；衝突時可安全拒絕載入，不得以靜默資料遺失換取表面正常。

## Acceptance Criteria

- [ ] 測試證明兩筆相同姓名但不同 `PresentRecordId` 的資料都會保留並各自呈現。
- [ ] 測試證明實際 API consumer collection 內同一 `PresentRecordId` 出現兩次時會拒絕發布，且不會把衝突集合交給 `DataSourceLoader`。
- [ ] 測試證明初始 Razor／API 讀取值與 Session holder 分離，呼叫端修改回傳集合或列物件不會污染下一次讀取。
- [ ] 至少 32 個同 scope 併發載入只建立一次完整候選；不同使用者、日期或認證 scope 不會共用可變資料。
- [ ] 前端測試模擬舊回應晚到、重複 refresh、重複 mount、取消與 dispose，證明只有最新 generation 可發布且資源計數回到基準線。
- [ ] ChurchReport Grid 的 row key 明確使用 `PresentRecordId`，且沒有任何依姓名或顯示內容去重的新增程式。
- [ ] `docs/publication-contracts.json` 可由自動化測試解析，並且 ChurchReport consumer 的 endpoint、view、identity 與測試套件欄位完整。
- [ ] 針對修改範圍的單元／整合測試、Release build、Session A/B 隔離與資源 drain 驗證全部通過。
- [ ] 所有新增或修改的 `.cs`、`.cshtml` 都有完整繁體中文註解，且 byte-level 驗證為 UTF-8 without BOM、CRLF only、final CRLF。
- [ ] 完成 CCG 雙模型分析及雙模型 review；若供應商 quota 阻擋，只能依規則標示 degraded fallback，不能冒稱雙模型成功。

## Out of Scope

- 不修改教會防火牆、Wi-Fi、代理、DNS、TLS 深度檢查或現場設備設定。
- 不宣稱已確定現場設備的單一根因；本任務以可重現的網路與時序故障模型建立程式防線。
- 不部署、不 push、不修改正式 Dataverse 資料，也不一次重寫 Solution 內所有既有產品。
- 不用姓名或內容相似度清理既有資料，也不自動刪除任何權威記錄。

## Open Questions

目前沒有阻擋實作的產品決策。若實作證據顯示現有 DevExtreme transport 無法在不改變既有 API 契約下套用 generation coordinator，應先回到設計階段記錄最小相容調整，不可用未驗證的全域狀態替代。

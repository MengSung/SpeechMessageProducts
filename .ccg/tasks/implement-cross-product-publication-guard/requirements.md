# 實作跨產品資料唯一 ID 與網路時序防護：需求

## 問題

ChurchReport 週報剛開啟、尚未操作時，在部分教會 Wi-Fi 環境偶發同一會友資料顯示兩次。供應商無法進入數十間教會逐站測試，因此程式必須直接抵抗高延遲、HTTP 重送、重複初始化、回應亂序與同 Session 併發，而不能依賴特定防火牆型號或現場設定。

## 不變條件

1. 一列是否相同只依資料庫／Dataverse 唯一 ID。ChurchReport 出席列使用 `PresentRecordId`。
2. 不同 ID 即使姓名與所有欄位相同也完整保留；同一 consumer collection 內相同 ID 重複時 fail closed。
3. 伺服器只發布完整、驗證後、detached 的 request-owned snapshot，不直接發布 Session 可變集合。
4. 前端每個元件只有一個 owner、一個 active request、一個 pending refresh；generation token 阻止舊 callback 改寫新 UI。
5. Session、identity、credential、cache、XHR、timer、event、task、connection 與 disposable 都有有界 owner 及確定 cleanup。
6. 新增／修改 `.cs`、`.cshtml` 具有深入繁體中文註解，格式為 UTF-8 without BOM、CRLF、final CRLF。
7. 新增跨產品 publication manifest，讓未來採購協會、建設公司等產品登記同一契約。

## 驗收

以 backend／frontend automated tests、Release build、A/B isolation、32-way concurrency、failure/retry、mutation isolation、resource drain、manifest validation、byte-level encoding check 及雙模型 review 證明上述契約。

## 不做事項

不修改現場網路設備、不部署、不 push、不刪除 Dataverse 資料、不按姓名或內容去重，也不宣稱已確定教會防火牆是唯一根因。

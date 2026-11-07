# [P2] QR 內容編碼與解碼

## Goal

在 `SpeechMessageProducts.GoodShepherd.Core/QrCodes` 建立不依賴 D365、LINE、Hosting 或圖片函式庫的雙目標 QR 內容編碼／解碼 API，供後續 P5/P6 消費端共用；不改變目前 ChurchReport 掃描流程。

## Requirements

- 僅處理課程、主日、個人與小組目前掃描器實際讀取的資料欄位。
- 使用非空資料庫 `Guid` 作為第一段穩定記錄身分；顯示名稱、索引、LINE ID 或呼叫端暫存值不得取代資料庫身分。
- 保留主日 `@` 與 `_` 兩種既有格式及其分支差異，保留課程 `enroll` 特例；其他欄位不可因 trim、大小寫轉換或 URL 編碼而改變。
- 無效輸入由 TryDecode 明確拒絕且輸出為空值；不把 payload 內容寫入例外或日誌。
- API 必須是純、同步、無狀態，不保存每次呼叫的內容或跨使用者狀態。
- 本切片使用合成黃金向量，清楚標示未取得實際 D365／正式圖片 payload；P2-07 之後的圖片產生與 ZXing 解碼 round-trip 仍是 P2-09 必要驗收的一部分。

## Out of Scope

- `QrImageGenerate`、ZXing.Net、ImageSharp、檔案／stream／背景工作生命週期。
- ChurchReport 舊類別改接共用 codec（規劃於 P6-01）。
- D365 實機讀寫、LINE 推播或任何真實外部資料操作。

## Acceptance Criteria

- [ ] 黃金測試精確比對 Encode 字串，並將其 Decode 回相同型別化欄位；包括主日 `@`／`_`、課程 enrollment、個人 GUID 與小組週報。
- [ ] 空 GUID、錯誤 GUID、缺欄、分隔符歧義與空欄位以 `false` 拒絕；不因無效內容洩漏原始 payload。
- [ ] `SpeechMessageProducts.GoodShepherd.Core` 實際 net462／net10.0 及測試宿主 net48／net10.0 均成功建置與測試。
- [ ] 新增／修改 C# 文件通過完整繁中檔案說明及 UTF-8 無 BOM、CRLF、最後 CRLF 位元組檢查。
- [ ] 維持 P2-09 進行中，直到 P2-07 依賴的圖片待辦處理器與 ZXing 反解驗收完成。

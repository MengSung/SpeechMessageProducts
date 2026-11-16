# P6-01 執行紀錄

依使用者與 06A 固定設計，四個工具及其副作用流程保留，新增無狀態 internal
`QrScanContent` 委派既有 Codec；在原切字串處呼叫，透過局部輸出保留原欄位寫回時點。
主日解析的分隔符由當次工具保存供 SigningProcess 沿用；不新增跨 request 狀態。

## 先紅後綠

- 2026-11-16 19:24 Asia/Taipei (UTC+08:00)：四個指定測試、11 實例先紅後綠。
  初始測試 helper 有 out 變數範圍編譯問題，先修測試宣告後才取得有效紅燈；
  `p6-01-red.log` 為可執行的 11 紅（NotImplemented／原解析仍存在），不是編譯失敗。
- 四個工具已完整讀取。HandleError 僅依 AJAX header 分流，不依例外型別，
  因此照 06A 使用固定 FormatException，不需 IndexOutOfRangeException 特例。
- 課程保留非 enroll 才寫 m_OnboardType；主日與小組保留原欄位寫回位置。
- 個人原 `Split` 結果永遠至少一欄，刪除其不可達空陣列分支；
  不刪／改可達回應文字。空內容仍由控制器處理解析失敗，屬 D6-3 核定差異。

## 驗收待辦

- [x] 指定四個測試同名，連結同一份合成黃金，六類差異及 at 底線屬性對照。
- [x] 原位置接入與受影響文件同步，不改 Codec 執行邏輯、不連 D365。
- [x] ChurchReport 734、Core net10/net48 各 369、Hosting 架構 82 全綠（含真實方案 Debug／Release 及 R4）。
- [x] 預提交編碼 42 檔／文件 1749 檔／進度頁 28 案全綠，git diff --check 通過；只有本包預期變更。
  DATA 外文字不變（Git blob 為正規化 LF、工作樹 CRLF）；06A SHA-256 與開工完全相同。
  共用 Codec 只同步導讀，不改執行邏輯；沒有新增需沉澱的架構規則。
- [x] d66dc770 固定 guard 完整 00 §8.1 全過（Release 7145、Debug 7、進度 28）；一頁回報及 DATA 驗收／時間／提交齊全。
- [ ] 紀錄型收尾輕量檢查，明確推送後 ls-remote 核對。

P6-04 stash 與待核定契約不屬本包，不套用、不改期望；頁面實機另於階段末。

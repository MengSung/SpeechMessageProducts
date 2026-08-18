# 審查任務：個人照片即時預覽修正

請審查目前工作樹中與本任務相關的 git diff，重點檔案為：

- `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`
- `ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs`

使用者問題：個人相關資料頁選取新照片後，畫面沒有立即顯示更新的照片。

已知根因與修正：前端先用 FileReader 顯示本地預覽，但上傳成功後切換到伺服器 URL；伺服器的 `GetContactImage` 依 Contact 與尺寸使用 IMemoryCache，而 `UploadContactImage` 原本沒有在 CRM 更新成功後清除快取，導致舊圖覆蓋新預覽。現在新增集中清除流程，在 CRM 更新成功後移除完整圖與 32..256 所有縮圖鍵；測試先建立完整圖與 80/256 縮圖快取，再驗證全部移除。

請檢查：

1. 正確性與是否真正修正使用者問題
2. 快取隔離、跨使用者／跨租戶資料洩漏
3. 競態、記憶體與資源生命週期
4. 效能、可維護性與測試品質
5. 是否有 Critical / Warning / Info

請輸出分級審查報告，並明確指出是否可以交付。

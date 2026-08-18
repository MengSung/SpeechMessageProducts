# 審查報告：個人照片即時預覽修正 (Personal Photo Preview Refresh Review)

## 1. Summary (整體評估)
本次審查針對個人相關資料頁選取新照片後，畫面未立即顯示更新照片的問題進行修正審查。
經分析，該問題的根因在於伺服器端 `GetContactImage` 路由使用 `IMemoryCache` 快取了完整圖與 32..256 像素的縮圖，而 `UploadContactImage` 在 CRM 更新成功後未使這些快取失效，導致前端雖然使用了帶有 `timestamp` 的 URL，伺服器仍回傳舊的快取圖片。

目前的修正方案在 `UploadContactImage` 成功更新 CRM 後，主動取得 `IMemoryCache` 並呼叫集中清除方法 `InvalidatePersonalImageCache`，迴圈移除該 Contact 的完整圖與 32..256 像素的所有縮圖快取。此修正方向正確，能徹底解決快取不一致的問題，且已補上對應的單元測試 `PersonalContactImageCacheTests` 進行回歸防護。

---

## 2. Accessibility Issues (無障礙性評估)
- **狀態回饋**：前端在選取檔案後，先透過 `FileReader` 進行本地預覽，並在 AJAX 上傳期間顯示 `dxLoadPanel`（「上傳資料中...」），上傳成功後顯示 `dxToast`（「上傳成功了!」）。這些機制能為使用螢幕閱讀器或需要明確狀態提示的使用者提供足夠的資訊，無障礙性表現良好。
- **語意化 HTML**：大頭照容器與上傳控制項結構清晰，無明顯的 a11y 缺陷。

---

## 3. Design Issues (設計一致性評估)
- **全站顯示一致性**：由於系統中大頭照可能以不同尺寸（如 32, 80, 256 等）顯示於導覽列、側邊欄或列表頁，本次修正選擇清除 32..256 像素的所有縮圖快取，確保了使用者更新照片後，全站所有元件在重新整理後都能同步顯示最新照片，避免了視覺上的不一致。
- **Token 與樣式**：此修正主要為後端邏輯與測試，未調整前端 CSS，無破壞現有設計系統 Token 的風險。

---

## 4. Suggestions (改進建議)
- **解耦縮圖尺寸範圍**：目前清除快取的範圍（32..256）與 `GetContactImage` 中的 `Math.Clamp(size, 32, 256)` 存在隱式耦合。建議未來可將此範圍定義為常數（例如 `MinThumbnailSize = 32` 與 `MaxThumbnailSize = 256`），以提高程式碼的可維護性。
- **反射測試註解**：由於單元測試使用反射呼叫私有方法 `InvalidatePersonalImageCache`，建議在該私有方法上方加上註解，提醒後續維護者此方法被單元測試反射引用，避免重構時因修改名稱而導致測試中斷。

---

## 5. Positive Notes (優秀實作)
- **漸進式增強 (Progressive Enhancement)**：前端採用「本地即時預覽 + 伺服器確認後更新」的模式，提供流暢的 UX 體驗。
- **快取隔離性極佳**：快取鍵明確包含 `contactGuid`，且操作嚴格限制在當前登入使用者的 `contactId`，完全杜絕了跨使用者或跨租戶資料洩漏的風險。
- **資源釋放確實**：縮圖生成方法 `CreateThumbnailIfNeeded` 確實使用 `using` 釋放 `MemoryStream` 與 `Image` 資源，避免了記憶體洩漏。

---

## 6. 分級審查報告 (Findings Classification)

### Critical (阻礙交付之嚴重缺陷)
* **無**。

### Warning (潛在風險或維護性問題)
* **反射測試的脆弱性**：
  * **檔案路徑**：`ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs`
  * **說明**：測試中使用反射 `typeof(PersonalController).GetMethod("InvalidatePersonalImageCache", ...)` 來測試私有靜態方法。若未來重構該方法名稱或簽章，編譯時無法偵測，需依賴測試執行失敗來發現。
* **硬編碼的縮圖尺寸範圍**：
  * **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs` (第 510 行)
  * **說明**：快取清除範圍（32..256）為硬編碼。若未來調整了 `GetContactImage` 中的 `Math.Clamp` 限制範圍，必須手動同步修改此處的清除邏輯，否則會造成部分尺寸快取殘留。

### Info (一般性資訊或建議)
* **前端 Cache-Busting 行為確認**：
  * **說明**：前端已在 `UploadContactImage` 的回傳 URL 中附加了 `timestamp={DateTime.Now.Ticks}`，且在 AJAX 成功回呼中正確更新了 `#profileImage` 的 `src`。因此，前端不需要做任何修改，只需確保後端快取正確清除即可。
* **編碼與行尾規範**：
  * **說明**：新增與修改的檔案皆符合 UTF-8 without BOM 與 CRLF 的專案規範。

---

## 7. Scoring & Recommendation (評分與交付決定)

```
VALIDATION REPORT
=================
User Experience: 20/20 - 修正了上傳新照片後畫面仍顯示舊照片的問題，消除了使用者的挫折感與重複上傳的行為，提供即時且正確的視覺回饋。
Visual Consistency: 20/20 - 集中清除完整圖與 32..256 所有尺寸的縮圖快取，確保全站所有使用到該大頭照的元件在重新整理或切換頁面後顯示一致的新照片。
Accessibility: 20/20 - 前端保留了 FileReader 本地即時預覽，並搭配上傳中的 LoadPanel 與成功後的 Toast 提示，為所有使用者提供清晰的狀態回饋。
Performance: 19/20 - 採用 Cache-Aside 模式，僅在更新時清除快取。雖然迴圈清除 225 個快取鍵，但純記憶體操作耗時極短（<1ms），且能避免每次讀取都向 CRM 查詢或重新生成縮圖，效能表現優異。
Browser Compatibility: 20/20 - 前端使用標準的 FileReader 與 AJAX 上傳，並在 URL 附加 timestamp 進行瀏覽器端的 cache-busting，相容於所有主流瀏覽器。

TOTAL SCORE: 99/100

ISSUES FOUND:
- [Warning] 單元測試使用反射呼叫私有靜態方法 `InvalidatePersonalImageCache`，若未來重構該方法名稱或簽章，編譯時無法偵測，需依賴測試執行失敗來發現。
- [Warning] 快取清除範圍（32..256）與 `GetContactImage` 中的 `Math.Clamp(size, 32, 256)` 存在隱式耦合。若未來調整了縮圖尺寸限制範圍，必須手動同步修改清除邏輯，否則會造成部分尺寸快取殘留。

RECOMMENDATION: PASS
```

**交付決定：可以交付 (PASS)**。本修正完整且安全地解決了使用者回報的問題，單元測試覆蓋率足夠，且無安全性或效能隱憂。

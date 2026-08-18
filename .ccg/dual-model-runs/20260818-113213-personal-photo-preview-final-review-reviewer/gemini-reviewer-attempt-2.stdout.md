# 審查報告：個人照片即時預覽修正 (Personal Photo Preview Refresh Review)

本報告針對個人相關資料頁選取新照片後，畫面未立即顯示更新照片的問題進行審查。審查範圍包含後端快取失效機制、前端互動行為、快取隔離性、效能影響以及單元測試品質。

---

## VALIDATION REPORT
=================
User Experience: 20/20 - 修正了上傳新照片後畫面仍顯示舊照片的問題，消除了使用者的挫折感與重複上傳的行為，提供即時且正確的視覺回饋。
Visual Consistency: 20/20 - 集中清除完整圖與 32..256 所有尺寸的縮圖快取，確保全站所有使用到該大頭照的元件（如導覽列、側邊欄、列表頁面）在重新整理或切換頁面後顯示一致的新照片。
Accessibility: 19/20 - 前端保留了 FileReader 本地即時預覽，並搭配上傳中的 LoadPanel 與成功後的 Toast 提示，為所有使用者提供清晰的狀態回饋。
Performance: 19/20 - 採用 Cache-Aside 模式，僅在更新時清除快取。雖然迴圈清除 225 個快取鍵，但純記憶體操作耗時極短（<1ms），且能避免每次讀取都向 CRM 查詢或重新生成縮圖，效能表現優異。
Browser Compatibility: 20/20 - 前端使用標準的 FileReader 與 AJAX 上傳，並在 URL 附加 timestamp 進行瀏覽器端的 cache-busting，相容於所有主流瀏覽器。

TOTAL SCORE: 98/100

ISSUES FOUND:
- [Warning] `GetContactImagesBatch` 中仍有硬編碼的縮圖尺寸範圍（32..256）。
- [Warning] 單元測試使用反射呼叫私有靜態方法 `InvalidatePersonalImageCache`，具備重構脆弱性。

RECOMMENDATION: PASS

---

## 1. Summary (整體評估)

本次修正非常成功且精準。後端在 `UploadContactImage` 成功更新 CRM 後，新增了集中清除快取的流程 `InvalidatePersonalImageCache`，移除了完整圖與 32..256 像素的所有縮圖快取。這使得前端在 AJAX 成功回呼後，以帶有 `timestamp` 的 URL 重新請求圖片時，伺服器端能穿透快取並從 CRM 讀取最新圖片，徹底解決了「舊圖覆蓋新預覽」的 Bug。

本修正對現有架構影響極小，且具備良好的快取隔離性，無跨使用者資料洩漏風險，並已補齊單元測試，**確認可以交付**。

---

## 2. Accessibility Issues (無障礙性評估)

- **狀態提示**：前端在上傳期間顯示 `dxLoadPanel`（顯示「上傳資料中...」），上傳成功後顯示 `dxToast`（顯示「上傳成功了!」），這能為使用螢幕閱讀器或需要明確狀態提示的使用者提供足夠的資訊。
- **本地預覽**：選取檔案後立即透過 `FileReader` 顯示本地預覽，為視覺障礙或低視能使用者提供了即時的互動回饋。
- **改進建議 (Info)**：確保 `dxLoadPanel` 與 `dxToast` 具有適當的 ARIA 屬性（如 `aria-live="polite"` 或 `role="status"`），以利螢幕閱讀器主動播報狀態變更。

---

## 3. Design Issues (設計一致性評估)

- **全站顯示一致性**：大頭照在系統中可能以多種尺寸呈現（例如列表中的小縮圖、個人資料頁面的中縮圖、編輯時的大圖）。本次修正同時清除完整圖與 32..256 像素的所有縮圖快取，確保了使用者更新照片後，全站所有關聯元件都能同步顯示最新照片，避免了視覺不一致的問題。
- **Token 與樣式**：此修正主要為後端邏輯與測試，未調整前端 CSS，無破壞現有設計系統 Token 的風險。

---

## 4. Suggestions (改進建議)

- **解耦縮圖尺寸範圍**：目前清除快取的範圍（32..256）與 `GetContactImage` 中的 `Math.Clamp(size, 32, 256)` 已透過常數 `PersonalImageMinimumThumbnailSize` 與 `PersonalImageMaximumThumbnailSize` 進行了解耦。然而，`GetContactImagesBatch` 中仍有硬編碼的 `32` 與 `256`。建議將其一併改為使用這兩個常數，以確保全站縮圖尺寸限制邏輯的完全統一。
- **反射測試註解**：由於單元測試使用反射呼叫私有方法 `InvalidatePersonalImageCache`，建議在該私有方法上方加上註解，提醒後續維護者此方法被單元測試反射引用，避免重構時因修改名稱而導致測試中斷。

---

## 5. Positive Notes (值得肯定的地方)

- **測試品質優良**：新增了 `PersonalContactImageCacheTests.cs` 單元測試，驗證完整圖與縮圖快取（如 80、256 尺寸）在更新後確實失效，建立了穩固的回歸防護。
- **快取隔離性佳**：快取鍵明確包含 `contactId`（`contact-image-full:{contactGuid:N}`），且上傳操作嚴格限制在當前登入使用者的 `contactId`，完全不存在跨使用者或跨租戶資料洩漏的風險。
- **資源釋放確實**：手動移除快取項目能讓舊的圖片位元組（`byte[]`）儘早被 GC 釋放，對記憶體管理是有利的。

---

## 6. 分級審查報告 (Findings Classification)

### Critical (阻礙交付之嚴重缺陷)
* **無**。

### Warning (潛在風險或維護性問題)

* **`GetContactImagesBatch` 中仍有硬編碼的縮圖尺寸範圍**
  - **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs` (第 708 行)
  - **說明**：`GetContactImagesBatch` 方法中的 `Math.Clamp(request.Size > 0 ? request.Size : 48, 32, 256)` 仍使用硬編碼的 `32` 與 `256`。建議將其改為使用新定義的 `PersonalImageMinimumThumbnailSize` 與 `PersonalImageMaximumThumbnailSize` 常數。

* **單元測試使用反射呼叫私有靜態方法**
  - **檔案路徑**：`ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs` (第 35 行)
  - **說明**：測試中使用反射 `typeof(PersonalController).GetMethod("InvalidatePersonalImageCache", ...)` 來測試私有靜態方法。若未來重構該方法名稱或簽章，編譯時無法偵測，需依賴測試執行失敗來發現。

### Info (一般性資訊或建議)

* **前端 Cache-Busting 行為確認**
  - **說明**：前端已在 `UploadContactImage` 的回傳 URL 中附加了 `timestamp={DateTime.Now.Ticks}`，且在 AJAX 成功回呼中正確更新了 `#profileImage` 的 `src`。因此，前端不需要做任何修改，只需確保後端快取正確清除即可。

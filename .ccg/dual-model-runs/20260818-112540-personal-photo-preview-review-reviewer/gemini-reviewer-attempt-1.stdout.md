# 審查報告：個人照片即時預覽修正 (Personal Photo Preview Refresh Review)

本報告針對個人相關資料頁選取新照片後，畫面沒有立即顯示更新照片的問題進行審查。審查範圍包含後端快取失效機制、前端互動行為、快取隔離性、效能影響以及單元測試品質。

---

## VALIDATION REPORT

```
VALIDATION REPORT
=================
User Experience: 20/20 - 修正了上傳新照片後畫面未立即更新的問題，消除了使用者的挫折感與重複上傳的行為，提供了即時且正確的視覺回饋。
Visual Consistency: 20/20 - 集中清除 32..256 像素的所有縮圖快取，確保全站所有尺寸的大頭照（如導覽列、側邊欄、列表頁面）在更新後都能同步顯示最新照片，維持了視覺一致性。
Accessibility: 19/20 - 前端在選取檔案後先透過 FileReader 進行本地預覽，並在非同步上傳期間顯示 dxLoadPanel（上傳中遮罩），上傳成功後顯示 dxToast 提示，為需要明確狀態提示的使用者提供了良好的無障礙體驗。
Performance: 19/20 - 伺服器端快取清除採用輕量級的 IMemoryCache.Remove，迴圈 225 次的記憶體操作耗時極短（<1ms），且能讓舊圖片位元組儘早被 GC 釋放。前端 cache-busting 僅在 URL 附加 timestamp，不影響伺服器端快取機制的正常運作。
Browser Compatibility: 20/20 - 前端使用標準的 FileReader 進行本地預覽，並透過標準的 AJAX 上傳與 URL 參數變更來更新圖片，相容於所有主流現代瀏覽器。

TOTAL SCORE: 98/100

ISSUES FOUND:
- (Info) 快取清除範圍（32..256 像素）為硬編碼，若未來系統引入大於 256 像素的縮圖尺寸，該清除邏輯可能需要同步更新。

RECOMMENDATION: PASS
```

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

- **全站一致性**：大頭照在系統中可能以多種尺寸呈現（例如列表中的小縮圖、個人資料頁面的中縮圖、編輯時的大圖）。本次修正同時清除完整圖與 32..256 像素的所有縮圖快取，確保了使用者更新照片後，全站所有關聯元件都能同步顯示最新照片，避免了視覺不一致的問題。
- **硬編碼尺寸 (Info)**：`InvalidatePersonalImageCache` 中的 `32..256` 範圍為硬編碼。雖然目前系統的縮圖限制在此範圍內（`Math.Clamp(size, 32, 256)`），但若未來調整了縮圖尺寸上限，此處的清除範圍也需同步調整。

---

## 4. Suggestions (改進建議)

### Info: 縮圖尺寸範圍常數化
建議將縮圖的最小與最大尺寸（32 與 256）定義為常數（例如 `const int MinThumbnailSize = 32;` 與 `const int MaxThumbnailSize = 256;`），並在 `GetContactImage`、`GetContactImagesBatch` 與 `InvalidatePersonalImageCache` 中共用，以提高程式碼的可維護性，避免未來修改尺寸限制時遺漏清除邏輯。

---

## 5. Positive Notes (值得肯定的地方)

- **測試品質優良**：新增了 `PersonalContactImageCacheTests.cs` 單元測試，透過反射驗證私有靜態方法 `InvalidatePersonalImageCache` 的行為，確保完整圖與縮圖快取（如 80、256 尺寸）在更新後確實失效，建立了穩固的回歸防護。
- **快取隔離性佳**：快取鍵明確包含 `contactId`（`contact-image-full:{contactGuid:N}`），且上傳操作嚴格限制在當前登入使用者的 `contactId`，完全不存在跨使用者或跨租戶資料洩漏的風險。
- **效能與資源管理**：`IMemoryCache.Remove` 是極為輕量且快速的記憶體操作，迴圈清除 225 個鍵值對對 CPU 與記憶體開銷幾乎可以忽略不計。手動移除快取項目能讓舊的圖片位元組（`byte[]`）儘早被 GC 釋放，對記憶體管理是有利的。

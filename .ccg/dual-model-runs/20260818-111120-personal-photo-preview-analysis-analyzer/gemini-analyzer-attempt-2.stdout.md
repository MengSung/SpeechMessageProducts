# 個人相關資料照片選取後未立即顯示新照片之設計與架構分析報告

本報告針對個人相關資料頁面選取新照片後，畫面未立即顯示更新照片的問題進行根因確認、架構評估，並提出最小修正建議與驗證方案。

---

## 1. UX Analysis (使用者體驗分析)

### Root Cause 成立性評估
**完全成立。** 
* **前端行為**：當使用者選取新照片時，前端 `FileReader` 會先將本地的 Base64 預覽圖賦值給 `#profileImage`，此時畫面會短暫顯示新照片。然而，當 AJAX 上傳成功後，前端會將 `#profileImage` 的 `src` 替換為伺服器回傳的 `response.imageUrl`（帶有 `timestamp` 參數）。
* **後端行為**：伺服器端的 `GetContactImage` 路由在處理請求時，會根據 `contactId` 與 `size` 從 `IMemoryCache` 讀取快取。由於 `UploadContactImage` 在更新 CRM 後**沒有清除伺服器端的快取**，且伺服器端快取鍵（`contact-image-full:{contactId}` 與 `contact-image-thumb:{contactId}:{size}`）並未將 `timestamp` 納入快取鍵的計算中，因此伺服器仍會回傳舊的快取影像。
* **結果**：前端原本已顯示的本地預覽圖被伺服器回傳的舊圖覆蓋，造成「照片未立即更新」的現象。

### 使用者旅程影響 (User Journey Implications)
使用者在點擊上傳後，預期會看到新照片。但上傳成功後，照片卻突然變回舊照片，這會讓使用者產生「上傳失敗」或「系統出錯」的挫折感，甚至導致重複上傳，增加伺服器負擔。

### 行動端與桌面端體驗 (Mobile vs Desktop Experience)
行動端網路延遲通常較高，本地預覽與伺服器回傳之間的時間差更明顯，這種「先變新、再變舊」的閃爍感會更加強烈，嚴重影響行動端體驗。

---

## 2. Design Evaluation (設計系統評估)

### 一致性與模式 (Consistency with Existing Patterns)
系統中大頭照的顯示採用了多種尺寸（例如列表中的小縮圖、個人資料頁面的中縮圖、編輯時的大圖）。為了保持一致性，當使用者更新個人照片時，所有相關元件（如導覽列、側邊欄、列表頁面）所使用的不同尺寸縮圖都必須同步更新。

### 快取清除範圍
必須同時清除完整圖（`contact-image-full`）以及所有可能生成的縮圖（`contact-image-thumb`，尺寸範圍 32 到 256 像素），以確保全站大頭照顯示的一致性。

---

## 3. Technical Considerations (技術與架構考量)

### 快取一致性 (Cache Consistency)
採用「Cache-Aside」模式，在 CRM 資料庫更新成功後，立即主動清除（Invalidate）相關快取。

### 競態條件 (Race Conditions)
由於清除快取是在 CRM 更新成功後執行，因此不會有「快取被清除後又被寫入舊資料」的髒讀（Dirty Read）問題。

### 跨使用者資料洩漏 (Cross-user Data Leakage)
快取鍵中明確包含 `contactId`（`contact-image-full:{contactGuid:N}`），且上傳操作嚴格限制在當前登入使用者的 `contactId`，因此完全不存在跨使用者資料洩漏的風險。

### 效能與資源生命週期 (Performance & Resource Lifecycle)
雖然縮圖尺寸範圍為 32 到 256，但 `IMemoryCache.Remove` 是極為輕量且快速的記憶體操作，迴圈清除 225 個鍵值對對 CPU 與記憶體開銷幾乎可以忽略不計。縮圖生成使用 `ImageSharp`，已正確使用 `using` 釋放資源，無記憶體洩漏風險。

---

## 4. Options (替代方案評估)

### 方案 A：集中清除完整圖與 32..256 像素縮圖快取（推薦）
* **作法**：在 `UploadContactImage` 成功後，呼叫 `InvalidatePersonalImageCache`，以迴圈清除 `32` 到 `256` 之間的所有縮圖快取及完整圖快取。
* **優點**：完全符合單元測試要求，能徹底清除所有潛在的舊縮圖，保證全站大頭照一致性。
* **缺點**：需要執行 225 次 `Remove` 操作（但記憶體操作極快，實際無效能瓶頸）。

### 方案 B：將 Timestamp 納入伺服器端快取鍵
* **作法**：修改 `GetContactImage`，將 `timestamp` 作為參數傳入，並納入快取鍵（例如 `contact-image-thumb:{contactId}:{size}:{timestamp}`）。
* **優點**：不需要主動清除快取，每次上傳新圖時，因為 `timestamp` 改變，會自動產生新的快取鍵。
* **缺點**：會導致舊的快取影像殘留在記憶體中直到過期，造成記憶體浪費；且無法清除其他沒有帶 `timestamp` 的舊快取，容易導致全站其他地方顯示不一致。

### 方案 C：僅清除特定常用尺寸（如 80, 256）
* **作法**：只清除特定幾個預設尺寸的快取。
* **優點**：`Remove` 呼叫次數極少。
* **缺點**：若其他頁面使用了非標準尺寸（例如 48 或 120），該頁面仍會顯示舊圖，無法保證一致性，且無法通過現有的單元測試。

---

## 5. Recommendation (建議方案與實作細節)

### 首選方案
**方案 A**。

### 理由
1. **一致性保證**：能確保全站所有尺寸的縮圖在更新後皆為最新版本。
2. **測試相容性**：完全符合並能通過已新增的失敗測試 `PersonalContactImageCacheTests.cs`。
3. **資源安全**：記憶體快取清除開銷極低，且能避免方案 B 的記憶體殘留問題。

### 必要修改檔案與最小實作建議

#### 1. 修改檔案
* `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`

#### 2. 最小實作建議
在 `PersonalController` 類別中新增私有靜態方法 `InvalidatePersonalImageCache`，並在 `UploadContactImage` 更新 CRM 成功後呼叫它。

##### 新增方法：
```csharp
private static void InvalidatePersonalImageCache(IMemoryCache cache, Guid contactId)
{
    if (cache == null) return;

    // 清除完整圖快取
    cache.Remove($"contact-image-full:{contactId:N}");

    // 清除所有支援的縮圖尺寸快取 (32 到 256 像素)
    for (int size = 32; size <= 256; size++)
    {
        cache.Remove($"contact-image-thumb:{contactId:N}:{size}");
    }
}
```

##### 在 `UploadContactImage` 中呼叫：
```csharp
// ========================================
// 步驟 4: 更新 CRM Contact 的 EntityImage
// ========================================
var service = OrganizationService;

var contactToUpdate = new Entity("contact", contactId);
contactToUpdate["entityimage"] = imageBytes;

System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 開始更新 CRM Contact...");
service.Update(contactToUpdate);
System.Diagnostics.Debug.WriteLine($"[UploadContactImage] CRM Contact 更新成功");

// 清除伺服器端快取
var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
if (memoryCache != null)
{
    InvalidatePersonalImageCache(memoryCache, contactId);
}
```

---

## 6. Findings Classification (發現分類)

* **Critical**：無。
* **Warning**：無。
* **Info**：
  * **前端 Cache-Busting 行為確認**：前端已在 `UploadContactImage` 的回傳 URL 中附加了 `timestamp={DateTime.Now.Ticks}`，且在 AJAX 成功回呼中正確更新了 `#profileImage` 的 `src`。因此，前端不需要做任何修改，只需確保後端快取正確清除即可。

---

## 7. Verification Suggestions (驗證建議)

1. **單元測試驗證**：
   * 執行 `ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs` 中的 `InvalidatePersonalImageCache_removes_full_image_and_all_thumbnail_sizes` 測試，確保測試通過。這能驗證反射呼叫與快取清除邏輯的正確性。
2. **手動整合測試**：
   * 登入系統，進入個人資料頁面。
   * 開啟瀏覽器開發者工具（F12），切換至 Network 頁籤。
   * 上傳一張新照片，確認上傳成功後，畫面上的大頭照立即更新為新照片，且沒有發生「先變新、再變舊」的閃爍現象。
   * 檢查 `GetContactImage` 請求的 Response Headers，確認其回傳的是新照片的內容。

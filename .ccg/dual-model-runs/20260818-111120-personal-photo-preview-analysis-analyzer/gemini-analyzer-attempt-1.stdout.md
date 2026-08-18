# 設計分析報告：個人相關資料照片選取後未立即顯示新照片之修正方案

本報告針對「個人相關資料頁面選取並上傳新照片後，畫面未立即顯示更新照片」之問題進行 Root Cause 審查、使用者體驗（UX）影響評估、設計系統一致性檢視、前端架構影響分析，並提出最小實作建議與驗證方案。

---

## 1. Root Cause 成立性審查

**Root Cause 確實成立。**

### 根因分析：
1. **伺服器端快取未失效**：
   - 當使用者透過前端上傳新照片時，後端 `UploadContactImage` 會成功更新 CRM 中的 `entityimage` 欄位，並更新 Session 中的 `m_LoginContact`。
   - 然而，系統在讀取圖片時（`GetContactImage` 與 `GetContactImagesBatch`），會使用 `IMemoryCache` 進行快取，其快取鍵分別為：
     - 完整圖：`contact-image-full:{contactGuid:N}`
     - 縮圖：`contact-image-thumb:{contactGuid:N}:{thumbSize}`（其中 `thumbSize` 介於 32 至 256 像素之間）。
   - `UploadContactImage` 在更新 CRM 後，**並未清除或使這些快取失效**。
2. **前端 Cache-Busting 無法繞過伺服器快取**：
   - 雖然前端在請求圖片時，URL 帶有 `timestamp` 參數（例如 `timestamp=DateTime.Now.Ticks`），但這只能防止**瀏覽器快取（Browser Cache）**。
   - 伺服器端的 `GetContactImage` 在接收到請求時，其快取鍵僅依賴於 `contactGuid` 與 `size`，並不包含 `timestamp`。因此，伺服器依然會從 `IMemoryCache` 中讀取並回傳舊的圖片位元組。

---

## 2. UX Analysis (使用者體驗分析)

- **使用者影響評估**：
  - **目前問題**：使用者上傳新照片後，畫面依然顯示舊照片。這會讓使用者產生「上傳失敗」的錯覺，進而重複點擊上傳，造成挫折感，並對系統的穩定性產生懷疑。
  - **修正後預期**：上傳成功後，畫面立即更新為新照片，提供即時且正確的視覺回饋，符合使用者的直覺預期。
- **使用者旅程影響**：
  - 完整的旅程為：`點擊大頭照` -> `選擇檔案` -> `顯示上傳中遮罩（LoadPanel）` -> `上傳成功提示（Toast）` -> `大頭照立即更新`。修正後能確保此旅程順暢閉環。
- **無障礙性考量（Accessibility）**：
  - 前端在選取檔案後，會先透過 `FileReader` 進行本地預覽，這能提供即時的視覺反饋。
  - 上傳期間的 `dxLoadPanel`（顯示「上傳資料中...」）與上傳成功後的 `dxToast`（顯示「上傳成功了!」）能為使用螢幕閱讀器或需要明確狀態提示的使用者提供足夠的資訊，應予以保留。
- **行動端與桌面端體驗**：
  - 兩端皆使用相同的 AJAX 上傳與圖片更新邏輯。修正後，行動端與桌面端皆能獲得一致的即時更新體驗。

---

## 3. Design System Evaluation (設計系統評估)

- **一致性與模式**：
  - 前端採用「本地即時預覽（FileReader） + 伺服器確認後更新（AJAX success callback）」的模式，這是前端設計系統中非常推薦的**漸進式增強（Progressive Enhancement）**策略。
  - 使用 DevExtreme 的 `dxForm`、`dxToast` 和 `dxLoadPanel`，與系統中其他頁面的互動模式保持高度一致。
- **組件重用性**：
  - 目前的大頭照上傳區域（`.profile-image-container`）與 `uploadImage` 腳本直接寫在 `PersonalInfomationViewWithImage.cshtml` 中。
  - *建議*：未來若有其他頁面（如會員管理、聯絡人編輯）需要相同功能，可將此區域與腳本封裝成 Razor Partial View 或 View Component，以提高重用性。
- **Token 與主題使用**：
  - 樣式中存在硬編碼的顏色（如綠色 `#4CAF50`、灰色 `#f3f4f6`）。建議未來將這些顏色提取為 CSS 變數或設計系統的 Theme Tokens，以利後續支援深色模式或主題切換。

---

## 4. Technical Considerations (技術考量)

- **組件結構影響**：
  - 最小修正僅需在後端新增快取清除邏輯，對現有的前端 HTML/CSS/JS 結構沒有任何破壞性影響。
- **狀態管理**：
  - 前端狀態：上傳成功後，藉由修改 `<img>` 的 `src` 屬性來更新狀態。
  - 後端狀態：更新 CRM 實體，並更新 Session 中的 `m_LoginContact`。
  - 快取狀態：清除 `IMemoryCache` 中的舊快取。
  - 這三者在修正後能達到最終一致性。
- **效能與記憶體影響**：
  - 清除快取時，迴圈 225 次（32..256 像素）呼叫 `cache.Remove` 是在記憶體中進行雜湊表移除，耗時通常小於 1 毫秒，對伺服器效能影響微乎其微。
  - 手動移除快取項目能讓舊的圖片位元組（`byte[]`）儘早被垃圾回收（GC）釋放，對記憶體管理是有利的。
- **測試充分性**：
  - 已有單元測試 `PersonalContactImageCacheTests.cs`，該測試透過反射驗證 `InvalidatePersonalImageCache` 是否能正確清除完整圖與縮圖快取。
  - 實作時必須確保方法名稱、修飾詞（`private static`）與參數簽章與測試預期完全一致，以確保測試能順利通過。

---

## 5. Options (替代方案評估)

| 方案 | 實作方式 | 優點 | 缺點 |
| :--- | :--- | :--- | :--- |
| **方案 A：集中清除 32..256 像素縮圖快取（推薦）** | 在後端新增 `InvalidatePersonalImageCache` 方法，迴圈清除 `contact-image-thumb:{contactId}:{32..256}` 以及 `contact-image-full:{contactId}`。 | 1. 實作極其簡單且安全。<br>2. 不影響其他讀取邏輯。<br>3. **完美符合現有單元測試的設計**。 | 需要迴圈 225 次，但效能損耗可忽略不計。 |
| **方案 B：使用快取相依性（Cache Dependency）** | 在寫入快取時，為每個 Contact 建立一個 `CancellationTokenSource` 並關聯到快取項目。更新時，觸發該 Token 使所有相關快取失效。 | 不需要迴圈清除，能一次性使所有相關快取失效。 | 需要修改 `GetContactImage` 和 `GetContactImagesBatch` 的快取寫入邏輯，改動範圍較大，且無法直接通過現有的單元測試。 |
| **方案 C：僅在前端使用 Cache-Busting** | 前端在請求圖片時，永遠帶上隨機的 `timestamp`，且後端 `GetContactImage` 不使用快取（或快取鍵加入 `timestamp`）。 | 後端不需要清除快取邏輯。 | 會使伺服器端快取完全失效，每次請求都會重新查詢 CRM 或重新生成縮圖，嚴重影響伺服器效能與 CRM API 配額。 |

---

## 6. Recommendation & Implementation (建議與最小實作)

### 建議方案：首選方案 A
- **理由**：方案 A 是最小且最安全的修正，能以最少的程式碼改動解決快取不一致的問題，同時完全符合現有單元測試的設計，且對效能沒有實質影響。

### 最小實作建議：

#### 1. 修改檔案：`SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`

**步驟一：新增 `InvalidatePersonalImageCache` 方法**
在 `PersonalController` 類別中新增以下 `private static` 方法（確保與單元測試的反射呼叫一致）：

```csharp
/// <summary>
/// 清除指定 Contact 的完整圖與所有支援縮圖尺寸的快取
/// </summary>
private static void InvalidatePersonalImageCache(IMemoryCache cache, Guid contactId)
{
    if (cache == null) return;

    // 1. 清除完整圖快取
    cache.Remove($"contact-image-full:{contactId:N}");

    // 2. 清除 32 到 256 像素的所有縮圖快取
    for (int size = 32; size <= 256; size++)
    {
        cache.Remove($"contact-image-thumb:{contactId:N}:{size}");
    }
}
```

**步驟二：在 `UploadContactImage` 中呼叫清除快取**
在 `UploadContactImage` 方法中，於 CRM 更新成功後（`service.Update(contactToUpdate);` 之後），取得 `IMemoryCache` 並呼叫清除方法：

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

// --- 新增：清除伺服器端快取 ---
var memoryCache = HttpContext?.RequestServices?.GetService(typeof(IMemoryCache)) as IMemoryCache;
if (memoryCache != null)
{
    InvalidatePersonalImageCache(memoryCache, contactId);
    System.Diagnostics.Debug.WriteLine($"[UploadContactImage] 已清除 Contact {contactId} 的圖片快取");
}
// ----------------------------
```

#### 2. 前端成功回呼的 cache-busting 行為評估：
- 前端 `PersonalInfomationViewWithImage.cshtml` 中的 `uploadImage` 函數在成功時會執行：
  `$('#profileImage').attr('src', response.imageUrl);`
- 後端回傳的 `response.imageUrl` 已經包含了 `timestamp={DateTime.Now.Ticks}`。
- **結論**：前端已具備足夠的 cache-busting 行為，**無需進行任何修改**。只要後端正確清除伺服器端快取，前端即可立即顯示新照片。

---

## 7. 驗證建議

1. **單元測試驗證**：
   - 執行 `ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs` 中的 `InvalidatePersonalImageCache_removes_full_image_and_all_thumbnail_sizes` 測試，確保測試通過。這能驗證反射呼叫與快取清除邏輯的正確性。
2. **手動整合測試**：
   - 登入系統，進入個人相關資料頁面。
   - 點擊大頭照並選擇一張新圖片進行上傳。
   - 觀察上傳成功後，大頭照是否立即更新為新圖片。
   - 重新整理頁面，確認大頭照依然是新圖片（未因快取而回退為舊圖）。
3. **快取隔離測試**：
   - 使用帳號 A 上傳新照片，確認帳號 B 的大頭照快取未受影響，且帳號 A 的大頭照在帳號 B 的畫面上（若有顯示）也能在快取過期或重新整理後正確更新。

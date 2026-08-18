# 審查報告：個人照片即時預覽修正

本報告針對「個人照片即時預覽修正」任務進行程式碼審查，評估其正確性、安全性、效能及可維護性。

---

## 1. Summary (整體評估)
本次修正成功解決了「個人相關資料頁選取新照片後，畫面沒有立即顯示更新照片」的快取殘留問題。
- **修正邏輯正確**：在 `UploadContactImage` 成功更新 CRM 後，主動取得 `IMemoryCache` 並呼叫集中清除方法 `InvalidatePersonalImageCache`，徹底移除該 Contact 的完整圖與 32..256 像素的所有縮圖快取。
- **前端配合無誤**：前端在 AJAX 成功回呼後，會取得帶有 `timestamp` 的新 URL 並更新 `src`，配合後端快取已清除，能確保下一次請求直接穿透快取並從 CRM 讀取最新圖片。
- **已補強單元測試**：新增了 `PersonalContactImageCacheTests` 驗證快取失效邏輯，建立了基本的防護網。

整體評估為 **PASS (可交付)**，但建議針對下方列出的 Warning 進行微調以提升程式碼品質。

---

## 2. Accessibility Issues (無障礙空間評估)
- 本次修正主要為後端快取清除邏輯與 API 回傳值調整，不涉及前端 HTML 結構、ARIA 屬性或鍵盤導覽等 UI 變更，因此**無新增的 Accessibility 問題**。

---

## 3. Design Issues (設計一致性問題)
- **常數未完全共用**：在 `PersonalController.ImageUpload.cs` 中定義了 `PersonalImageMinimumThumbnailSize` (32) 與 `PersonalImageMaximumThumbnailSize` (256) 常數，但同檔案的 `GetContactImagesBatch` 端點仍使用硬編碼的 `32` 與 `256`，存在設計不一致性。

---

## 4. Suggestions (改進建議)
1. **共用縮圖尺寸常數**：將 `GetContactImagesBatch` 中的 `Math.Clamp(..., 32, 256)` 改為使用新定義的常數，避免未來調整尺寸限制時遺漏。
2. **提升測試的型別安全**：將 `InvalidatePersonalImageCache` 的存取修飾詞改為 `internal`，並在專案中設定 `[assembly: InternalsVisibleTo]`，取代反射（Reflection）呼叫，使單元測試具備編譯期型別檢查。
3. **修復測試檔案編碼**：修復 `PersonalContactImageCacheTests.cs` 的亂碼註解，確保團隊成員閱讀無礙。

---

## 5. Positive Notes (優秀設計)
- **快取隔離性佳**：快取鍵（Cache Key）皆綁定 `contactId` (Guid)，確保不同使用者與租戶之間的圖片快取完全隔離，無資料洩漏風險。
- **資源管理妥當**：快取清除方法 `InvalidatePersonalImageCache` 為靜態方法，且不持有任何 HTTP Request 或 Session 資源，生命週期管理正確，無記憶體洩漏疑慮。
- **常數化重構**：將縮圖範圍限制重構為常數，提升了程式碼的可讀性。

---

## 6. Scoring Format (Bugfix 驗證評分)

```
VALIDATION REPORT
=================
User Experience: 19/20 - 修正後上傳照片能立即更新預覽，體驗良好。扣 1 分係因若網路延遲，切換伺服器 URL 時可能會有極短暫的閃爍。
Visual Consistency: 20/20 - 縮圖尺寸限制與原本一致，無視覺不一致問題。
Accessibility: 20/20 - 本次修正不涉及前端 UI 元素，無 a11y 影響。
Performance: 19/20 - 迴圈清除 225 個快取鍵在記憶體中執行極快，但若未來快取鍵變多，建議評估快取相依性（Cache Dependency）設計。
Browser Compatibility: 20/20 - 後端快取清除與瀏覽器無關，相容性無虞。

TOTAL SCORE: 98/100

ISSUES FOUND:
- [Warning] GetContactImagesBatch 中仍有硬編碼的 32 與 256 縮圖尺寸限制，未共用新定義的常數。
- [Warning] 單元測試使用反射呼叫私有靜態方法 InvalidatePersonalImageCache，若未來重構該方法名稱或簽章，編譯時無法偵測。
- [Info] 測試檔案 PersonalContactImageCacheTests.cs 的中文註解存在亂碼。

RECOMMENDATION: PASS
```

---

## 7. Detailed Findings (分級審查報告)

### Critical
*無*

### Warning

#### 1. 常數未完全共用
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/PersonalController.ImageUpload.cs`
* **程式碼位置**：第 708 行（`GetContactImagesBatch` 方法內）
* **問題描述**：
  雖然在類別開頭定義了 `PersonalImageMinimumThumbnailSize` (32) 與 `PersonalImageMaximumThumbnailSize` (256) 常數，並已應用於 `InvalidatePersonalImageCache` 與 `GetContactImage`，但 `GetContactImagesBatch` 內仍使用硬編碼：
  ```csharp
  var thumbSize = Math.Clamp(request.Size > 0 ? request.Size : 48, 32, 256);
  ```
* **後果**：若未來調整縮圖尺寸限制（例如改為 64 至 512），只修改常數會導致批次讀取端點的限制與快取清除範圍不一致，造成快取無法正確清除的 Bug 再次發生。
* **建議修正**：
  將該行修改為：
  ```csharp
  var thumbSize = Math.Clamp(request.Size > 0 ? request.Size : 48, PersonalImageMinimumThumbnailSize, PersonalImageMaximumThumbnailSize);
  ```

#### 2. 單元測試反射呼叫私有方法
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs`
* **程式碼位置**：第 35-37 行
* **問題描述**：
  測試中透過反射獲取私有方法：
  ```csharp
  var invalidator = typeof(PersonalController).GetMethod(
      "InvalidatePersonalImageCache",
      BindingFlags.Static | BindingFlags.NonPublic);
  ```
* **後果**：若未來重構 `PersonalController` 時修改了 `InvalidatePersonalImageCache` 的名稱或參數簽章，編譯器無法在編譯期發現此處的依賴中斷，必須依賴執行測試失敗才能發現。
* **建議修正**：
  1. 在 `PersonalController` 的 `InvalidatePersonalImageCache` 方法上方加上註解，提醒維護人員該方法被單元測試反射引用。
  2. （更佳做法）將該方法改為 `internal static void InvalidatePersonalImageCache`，並在 `SpeechMessageProducts.ChurchReport` 專案的 `AssemblyInfo.cs` 中加入：
     ```csharp
     [assembly: InternalsVisibleTo("ChurchReport.MemberInfo.Tests")]
     ```
     如此一來，測試專案可以直接呼叫該方法，享有編譯期型別安全檢查。

### Info

#### 1. 測試檔案註解亂碼
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/Personal/PersonalContactImageCacheTests.cs`
* **問題描述**：
  該檔案的開頭與部分註解（例如第 2-7 行、第 19-22 行、第 39 行）在讀取時呈現亂碼（例如 `AI-蝜?...`），可能是因為檔案儲存時的編碼格式（如 UTF-8 with/without BOM 或 Big5）與編輯器解析不一致。
* **建議修正**：
  重新將該檔案儲存為標準 **UTF-8 (without BOM)** 編碼，並修復亂碼的中文註解，以提升程式碼可讀性與維護性。

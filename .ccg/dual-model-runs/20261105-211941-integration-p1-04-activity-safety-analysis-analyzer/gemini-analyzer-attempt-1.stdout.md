# Gemini 設計分析報告：Activity 整合與安全性分析

本報告針對 `ActivityService.cs` 的修正方案進行架構、UX/行為影響、前端/服務端相容性及安全性評估。

---

## 1. 方案摘要與驗證評估

根據提示所提出的方案重點：
1. **API / 命名空間 / 建構子 / 合法 append**：維持既有公開簽章與相依注入介面。
2. **ArrayList 集合安全與型別限制**：只接受獨占普通 `ArrayList`（拒絕 alias、subclass、fixed、read-only），預配置 2 個 Capacity 後透過兩個 `object[] AddRange` 填入。
3. **部分發布與執行緒安全**：無 I/O 前進行部分發布驗證，但不聲稱兩物件跨執行緒具備原子性。
4. **查詢與分頁控制**：
   - 來源容器與輸出列最多 5000 筆，每批 500 IDs。
   - 保留舊單頁無排序 golden 行為。
   - 針對 `MoreRecords`，先驗證 probe 不能為 empty 且不能全數仍為 `More`；隨後丟棄 probe，重啟正式主鍵升冪有界分頁。
   - `PageNumber` / cookie 正確前進，每頁必須有唯一且 requested 的實體；任何缺漏、重複、錯表或無進度狀況一律拒絕。
5. **EntityQueryService 與狀態管理**：
   - 使用 `EntityQueryService` 驗證正式 ID 與根表。
   - 不保存 logger，SDK 採 scope 借入，`IDisposable` 僅清除而不 `Dispose` SDK，同步呼叫由 host 負責 drain。
   - `SetState` 保留 `1/4` 與 `3/5` 組合，拒絕空 ID / null 或非 `SetStateResponse` 回應，固定英文 wrapper 並保留原 `InnerException`，交由上層共用 notifier 處理。

---

## 2. 審查發現分類 (Code Review Findings)

### **【Info】Info 級別：記憶體與效能考量**
- **說明**：使用 `ArrayList` 與 `object[] AddRange` 是為了相容舊版呼叫端的合約（合約指定接收 `ArrayList`）。雖然在強型別時代 `ArrayList` 會產生 boxing/unboxing 額外開銷，但在此工具層的資料量（上限 5000 筆、每批 500 IDs）下，效能仍在可接受範圍內。

### **【Warning】Warning 級別：跨執行緒與並行呼叫約束**
- **說明**：方案中明訂「不聲稱兩物件跨執行緒原子性」。若有多執行緒同時寫入或共用同一個 `ArrayList` 或 `ActivityService` 實例，可能導致競態條件（Race Condition）。
- **建議**：呼叫端必須保證此服務與傳入的 `ArrayList` 處於單執行緒作業範疇，或由呼叫端自行加鎖保護。

---

## 3. 安全性與相容性分析

1. **型別與防禦性檢查 (Defensive Validation)**：
   - 嚴格拒絕非普通 `ArrayList`（如唯讀、固定大小或衍生子類別），能有效防止惡意物件竄改內部陣列或造成列舉異常。
   - 身份驗證涵蓋 `(logicalname, GUID)` 與 prefix+new 集合比對，防止跨表同 GUID 的資料混淆與注入漏洞。
2. **分頁與迴圈防護 (Pagination & Infinite Loop Guard)**：
   - 對於 `MoreRecords` 的 probe 驗證與有界分頁（Max 5000 rows, 500 per batch），能有效防止 CRM 查詢因分頁標記異常而陷入無限迴圈或記憶體爆量（Denial of Service）。
3. **例外包裝與資訊安全 (Exception Handling)**：
   - 固定英文 wrapper 搭配原始 `InnerException`，既能提供足夠的診斷資訊，又不會洩漏底層過多的敏感系統架構路徑。

---

## 4. 總結與建議

- **偏好方案**：依據上述各項防禦性驗證機制，此修正方案在**不破壞現有 API 契約**的前提下，大幅提升了對惡意/異常資料結構的防禦能力（防止錯頁、重覆、缺列、跨表 GUID 碰撞）。
- **結論**：方案設計嚴謹，未發現未覆蓋的嚴重安全漏洞，建議可直接照此方案進行內聯（inline）實作與測試。

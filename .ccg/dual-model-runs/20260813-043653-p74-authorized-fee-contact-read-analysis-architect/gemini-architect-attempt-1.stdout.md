```diff
--- /dev/null
+++ b/docs/analyses/p74-authorized-fee-contact-read-analysis.md
@@ -0,0 +1,150 @@
+# P7.4 Authorized Fee Contact Read 架構與安全性分析報告
+
+## 1. Analysis (現有架構評估)
+
+### 1.1 現有端點漏洞與 IDOR 風險
+- **漏洞描述**：現有的 `DedicationAuditController.GetFeesByContactId(string id)` 是一個 AJAX GET 端點，直接接受瀏覽器傳入的 `id` (contact GUID)，並委託給 `DonationPaymentManager.GetDedicationFeesByContactIdAsync`。
+- **安全缺陷**：該端點完全沒有對當前登入用戶進行任何身份驗證或角色授權檢查。任何登入或未登入的用戶只要通過瀏覽器或 API 工具傳入任意聯絡人的 GUID，即可獲取該聯絡人的敏感奉獻費用數據。這是一個典型的 **IDOR (不安全的直接對象引用)** 漏洞。
+- **身份權威性錯誤**：現有設計中，瀏覽器傳入的 `id` 被直接用作查詢目標，而沒有在伺服器端校驗當前操作者是否具備審計該數據的權限。
+
+### 1.2 Session 狀態污染與競態條件
+- **狀態修改缺陷**：Legacy 查詢路徑中，`DonationDedicationFeeFormService.GetFeesByContactIdAsync` 會調用 `FillFromContactAsync`，這會直接修改 Session 擁有的 `DonationPaymentFormModel` (即 `m_DonationPaymentFormModel`)。
+- **併發衝突**：在多標籤頁操作或併發 AJAX 請求下，Session 共享的 Model 狀態會被互相覆蓋，導致 `TotalAmount` 或 `DedicationFeeList` 數據錯亂，產生嚴重的數據不一致性。
+
+### 1.3 過度獲取與 Legacy 依賴
+- **I/O 開銷**：Legacy 路徑在查詢費用前，必須先通過 `ToolUtility.RetrieveEntity("contact", id)` 從 CRM 獲取完整的聯絡人實體並重新水合，這增加了不必要的 CRM SDK I/O 負擔。
+
+---
+
+## 2. Architecture Decision (架構決策)
+
+### 2.1 決策 1：引入會計角色授權邊界 (Accounting-Role Authorization Boundary)
+- **決策內容**：在 `GetFeesByContactId` 執行任何查詢前，必須先調用 `EnsureCorrectUserData()` 重新水合 Session，然後從伺服器解析的當前登入聯絡人 (`InMemoryContext.PersonalInfomationModel.m_LoginContact`) 獲取其 `new_church_jobtitle`，並通過 `DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle)` 進行校驗。
+- **決策理由**：奉獻稽核屬於敏感財務數據，僅應開放給具備會計權限的人員。
+- **拒絕的替代方案**：僅檢查用戶是否登入。*拒絕原因*：普通登入用戶不應有權限查看其他聯絡人的奉獻費用。
+- **假設**：`m_LoginContact` 在 Session 正常時應能正確解析。
+- **潛在副作用**：若 Session 逾期或 `m_LoginContact` 未及時加載，會計人員可能會遇到拒絕訪問，需引導重新登入。
+
+### 2.2 決策 2：Fail-Closed 安全策略
+- **決策內容**：若當前登入聯絡人資訊無法取得 (例如 `m_LoginContact` 為 null)、Session 無效、或 `new_church_jobtitle` 不符合會計角色，系統必須立即中斷並返回 `{ status: "0", message: "Unauthorized" }`，絕不執行後續查詢。
+- **決策理由**：確保在任何異常或未授權情況下，系統默認拒絕訪問，防止安全降級。
+
+### 2.3 決策 3：True-Gate 隔離與 Request-Local 數據流
+- **決策內容**：當 `Package01FeeReadsEnabled` 為 true 時，直接解析 GUID 並調用 `IPackage01FeeReadClient`。返回的數據直接映射為 AJAX rows，**絕不**修改 Session 擁有的 `DonationPaymentFormModel`，實現無狀態 (Stateless) 的 Request-Local 數據流。
+- **決策理由**：消除 Session 狀態污染，提升併發安全性。
+- **拒絕的替代方案**：在 typed client 失敗時回退到 legacy `ToolUtility`。*拒絕原因*：這會破壞 A/B 隔離性，並在 typed client 故障時引入安全降級與 legacy 依賴。
+
+---
+
+## 3. Implementation Plan (實施計劃與虛擬代碼)
+
+### 3.1 Controller 授權與路由設計 (虛擬代碼)
+```csharp
+[HttpGet]
+public async Task<IActionResult> GetFeesByContactId(string id)
+{
+    try
+    {
+        // 1. 重新水合 Session 資料
+        EnsureCorrectUserData();
+
+        // 2. 獲取伺服器解析的當前登入聯絡人 (Fail-Closed)
+        var loginContact = InMemoryContext.PersonalInfomationModel?.m_LoginContact;
+        if (loginContact == null)
+        {
+            return Json(new { status = "0", message = "Unauthorized: Session or login contact missing" });
+        }
+
+        // 3. 校驗會計角色 (Fail-Closed)
+        var jobTitle = ToolUtility.GetEntityStringAttribute(ref loginContact, "new_church_jobtitle") ?? string.Empty;
+        if (!DonationNavigationAccessResolver.CanAccessDonationManagement(jobTitle))
+        {
+            return Json(new { status = "0", message = "Unauthorized: Insufficient permissions" });
+        }
+
+        // 4. 解析瀏覽器傳入的 GUID 定位器
+        if (string.IsNullOrEmpty(id) || !Guid.TryParse(id, out Guid targetContactId))
+        {
+            return Json(new { status = "0", message = "Invalid or missing contact ID" });
+        }
+
+        // 5. 根據 Feature Flag 路由
+        if (Package01FeeReadsEnabled)
+        {
+            // True-Gate: Typed Client 查詢 (Request-Local, 無狀態)
+            var profileAlias = DynamicsAccess?.ProfileAlias;
+            if (string.IsNullOrWhiteSpace(profileAlias))
+            {
+                throw new InvalidOperationException("DynamicsAccess:ProfileAlias is required.");
+            }
+
+            // 直接調用 typed client，不檢索目標 CRM Entity，不回退到 ToolUtility
+            var rows = await Package01FeeReadClient.RetrieveDedicationFeesByContactDateRangeAsync(
+                profileAlias,
+                "church-report-service",
+                targetContactId,
+                DateTime.MinValue, // 或從請求參數獲取
+                DateTime.MaxValue,
+                null,
+                HttpContext.RequestAborted);
+
+            var mappedFees = rows.Select(MapFeeDto).ToList();
+            long totalAmount = mappedFees.Sum(f => (long)f.Amount);
+
+            if (totalAmount > int.MaxValue || totalAmount < int.MinValue)
+            {
+                throw new OverflowException("Total amount exceeds supported range.");
+            }
+
+            var ajaxRows = DonationFeeQueryService.ToAjaxRows(mappedFees);
+            return Json(new { status = "1", DedicationFeeList = ajaxRows, TotalAmount = (int)totalAmount });
+        }
+        else
+        {
+            // False-Gate: Legacy 查詢 (保留舊行為，但已通過授權邊界)
+            var feeList = await InMemoryContext.DonationPaymentManager.GetDedicationFeesByContactIdAsync(
+                id,
+                HttpContext.RequestAborted);
+
+            return Json(new { 
+                status = "1", 
+                DedicationFeeList = feeList, 
+                TotalAmount = InMemoryContext.DonationPaymentManager.m_DonationPaymentFormModel.TotalAmount 
+            });
+        }
+    }
+    catch (Exception e)
+    {
+        return Json(new { status = "0", message = e.Message });
+    }
+}
+```
+
+---
+
+## 4. Considerations (考量事項)
+
+### 4.1 安全性 (Security)
+- **徹底消除 IDOR**：伺服器端權限校驗為唯一權威來源，瀏覽器傳入的 ID 僅作為查詢定位器，不作為身份或角色來源。
+- **無狀態設計**：True-Gate 避免了對 Session Model 的修改，防止了併發請求下的狀態交叉污染。
+
+### 4.2 效能與資源釋放 (Performance & Resource Release)
+- **減少 CRM I/O**：True-Gate 避免了對 CRM `contact` 實體的額外查詢，減少了一次 CRM SDK I/O，顯著提升響應速度。
+- **確定性釋放**：所有非同步操作均傳遞 `CancellationToken`，且任何鎖或租約必須在 `finally` 塊中釋放，避免死鎖。
+
+### 4.3 測試建議 (Test Recommendations)
+- **False-Gate 兼容性測試**：驗證當 flag 為 false 時，系統是否正確執行 legacy 查詢路徑，且該路徑必須在會計角色授權檢查通過後才能執行。
+- **True-Gate 無目標 Entity / 無 Legacy 回退測試**：驗證當 flag 為 true 時，系統是否直接調用 `IPackage01FeeReadClient`，且**絕不**調用 `ToolUtility.RetrieveEntity`。模擬 typed client 拋出異常，驗證系統是否直接向客戶端返回錯誤，而**絕不**回退到 legacy 查詢。
+- **A/B 隔離性測試**：確保 true-gate 和 false-gate 的代碼路徑完全隔離，不會有交叉調用。
+- **取消 (Cancellation) 測試**：傳入已取消的 `CancellationToken`，驗證所有獲取的鎖或租約是否都在 `finally` 塊中被確定性釋放。
+- **原子結果 / 無 Model 修改測試**：驗證 typed client 分支返回的數據是全新的 request-local 對象，且 Session 中的 `DonationPaymentFormModel` 在請求前後保持不變。
+
+### 4.4 排除在外的範疇 (Out of Scope)
+- 任何 profile/endpoint/credential 的動態選擇邏輯。
+- 任何 CE (Common Engine) 寫入或變更操作。
+- 任何 feature flag 的啟用或切換。
+- 對 `ToolUtility` 進行大範圍的重構。
```

# P7 QR 出勤指令准入分析報告 (P7 QR Attendance Command Admission Analysis)

根據對當前原始碼及封存任務證據的審查，現有的 `ChurchReport` 程式碼**並未提供**任何由伺服器發行、請求局部（request-local）的 QR 出勤描述符（server-issued, request-local QR attendance descriptor）。因此，無法在解析瀏覽器/LINE 輸入、寫入 `InMemoryContext` 或執行 CRM I/O 之前，將其綁定到 `P7GatewayRequestScope` 進行安全准入驗證。

本任務判定為 **Local Design No-Go**。以下為詳細的審查發現與下一步前提條件。

---

## 1. 架構評估與設計發現 (Architecture Assessment & Findings)

### [Critical] 缺乏伺服器端發行的不可變 QR 出勤描述符 (Missing Server-Issued QR Descriptor)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs`
* **依據與危害**：
  * 現有的 `P7GatewayRequestScope` 僅包含 `SubjectContactId` (Guid)、`ProductBoundary` (string) 與 `LoginKind` (P7GatewayLoginKind)。它僅能證明 Cookie 登入者的身分，無法對 QR 掃碼請求中的 target、meeting、weekly report 等進行授權。
  * 系統中不存在任何由伺服器發行、請求局部（request-local）的 QR 出勤描述符（例如 `P7QrAttendanceDescriptor`），無法在解析瀏覽器/LINE 輸入或讀寫 `InMemoryContext` 之前進行安全准入驗證。

### [Critical] 授權前寫入共享狀態與跨使用者隔離失效 (Pre-Auth Shared State Mutation)
* **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs`
* **依據與危害**：
  * 現有的 QR 路由（如 `QrCodeView`、`QrCodeGetLineId` 等）在進行任何伺服器端授權驗證之前，會直接將瀏覽器/路由傳入的 caller-supplied 值（如 `UserLineId`、`GroupId`、`RoomId`、`ViewType`、`QrCodeId`）寫入到 process-wide 的 `InMemoryContext` 中（例如 `InMemoryContext.ListManager.QrCodeId = QrCodeId;`）。
  * 這會導致跨使用者/跨請求的狀態污染風險，且易遭受重放攻擊（Replay Attack）。

### [Warning] 遺留 Utility 混雜副作用且缺乏冪等性 (Legacy Utility Side Effects & Lack of Idempotency)
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Tools/QrCodeUtility.cs`
  * `SpeechMessageProducts.ChurchReport/Tools/SundayQrCodeUtility.cs`
  * `SpeechMessageProducts.ChurchReport/Tools/PersonalQrCodeUtility.cs`
* **依據與危害**：
  * 路由在寫入 `InMemoryContext` 後，直接調用遺留的 QR 實用工具，這些工具會執行混合了出勤 Create/Update、關係、週報和通知的 CRM I/O，缺乏單一的、可回滾的寫入擁有者（rollback owner）。
  * 遺留的 QR 實用工具將讀取、寫入、週報重算與通知發送強耦合在單一呼叫中，無法在 I/O 發生前進行純粹的准入驗證，且缺乏分散式鎖或冪等性保障。

---

## 2. 最小下一步前提條件 (Minimal Next Prerequisite)

若要安全地實作 QR 出勤指令授權，必須滿足以下前提條件：

1. **設計並實作獨立的 QR 出勤描述符**：
   * 必須建立一個由伺服器發行且不可變的描述符（例如 `P7QrAttendanceDescriptor`），該描述符應包含：
     * 經伺服器簽章或加密的 QR 識別碼與時效性校驗（Expiry/Timestamp/Nonce），以防止重放攻擊。
     * 允許的掃描動作與目標策略（Target Policy Proof）。
2. **建立 Fixed Command Admission 驗證邊界**：
   * 必須在解析任何瀏覽器/LINE 輸入或讀寫 `InMemoryContext` 之前，將此描述符與 `P7GatewayRequestScope` 結合，進行 fixed command admission 驗證。
3. **讀寫職責分離 (CQRS)**：
   * 必須將讀寫職責完全分離，將遺留的 QR 寫入邏輯重構為單一、冪等的寫入命令服務，並移除對共享 `InMemoryContext` 的依賴。

---

## 3. 唯讀聲明 (Read-Only Patch)

由於本任務為唯讀分析且判定為 No-Go，因此不對原始碼進行任何修改。

```diff
--- a/SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs
+++ b/SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs
@@ -0,0 +1,12 @@
+// NO-GO: Existing ChurchReport code does not provide a server-issued, request-local QR attendance descriptor.
+// Prerequisite: Design and implement an immutable P7QrAttendanceDescriptor containing server-signed expiry,
+// nonce, and target policy proof before parsing browser/LINE input or writing to InMemoryContext.
+//
+// [Critical] Missing Server-Issued QR Descriptor
+// [Critical] Pre-Auth Shared State Mutation (InMemoryContext.ListManager.QrCodeId)
+// [Warning] Legacy Utility Side Effects & Lack of Idempotency (QrCodeUtility, SundayQrCodeUtility, PersonalQrCodeUtility)
```

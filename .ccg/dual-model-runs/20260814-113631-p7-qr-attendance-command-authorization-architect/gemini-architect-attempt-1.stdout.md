# P7 QR 出勤指令准入分析報告 (P7 QR Attendance Command Admission Analysis)

根據對當前原始碼及封存任務證據的審查，現有的 `ChurchReport` 程式碼**並未提供**任何由伺服器發行、請求局部（request-local）的 QR 出勤描述符（QR attendance descriptor）。因此，無法在解析瀏覽器/LINE 輸入、寫入 `InMemoryContext` 或執行 CRM I/O 之前，將其綁定到 `P7GatewayRequestScope` 進行安全准入驗證。

本評估判定為 **Local Design No-Go**。以下為基於程式碼證據的詳細設計發現：

---

## 設計發現 (Design Findings)

### 1. Critical: 缺乏伺服器發行的安全 QR 出勤描述符 (No Safe Descriptor Exists)
* **檔案路徑**: 
  * `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs`
  * `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs`
* **判定依據**:
  * 現有的 `P7GatewayRequestScope` 僅包含 `SubjectContactId` (Guid)、`ProductBoundary` (string) 與 `LoginKind` (P7GatewayLoginKind)。它僅能證明 Cookie 登入者的身分，無法對 QR 掃碼請求中的 target、meeting、weekly report 等進行授權。
  * 儲存庫中不存在任何由伺服器發行、請求局部且不可變的 QR 出勤描述符（例如包含 QR 識別碼、過期時間、允許的 target-policy 等的結構）。

### 2. Critical: 狀態污染與過早寫入 (State Pollution & Premature Writes)
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs` (第 93, 173, 262, 337, 415 行)
* **判定依據**:
  * `QrCodeController` 中的所有 QR 掃碼 HTTP POST 進入點（例如 `QrCodeGetLineId`、`SundayQrCodeGetLineId` 等）在進行任何安全驗證或 I/O 之前，會先呼叫 `SetupLineContext`，將瀏覽器/LINE 傳入的 `UserLineId`、`GroupId`、`RoomId`、`ViewType` 寫入 process-wide/session-facing 的 `InMemoryContext`：
    ```csharp
    private void SetupLineContext(string userLineId, string groupId, string roomId, string viewType)
    {
        InMemoryContext.LineBindingViewModel.LineUserId = userLineId;
        InMemoryContext.AppointmentsListManager.LineUserId = userLineId;
        // ... 寫入其他欄位 ...
    }
    ```
  * 這違反了 "no-I/O-before-authorization" 與 "no-shared-state-before-authorization" 的安全原則。

### 3. Warning: 遺留工具程式混合副作用 (Legacy Utility Side Effects)
* **檔案路徑**: `SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs`
* **判定依據**:
  * 路由在寫入 `InMemoryContext` 後，直接調用遺留的 QR 實用工具（如 `QrCodeUtility`、`SundayQrCodeUtility`、`PersonalQrCodeUtility`），這些工具會執行混合了出勤 Create/Update、關係、週報和通知的 CRM I/O，缺乏單一的、可回滾的寫入擁有者（rollback owner）。

---

## 確切的本機 No-Go 原因 (Precise Local No-Go Rationale)

1. **權限邊界不足**：現有的 `P7GatewayRequestScope` 僅能證明 Cookie 登入者的 SubjectContactId，無法對 QR 掃碼請求中的 target、meeting、weekly report 等進行授權。
2. **過早寫入共享狀態**：現有的 QR 路由在進行任何安全驗證或 I/O 之前，會先將呼叫端提供的 `UserLineId`、`GroupId`、`RoomId`、`ViewType` 寫入 `InMemoryContext`，這違反了安全邊界原則。
3. **缺乏獨立的描述符**：儲存庫中不存在任何獨立於 `InMemoryContext`、Session、Entity 或遺留工具程式的、由伺服器發行且不可變的 QR 出勤描述符。

---

## 最小的下一個先決條件 (Minimal Next Prerequisite)

必須先設計並實作一個獨立的、由伺服器發行且不可變的 QR 出勤描述符（例如 `P7QrAttendanceDescriptor`），該描述符能夠在解析任何瀏覽器/LINE 輸入或讀寫 `InMemoryContext` 之前，與 `P7GatewayRequestScope` 結合進行 fixed command admission 驗證。

---

## 格式限制輸出 (Unified Diff Patch)

由於本任務為純唯讀分析，無須修改任何程式碼，以下輸出空 Diff 以符合格式限制：

```diff
--- a/dev/null
+++ b/dev/null
```

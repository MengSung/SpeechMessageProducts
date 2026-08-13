# CCG 架構評審報告：P7.4 ORG-CALL-00030 No-Go 邊界審查

## 1. 決策摘要 (Decision Summary)
本評估確認 **P7.4 MemberInfo basic-info consumer boundary** 任務之可行性評估結論為 **No-Go**。
由於 `MemberInfoController.UpdateContactInfo` 具備更新四個聯絡人欄位（`mobilephone`、`address2_line1`、`customertypecode`、`new_spiriitual_identity`）的能力，而現有的 typed/Data8 契約 `memberinfo.contact.update.basic.info` 僅支援前兩個字串欄位，且不支援 OptionSet 欄位。若強行進行局部遷移，將導致 Gateway 與 ToolUtility 之間的分裂腦（Split-Brain）寫入或靜默行為變更。

因此，本任務正確地將狀態定為 **No-Go**，維持所有相關 Feature Gates 為 `false`，且未對運行時、配置、CE 實證或 P7.5/P8 進行任何變更。

---

## 2. 評審發現 (Findings)

### Critical (關鍵缺陷/風險)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs` (第 1377-1415 行)
    *   **判定理由**：`UpdateContactInfo` 接收並處理四個欄位的變更，包含兩個 OptionSet 欄位（`membershipStatusValue` 映射至 `customertypecode`，`spiritualIdentityValue` 映射至 `new_spiriitual_identity`）。
    *   **風險說明**：現有 typed 契約 `IPackage02ContactBasicInfoUpdateClient` 僅定義了 `Phone` 與 `Address`。若在此狀態下將 Controller 接入新 Client，OptionSet 欄位將被靜默丟棄，或被迫形成「部分走新管道、部分走舊 ToolUtility 管道」的分裂腦 composite 寫入，嚴重違反資料一致性與安全隔離邊界。

*   **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package02Data8ContactBasicInfoWriteOperations.cs` (第 62-92 行)
    *   **判定理由**：Data8 執行器 `Execute` 方法中，僅對 `mobilephone` 與 `address2_line1` 進行 allowlist 寫入與 read-back 驗證。任何未列入 allowlist 的參數或 OptionSet 欄位寫入皆會觸發 fail-closed。這證實了現有 typed 管道無法承接 Controller 的完整寫入職責。

---

### Warning (警告事項)
*   **檔案路徑**：`.trellis/tasks/08-14-p74-memberinfo-basic-info-consumer-boundary/prd.md`
    *   **判定理由**：歷史 P7.2 Slice C 的 `write-not-committed` 失敗已被永久封存並完成 exact cleanup。後續任何針對 `ORG-CALL-00030` 的設計，必須確保不會重試、復用或修改已封存的 Slice C 資產（如舊的 nonce、ledger 或 fixture）。

---

### Info (一般資訊)
*   **檔案路徑**：`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs` (第 303-337 行)
    *   **判定理由**：確認 `IsPackage02ContactBasicInfoUpdatesEnabled` 閘門預設維持 `false`。在閘門關閉時，`TryCreatePackage02ContactBasicInfoClient` 確定返回 `null`，符合運行時 fail-closed 的安全預期。
*   **檔案路徑**：`.trellis/tasks/08-14-p74-memberinfo-basic-info-consumer-boundary/design.md`
    *   **判定理由**：設計文檔已正確記錄 source-only 限制，無任何 CE 流量、主機環境或 P7.5/P8 授權的溢出。

---

## 3. 未來恢復條件 (Recovery Conditions)
若未來欲重啟此四欄位寫入家族的遷移，必須滿足以下精確條件：
1.  **DTO-only 寫入契約升級**：必須設計支援四欄位（含 OptionSet）的全新 DTO 契約，禁止使用舊的兩欄位契約進行折衷。
2.  **OptionSet 值域驗證策略**：必須在 Data8 執行器端建立針對 `customertypecode` 與 `new_spiriitual_identity` 的 metadata 驗證與 valid-value policy。
3.  **伺服器端授權與等冪性**：必須引入明確的 Server-derived Authorization 檢查，並配備 Idempotency Key 進行重複請求過濾。
4.  **完整的 Read-Back 與對帳機制**：寫入後必須對四個欄位進行 Retrieve 並與寫入前 baseline 進行比對，若有不一致須立即觸發 fail-closed。
5.  **單一 Rollback 擁有者**：必須指定明確的 Rollback Owner，在寫入失敗或超時（Ambiguous Mutation）時執行確定的清理合約。

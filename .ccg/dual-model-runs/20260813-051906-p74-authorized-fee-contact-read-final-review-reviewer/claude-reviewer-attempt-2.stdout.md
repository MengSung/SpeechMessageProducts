# P7.4 最終審查報告：`fee.dedication.retrieve.by.contact`（ORG-CALL-00005）

## 驗證證據（本次重新執行）

| 項目 | 結果 |
|---|---|
| 目標測試（`DonationFeeQueryServiceAsyncTests` \| `DonationFeeAuditAccessResolverTests` \| `DedicationAuditControllerFeeAuditContractTests`） | **13 通過 / 0 失敗** |
| `dotnet build SpeechMessageProducts.sln -c Release` | **建置成功，0 警告 / 0 錯誤** |
| `Package01FeeReadsEnabled` 於 `appsettings.json` / `appsettings.Development.json` | 皆為 `false`，符合本機停用邊界 |

以上與之前回報的 GREEN 證據一致，未發現新的建置或測試回歸。

## Critical

無。

## Warning

1. **兩個變更檔案並未真正達成「UTF-8 without BOM、CRLF-only、最終 CRLF」的最終硬化聲明，違反 `.editorconfig`（`end_of_line = crlf`）。**
   - `SpeechMessageProducts.ChurchReport/Models/DonationFeeAuditReadResult.cs`：第 11–13、21–25、46–49、54–55 行為裸 LF（無 `\r`），檔案其餘部分為 CRLF，形成混合換行。
   - `ChurchReport.MemberInfo.Tests/Payments/DonationFeeQueryServiceAsyncTests.cs`：第 238–258 行（新增的 `Package01_fee_audit_uses_contact_operation_with_null_name_and_request_local_result` 測試內、含 `typeof(DonationFeeAuditRow).GetProperties()` 與後續斷言的區塊）同樣為裸 LF。
   - 影響：不影響編譯或測試結果（已驗證兩者皆綠燈），但下次任何人以 Git/Visual Studio 觸碰這兩個檔案時，會被靜默轉換為 CRLF，產生與本次變更無關的雜訊 diff；也與交付說明中「所有變更 C# 檔案已正規化為 UTF-8 without BOM、CRLF-only 與最終 CRLF」的聲明不符。
   - 建議：在合併前對這兩個檔案執行行尾正規化（不需改動任何邏輯）。

## Info

1. **`ChurchReport.MemberInfo.Tests/Controllers/DedicationAuditControllerFeeAuditContractTests.cs` 是原始碼文字比對測試，而非執行期行為測試。**
   它以 `File.ReadAllText` 讀取 `DedicationAuditController.cs` 的方法本體並檢查子字串出現順序（授權 → GUID parse → manager 建立），檔案自身註解已承認這是因為現有大型 MVC host 難以在單元測試中啟動的權衡。此測試能防止「明顯的」順序回歸（例如把 `EnsureCorrectUserData()` 移到 `Guid.TryParse` 之後），但無法偵測邏輯等價卻改變安全語意的重構（例如把 `CanAccessFeeAudit` 結果暫存後於稍後才短路判斷）。不阻擋合併，但後續若有機會補上真正的 controller 整合測試會更穩固。

## 逐項對照「必要邊界」檢查結果

- **伺服器端登入 contact / 會計角色先於 GUID parse、manager 存取與任何 dispatch 授權**：`DedicationAuditController.cs` `GetFeesByContactId` 先呼叫 `EnsureCorrectUserData()` 與 `DonationFeeAuditAccessResolver.CanAccessFeeAudit(loginContact)`，才進行 `Guid.TryParse` 與 `InMemoryContext.DonationPaymentManager` 存取，順序正確；`DonationFeeAuditAccessResolver.CanAccessFeeAudit` 只接受伺服器端已解析的 `Entity` snapshot，完全不接受瀏覽器 GUID 作為授權依據 → **符合，無 IDOR 疑慮**。
- **瀏覽器 GUID 僅作 locator，禁止 target Entity 補查 / DTO→Entity 還原 / request-time fallback 或 retry**：`RetrieveFeeAuditByContactAsync` 全程只用 typed `IPackage01FeeReadClient`，`profileAlias`／`workloadSubjectId` 皆為 deployment-owned 常數／設定值，`contactName` 固定傳 `null`；找不到任何 `RetrieveEntity("contact"...)` 或 legacy fallback 呼叫 → **符合**。
- **`Package01FeeReadsEnabled=false` 在所有已提交部署設定中維持關閉**：已於 `appsettings.json:595`、`appsettings.Development.json:10` 確認為 `false` → **符合**。
- **true 分支只用 typed 操作、固定 profile／workload subject、request-local 不可變 DTO 列、checked 整數總額**：`RetrieveFeeAuditByContactAsync`（`DonationFeeQueryService.cs`）以 `checked(totalAmount + mappedFee.Amount)` 累加並在發布前驗證 `Int32` 範圍；`DonationFeeAuditReadResult` 建構子複製輸入陣列並以 `ReadOnlyCollection<DonationFeeAuditRow>` 包裝發布，`DonationFeeAuditRow` 全屬性唯讀 → **符合**（並有測試以反射與轉型嘗試覆寫驗證回歸）。
- **取消需逃逸一般 controller 錯誤處理；semaphore/lease 需確定性釋放**：`catch (Exception e) when (e is not OperationCanceledException)` 確保取消不被吞掉；`DonationPaymentManager.RetrieveFeeAuditByContactAsync` 以既有 `_feeRefreshLock` 搭配 `try/finally` 確保成功、取消或例外情況下皆恰好釋放一次 → **符合**。
- **本機限定（無 CE request/mutation、旗標啟用、流量切換、ToolUtility 移除、P7.5/P8、push 或 PR）**：本次審查僅讀取程式碼與執行離線建置/測試，未進行任何上述動作；旗標維持 `false` → **符合**。

## 結論

本次最終硬化（不可變 `DonationFeeAuditRow`、`DonationFeeAuditReadResult` 的複製＋唯讀包裝、A/B 隔離與可寫陣列回歸測試）已正確落實，且所有必要授權邊界、no-fallback、checked 總額、取消與資源釋放語意皆通過檢視與重新執行的測試/建置驗證。僅發現 1 項 Warning（兩檔案行尾未完全正規化為 CRLF-only，與交付聲明不符但不影響功能），以及 1 項 Info（controller 契約測試為原始碼文字比對而非行為測試）。**無 Critical 發現，可視為此本機停用變更的最終審查通過，交由後續完整驗證流程確認。**

---
SESSION_ID: 5172cc4b-3ec9-4801-9ae4-37671402cc17

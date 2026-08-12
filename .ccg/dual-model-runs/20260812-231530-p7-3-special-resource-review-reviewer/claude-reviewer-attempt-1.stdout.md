我已直接讀取本次 P7.3 未提交變更的完整 diff 與新增檔案（connector executor、pool/lease、Data8 特殊資源實作、DTO 契約、metadata cache、Gateway normalizer、DI 註冊、matrix/schema/registry 與新增測試），並執行 `dotnet build`／`dotnet test`（171 個相關測試全數通過）以獨立驗證行為，未修改任何檔案。以下是審查結果。

---

## P7.3 特殊資源本機實作審查報告

**審查範疇**：`memberinfo.contact.retrieve.image`、`memberinfo.contact.update.image`、`newperson.contact.update.image`、`metadata.optionset.retrieve.by.attribute`、`stats.meeting.retrieve.by.sunday`

**驗證方式**：`dotnet build SpeechMessage.Dynamics.Tests` 建置成功（0 錯誤）；`dotnet test` 過濾 P7.3 相關測試類別，171/171 通過。

### Critical（嚴重缺陷）
無。

特別說明：曾懷疑 `Data8ProfileOperationExecutor.ExecuteOperationAsync`（`Data8ProfileOperationExecutor.cs:198-208`）僅在 `!connectorResult.Succeeded` 時呼叫 `lease.MarkFaulted()`，若 `lease.ExecuteAsync` 內部拋出未捕獲例外會繞過此淘汰。經追蹤 `Data8ConnectorPool.cs:343-358`（`Data8ConnectorLease.ExecuteAsync`）確認：任何例外（含 `OperationCanceledException` 與所有未分類例外）皆在 lease 內以 catch-all 呼叫 `MarkFaulted()` 後才 rethrow，形成雙層保護；且 `Data8ProfileOperationExecutorTests.cs:739-767` 的 `Execute_async_evicts_client_when_special_resource_connector_reports_unsuccessful_result` 已明確斷言 `factory.DisposedCount == 1`（未成功結果不回到 idle pool）。因此「未成功 connector 結果須故障驅逐」的要求已在多層落實並有回歸測試覆蓋，不構成缺陷。

### Warning（警告事項）

**缺陷：文件註解與實際實作矛盾，描述已存在的 decoder 防線為「尚未完成的後續工作」**
- **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Package03Data8SpecialResourceOperations.cs`
- **關鍵位置**：第 400-403 行（`ValidateImagePayload` 文件註解）與第 443-446 行（`DetectMediaKind` 文件註解）
  ```csharp
  /// 驗證固定 PNG/JPEG signature 與 payload byte ceiling。此初版 contract 不假裝具備 decoder-based dimension/pixel
  /// evidence；因此不接受任何其他格式，P7.4 cutover 前仍維持 consumer disabled/evidence-pending。byte[] 不被保存。
  private static void ValidateImagePayload(byte[] bytes, ContactImageMediaKind mediaKind)
  ...
  /// 依最小 magic bytes 判斷允許格式。這不是 MIME/副檔名 trust；未知或過短資料都立即拒絕。實際 decode/dimension
  /// guard 需在具備受控 decoder dependency 與相稱 CE/host evidence 的後續深化工作完成前保持 fail closed。
  private static ContactImageMediaKind DetectMediaKind(byte[] bytes)
  ```
- **具體成因與危害**：`ValidateImagePayload` 本體（第 404-441 行）實際上已呼叫 `Image.DetectFormat`／`Image.Identify` 並驗證 width/height/pixel 上限（真正的 decoder-based 防線），且在 `ReadRequiredContactImage`（第 380-381 行）中緊接在 `DetectMediaKind` 之後被呼叫。但兩段註解都聲稱這類 decoder-based dimension/pixel 驗證「尚未實作」、要等「後續深化工作」或「P7.4 cutover」才會完成。這與同檔案、同 executor 層（`Data8ProfileOperationExecutor.cs` 的 `IsValidDecodedImage`）已重複實作的深度防禦事實不符。這類過時註解可能誤導：(1) 未來工程師誤以為 `ValidateImagePayload` 是冗餘/尚未生效的程式碼而移除它；(2) 審查者或 P7.4 gate 決策者誤判目前是否已具備 decoder 證據，導致重工或錯誤延後 cutover 判斷。
- **修復建議**：更新兩處註解以反映目前狀態（decoder-based signature/format/dimension/pixel 驗證已存在且為雙層防禦），並將「P7.4 前 evidence-pending」限定在其實際仍待完成的範圍（例如正式流量、CE 寫入證據），避免與程式碼描述的能力互相矛盾。

### Info（一般資訊）

**說明一：同一影像 payload 在單次 request 中重複執行完整 decoder 驗證，屬效能可優化項目**
- **檔案路徑**：`SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileOperationExecutor.cs:621, 977, 1095, 1298`；`SpeechMessage.Dynamics.Connectors.Data8/Package03Data8SpecialResourceOperations.cs:122, 396`
- **說明**：單次 image write 請求會歷經：executor admission 前驗證（`HasValidSpecialResourceImagePayload` → `IsValidDecodedImage`）、envelope 估算（呼叫 `GetImageBytes()` 只為取得 `Length`，觸發一次完整 32 KiB 陣列複製）、connector 寫入前再驗證（`ValidateImagePayload`）、寫入後 read-back 再解碼一次（`DetectMediaKind` + `ValidateImagePayload`）。ImageSharp `Identify`／`DetectFormat` 因此在單一 write 請求中至少執行 3 次，且 `GetImageBytes()` 的防禦性複製被呼叫 5 次以上。這是有意的多層防禦設計（註解已說明「不依賴單一層」），在 32 KiB／2048×2048 像素上限下額外成本很小，非正確性缺陷，僅供未來若擴大影像上限時列入效能考量（例如新增內部 `ImageByteLength` 屬性以避免僅為取長度而複製陣列）。

**說明二：`GatewayOperationParameterNormalizer` 對 image-update 操作缺少與 `stats.meeting.retrieve.by.sunday` 相同的「精確欄位數」檢查，但不構成安全缺口**
- **檔案路徑**：`SpeechMessage.Dynamics.Gateway/RequestLimits/GatewayOperationParameterNormalizer.cs:55-65`（對照第 85-94 行 `stats.meeting.retrieve.by.sunday` 的 `copy.Count != 1` 檢查）
- **說明**：`MemberInfoContactUpdateImage`／`NewPersonContactUpdateImage` 分支只驗證 `imagePayload` 欄位本身，未檢查 `copy` 中是否含未預期的多餘鍵值；而 `stats.meeting.retrieve.by.sunday` 分支則明確要求 `copy.Count == 1`。經追查 `Data8ProfileOperationExecutor.TryCopyValidatedParameters`（`Data8ProfileOperationExecutor.cs:495-507`）已對 `source.Count > definition.Parameters.Count` 與未登錄參數名稱一律 fail closed（registry 只定義 `contactId`／`imagePayload`），因此多餘欄位仍會在 executor 層被攔截，不构成可利用漏洞，僅是 Gateway 層防禦深度的一致性小落差。

### 其他確認事項（依任務要求逐項核對，未發現異常）
- **Profile／Generation 隔離**：`MetadataOptionSetCacheKey`（`MetadataOptionSetCache.cs:22-77`）強制要求已驗證的 `ProfileAlias`／`GenerationId`／伺服器解析 locale，無法由 caller 或 HTTP header 影響；cache 由 `Data8ProfileRuntime` 唯一擁有並在建構失敗與 Dispose 路徑都確定釋放（`Data8ProfileRuntime.cs:61-64, 74-79, 116-119`）。
- **CRM SDK／stream／cookie／Entity 不跨界**：`Package03Data8SpecialResourceOperations.cs` 中所有 `Entity`、`QueryExpression`、`PagingCookie` 均只存在於同步 method scope，回傳前一律投影為 pure-value record（`OptionSetOptionRecord`、`MeetingStatisticRecord`、`ContactImageResponseData`）。
- **Fail-closed 回應契約**：`OperationResponseData` 新增的四個 branch（`ContactImage`／`ContactImageUpdate`／`OptionSetOptions`／`MeetingStatistics`）均透過 `IsValid*` 方法強制單一 branch、bounded 筆數與欄位；`P7_3_response_union_rejects_mixed_branches_and_defensively_copies_image_bytes`（`Package01OperationRegistryTests.cs`）驗證混合 branch 會被 constructor 拒絕、影像 bytes 為互不共享的防禦性複製。
- **Matrix／Registry／Schema 一致性**：`Package01OperationRegistry.cs`、`phase0-organization-call-matrix.json`、`phase0-organization-call-matrix.schema.json`、`OperationRegistryAgreementTests.cs`（17→21 列）彼此完全對應，無遺漏或不同步。
- **未宣稱越權事項**：`prd.md`／`roadmap-p5-p7.md` 新增段落明確聲明 P7.3「不開啟 ChurchReport consumer、feature gate 或 CE 寫入，也不構成 CE evidence、ToolUtility removal 或 P7.4/P7.5/P8 的完成證明」，未發現違反此邊界的程式碼或文件敘述；`AddSpeechMessageDynamicsPackage03SpecialResources` 僅註冊 typed client（DI 層面），未在 `Program.cs` 或任何 composition root 中被啟用進 ChurchReport 實際呼叫路徑。
- **P7.2 Slice C**：未發現任何重試該已封存路徑的程式碼或文件建議。

### 結論
本次 P7.3 變更整體品質良好：build／既有與新增測試全數通過，隔離、防禦性複製、bounded 資源、fail-closed 與故障驅逐等核心安全要求皆有程式碼與測試雙重佐證。僅發現 1 項 Warning（過時且與實作矛盾的文件註解，可能誤導未來維護與 P7.4 決策）與 2 項 Info（效能與防禦深度一致性的次要建議），無 Critical 缺陷。

---
SESSION_ID: 5303a97c-97ff-48d9-adf2-8947876ed51b

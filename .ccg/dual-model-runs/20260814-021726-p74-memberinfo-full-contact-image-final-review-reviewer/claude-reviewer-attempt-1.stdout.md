# P7.4 ORG-CALL-00028 Final Local-Only Review 結果

## 審查範圍
`.trellis/tasks/08-14-08-14-p74-memberinfo-contact-image-full-response` 對應的目前未提交變更集：Abstractions（operation/response union）、Data8 connector（allowlist/identity 驗證）、ProductClient facade、ChurchReport service/controller/appsettings，以及新增的 4 個測試檔。已逐檔讀取實際 diff／新檔全文，並用位元組層級掃描交叉驗證行尾編碼，沒有依賴推測。

---

### Critical 🔴
無。

### Warning 🟡

- **`SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package03MemberInfoFullContactImageReadService.cs:130-156`**（`CreateLineRedirectResult` / `IsApprovedLineHost`）
  - **問題**：ChurchReport 層對 LINE redirect URL 的再次驗證（scheme、user-info、host allowlist）**沒有檢查 `uri.IsDefaultPort`**，而 Data8 connector 端的對應驗證函式 `TryCreateAllowlistedLineUri`（`SpeechMessage.Dynamics.Connectors.Data8/Package03Data8SpecialResourceOperations.cs:187-208`，第 194 行 `!parsed.IsDefaultPort`）明確拒絕非預設 port。這與本次審查要求「Image > exact HTTPS allowlisted LINE hosts... No generic URL, non-default port... Data8 and ChurchReport host validation must agree」不完全一致——兩層驗證的欄位集合不對稱。
  - **目前是否可被利用**：不可，因為目前唯一產生 `LineRedirectUri` 的路徑（Data8）已先行擋下非預設 port，ChurchReport 端收到的值必然合法。但 design.md 明白指出 ChurchReport 之所以「再次使用同樣封閉 validator」正是為了避免「跨層 allowlist 漂移」造成的縱深防禦失效；目前實作並未真正重用同一 validator，而是各自重寫了一份不同欄位集合的檢查，屬於防禦縱深缺口。專案內既有先例（`SpeechMessage.Dynamics.ProductClient/MemberInfo/Package02ContactProfileClient.cs:272`、`SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs:276`）在類似 URL allowlist 情境都包含 `IsDefaultPort` 檢查，此處是唯一遺漏的一處。
  - **建議**：在 `CreateLineRedirectResult` 加入 `!uri.IsDefaultPort` 的 fail-closed 條件，使兩層驗證欄位完全一致。

- **byte-level 編碼規範未達成（design.md／implement.md 自訂 Check 條件：「UTF-8 無 BOM、CRLF-only、final CRLF」）**
  - `ChurchReport.MemberInfo.Tests/Services/Package03MemberInfoFullContactImageReadServiceTests.cs`：全檔 169 行**全部**為裸 LF（0 行 CRLF），且檔案結尾也不是 CRLF（最後 4 bytes = `7d 0a 7d 0a`）。整份新檔完全未套用專案的 CRLF 慣例。
  - `SpeechMessageProducts.ChurchReport/Services/MemberInfo/Package03MemberInfoFullContactImageReadService.cs`（production 程式碼，非測試）：第 149-156 行（`IsApprovedLineHost` 方法區塊）共 8 行為裸 LF。
  - `ChurchReport.MemberInfo.Tests/Controllers/MemberInfoControllerPackage03FullContactImageContractTests.cs`：第 85-89 行共 5 行裸 LF。
  - `SpeechMessage.Dynamics.Tests/ContactImageDisplayProductClientTests.cs`：第 88 行為裸 LF。
  - 這些都是本 child 全新建立的檔案（100% 屬本次變更），已違反 implement.md 第 5 步「byte-level 驗證所有本 child 實質變更 .cs 為 UTF-8 無 BOM、CRLF-only、final CRLF；執行 `git diff --check`」的自訂 Check 條件。不會造成編譯或測試失敗（C# 編譯器不在意行尾），但代表 Check 階段承諾的位元組層級驗證步驟未被確實執行或在其後被繞過，屬流程合規缺口，建議在下一次提交前修正並重新以位元組層級掃描確認。

### Info 🟢

- **測試覆蓋缺口**：Data8（`Package03Data8SpecialResourceOperationsTests`／`OnPremiseData8ConnectorClientFactoryTests.cs:372-401`）與 ChurchReport 兩層目前都沒有「host 在 allowlist 內、但 port 非預設」（如 `https://profile.line-scdn.net:8443/avatar.png`）的 negative test。補上此案例可同時驗證程式碼行為，也能作為上方 Warning 修正後的迴歸防護。
- **`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs:169, 179`**：`Package03FullContactImage` 內對 `DonationDynamicsAccessBootstrap.BindOptions(configuration)` 實際呼叫了兩次——一次隱含於 `TryCreatePackage03MemberInfoFullContactImageReadClient(configuration)` 內部，一次直接在 controller 取得 `ProfileAlias`。屬輕微重複解析設定，非正確性問題，不影響行為。

---

## 已驗證且符合規格的重點（正面證據）

- **獨立 typed operation**：`OperationIds.MemberInfoContactRetrieveImageDisplay = "memberinfo.contact.retrieve.image.display"`（`OperationIds.cs:37-40`）與既有 `MemberInfoContactRetrieveImage` 完全分離，固定投影 `entityimage`／`new_line_picture_url`／`gendercode` 三欄（`Package03Data8SpecialResourceOperations.cs:124-127`）。
- **身分嚴格比對 fail-closed**：`Package03Data8SpecialResourceOperations.cs:132-137` 在任何分支選擇前，先比對 `entity.LogicalName == "contact"` 且 `entity.Id == contactId`；並有專屬測試 `Created_client_rejects_a_full_contact_image_display_with_a_mismatched_contact_identity`（`OnPremiseData8ConnectorClientFactoryTests.cs:289`）驗證跨聯絡人 LINE URL/gender 不會外洩。
- **Image > LINE allowlist > avatar 優先權**：`ExecuteRetrieveContactImageDisplay`／`CreateLineOrAvatarDisplay` 邏輯正確依序退回；Data8 端 `TryCreateAllowlistedLineUri`（第 187-208 行）精確檢查 HTTPS、無 user-info、無 fragment、預設 port、與兩個固定 host（`profile.line-scdn.net`／`obs.line-apps.com`）字串相等比對，無泛用 URL 或 legacy fallback。
- **Gate 預設關閉**：`appsettings.json`／`appsettings.Development.json` 均新增 `Package03MemberInfoFullContactImageReadEnabled: false`，並有 `Checked_in_configuration_keeps_full_contact_image_sub_gate_disabled` 測試鎖定。
- **Controller 執行順序**：`MemberInfoController.cs:128-207` 確認為 gate → `EnsureCorrectUserData`/`GetAccess`（scope）→ `Guid.TryParse` → `CanViewContact` → typed client → dispatch，與 contract test（`MemberInfoControllerPackage03FullContactImageContractTests.cs:33-70`）以字串位置斷言完全一致；且以獨立 test 掃描確認 action 本體不含 `ToolUtility`／`Entity`／`IMemoryCache`／`retry`／`fallback` 等禁用符號，經手動 grep 交叉確認屬實（該檔其餘既有 legacy 路由仍保留 ToolUtility，但新 action 未觸及）。
- **取消傳遞**：Controller 用 `catch (Exception ex) when (ex is not OperationCanceledException)` 排除取消；service／connector 三層皆有專屬測試（`RetrieveAsync_propagates_display_cancellation_without_publishing_a_partial_result`、`Created_client_cancels_full_contact_image_display_before_any_crm_dispatch`）證明取消在觸及 CRM 前中止且無 partial 結果。
- **A/B isolation 與防禦性複製**：`RetrieveAsync_keeps_interleaved_display_image_results_request_local`（`Package03MemberInfoFullContactImageReadServiceTests.cs:61-84`）以交錯 `TaskCompletionSource` 驗證兩位使用者互不干擾，且每次 getter 皆回傳新陣列；abstraction 層 `ContactImageDisplayResponseData`／`ContactImageDisplayResult`／`Package03MemberInfoFullContactImageReadResult` 三層 constructor 與 getter 均 `.ToArray()` 複製。
- **無資源/連線洩漏**：新路由僅透過既有 `GetOrCreateEmbeddedExecutor`/`GetOrCreateGatewayExecutor`（process host 既有池）建立 client facade，未新增 per-request connection/pool/handler；未使用 `IMemoryCache`。

---

## 結論
本次變更在**核心安全邊界**（身分比對、host allowlist、gate 順序、取消傳遞、資料隔離）上實作正確且有對應測試證據支持，未發現 Critical 等級問題。存在 1 項 Warning（ChurchReport 層 LINE URL 驗證缺少 port 檢查，屬縱深防禦不對稱而非目前可利用漏洞）與 1 項 Warning（新檔案行尾編碼未依 implement.md 承諾的 CRLF-only／final-CRLF 規範，屬流程合規缺口）。建議在下一輪修正上述兩項 Warning 後即可視為此 local-only candidate 完成；CE 9.1 實際執行、Embedded/Dedicated parity、capacity/rollback 等仍如 design.md 所述屬後續獨立證據工作，本次審查不涉及也未偽稱涵蓋。

---

**流程說明**：本審查由 self-healing CCG entrypoint 派送給 Claude reviewer 執行；同一批次的 Gemini reviewer（`20260814-021726-p74-memberinfo-full-contact-image-final-review-reviewer/gemini-reviewer-attempt-1.stderr.md`）因 `Gemini adapter reached the turn limit without a final response` 失敗，已保留其 prompt/stdout/stderr/health 證據於 `.ccg/dual-model-runs/` 供後續判斷是否需重跑或依 `-AllowSingleModelWhenQuotaBlocked` 政策採計本次 Claude 單模型結果。

---
SESSION_ID: b74df759-c2ea-4d0a-a914-44fcb88b0be1

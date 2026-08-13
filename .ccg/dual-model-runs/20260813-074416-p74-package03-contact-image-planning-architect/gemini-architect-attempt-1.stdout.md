--- a/.trellis/tasks/08-13-08-13-p74-package03-contact-image-read-boundary/design.md
+++ b/.trellis/tasks/08-13-08-13-p74-package03-contact-image-read-boundary/design.md
@@ -1,38 +1,52 @@
-# P7.4 Package03 ?舐窗鈭箏??霈€??閮剛?
-
-## ??
-
-??child ?啣?銝€璇蝡? `/MemberInfo/Package03ContactImage` GET  頝舐????request-local service??銝 `GetContactImage` ?隞?祕雿?銋??曹澈閰?legacy route ?翰?€INE redirect ??avatar fallback??
-## ?瘚?靽∩遙??
-
-```text
-HTTP request
-  -> fixed false gate (404; no parse / authorization / DI / I/O)
-  -> EnsureCorrectUserData
-  -> Guid parse as locator
-  -> CanViewContact server-side authorization
-  -> Package03ContactImageReadService
-  -> fixed configuration profile + fixed workload
-  -> IPackage03SpecialResourceClient.RetrieveContactImageAsync(RequestAborted)
-  -> immutable defensive-copy result
-  -> File(content bytes, closed content type)
-```
-
-? `CanViewContact` ?€閬歇閫????Guid嚗?甇??鞈???隞?erver authorization ? ProductClient dispatch??locator ?祈澈銝瘙箏? visibility?rofile ??operation?false-gate ?舀?拍?撘瑕?迫暺??迨 gate ???€?parse ?賭??潛???
-## 閮剛?瘙箇?
-
-### Feature gate
-
-?啣? deployment-owned `DynamicsAccess:Package03SpecialResourcesEnabled=false`?oute ?湔敺?`IConfiguration` ?斗嚗????芸???`NotFound`嚗?閫??頛詨???? service???澆隞颱? legacy/typed 靘陷?€? rollback ?臬銝€閮剖???嚗?銝遣蝡???fallback??
-### ???摰???
-?批?典 true-gate path ?瑁? `EnsureCorrectUserData`嚗蒂隞交??`CanViewContact(contactId)` 撽??桀??餃?€ervice 敺?????deployment-owned `DynamicsAccess:ProfileAlias`嚗?銝??caller profile嚗orkload 瘞賊??箇楊霅臬虜??`church-report-member-info-image-read`?征??profile fail closed嚗??澆 client??
-### DTO ????
-service ????`ContactImageResult`嚗??喳?敺?defensive image copy嚗? `ContactImageMediaKind` ???Ｗ? JPEG ??PNG content type嚗蒂撱箇??芸楛??immutable result copy??銝蝙??cache?DK type?tream?egacy connector ??fallback?ontroller ?芣? result copy ?喟策 `File`??
-### ???仃??
-`HttpContext.RequestAborted` ?見?喟策 service/client嚗OperationCanceledException` 銝◤ controller ??generic catch ???隞?typed failure ?芸??喳摰?404嚗?銝靘?閮??? legacy CRM???箸??神?乓€ache ???航?皞?瘝? CE reconciliation ??data cleanup嚗銝€?怠? image array ??owner ?舐??request/result嚗??action 敺 managed memory ?學??
-## ?詨捆?扯? rollback
-
-`GetContactImage` 摰靽? legacy 銵? route 瘝??暹? UI consumer嚗? gate ????false嚗??函蔡敺???traffic 銵?寡??? gate 蝬剜?/?孵? false ?喳?甇Ｘ route嚗鞈? mutation?ache ??process/resource owner 閬??€€???CE?arity ??cutover evidence??
-## 皜祈岫閮剛?
-
-- service unit tests嚗摰?request?ontent type?efensive copy?/B 鈭日??瘨? no-image/fault??-- source contract tests嚗alse-gate order??甈?dispatch order?egacy isolation? forbidden types/cache/fallback??瘨?catch rule??-- configuration tests嚗ase/development ? false??-- solution tests/build嚗Ⅱ隤 C# ????runtime ?游?瘝?銝???+# P7.4 Package03 聯絡人頭像唯讀邊界設計與規劃審查
+
+## 1. 架構分析 (Analysis)
+本規劃旨在為 P7.4 Package03 建立一個獨立的、預設關閉的 HTTP GET 路由 `/MemberInfo/Package03ContactImage` 與專屬的 request-local 服務，用於讀取聯絡人頭像。此設計必須與既有的 `GetContactImage` 隔離，不共享快取、不進行 legacy fallback（如 LINE 重導向或性別剪影），且不影響既有流量。
+
+## 2. 架構決策 (Architecture Decision)
+* **決策**: 採用動態服務解析（Dynamic Service Resolution）以實現真正的 Gate-False 隔離。
+  * *原理*: 若使用建構子注入，即使 Gate 為 false，ASP.NET Core 仍會解析依賴項。改用 `HttpContext.RequestServices.GetRequiredService` 延遲解析，可確保在 Gate 關閉時達到零 DI 實例化與零 I/O。
+  * *拒絕的替代方案*: 建構子注入（因違反 "no DI" 約束而被拒絕）。
+  * *假設*: 系統在 Gate 關閉時不應載入任何新服務的依賴。
+  * *潛在副作用*: 略微增加 Action 內部的 Service Locator 使用，但在 Gate 控制的邊界路由中是可接受的權衡。
+* **決策**: 嚴格的異常傳播與取消令牌（Cancellation Token）傳遞。
+  * *原理*: 必須將 `HttpContext.RequestAborted` 傳遞給 `RetrieveContactImageAsync`，且必須在 `catch` 區塊中明確排除 `OperationCanceledException`，以避免吞掉取消異常。
+* **決策**: 動態 MIME 類型映射。
+  * *原理*: 依據 `ContactImageResult.MediaKind` 動態決定回傳 `image/png` 或 `image/jpeg`，避免瀏覽器解析錯誤。
+
+## 3. 實作計畫 (Implementation Plan)
+1. **Gate 檢查與延遲解析**:
+   * 在 Action 最前端檢查 `DynamicsAccess:Package03SpecialResourcesEnabled`。若為 false，立即回傳 `NotFound()`。
+   * 僅在 Gate 為 true 時，才解析 `IPackage03SpecialResourceClient`。
+2. **Locator 解析與授權**:
+   * 嘗試解析 `contactId` 為 `Guid`。若失敗，立即回傳 `NotFound()`。
+   * 呼叫 `CanViewContact(contactGuid)`。若未授權，立即回傳 `NotFound()`。
+3. **服務呼叫與取消**:
+   * 呼叫 `RetrieveContactImageAsync(..., RequestAborted)`。
+   * 使用 `try-catch` 區塊，但必須 `catch (OperationCanceledException) { throw; }` 以確保取消狀態正確傳播。
+4. **防禦性複製與回傳**:
+   * 取得 `ContactImageResult` 後，呼叫 `GetImageBytes()` 取得防禦性複製的 byte array。
+   * 依據 `MediaKind` 設定 Content-Type 並回傳 `File`。
+
+## 4. 考量事項 (Considerations)
+* **安全性**: 嚴格執行授權檢查（CanViewContact），且在授權失敗時直接回傳 404，不洩漏任何資訊。
+* **效能**: 由於不使用快取，此端點僅供特定邊界讀取，不應被高頻率的 UI 網格直接呼叫。
+* **可維護性**: 與既有 `GetContactImage` 完全隔離，便於後續 P7.5/P8 的收斂與清理。
+
+## 5. 審查發現 (Findings)
+* **Critical**: Feature-Gate 檢查必須在任何 DI 解析之前。若使用建構子注入，會導致 Gate 為 false 時仍解析依賴項。必須改用動態解析。
+* **Critical**: 異常處理必須排除 `OperationCanceledException`，避免吞掉取消狀態。
+* **Critical**: 缺少靜態合約測試來驗證無快取、無 legacy fallback 等約束。
+* **Warning**: 必須依據 `MediaKind` 動態決定 Content-Type，避免 PNG 被當作 JPEG 回傳。
+* **Warning**: `contactId` 解析失敗時應立即 fail-fast，不應進入授權檢查。
--- a/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs
+++ b/ChurchReport.MemberInfo.Tests/MemberInfoTreeControllerContractTests.cs
@@ -161,4 +161,26 @@
         method.Should().NotContain("service.Retrieve(");
     }
 
+    [Fact]
+    public void Controller_Package03ContactImage_EnforcesStrictReadBoundary()
+    {
+        Source.Should().Contain("public async Task<IActionResult> Package03ContactImage(");
+
+        var action = Slice(
+            "public async Task<IActionResult> Package03ContactImage(",
+            "public IActionResult GetContactImagesBatch(");
+
+        action.Should().Contain("Package03SpecialResourcesEnabled");
+        action.Should().Contain("EnsureCorrectUserData");
+        action.Should().Contain("CanViewContact");
+        action.Should().Contain("RequestAborted");
+        action.Should().Contain("catch (OperationCanceledException)");
+
+        action.Should().NotContain("IMemoryCache");
+        action.Should().NotContain("IOrganizationService");
+        action.Should().NotContain("GetConnection(");
+        action.Should().NotContain("GetDefaultImage(");
+        action.Should().NotContain("Redirect(");
+    }
+
     private static string FindRepositoryRoot()

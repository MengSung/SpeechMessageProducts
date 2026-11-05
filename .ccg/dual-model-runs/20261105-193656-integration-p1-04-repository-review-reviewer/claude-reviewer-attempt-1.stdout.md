# P1-04 Repository 切片 Review（唯讀）

範圍：`ToolUtility.Core/EntityOperations/EntityRepository.cs`、`ToolUtility.Core/EntityOperations/EntityQueryService.cs`、`ToolUtility.Tests/CoreTargetFrameworkTests.cs`、`ToolUtility.Tests/DataOperations/RepositoryCharacterizationTests.cs`、`ToolUtility.Tests/DataOperations/RepositorySafetyTests.cs`、`ToolUtility.Tests/EntityOperations/RepositoryCallerRouteTests.cs`、`wp-reports/P1-04-repository.md`，對照原 host 基準。未探索 Facade/設定/憑證/其他工作樹。

## Critical
無。核心六入口（Create/Update/Delete/Retrieve/RetrieveMultiple/AssignOwner）未發現阻斷性錯誤。

## High
無。

## Warning

1. **`EntityQueryService.ValidatePage`（EntityQueryService.cs:283-293）對每一列強制 `row.Id != Guid.Empty`**，這是明列的「完整頁識別驗證」安全差異，已獲授權、也有對應紅測（`RetrieveMultiple_InvalidPage_RejectsBeforePublication` 的 `"empty"` shape）。但此規則對「彙總/GroupBy」類查詢（CRM 聚合列通常沒有真實 `Id`）會整頁 fail-closed 拒絕。本次審查範圍內沒有看到任何針對聚合查詢的例外處理或文件化排除。這不代表實作未獲授權，但建議在交付文件中明確記錄「本次 Repository/QueryService 不支援聚合查詢」，避免未來有消費點用到聚合查詢時被誤判成 regression。

2. **`EntityQueryService.RetrieveMultiple`（EntityQueryService.cs:118-121）會原地改寫 caller 擁有的 `QueryExpression.PageInfo`**（`expression.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }`），而不是複製一份新查詢物件再改。`RepositorySafetyTests.FailureRetryAndCallerMutation_UseFreshCandidatesAndBorrowSdk` 已覆蓋並確認此行為、且報告也載明為刻意決策，因此不視為未授權差異。但這與 class remarks（EntityRepository.cs:49「不保存 Entity、Query 或回應」）在語感上容易被後續維護者誤讀成「純讀取無副作用」，建議在 `RetrieveMultiple` 的 XML 文件再加一行明確說明「會在呼叫端物件上寫入 PageInfo」，降低日後誤用風險。

## Info

1. **`#nullable` pragma 風格不一致**：`EntityQueryService.cs:1` 明寫 `#nullable enable`，`EntityRepository.cs` 全檔沒有對應宣告（依賴 Core 專案層級統一開啟 Nullable，如 `CoreTargetFrameworkTests.cs` 註解所述）。功能上不是 bug（專案層級設定已生效、且 `IEntityRepository` 介面確實被 `CoreInterfaces_EmitNonObliviousStringContracts` 驗證為 NotNull contract），純粹是兩個搬移後檔案的風格不一致，建議統一（都省略或都明寫）。

2. **效能**：`EntityRepository.Retrieve`（EntityRepository.cs:171）與 `RetrieveMultiple`（EntityRepository.cs:197）每次呼叫都 `new EntityQueryService(...)` 再 `using` 釋放，比原 host 直接呼叫 SDK多一層短命物件配置（Retrieve 內部必要時還會再 `new ColumnSet(true)`）。相對 CRM 網路延遲此配置成本可忽略，不構成效能問題，僅供留意高頻呼叫情境下的 GC 壓力。

3. **`wp-reports/P1-04-repository.md` 中的數字性宣稱**（362/362、456/456、coverage 99.42%／100%、A/B 併發1000次等）無法只靠靜態程式碼審查覆核；本次僅能驗證測試案例設計邏輯本身正確（例如 `ConcurrentScopes_...` 內 `59 = 1000/17 的故障次數` 數學吻合），建議連同對應 trx/coverage 報表一併留存供稽核，不要僅憑報告文字宣稱。

## 核對結果（逐項對應需求）

- **Update 非空 ID 不放寬**：`EntityRepository.cs:116-117` 維持舊檢查，`RepositoryCharacterizationTests.ExistingValidation_RejectsBeforeSdkWithOriginalParameterNames` 以純替代鍵實體驗證仍拋 `ArgumentException("entity")`。✅ 符合。
- **SDK 自身 ArgumentException 仍保留同一 InnerException**：六入口 `catch (Exception ex) => throw new InvalidOperationException(..., ex)` 一律用通用 `Exception` 捕捉並保留原物件參照（非 `catch (ArgumentException)` 分流），`RepositorySafetyTests.SdkFailure_PreservesRealExceptionAndFixedOuterMessage` 的 `argumentFault: true` 案例以 `Assert.Same(failure, actual.InnerException)` 驗證引用相等。✅ 符合，且確認未採用「一律 catch ArgumentException 重拋」的做法（沒有任何針對 `ArgumentException` 的特殊分流）。
- **固定外層訊息不含 logical name/ID/原訊息**：六個訊息常數（`"建立實體失敗"`、`"更新實體失敗"`…）皆為硬編碼字串，未插入任何參數。✅ 符合。
- **新名稱/Assign 非空 ID 與一致性/完整頁/Fetch 根表驗證為明列安全差異**：`AdapterInvalidInput_RejectsWithoutSdk` 六案（create-name/update-name/assign-name/assign-id/fetch-dtd/fetch-two-roots）皆在 SDK 呼叫前被攔截，`sdk.VerifyNoOtherCalls()` 確認未觸發 I/O。✅ 符合，且與原 host 行為差異已在報告列明。
- **舊檢查在 try 前、新 adapter 防禦與真 SDK 故障皆用舊 wrapper**：`EntityRepository.cs` 六方法中既有 `string.IsNullOrWhiteSpace`/`Guid.Empty` 檢查均位於 `try` 之前（如 Delete 135-141、Retrieve 163-167、AssignOwner 215-226）；`EntityQueryService` 內的新防禦（`RequireLogicalName`、身分比對、`ValidatePage`）則在 `try` 內觸發後被同一 `catch (Exception ex)` 包裝。✅ 符合，未發現把新防禦誤放在 try 外或繞過包裝的情況。
- **三參數 overload 只加具體類別、不加舊介面**：`EntityQueryService.cs:64` 的 `RetrieveEntity(string, Guid, ColumnSet?)` 只在具體類別新增，文件明寫「不擴充既有介面」；`RepositoryCharacterizationTests`/`RepositorySafetyTests` 皆透過 `EntityRepository`（具體型別持有 `EntityQueryService` 具體型別）呼叫，未經 `IEntityQueryService` 介面。ColumnSet 傳入非 null 時原參照直傳 SDK（69行 `columnSet ?? new ColumnSet(true)`），`QueryColumnOverload_PreservesColumnsWithoutChangingExistingInterface` 以 `Assert.Same` 驗證。✅ 符合。
- **Repository 不得新增 IDisposable 義務或保存結果；query 每次 using、借入 SDK 不 Dispose**：`EntityRepository` 類本身未實作 `IDisposable`；`Retrieve`/`RetrieveMultiple` 內部 `using var query/reader = new EntityQueryService(...)`（171、197行），`EntityQueryService.Dispose`（189-193行）只清除本地參照、未呼叫 `_organizationService` 的 Dispose。`FailureRetryAndCallerMutation_UseFreshCandidatesAndBorrowSdk` 與 `ConcurrentScopes_...` 均以 `disposable.Verify(d => d.Dispose(), Times.Never)` 確認。✅ 符合。
- **6 個真實消費點 12 案，僅替換 SDK I/O**：`RepositoryCallerRouteTests.cs` 覆蓋 QueryService 五入口＋MarketingListService 一入口 × (duplicate:false/true) = 12 案，呼叫鏈為實際產品方法 → Repository → Mock SDK，無額外授權/實機宣稱。✅ 符合，且報告用語（"不聲稱授權/實機/全部分支"）與程式碼/測試行為一致。

## 總結

此切片在列出的六個檔案範圍內邏輯自洽：舊契約（ParamName、驗證順序、Update 非空 ID、InnerException 保留、固定訊息）確實原樣保留；新增的 fail-closed 差異（名稱/一致性/完整頁/Fetch 根表）皆有對應紅測且在 SDK I/O 前觸發，未發現「新 adapter 防禦繞過 try/wrapper」或「InnerException 被覆寫導致診斷資訊遺失」等阻斷性問題。僅有兩項 Warning（聚合查詢的 fail-closed 相容性風險、`PageInfo` 原地改寫的文件補強建議）值得在後續切片或交付文件中補記，不影響目前判定為「局部通過」的結論。未發現需要擴大為整專案重寫的證據。

---
SESSION_ID: 9c06df50-e6c0-4d76-9fcd-7ea6e1bfb87a

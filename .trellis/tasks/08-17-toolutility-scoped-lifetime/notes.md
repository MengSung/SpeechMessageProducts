# 執行備註

## Run 1 期間發現、但刻意不處理的項目

### 1. `ToolUtility/Diagnostics/TraceLogger.cs` 是死碼，且會重複掛 listener

- `ToolUtilityNameSpace.Diagnostics.ITraceLogger` / `TraceLogger` 全專案**零使用**
  （`SpeechMessageProducts.ChurchReport/Logging/TraceLoggerProvider.cs` 內的
  `TraceLogger` 是同名但不同的巢狀 `ILogger`，兩者無關）。
- 該檔第 87 行同樣執行 `Trace.Listeners.Add(listener)`。
- 目前無害（沒有人建立它），但若日後有人啟用，會與
  `FileToolUtilityTracer` 重複掛上 listener，造成日誌重複輸出。

**建議**：另立票刪除該死碼，或明確標記為 obsolete。
本 Run 未處理，因為它不在 Run 1 的檔案白名單內。

### 2. `SpeechMessageProducts.ChurchReport/Program.cs:170` 也有 `Trace.Listeners.Add`

- 屬於應用程式啟動階段的既有行為，與 ToolUtility 的追蹤資源不同來源。
- 未確認兩者是否寫入同一檔案。若是，日誌會有兩份來源。

**建議**：Run 2 的人工回歸時一併觀察追蹤檔是否出現重複行。
本 Run 未處理，同樣不在白名單內。

## Run 0 的調查結論

見 `research/findings-scope-boundaries.md`。三個阻礙中，第 3 項
（`InMemoryDataContextSmallGroup` 以 Session 為鍵快取 `ToolUtilityClass`）
為原規劃未預見，已據此在 `implement.md` 新增 Run 1.5。

## Run 1.5 結果

- 工作 A：DONE。`InMemoryDataContextSmallGroup.ToolUtilityClass` 已直接由
  `ToolUtilityFactory` 取得既有單例；Session ID 加上
  `_ToolUtilityClass` 的 IMemoryCache 快取與 `m_ToolUtilityClass` 欄位均已移除。
- 工作 B：DONE。`PersonalController` 與 `SmallGroupController.Save` 的
  fire-and-forget 工作均在 lambda 內建立自己的 DI scope，並由 `using` 在工作
  結束時確定性釋放。resolved service 不會被個別 Dispose。
- A-2 調查：`rg -n '"dirty"|dirty' --glob '*.cs' SpeechMessageProducts.ChurchReport`
  只找到 `session.SetInt32("dirty", 1)` 的寫入端與診斷文字，沒有讀取端；因此
  移除 ToolUtility 快取時一併移除其快取未命中的 `SetSessionDirtyFlag()` 呼叫，
  不會改變任何讀取者的行為。
- 範圍外發現：`ListSmallGroupWeeklyReport` / `UploadIntegrateData` 仍在其內部以
  `ToolUtilityFactory` 建立舊有 ToolUtility。Run 1.5 的白名單不包含這些檔案，
  因此未修改；SmallGroup 背景 scope 已用於取得其自身 ToolUtility 並記錄背景
  失敗。內部上傳器改採 scoped ToolUtility 應在 Run 3 的 Factory 呼叫點遷移中處理。

### 完成判定實際輸出

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤

最終重跑時 IIS Express Worker Process 鎖住既有輸出 DLL，MSBuild 重試後仍建置成功；
該次輸出為 24 個 MSB3026 檔案鎖定警告、0 個錯誤，並非本次程式碼警告。

dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63

dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-build
已通過! - 失敗:     0，通過:     7，略過:     0，總計:     7

grep -n "_ToolUtilityClass\"" SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
（無輸出）

grep -n "m_ToolUtilityClass" SpeechMessageProducts.ChurchReport/Models/InMemoryDataContextSmallGroup.cs
（無輸出）

grep -c "CreateScope()" SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs
1

grep -c "CreateScope()" SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs
1

ENCODING OK
```

本 Run 的程式碼提交：`3c4f99bb7`（`fix(toolutility): 移除跨請求持有者，背景工作自建 DI scope`）。

## Run 1.5 缺口補正（Run 2 前置）

- 缺口 A：DONE。兩個背景工作的 catch 區塊改用
  `ToolUtilityClass.TraceByLevelStatic(...)`；catch 位於 `using` scope 之外，
  不再觸碰已釋放的 Scoped ToolUtility。
- 缺口 B：維持原判斷。SmallGroup 的實際上傳仍由捕獲的
  `weeklyReportRef.UploadIntegrateDataAsync(...)` 執行，內部 Factory 路徑是 Run 3
  的阻擋項；本次不擴大修改 Models/WebServiceConnector。背景 scope 仍只負責其自身
  scope 內的 DI 解析與生命週期邊界。
- 缺口 C：測試延至 Run 2 一併涵蓋。Run 2 將以 scoped ToolUtility／連線生命週期
  測試覆蓋背景 scope 不共用 request 連線的合約，避免在白名單外新增測試替身。
- 缺口 D：四個 Run 1.5 `.cs` 檔案已重新寫成 UTF-8 無 BOM、完整 CRLF；Run 2
  開始前會再次執行 G4/G4b。

### Run 1.5 缺口補正實際驗證輸出

```text
dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj -c Debug
建置成功。
    0 個警告
    0 個錯誤

dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-build
已通過! - 失敗:     0，通過:     7，略過:     0，總計:     7

ENCODING OK
CRLF OK
```

缺口補正的獨立提交 hash：`3c4f99bb7`；Run 2 不會與這些 `.cs` 修正混在同一個提交。

## Run 2 結果

### 核取項與六個雷

- DONE：ToolUtilityClass 新增 DI 建構式，直接接收 scoped `IOrganizationService`，不呼叫 `InitializeCrmConnection()`。
- DONE：legacy Factory 建構式仍自行建立連線，`_ownsConnection = true`；DI 建構式為 `false`。
- DONE：`Dispose(bool)` 僅在 `_ownsConnection` 為 true 時釋放 legacy 連線與 `CrmConnectionService`；DI 連線由 scope 擁有。
- DONE：`AddToolUtility()` 以明確 factory 註冊 `ToolUtilityClass` 為 Scoped，Provider 改為 Scoped。
- DONE：ToolUtilityProvider 改為建構式注入，不再呼叫 Factory。
- DONE：新增 4 個 scoped lifetime 測試（原 7 個測試仍保留）。

雷 1：未在 ChurchReport 組件直接使用 `AddScoped<ToolUtilityClass>()`。註冊位於 ToolUtility 組件的 `ServiceCollectionExtensions.AddToolUtility()`，以 factory 呼叫 public DI 建構式，避開 internal legacy 建構式與 ref 參數。

雷 2：DI 建構式將 `_ownsConnection` 設為 false；ToolUtilityClass 與 Facade 都不釋放注入的 `IOrganizationService`，只由 ASP.NET Core scope 在 request 結束時釋放。

雷 3：實際讀取並核對下列 Facade 與子服務檔案：

`ToolUtility/Core/ToolUtilityFacade.cs`、`ToolUtility/EntityOperations/EntityQueryService.cs`、`ToolUtility/EntityOperations/EntityCrudService.cs`、`ToolUtility/AttributeOperations/AttributeServiceComposite.cs`、`ToolUtility/ContactOperations/ContactService.cs`、`ToolUtility/ListOperations/ListService.cs`、`ToolUtility/AttachmentOperations/AttachmentService.cs`、`ToolUtility/LineMessaging/LineMessageService.cs`、`ToolUtility/AppointmentOperations/AppointmentService.cs`、`ToolUtility/LessonsOperations/LessonsService.cs`、`ToolUtility/FeeOperations/FeeService.cs`、`ToolUtility/CollectionOperations/CollectionQueryService.cs`、`ToolUtility/MeetingStatisticsOperations/MeetingStatisticsService.cs`、`ToolUtility/ConnectionOperations/CrmConnectionService.cs`、`ToolUtility/QueryOperations/PresentRecordQueryService.cs`、`ToolUtility/QueryOperations/RelationshipQueryService.cs`、`ToolUtility/QueryOperations/FetchXmlQueryService.cs`、`ToolUtility/OwnerOperations/OwnerManagementService.cs`、`ToolUtility/AttributeOperations/EntityAttributeUtilityService.cs`、`ToolUtility/ActivityOperations/ActivityService.cs`。

查證結論：Facade 的 lazy 子服務沒有釋放傳入的 `IOrganizationService`；`EntityQueryService.Dispose` 明確註解為外部管理、不釋放組織服務。Facade 移除原本的 `_organizationService as IDisposable` 釋放，仍保留自己建立的 `CrmConnectionService` 子服務釋放。

雷 4：`ToolUtilityFactory` 保留自行建立連線的 legacy 建構式，未解析 DI、未接觸 scoped connection；XML 註解標明 Run 3 遷移前的過渡路徑。

雷 5：Provider 與 ToolUtilityClass 均註冊 Scoped。新增測試以 `ValidateScopes = true`、`ValidateOnBuild = true` 建置容器；跨 scope 解析出不同 ToolUtility 與不同組織服務，未發現 captive dependency。

雷 6：`ResetInstance()` 仍只釋放 Factory 自行建立的 legacy 單例；DI scoped 實例不會寫入 Factory 的 `_instance` 靜態欄位，兩條擁有權路徑在建構式與註冊型別上分開。

### 第 6 節品質門檻原始輸出

```text
dotnet build SpeechMessageProducts.sln -c Debug
  正在判斷要還原的專案...
  所有專案都在最新狀態，可進行還原。
  Line.Messaging -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\Line.Messaging\bin\Debug\net10.0\Line.Messaging.dll
  PowerPlatform.Dataverse.Client -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\PowerPlatform.Dataverse.Client\bin\Debug\net10.0\PowerPlatform.Dataverse.Client.dll
  SpeechMessage.Payments -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\SpeechMessage.Payments\bin\Debug\net10.0\SpeechMessage.Payments.dll
  LineMessagingProcessor -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor\bin\Debug\net10.0\LineMessagingProcessor.dll
  ToolUtility -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ToolUtility\bin\Debug\net10.0\ToolUtility.dll
  ToolUtility.Dataverse.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ToolUtility.Dataverse.Tests\bin\Debug\net10.0\ToolUtility.Dataverse.Tests.dll
  LineMessagingProcessor.Workflows -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor.Workflows\bin\Debug\net10.0\LineMessagingProcessor.Workflows.dll
  LineMessagingProcessor.RichMenus -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor.RichMenus\bin\Debug\net10.0\LineMessagingProcessor.RichMenus.dll
  LineMessagingProcessor.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor.Tests\bin\Debug\net10.0\LineMessagingProcessor.Tests.dll
  Line.Messaging.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\Line.Messaging.Tests\bin\Debug\net10.0\Line.Messaging.Tests.dll
  SpeechMessage.Payments.Workflows -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\SpeechMessage.Payments.Workflows\bin\Debug\net10.0\SpeechMessage.Payments.Workflows.dll
  SpeechMessage.Payments.AspNetCore -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\SpeechMessage.Payments.AspNetCore\bin\Debug\net10.0\SpeechMessage.Payments.AspNetCore.dll
  LineMessagingProcessor.AspNetCore -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor.AspNetCore\bin\Debug\net10.0\LineMessagingProcessor.AspNetCore.dll
  SpeechMessage.Payments.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\SpeechMessage.Payments.Tests\bin\Debug\net10.0\SpeechMessage.Payments.Tests.dll
  LineMessagingProcessor.Workflows.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor.Workflows.Tests\bin\Debug\net10.0\LineMessagingProcessor.Workflows.Tests.dll
  LineMessagingProcessor.RichMenus.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor.RichMenus.Tests\bin\Debug\net10.0\LineMessagingProcessor.RichMenus.Tests.dll
  LineMessagingProcessor.AspNetCore.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\LineMessagingProcessor.AspNetCore.Tests\bin\Debug\net10.0\LineMessagingProcessor.AspNetCore.Tests.dll
  SpeechMessageProducts.ChurchReport -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\SpeechMessageProducts.ChurchReport\bin\Debug\net10.0\SpeechMessageProducts.ChurchReport.dll
  ChurchReport.MemberInfo.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ChurchReport.MemberInfo.Tests\bin\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll

建置成功。
    0 個警告
    0 個錯誤

經過時間 00:00:07.07
```

```text
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
  正在判斷要還原的專案...
  所有專案都在最新狀態，可進行還原。
  ToolUtility.Tests -> D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ToolUtility.Tests\bin\Debug\net10.0\ToolUtility.Tests.dll
  ToolUtility.Tests.dll 的測試回合 (.NETCoreApp,Version=v10.0)
總共有 1 個測試檔案與指定的模式相符。

已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 205 ms - ToolUtility.Tests.dll (net10.0)
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
  正在判斷要還原的專案...
  所有專案都在最新狀態，可進行還原。
  ToolUtility.Dataverse.Tests.dll 的測試回合 (.NETCoreApp,Version=v10.0)
總共有 1 個測試檔案與指定的模式相符。

已通過! - 失敗:     0，通過:    11，略過:     0，總計:    11，持續時間: 65 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
  ChurchReport.MemberInfo.Tests.dll 的測試回合 (.NETCoreApp,Version=v10.0)
[xUnit.net 00:00:01.45]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentViewDefaultsTests.Web_login_flow_persists_contact_id_and_donation_payment_view_uses_it_to_restore_missing_model_state [FAIL]
[xUnit.net 00:00:01.46]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_booking_workflow [FAIL]
[xUnit.net 00:00:01.46]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_dedication_fee_form_refresh [FAIL]
[xUnit.net 00:00:01.46]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_payment_model_assembly [FAIL]
[xUnit.net 00:00:01.46]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_contact_mapping_to_contact_service [FAIL]
[xUnit.net 00:00:01.46]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_key_in_dedication_workflow [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.PaymentPostPaymentArchitectureTests.ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.PaymentProductServiceNamingTests.Product_payment_services_use_provider_neutral_names [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentNamingCompatibilityTests.New_payment_return_controller_exists_after_rename [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Donation_payment_form_model_is_the_primary_churchreport_form_state_type [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_payment_result_helper_and_debug_logger_exist [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentProcessorNamingTests.New_donation_payment_processor_exists_as_primary_product_workflow_processor [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnWorkflowNamingTests.New_donation_payment_workflow_result_exists_after_rename [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentManagerNamingTests.Legacy_qpay_manager_remains_as_compatibility_alias [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_donation_fee_payment_processor_exists_as_primary_fee_return_processor [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentManagerNamingTests.New_donation_payment_manager_exists_as_primary_ui_payment_state_manager [FAIL]
[xUnit.net 00:00:01.54]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Provider_specific_names_are_confined_to_provider_code_or_legacy_route_templates [FAIL]
[xUnit.net 00:00:01.55]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentProcessorNamingTests.Donation_payment_processor_constructors_require_neutral_gateway_create_adapter [FAIL]
[xUnit.net 00:00:01.55]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor [FAIL]
[xUnit.net 00:00:01.55]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Product_layer_file_names_should_not_contain_provider_brand_names [FAIL]
[xUnit.net 00:00:01.57]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_donation_login_contact_workflow [FAIL]
[xUnit.net 00:00:01.57]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_contact_creation_numbering_workflow [FAIL]

失敗!  - 失敗:    22，通過:   304，略過:     0，總計:   326，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
```

```text
python G4 encoding check
ENCODING OK
python G4b line-ending check
CRLF OK
```

缺口補正的獨立提交 hash 將於提交後填入；Run 2 不會與這些 `.cs` 修正混在同一個提交。

# Dataverse Gateway 架構 v1 執行紀錄

## 範圍外發現

- 工作樹原本已有 `.ccg/tasks/design-four-product-dataverse-connection-architecture/.turns.json` 變更，以及未追蹤的舊 CCG review 產物；本任務沒有修改、恢復或納入它們。
- Run A 沒有修改任何 `SpeechMessageProducts.ChurchReport` 檔案，也沒有修改 13 個 session key cache。

## Run A 結果

### 實作

- 新增 `ToolUtility/Dataverse/DataverseConnectionKey.cs`：四欄位值相等的隔離鍵。
- 新增 `ToolUtility/Dataverse/DataversePoolOptions.cs`：MinSize、MaxN、AcquireTimeout、IdleTimeout、HealthInterval 與 fail-fast 驗證。
- 新增 `ToolUtility/Dataverse/PooledClient.cs`：Idle／Leased／Faulted／Disposed 狀態機與確定性底層釋放。
- 新增 `ToolUtility/Dataverse/IClientLease.cs`、`IBoundedClientPool.cs`、`DataversePoolMetrics.cs`。
- 新增 `ToolUtility/Dataverse/BoundedClientPool.cs`：keyed 子池、SemaphoreSlim(MaxN)、健康檢查、閒置淘汰、故障淘汰、shutdown cleanup 與 metrics。
- Lease Dispose 使用原子冪等閘門；短命租約只歸還 semaphore，底層 client 只由 pool 決定是否 Dispose。
- 測試使用假的 `IOrganizationService`，未連線真實 Dataverse。

### 紅燈（TDD）原文

```text
ToolUtility.Dataverse.Tests/BoundedClientPoolTests.cs(...): error CS0234: 命名空間 'ToolUtilityNameSpace' 中沒有類型或命名空間名稱 'Dataverse'
... error CS0246: 找不到類型或命名空間名稱 'DataverseConnectionKey'
... error CS0246: 找不到類型或命名空間名稱 'DataversePoolOptions'
... error CS0246: 找不到類型或命名空間名稱 'BoundedClientPool'
```

### Run A 品質門檻原文

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤
BUILD_EXIT=0
```

```text
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 221 ms - ToolUtility.Tests.dll (net10.0)
TOOLUTILITY_TEST_EXIT=0
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
已通過! - 失敗:     0，通過:    18，略過:     0，總計:    18，持續時間: 735 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
DATAVERSE_TEST_EXIT=0
```

```text
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
失敗!  - 失敗:    22，通過:   305，略過:     0，總計:   327，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
MEMBERINFO_TEST_EXIT=1
```

MemberInfo 結果符合任務門檻（失敗不超過 22、通過不少於 305）；失敗是既有 Payments 命名／repository-root 測試。

```text
G5 grep -rln "IDataverseGateway|IDataverseConnectionManager|IBoundedClientPool|IClientLease|DataverseConnectionKey" --include=*.cs SpeechMessageProducts.ChurchReport/
NO OUTPUT

git diff --stat HEAD -- SpeechMessageProducts.ChurchReport/
(no output)
```

```text
ENCODING OK
CRLF OK
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-restore --filter FullyQualifiedName~BoundedClientPoolTests
已通過! - 失敗:     0，通過:     5，略過:     0，總計:     5，持續時間: 198 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

### Commit

Run A commit：`7094489c`，訊息 `feat(dataverse): 新增 Keyed Bounded Pool 與 Lease 型別契約`。

## Run B 結果

### 實作

- 新增 `IDataverseConnectionManager`／`DataverseConnectionManager`：集中解析 Product、Environment、ServerUrl、Username 四段 Pool Key；建立 client 的唯一 factory 與 WhoAmI 健康檢查均在 manager 的 pool 內部。
- 新增 `IDataverseGateway`／`DataverseGateway`：Scoped、per-operation lease、巢狀 Execute 深度計數、例外標記故障、finally 歸還。
- 新增 `GatewayOrganizationService`：八個 `IOrganizationService` 方法逐一透過 Gateway 委派。
- 新增 `AmbientGatewayOrganizationService`：使用當前 request services；無 request 時建立短命 scope，用完立即釋放，作為 20 個 legacy session holder 的過渡橋樑。
- 修正新建 client 首次健康驗證時間為 `DateTime.MinValue`，確保第一次出借一定執行 WhoAmI。
- Run B 仍未接線；沒有修改 ChurchReport、Startup、ToolUtility DI 或既有上層呼叫點。

### Run B 首次失敗與修正

```text
GatewayArchitectureTests.Connection_manager_builds_key_and_exposes_pool_metrics [FAIL]
Moq.MockException: IOrganizationService.Execute(It.IsAny<OrganizationRequest>()) setup was not matched.
```

原因是新建 client 的 `LastValidatedUtc` 初始為現在，Manager 的首次 WhoAmI 沒有被觸發；已改為未驗證狀態並重跑通過。

### Run B 品質門檻原文

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-restore --filter FullyQualifiedName~GatewayArchitectureTests
已通過! - 失敗:     0，通過:     5，略過:     0，總計:     5，持續時間: 76 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤
BUILD_EXIT=0
```

```text
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 111 ms - ToolUtility.Tests.dll (net10.0)
TOOLUTILITY_TEST_EXIT=0
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
已通過! - 失敗:     0，通過:    23，略過:     0，總計:    23，持續時間: 510 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
DATAVERSE_TEST_EXIT=0
```

```text
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
失敗!  - 失敗:    22，通過:   305，略過:     0，總計:   327，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
MEMBERINFO_TEST_EXIT=1
```

```text
ENCODING OK
CRLF OK
```

```text
G5：NO OUTPUT
git diff --stat HEAD -- SpeechMessageProducts.ChurchReport/
(no output)
```

### Commit

Run B commit：`78d9bf38`，訊息 `feat(dataverse): 新增 ConnectionManager、Gateway 與 per-operation 代理`。

## Run C 結果

### 實作

- `ServiceCollectionExtensions.AddToolUtility()` 現在註冊 Singleton `DataverseConnectionManager`、`IDataverseConnectionManager`、由 manager 擁有的 `IBoundedClientPool`、`ConnectionPoolStatsAdapter`，以及 Scoped `IDataverseGateway` 與 `GatewayOrganizationService`。
- 池設定由 `Dataverse:Pool` 綁定五個參數；環境名稱優先取 Web Host 提供的 `ASPNETCORE_ENVIRONMENT`／`DOTNET_ENVIRONMENT` 組態，並進入 Pool Key。
- `Startup.cs` 移除舊 `CrmConnectionPool`／`PooledOrganizationService` 建立路徑，不再建立第二個池；Controller 與 `ICrmConnectionPool` 注入契約維持不變。
- 新增 `ConnectionPoolStatsAdapter`。它只映射 `GetStats()`，其餘 raw client API 明確拒絕，且不釋放不由它擁有的 Singleton manager。
- 刪除 `ToolUtility/ConnectionOperations/PooledOrganizationService.cs` 及其已淘汰的測試；新增 `RunCServiceGraphTests.cs` 覆蓋 C1～C4。
- 取捨理由：ToolUtility 不直接依賴 ASP.NET Hosting 套件，因此以 Host 組態提供的環境名稱解析值；不引入新的產品層參數或修改 Program.cs（不在 Run C 白名單）。

### Run C TDD 紅燈原文

```text
RunCServiceGraphTests：4 個測試失敗
System.InvalidOperationException: No service for type 'ToolUtilityNameSpace.Dataverse.IDataverseConnectionManager' has been registered.
System.InvalidOperationException: No service for type 'Microsoft.Xrm.Sdk.IOrganizationService' has been registered.
（失敗原因為 Run C 尚未註冊 Gateway／Manager 服務圖。）
```

### Run C C1～C4 原文

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --filter FullyQualifiedName~RunCServiceGraphTests --no-restore
已通過! - 失敗:     0，通過:     4，略過:     0，總計:     4，持續時間: 89 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

### Run C 品質門檻原文

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤
經過時間 00:00:06.16
```

```text
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 148 ms - ToolUtility.Tests.dll (net10.0)
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
已通過! - 失敗:     0，通過:    23，略過:     0，總計:    23，持續時間: 316 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
失敗!  - 失敗:    22，通過:   305，略過:     0，總計:   327，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
```

MemberInfo 維持既有基線，符合門檻（失敗不超過 22、通過不少於 305）。

```text
ENCODING OK
CRLF OK
```

```text
G5：NO OUTPUT
```

```text
grep -rn "PooledOrganizationService" --include=*.cs . --exclude-dir=obj --exclude-dir=bin | grep -vE ":[0-9]+:\s*(//|///)"
(no output)
git diff --stat HEAD -- SpeechMessageProducts.ChurchReport/Controllers/
(no output)
```

Run C 完成判定：C1、C2、C3、C4 全部通過；Controller 目錄無 diff；PooledOrganizationService 非註解命中為 0。

### Commit

Run C commit：訊息 `refactor(dataverse): 切換為 per-operation Gateway，淘汰 per-request 租約`。

## Run D 停止紀錄

停止條件：**第 2 項——必須修改白名單以外的檔案才能繼續。**

Run D 的完成判定要求移除 `ToolUtilityClass.Core.cs` 的 `m_OrganizationService` 欄位，並使全專案非註解 grep 為 0。但在動手前查核發現下列白名單外檔案仍含有實際程式碼參照或參數宣告；刪除欄位會直接造成編譯錯誤，改名或改走 `m_Crm2011OrganizationService` 也必須修改這些檔案：

```text
ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs:57,76
ToolUtility/ToolUtilityPartials/ToolUtilityClass.List.cs:36,51,54
ToolUtility/ToolUtilityPartials/ToolUtilityClass.Entity.cs:44,136,154
ToolUtility/ToolUtilityPartials/ToolUtilityClass.Core.cs:40,192,196
```

查核原文：

```text
rg -n "OrganizationServiceProxy|m_OrganizationService|_crmConnectionService|_ownsConnection|InitializeCrmConnection|CreateOnPremiseClient" ToolUtility/ToolUtilityPartials ToolUtility --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**'
ToolUtility/ToolUtilityPartials\ToolUtilityClass.ActivityAttachment.cs:57: _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, m_OrganizationService);
ToolUtility/ToolUtilityPartials\ToolUtilityClass.ActivityAttachment.cs:76: _facade.SetAppointmentStatusToScheduled(aActivityId, m_OrganizationService);
ToolUtility/ToolUtilityPartials\ToolUtilityClass.List.cs:36: RetrieveMemberListCollectionByListIdDynamics365(ref OrganizationServiceProxy aOrganizationService, Guid aListId)
ToolUtility/ToolUtilityPartials\ToolUtilityClass.List.cs:51: RetrieveDynamicMemberListDynamics365(OrganizationServiceProxy service, Guid strList)
ToolUtility/ToolUtilityPartials\ToolUtilityClass.List.cs:54: RetrieveDynamicMemberListDynamics365(ref OrganizationServiceProxy service, Guid aListId)
ToolUtility/ToolUtilityPartials\ToolUtilityClass.Entity.cs:44: CreateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeToCreate)
ToolUtility/ToolUtilityPartials\ToolUtilityClass.Entity.cs:136: UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, ref Entity aEntityTobeUpdated)
ToolUtility/ToolUtilityPartials\ToolUtilityClass.Entity.cs:154: UpdateEntityDynamics365(ref OrganizationServiceProxy aOrganizationService, Entity aEntityTobeUpdated)
```

Run D 尚未修改任何檔案、尚未執行清除或提交。依憲章不得自行擴大白名單，因此本任務在此停止，等待白名單允許上述 ToolUtility partial 檔案後再繼續。

### Run D 延續查核（未改動）

```text
目前 Run D 白名單（implement.md:260-270）未包含 ToolUtility/ToolUtilityPartials/ToolUtilityClass.ActivityAttachment.cs。
rg -n "m_OrganizationService" ToolUtility/ToolUtilityPartials --glob '*.cs' --glob '!**/bin/**' --glob '!**/obj/**'
ToolUtility/ToolUtilityPartials\ToolUtilityClass.ActivityAttachment.cs:57: _facade.SetActivityStatusToCompleted(ActivityName, aActivityId, m_OrganizationService);
ToolUtility/ToolUtilityPartials\ToolUtilityClass.ActivityAttachment.cs:76: _facade.SetAppointmentStatusToScheduled(aActivityId, m_OrganizationService);
ToolUtility/ToolUtilityPartials\ToolUtilityClass.Core.cs:40: public OrganizationServiceProxy m_OrganizationService;
ToolUtility/ToolUtilityPartials\ToolUtilityClass.Core.cs:196: try { (m_OrganizationService as IDisposable)?.Dispose(); } catch (ObjectDisposedException) { }
```

若不修改 `ToolUtilityClass.ActivityAttachment.cs`，A2 的全專案非註解 grep 不可能為 0；若保留欄位，Run D 的設計與 A2 又不成立。故停止條件 2 仍成立。

## Run D 結果

### 實作與取捨

- 依更新後的白名單，移除 `ToolUtilityClass` 的兩個自建連線建構式、`InitializeCrmConnection()`、`m_OrganizationService`、`_crmConnectionService` 與 `_ownsConnection`；`Dispose` 僅釋放 Facade，不釋放 gateway 代理、lease、pool、client 或 tracer。
- 依 D-a 的編譯期常數事實，`ActivityAttachment.cs` 的兩個 `CRM_TYPE == "DYNAMICS365"` 恆假分支已整段刪除，保留原本 else 的 `m_Crm2011OrganizationService` 呼叫。此名稱相容欄位現在保存 gateway 代理，不保存 raw client。
- `ToolUtilityFactory` 保留兩個既有 `GetInstance` 簽章，新增 `SetAmbientService`。Factory 單例只保存 `AmbientGatewayOrganizationService`，不保存 `HttpContext`、`RequestServices`、scope、lease 或 raw client；背景呼叫由 ambient 代理自建 scope 並立即釋放。
- `Startup.Configure` 在 `SetTracer` 後，以延遲 delegate 設定 ambient 代理。delegate 每一次操作才讀取 `IHttpContextAccessor.HttpContext?.RequestServices`，不會捕獲某個 request。
- `CrmConnectionPool.cs` 已刪除；Run C 的 `ConnectionPoolStatsAdapter` 仍是唯一的 `ICrmConnectionPool` 相容實作，未在 Run D 修改。
- `WebServiceConnector/**` 與 `RequestProfiler` 的 `m_OrganizationService` 實際使用／字串已清除；每個舊的 fallback 都保留為既有 `m_Crm2011OrganizationService` 呼叫。
- 新增 `ToolUtilityFactoryAmbientGatewayTests`：D1 驗證有 request 時 Factory 操作不會自建 scope；D2 驗證無 request 時建立並釋放一個 fallback scope、Leased 回到 0；D3 驗證 100 次跨 scope 操作後 `Created` 維持熱身基線且只有一個假 client。

### TDD 紅燈原文

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-restore --filter FullyQualifiedName~ToolUtilityFactoryAmbientGatewayTests
ToolUtilityFactoryAmbientGatewayTests.cs(69,28): error CS0117: 'ToolUtilityFactory' 未包含 'SetAmbientService' 的定義
```

### D1～D3 測試原文

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~ToolUtilityFactoryAmbientGatewayTests --logger "console;verbosity=quiet"
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ToolUtility.Dataverse.Tests\bin\Debug\net10.0\ToolUtility.Dataverse.Tests.dll 的測試回合 (.NETCoreApp,Version=v10.0)
總共有 1 個測試檔案與指定的模式相符。

已通過! - 失敗:     0，通過:     1，略過:     0，總計:     1，持續時間: 142 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

### Run D 品質門檻原文

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤

經過時間 00:00:04.59
```

```text
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj --no-build --no-restore --logger "console;verbosity=quiet"
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ToolUtility.Tests\bin\Debug\net10.0\ToolUtility.Tests.dll 的測試回合 (.NETCoreApp,Version=v10.0)
總共有 1 個測試檔案與指定的模式相符。

已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 97 ms - ToolUtility.Tests.dll (net10.0)
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --no-build --no-restore --logger "console;verbosity=quiet"
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ToolUtility.Dataverse.Tests\bin\Debug\net10.0\ToolUtility.Dataverse.Tests.dll 的測試回合 (.NETCoreApp,Version=v10.0)
總共有 1 個測試檔案與指定的模式相符。

已通過! - 失敗:     0，通過:    24，略過:     0，總計:    24，持續時間: 176 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-build --no-restore --logger "console;verbosity=quiet"
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ChurchReport.MemberInfo.Tests\bin\Debug\net10.0\ChurchReport.MemberInfo.Tests.dll 的測試回合 (.NETCoreApp,Version=v10.0)
總共有 1 個測試檔案與指定的模式相符。
[xUnit.net 00:00:00.24]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentViewDefaultsTests.Web_login_flow_persists_contact_id_and_donation_payment_view_uses_it_to_restore_missing_model_state [FAIL]
[xUnit.net 00:00:00.24]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_booking_workflow [FAIL]
[xUnit.net 00:00:00.24]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_dedication_fee_form_refresh [FAIL]
[xUnit.net 00:00:00.25]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_payment_model_assembly [FAIL]
[xUnit.net 00:00:00.25]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_contact_mapping_to_contact_service [FAIL]
[xUnit.net 00:00:00.25]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_key_in_dedication_workflow [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentNamingCompatibilityTests.New_payment_return_controller_exists_after_rename [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.PaymentPostPaymentArchitectureTests.ChurchReport_specific_handlers_do_not_move_to_reusable_workflow_project [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.PaymentProductServiceNamingTests.Product_payment_services_use_provider_neutral_names [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_payment_result_helper_and_debug_logger_exist [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentManagerNamingTests.Legacy_qpay_manager_remains_as_compatibility_alias [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentProcessorNamingTests.New_donation_payment_processor_exists_as_primary_product_workflow_processor [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Donation_payment_form_model_is_the_primary_churchreport_form_state_type [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnWorkflowNamingTests.New_donation_payment_workflow_result_exists_after_rename [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentManagerNamingTests.New_donation_payment_manager_exists_as_primary_ui_payment_state_manager [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_donation_fee_payment_processor_exists_as_primary_fee_return_processor [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Provider_specific_names_are_confined_to_provider_code_or_legacy_route_templates [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentProcessorNamingTests.Donation_payment_processor_constructors_require_neutral_gateway_create_adapter [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentReturnProcessorNamingTests.New_recurring_donation_payment_processor_exists_as_primary_recurring_return_processor [FAIL]
[xUnit.net 00:00:00.29]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentFormModelNamingTests.Product_layer_file_names_should_not_contain_provider_brand_names [FAIL]
[xUnit.net 00:00:00.30]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_donation_login_contact_workflow [FAIL]
[xUnit.net 00:00:00.30]     ChurchReport.MemberInfo.Tests.Payments.DonationPaymentServiceExtractionTests.DonationPaymentManager_should_delegate_contact_creation_numbering_workflow [FAIL]

失敗!  - 失敗:    22，通過:   305，略過:     0，總計:   327，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
```

MemberInfo 的 22 個失敗／305 個通過與 Run A～C 基線相同，符合本任務門檻；其失敗是既有 Payments 命名／repository-root 測試，未因 Run D 惡化。

### A1、A2、G4、G5 原文

```text
--- A2：PowerShell 等價（排除註解） ---
NO OUTPUT
--- A1 ---
NO OUTPUT
--- G5 ---
NO OUTPUT
--- G4：刪除檔案排除後 ---
ENCODING OK
CRLF OK
```

原計畫的 Unix `grep`／heredoc 指令在此 Windows 工作樹不可執行：`bash` 不存在，且 G4 範例會嘗試讀取 Run D 已刪除的 `CrmConnectionPool.cs`。依錯誤訊息查明原因後，以 PowerShell + `rg` 的等價 A1/A2/G5 查核、並在 G4 加上 `os.path.exists` 排除已刪檔；查核邏輯與原門檻相同，且涵蓋未追蹤的新測試檔。

### G3 與組件歸屬查核

- `ToolUtilityFactory`、`ToolUtilityClass.Core`、`ActivityAttachment` 與新增測試均補足或維持繁體中文 XML／資源生命週期說明；Startup 的組合根設定註解明載不捕獲 request 狀態與 fallback scope 的確定性釋放。
- G5 為 `NO OUTPUT`：ChurchReport 除組合根既有設定外沒有新增任何 `IDataverseGateway`、Manager、Pool、Lease 或 Key 型別；所有新／核心實作仍在 `ToolUtility`。

### 範圍外發現

- `ToolUtilityClass.Core.cs` 仍保留未使用的私有 `CrmConnection` 設定 getter（包括歷史預設值）。既有 `CreateOnPremiseClient` 路徑已完全移除，但本 Run 未為整理私有設定而擴大範圍。

### Run D 重新驗證（提交前原文）

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤
經過時間 00:00:02.93
```

```text
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 73 ms - ToolUtility.Tests.dll (net10.0)
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
已通過! - 失敗:     0，通過:    24，略過:     0，總計:    24，持續時間: 168 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
失敗!  - 失敗:    22，通過:   305，略過:     0，總計:   327，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --filter FullyQualifiedName~ToolUtilityFactoryAmbientGatewayTests
已通過! - 失敗:     0，通過:     1，略過:     0，總計:     1，持續時間: 71 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
--- A2：PowerShell 等價（排除註解） ---
NO OUTPUT
--- A1 ---
NO OUTPUT
--- G5 ---
NO OUTPUT
--- G4 ---
ENCODING OK
CRLF OK
--- git diff --check HEAD ---
(no output)
```

Run D 重新驗證維持 0 build error／0 build warning、ToolUtility 63 綠、Dataverse 24 綠、MemberInfo 22 失敗／305 通過基線；D1～D3、D4、A1、A2、G4、G5 均符合門檻。CCG 審查由 Gemini 完成且無 Critical；Claude runner 兩次均無可用輸出，未宣稱雙模型審查完成。審查唯一 Warning 為 Core.cs 未使用的歷史設定 getter，屬本 Run 範圍外清理，不修改。

## Run E 結果

### 實作與取捨

- 在 base、Development、Production 三個 `appsettings*.json` 都建立 `Dataverse:Pool`，完整外部化 `MinSize`、`MaxN`、`AcquireTimeout`、`IdleTimeout`、`HealthInterval`。Development 保持較小容量以加速本機測試；Production 保留舊池的 5／30 容量並補上未曾外部化的 `HealthInterval`。
- 已刪除 `appsettings.Production.json` 孤立的舊 `ConnectionPool` 區段，新的 `Dataverse:Pool` 是唯一 pool 調校來源；base `CrmConnection` 中的連線端點／認證相容設定沒有變動。
- 新增 `docs/architecture/dataverse-gateway-v1.md`，逐格對照圖上 ①～⑩、實作型別、生命週期邊界與對應測試，並說明產品 B/C/D 的安全複用規則。
- `prd.md` 的 A1～A14 已逐條標記達成並連結實際命令／測試證據；A15 仍明確標為等待人工回歸。
- 本 Run 沒有新增或實質修改 `.cs`；G3 的繁中 XML 文件檢查不適用，既有 Run A～D 的程式文件驗證維持有效。

### Run E 品質門檻原文

```text
dotnet build SpeechMessageProducts.sln -c Debug
建置成功。
    0 個警告
    0 個錯誤
經過時間 00:00:11.40
```

```text
dotnet test ToolUtility.Tests/ToolUtility.Tests.csproj
已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 160 ms - ToolUtility.Tests.dll (net10.0)
```

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj
已通過! - 失敗:     0，通過:    24，略過:     0，總計:    24，持續時間: 737 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj
失敗!  - 失敗:    22，通過:   305，略過:     0，總計:   327，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
```

MemberInfo 的 22 個失敗／305 個通過與本任務基線相同；失敗仍是既有 Payments 命名／repository-root 測試，不因本 Run 的組態與文件變更而惡化。ToolUtility test 輸出的既有 NU1701／nullable warnings 未出現在 solution build；G1 所要求的 solution build 仍為 0 warnings／0 errors。

```text
ConvertFrom-Json（base／Development／Production）
BASE JSON OK
DEVELOPMENT JSON OK
PRODUCTION JSON OK

Dataverse:Pool 五個鍵檢查
base POOL KEYS OK
development POOL KEYS OK
production POOL KEYS OK
PRODUCTION LEGACY CONNECTIONPOOL ABSENT
```

```text
A1 ToolUtilityClass CreateOnPremiseClient
NO OUTPUT
A2 m_OrganizationService non-comment
NO OUTPUT
A3 PooledOrganizationService non-comment
NO OUTPUT
G5 ChurchReport type ownership
NO OUTPUT
G4
ENCODING OK
CRLF OK
git diff --check HEAD
(no output)
```

### 範圍外發現

- Run D 驗證期間的 CCG runner 由外部流程建立並被外部提交 `62e05a60` 一併納入 `.ccg/dual-model-runs/**` 與 `.ccg/tasks/**/.turns.json`；這些不屬 Run D 白名單。本 Run 沒有再加入、修改、暫存或提交 CCG 狀態。因該提交已存在於 `origin/feat/dataverse-scoped-connection`，未自行重寫共享分支歷史。
- `ToolUtilityClass.Core.cs` 的未使用歷史設定 getter 仍保留，見 Run D 範圍外發現；本 Run 沒有擴大清理。

## 本任務結案

### 圖 ①～⑩ 的完成狀態

| 圖上格 | 狀態 | 實作 |
|---|---|---|
| ① | 完成 | `ToolUtilityClass`／`ToolUtilityFacade` 維持既有 API，DI 取得 gateway 代理。 |
| ② | 完成 | Scoped `GatewayOrganizationService` 與 `IDataverseGateway`。 |
| ③ | 完成 | `IClientLease`、`DataverseConnectionManager`、`DataversePoolMetrics`。 |
| ④ | 完成 | Keyed `BoundedClientPool` 與 `PooledClient` 狀態機。 |
| ⑤ | 完成 | `DataverseConnectionKey` 的四段隔離鍵。 |
| ⑥ | 完成 | Scoped `DataverseGateway` reentrant depth。 |
| ⑦ | 完成 | 每次最外層 CRM 操作取得／歸還 lease，例外 fault eviction。 |
| ⑧ | 完成 | `DataversePoolOptions` + 三份 `Dataverse:Pool` 五參數組態。 |
| ⑨ | 完成 | 移除 legacy 自建連線、raw proxy 死碼，Factory 改 ambient gateway。 |
| ⑩ | 完成 | Manager 以 Product／Environment／URL／服務帳號解析 key。 |

### A1～A15 最終狀態

`prd.md` 的 A1～A14 均已達成且附上實際證據；A15 是唯一保留的人工工作，不是 agent 完成條件。人工回歸請使用 `.trellis/tasks/08-17-toolutility-scoped-lifetime/regression-checklist.md`。

### Run Commit 紀錄

| Run | Commit |
|---|---|
| A | `7094489cc` — `feat(dataverse): 新增 Keyed Bounded Pool 與 Lease 型別契約` |
| B | `78d9bf38c` — `feat(dataverse): 新增 ConnectionManager、Gateway 與 per-operation 代理` |
| C | `0cf0fdf2b` — `refactor(dataverse): 切換為 per-operation Gateway，淘汰 per-request 租約` |
| D | `62e05a60c` — 外部流程提交的 legacy 路徑清除（驗證結果見 Run D）。 |
| E | 本節所在的 `docs(dataverse): Gateway 架構 v1 收斂與驗收紀錄` 提交；其最終 hash 由提交後的 `git log -1` 交付。 |

結論：Run A～E 的程式、組態、架構文件與自動驗收均已收斂；等待使用者執行人工登入／CRM 回歸清單。

## Run F 結果

### 實作與隔離決策

- F1：`PooledClient.ReturnHealthy()` 在狀態轉為 `Idle` 前，僅以既有組件參考的 `OnPremiseClient` 型別檢查處理 `CallerId`。它將值重設為 `Guid.Empty` 並回讀確認；任何例外或不一致一律 fail-closed，將 client 標為 Faulted 後由 pool 淘汰。未使用 `dynamic`，也沒有新增對 PowerPlatform 的專案參考。
- F2：cleanup 先在子池鎖內選出候選項，鎖外才 Dispose。若在兩者之間被 Acquire 成功租借，`DisposeUnderlying()` 拒絕中斷 lease、記錄「歸還時淘汰」，而 `ReturnHealthy()` 會將它轉為 Faulted 讓 pool 確定性釋放。shutdown 時仍由相同歸還路徑處理已租借的 client。
- F3：cleanup 改成每選取一條過期 idle client 就遞減局部 idle 計數，因此任何子池都不會被淘汰到 `MinSize` 以下。
- F4：Manager 的 `CrmConnection:ServerUrl` 與 `CrmConnection:Username` 改為必要組態；缺漏時擲出包含設定鍵的 `InvalidOperationException`，不再靜默回退到硬編碼主機或 `service-account`。

### TDD 紅燈原文

首次加入 cleanup/acquire 交錯測試時，尚未提供控制交錯的建構式注入點：

```text
error CS1729: 'BoundedClientPool' 未包含使用 4 個引數的建構函式
```

補齊注入點後，歸還淘汰的契約先以單一失敗測試確認目前實作確實漏做：

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --filter FullyQualifiedName~Cleanup_does_not_dispose_client_leased_after_selection --no-restore
[xUnit.net 00:00:00.15]     ToolUtility.Dataverse.Tests.BoundedClientPoolTests.Cleanup_does_not_dispose_client_leased_after_selection [FAIL]
  失敗 ToolUtility.Dataverse.Tests.BoundedClientPoolTests.Cleanup_does_not_dispose_client_leased_after_selection [39 ms]
  錯誤訊息:
   Assert.True() Failure
Expected: True
Actual:   False
  堆疊追蹤:
     at ToolUtility.Dataverse.Tests.BoundedClientPoolTests.Cleanup_does_not_dispose_client_leased_after_selection() in D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree\ToolUtility.Dataverse.Tests\BoundedClientPoolTests.cs:line 214

失敗!  - 失敗:     1，通過:     0，略過:     0，總計:     1，持續時間: 39 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

### 綠燈與品質門檻原文

```text
dotnet test ToolUtility.Dataverse.Tests/ToolUtility.Dataverse.Tests.csproj --filter FullyQualifiedName~Cleanup_does_not_dispose_client_leased_after_selection --no-restore
已通過! - 失敗:     0，通過:     1，略過:     0，總計:     1，持續時間: 39 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet build SpeechMessageProducts.sln -c Debug

建置成功。
    0 個警告
    0 個錯誤

經過時間 00:00:04.14
```

```text
dotnet test ToolUtility.Tests

已通過! - 失敗:     0，通過:    63，略過:     0，總計:    63，持續時間: 230 ms - ToolUtility.Tests.dll (net10.0)
```

```text
dotnet test ToolUtility.Dataverse.Tests

已通過! - 失敗:     0，通過:    29，略過:     0，總計:    29，持續時間: 541 ms - ToolUtility.Dataverse.Tests.dll (net10.0)
```

```text
dotnet test ChurchReport.MemberInfo.Tests

失敗!  - 失敗:    22，通過:   305，略過:     0，總計:   327，持續時間: 1 s - ChurchReport.MemberInfo.Tests.dll (net10.0)
```

`ToolUtility.Tests` 的完整終端輸出仍包含既有的 NU1701、重複 using 與 nullable warnings；solution build 為本 Run 的零警告門檻，已實際通過。本 Run 沒有修改該專案。MemberInfo 的 22 個失敗／305 個通過與既有基線相同，且本 Run 未修改 ChurchReport 原始碼。

### 範圍、編碼與文件稽核原文

```text
git diff --stat HEAD -- SpeechMessageProducts.ChurchReport/
(no output)

git diff --check HEAD
(no output)

ENCODING OK
CRLF OK
```

本 Run 的變更僅限 `ToolUtility/Dataverse/PooledClient.cs`、`ToolUtility/Dataverse/BoundedClientPool.cs`、`ToolUtility/Dataverse/DataverseConnectionManager.cs`、`ToolUtility.Dataverse.Tests/BoundedClientPoolTests.cs`，以及交付要求的本檔與 `docs/architecture/dataverse-gateway-v1.md`。四個變更的 C# 檔案均已重新以 UTF-8 without BOM、CRLF 與最終 CRLF 寫入，並補足 CallerId、Pool、Lease、Timer、Dispose、跨 request 隔離與測試 double 的繁體中文生命週期文件。

`.ccg/tasks/design-four-product-dataverse-connection-architecture/.turns.json` 在 Run F 開始前即已由外部流程變更；本 Run 未讀寫、暫存或提交它。

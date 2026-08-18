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

Run A commit：待建立，訊息 `feat(dataverse): 新增 Keyed Bounded Pool 與 Lease 型別契約`。

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

Run B commit：待建立，訊息 `feat(dataverse): 新增 ConnectionManager、Gateway 與 per-operation 代理`。

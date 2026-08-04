# P5 Dedicated Gateway 對齊實作計畫

> 本文件只規劃實作順序；必須先由使用者確認 `prd.md` 與 `design.md`，再以 `task.py start` 轉入實作階段。

## 影響檔案地圖

| 類別 | 檔案 | 責任 |
| --- | --- | --- |
| 新增 | `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileRuntime.cs` | 供 Embedded/Dedicated 重用的 Data8 resolver/admission/router/pool lifecycle owner。 |
| 修改 | `SpeechMessageProducts.ChurchReport/Services/EmbeddedData8Runtime.cs` | 縮減為 ChurchReport configuration mapper + shared runtime composition，移除重複生命週期。 |
| 新增 | `SpeechMessage.Dynamics.Gateway/DedicatedGatewayOptions.cs` | 不可由 request 改寫的 Dedicated host scalar options 與 fail-closed validation。 |
| 新增 | `SpeechMessage.Dynamics.Gateway/DedicatedData8RuntimeHostedService.cs` | Gateway DI 的 runtime owner；host stop 時 await runtime disposal。 |
| 修改 | `SpeechMessage.Dynamics.Gateway/Program.cs` | 依 host mode 註冊 Data8 Dedicated executor、In-Memory coordinator，使用 Dedicated request origin。 |
| 修改 | `SpeechMessage.Dynamics.Gateway/appsettings.Development.json` | 只加入開發 Dedicated profile/catalog/credential reference 的安全範例與 localhost contract，不放真實秘密。 |
| 修改 | `SpeechMessageProducts.ChurchReport/appsettings.Development.json` | 提供 DedicatedGateway localhost profile 範例，保留 Embedded 切換說明。 |
| 修改 | `SpeechMessage.Dynamics.Gateway/Properties/launchSettings.json` | 維持單一 HTTPS 7244 啟動 profile。 |
| 新增/修改 | `SpeechMessage.Dynamics.Tests/*Dedicated*Tests.cs` | runtime、host mode、HTTP request origin、cleanup 與 fail-closed tests。 |
| 修改 | `ChurchReport.MemberInfo.Tests/*Gateway*Tests.cs` | ChurchReport Dedicated configuration、preflight 與 process-host disposal tests。 |
| 修改 | `docs/dynamics-connection-management-plan.md`、`docs/dynamics-gateway-central-local-82-91-guide.zh-TW.md` | Visual Studio Multiple startup projects 與回滾說明。 |

## 實作順序

### 1. 先建立失敗測試：shared runtime contract

- 新增 `Data8ProfileRuntimeTests`，測試：Data8-only profile 可建立；non-Data8、disabled org、缺 ServiceUri、invalid credential reference 均在配置期失敗，且未建立 pool/client/permit。
- 加入故障注入 factory，驗證 runtime constructor rollback、dispose 時 pool 先於 admission、所有 cleanup failure 聚合。
- 執行 focused test，預期因 `Data8ProfileRuntime` 尚不存在而失敗。

### 2. 實作 shared `Data8ProfileRuntime`

- 在 `SpeechMessage.Dynamics.Connectors.Data8` 建立 `IAsyncDisposable` runtime，從 immutable profile/catalog snapshot 建構 resolver、single-host In-Memory admission、pool registry 和 executor。
- 使用 P3 的 `Data8ConnectorPoolRegistry`/`Data8ProfileOperationExecutor`，不可複製 lease/pool/client cleanup。
- 將 admission plan 建構與 Organization.svc root canonicalization 置於 shared runtime；公開安全 executor，不公開 credential、endpoint、client 或 pool internals。
- 將 Embedded runtime 改為委派 shared runtime，保留 ChurchReport mapper 與其 `IServiceProvider` ownership。
- 重跑 shared runtime 與既有 Embedded focused tests。

### 3. 先建立失敗測試：Dedicated host mode

- 新增 Gateway host options tests：Dedicated 只接受 `DedicatedGateway` deployment mode、Data8 profile、HTTPS localhost development policy；缺 profile/catalog/ServiceUri/credential、Official Worker 或 SQL coordinator 選項均在 startup 前拒絕。
- 新增 API test：Dedicated configuration 的 POST handler 使用 `RequestOrigin.DedicatedGateway`，且保留 principal binding、reserved parameter、HTTP/non-loopback 與 no-store 保護。
- 執行 focused tests，預期因 Dedicated host options/runtime registration 尚不存在而失敗。

### 4. 實作 Dedicated Gateway composition root

- 新增 `DedicatedGatewayOptions` 與 `DedicatedData8RuntimeHostedService`；使用 Generic Host 生命週期作為唯一 runtime owner。
- 修改 `Program.cs`：Dedicated mode 建立 shared Data8 runtime，將 executor 註冊到 API pipeline，使用 InMemory coordinator 並省略 SQL/Official Worker registration；Central/Official 既有路徑不在 P5 改寫。
- 將 request guard origin 由 deployment-owned mode 決定，絕不可從 request/header/query/body 取得。
- Gateway stop/startup exception 時 await dispose；不得留下 background task、timer、permit、client、handler 或 service provider。
- 重跑 Gateway focused tests。

### 5. ChurchReport Dedicated F5 設定與測試

- 在 Development JSON 加入明確、可切換的 DedicatedGateway localhost 範例，產品只保有 mode/alias/endpoint；不新增 CRM URL、Organization ID、connector 或 credential。
- 擴充 ChurchReport preflight/lifecycle tests：Dedicated endpoint invalid fail-closed、gateway unavailable bounded failure、process-host reuse/disposal 和 Embedded 不讀 endpoint。
- 更新 Visual Studio 多啟動專案文件：Gateway HTTPS profile 先於 ChurchReport；產品不啟動 child process；停 Gateway 或改回 Embedded 是回滾方式。

### 6. 完整品質驗證

依序執行：

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~Data8ProfileRuntime|FullyQualifiedName~DedicatedGateway" --no-restore --nologo
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~DynamicsGateway|FullyQualifiedName~DonationDynamicsAccess" --no-restore --nologo
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --no-restore --nologo
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --nologo
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo
```

- 對所有新增或實質修改 `.cs` / `.cshtml` 作位元組層級 UTF-8 無 BOM、CRLF-only、final CRLF 驗證。
- 執行 `git diff --check`。
- 不啟動外部 CE、SQL、IIS、DNS、ADFS、IFD 或 Web API 真機操作。

## 回滾點

1. shared runtime 抽取後先確認 P4 Embedded tests 仍全綠；若否，回復到抽取前的 Embedded composition，勿同時更動 Gateway。
2. Gateway Dedicated startup 驗證失敗時，保持現有 Central/Official 程式不變並拒絕 Dedicated 模式；不得 fallback 到 Official 或 Embedded。
3. ChurchReport 切回 `ConnectionMode=Embedded` 即可撤回 Dedicated 產品路徑；runtime 由 host stop 的 DI disposal drain。

## P6 交接

不在 P5 發送真實 CE 呼叫。完成 P6 程式與離線驗證後，才執行一次受控跨模式真機量測。

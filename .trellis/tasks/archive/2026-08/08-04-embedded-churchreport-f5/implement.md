# P4：ChurchReport Embedded F5 實作計畫

## TDD 順序

1. 在 `SpeechMessage.Dynamics.Tests` 撰寫 Embedded DI／Adapter 行為測試，先確認失敗：Embedded 無 endpoint 可註冊、非 Embedded mode 拒絕、保留參數在 admission 前拒絕、相同受控 executor 被呼叫一次。
2. 撰寫 profile／organization 隔離、取消、deadline、drain、dispose 的測試，確認 P3 lifecycle invariants 經 Embedded 保留。
3. 最小化重寫 `EmbeddedServiceCollectionExtensions`，新增 stateless `EmbeddedHostAdapter` 並建立必要 project references。
4. 於 ChurchReport composition root 接入 Embedded 選擇，將 Development mode 設為 `Embedded`／`sunnyvalechback`；feature flag 維持 false。
5. 補結果一致性與 microbenchmark 測試／記錄，執行 focused P4、完整 Dynamics、Release build 與位元組編碼檢查。
6. P4.1 先以測試固定「alias → Catalog GUID／CE 版本」與 Disabled fail-closed 合約，再將 D365 8.2 實機清單登錄至
   `CrmConnection:OrganizationCatalog`；未提供 ServiceUri 的 entry 不可建立連線。
7. 將 `LiveEmbeddedDynamicsComparisonTests` 保留為 opt-in 的 P6 後整合量測工具；P4 不填入真實密碼、不連 CE，
   不以略過的真機測試阻擋 P5／P6 程式實作。P6 完成後才由使用者安排同一組織的 legacy／Embedded／Dedicated
   結果一致性與 p50／p95／p99 測量。

## 驗證指令

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~Embedded" --nologo
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --nologo
dotnet build .\SpeechMessageProducts.sln --configuration Release --nologo
```

## 回滾

將 ChurchReport development `ConnectionMode` 改回 `DedicatedGateway`，並移除 Embedded DI registration 即可回到既有 HTTP 路徑；P3 pool、Data8 與 ToolUtility legacy pool 均不修改。

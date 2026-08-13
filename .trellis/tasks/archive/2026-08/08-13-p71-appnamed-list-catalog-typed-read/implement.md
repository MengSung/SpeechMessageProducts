# P7.1 App-named 名單目錄強型別讀取能力實作計畫

## 前置與審查狀態

- 已讀取 AGENTS.md、P5–P8 parent、authoritative matrix、legacy source、P7.1 dedication-booking precedent、
  backend isolation/connector/hosting specs 與 shared review guides。
- CCG self-healing architect run 已啟動並遵守 45 秒等待上限；未得到可用完整輸出，記錄為「雙模型未完成」。
  本 child 依 local repository evidence 前進，不能把此狀態宣稱為雙模型通過。
- 所有 feature gate 維持 false；禁止 CE、fixture、consumer cutover、P7.5、P8、push 或 PR。

## 工作分片與 TDD 順序

1. **RED：registry/matrix/response contract**
   - 建立 `AppNamedListCatalogReadRegistryTests.cs`，先以尚不存在的 operation/response branch/wire type 寫出
     `ORG-CALL-00014` exact ID/template/zero-parameter/response/bounds agreement、union uniqueness 和 matrix
     policy assertions。
   - 更新 matrix/schema 所需的 exact response kind/template hash policy，讓 RED 失敗只表示 compiled
     capability 尚不存在，而非 JSON syntax/CE failure。
2. **GREEN：abstraction and registry**
   - 修改 `OperationIds.cs`、`OperationResponseData.cs`、`Package01OperationRegistry.cs`，新增 immutable record、
     response discriminator/branch/factory、zero-parameter registry definition；以 definition hash 回寫 matrix。
   - 只以 constructor defensive copy 設計，無 dictionary、Entity、cache 或 raw response 逃逸。
3. **RED：Data8 query/projection**
   - 在 registry test／Data8 executor factory test 先寫 expected fixed QueryExpression：五欄、三條 filter、
     descending name+ascending ID、128 row page、bounded RetrieveMultiple only；並寫 pool-before-input no-I/O
     contract、null/invalid ID/response budget failure case。
4. **GREEN：Data8 connector and executor validation**
   - 修改 `Package01Data8ReadOperations.cs` 與 `Data8ProfileOperationExecutor.cs`，增加 allowlisted dispatch、
     strict no-parameter validation、projection、paging/byte accumulation 和 response validation。
   - 使用既有 `OnPremiseData8ConnectorClientFactoryTests` fake service，驗證 query/projection without CE。
5. **RED：ProductClient boundary**
   - 新建 `AppNamedListCatalogRecordDto.cs`、`IAppNamedListCatalogReadClient.cs`、
     `AppNamedListCatalogReadClient.cs` 對應 tests，先證明 exact request/token、wrong operation/branch fail closed、
     source collection mutation isolation、A/B interleaving、invalid routing input no-I/O。
6. **GREEN：ProductClient and DI**
   - client 只建立 request-local empty parameter map 和 DTO snapshots，DI 沿用 stateless existing registration
     pattern；不得將 client 接到 ChurchReport consumer 或 setting/gate。
7. **Integration matrix/update and check**
   - matrix 更新只反映 registry/executor/client implementation；authoritative gap matrix 仍註明 consumer/CE/host
     pending。append planning/check evidence、scope-only parent status、task metadata。

## 驗證命令

1. 每個 RED/Green 循環：
   ```powershell
   dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AppNamedListCatalog"
   ```
2. capability 完成後：
   ```powershell
   dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore
   dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore
   dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
   git diff --check
   python .\.trellis\scripts\task.py validate 08-13-p71-appnamed-list-catalog-typed-read
   ```
3. 對 child-owned `.cs` 執行 UTF-8 no-BOM/CRLF/final-CRLF byte scan；對 type path 執行 forbidden scan，
   禁止新 `EntityCollection`、`ToolUtility`、`IOrganizationService` 外洩、`Retrieve(`、retry、cache、
   `GetAwaiter().GetResult()`、gate true 或 CE dispatch。
4. 完成 diff 後以 `Start-CcgDualModelRun.ps1` 啟動 reviewer，最多等待 45 秒。沒有雙模型可用輸出即記錄
   「雙模型未完成」，不重試等待；本機 tests/build/byte/scope evidence 照常決定 quality gate。

## 回復點

- abstraction/registry 只在 local code；未接 consumer，rollback 為 scope-only revert。
- connector/project test 失敗時停止在本 child；不得改用 generic query、Entity bridge、legacy fallback 或 CE。
- 若任何 cleanup/lease/response uncertainty 出現，維持 gate false，修正 local code/tests 再做本機驗證；
  不把不確定結果推進成 host/CE/cutover evidence。

# P7.1 app-named 小組名單目錄強型別讀取實作計畫

## 實作順序

1. 完成 matrix、legacy fixed template、ChurchReport caller/cache inventory 與 CCG architect analysis；確認 consumer 不在本 child、無 CE/feature/traffic/P7.5/P8 scope。
2. RED：為 `ORG-CALL-00065` 新增 registry/matrix response contract tests；確認缺少 ID/branch/template 而失敗。
3. GREEN layer 1：新增 operation ID、response kind、immutable wire record、response factory、registry definition、matrix schema agreement；不可修改 `ORG-CALL-00014` contract。
4. RED/GREEN layer 2：新增 Data8 fixed query、strict empty parameter rejection、leader lookup scalar projection、bounded page/cumulative bytes、executor allowlist/validation 與 fake-service query tests。
5. RED/GREEN layer 3：新增 ProductClient DTO/interface/client/DI 與 mapping、wrong response/cancel/zero-I/O/source-mutation/A-B tests；不新增 ChurchReport dependency、cache 或 feature setting。
6. 更新 task records 和 matrix local states，執行 targeted 與 full quality gates、bounded external review、scope-only commit/archive。

## 品質與停止點

- 每一 layer 必先觀察 RED，再寫最小 GREEN code；任何 shared state、SDK graph escape、operation ambiguity、response mismatch、bound failure 或 test failure 一律 fail closed。
- 外部模型最多等待 45 秒；若未產出可用結果，記錄「雙模型未完成」，改採本機檢查，不重試等待。
- 不允許本 child 執行 CE、fixture、write、feature enablement、traffic switch、P7.5 removal 或 P8 deployment。

## 驗證命令

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AppNamedSmallGroupListCatalog"
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore
dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
python .\.trellis\scripts\task.py validate 08-13-08-13-p71-appnamed-smallgroups-list-catalog-typed-read
git diff --check
```

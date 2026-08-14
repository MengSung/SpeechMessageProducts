# P7.4 點名名單成員唯讀資料平面實作計畫

## 實作順序

1. 完成 matrix/legacy caller/既有 list-catalog code reuse audit，並記錄 45 秒 CCG architect result。
2. RED：新增 `AppNamedMembershipReadRegistryTests`，確認 operation ID、template、single contact parameter、
   closed response branch 和 bounds 尚不存在。
3. GREEN layer 1：新增 operation ID、response kind、wire record、response factory 和 registry definition；
   更新 operation response agreement tests。
4. RED/GREEN layer 2：新增 `AppNamedMembershipData8Tests` 及 Data8 fixed query/projection/order/bounds code；
   測試 invalid GUID zero-I/O、MoreRecords、duplicate and malformed row fail closed。
5. RED/GREEN layer 3：新增 ProductClient request/DTO/interface/DI and tests；驗證 routing before I/O、exact
   response branch、cancellation forwarding、readonly defensive copy、A/B isolation and no fallback。
6. 更新 task/CCG/parent records；執行 targeted/full verification、bounded review、scope-only commit/archive。

## 品質與停止點

- 每一 production layer 均先觀察 targeted test RED，再寫最小 GREEN code。
- 不修改 ChurchReport、ToolUtility、settings、feature gates、CE、fixture、traffic、P7.5 或 P8。
- CCG runner 每 backend 最多等待 45 秒、最多一次；若沒有 usable output，記錄「雙模型未完成」並使用本機 evidence，
  不重試等待。
- Any input/response/bound/isolation/lifecycle failure must fail closed; no retry or legacy fallback is allowed.

## 驗證命令

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~AppNamedMembership"
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore
dotnet test .\SpeechMessageProducts.sln --configuration Release --no-restore
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
python .\.trellis\scripts\task.py validate 08-14-p74-appnamed-membership-read-data-plane
git diff --check
```

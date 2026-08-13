# P7.4 認證聯絡人唯讀安全邊界實施計畫

## 執行順序

1. 盤點既有 operation/registry、Data8 query builder、ProductClient DI 與測試慣例；
   不修改既有 consumer。
2. 先新增 failing tests：operation IDs、wire/DTO secret rejection、LINE cardinality、
   disabled bootstrap ordering、cancellation forwarding、A/B request isolation。
3. 執行 focused tests 確認測試因缺少新契約而失敗（RED），記錄結果。
4. 實作最小 registry/wire/executor/ProductClient/disabled bootstrap；只使用固定查詢
   與 allowlisted columns，拒絕明文 password 欄位。
5. 重新執行 focused tests（GREEN），再跑 ChurchReport/Dynamics 受影響 tests。
6. 執行 Release build、encoding/CRLF byte scan、`git diff --check`、scope review。
7. 依 AGENTS.md 使用 CCG self-healing runner 做一次雙模型審查；45 秒上限，quota 時記錄
   degraded single-model fallback。
8. Trellis Check、必要 spec update、scope-only commit、archive；不修改無關 `.turns.json`。

## 明確禁止

- 不執行 CE、feature gate enablement、traffic、P7.5、P8、P9/P10。
- 不把 password、password hash、token、cookie 或 raw CRM entity 放入 wire/DTO/log。
- 不使用 `.Result`、`GetAwaiter().GetResult()`、Task.Run 包裝同步 CRM 以假裝 async，
  不做 legacy fallback 或 retry。

## 驗證命令

```powershell
dotnet test .\SpeechMessageProducts.Dynamics.Tests\SpeechMessageProducts.Dynamics.Tests.csproj --configuration Release --no-restore
dotnet test .\SpeechMessageProducts.ChurchReport.Tests\SpeechMessageProducts.ChurchReport.Tests.csproj --configuration Release --no-restore
dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
git diff --check
```

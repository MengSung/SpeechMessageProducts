# P7.4 認獻單讀取 disabled boundary 實作紀錄

## 執行順序

- [x] 以 bootstrap fail-first tests 固定 base gate、sub-gate、空 ProfileAlias、injected client
  與 host-less disabled state 的行為。
- [x] 實作 gate／factory，並在 appsettings、Development appsettings、DedicatedGateway launch profile
  將新 gate 保持 false。
- [x] 以 service／result fail-first tests 固定空 contact、固定 workload、cancellation forwarding、
  null／invalid DTO row 與 immutable publication。
- [x] 實作 async DTO-only service、immutable result／row 與 explicit model adapter；未修改同步
  `FillBookingList`，未引入 sync-over-async bridge。
- [x] 以 adapter tests 固定成功原子 replace、fault／cancellation 不修改舊 list，並以 interleaved
  A/B contact markers 驗證 request-local isolation。
- [x] 加入三種 host route 與 Embedded allowlist source contract tests；確認 service／adapter 沒有
  executable `.Result`、`.GetAwaiter().GetResult()`、`RetrieveEntity`、`ToolUtility`、
  `EntityCollection` 或 CRM entity construction。
- [x] 完成 focused tests、完整 ChurchReport／Dynamics test projects、Release build、位元組 encoding
  檢查、`git diff --check` 與 CCG review。

## 實際驗證命令與結果

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~DonationDynamicsAccessBootstrapLifecycleTests|FullyQualifiedName~DonationBookingReadServiceTests|FullyQualifiedName~DonationBookingReadBoundaryContractTests"
# 33 passed, 0 failed, 0 skipped

dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --configuration Release --no-restore
# 612 passed, 0 failed, 14 skipped (既有且明確標示的 live evidence tests)

dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --configuration Release --no-restore
# 753 passed, 0 failed, 7 skipped (既有且明確標示的 live SQL tests)

dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore
# 0 warnings, 0 errors

git diff --check
# exit 0
```

受影響五個 C# 檔案另以 strict UTF-8 decoder 及位元組掃描確認：UTF-8 無 BOM、零 lone LF、
零 lone CR、final CRLF。所有新設定仍為 false；本 child 沒有 CE request、mutation、fixture、
traffic switch 或 cleanup operation。

## 回復點與後續限制

- 任一 local contract 失敗時，回復本 child 的未提交程式碼；不得以改變 legacy 行為或開 gate 迴避。
- CCG 的 Claude reviewer 因 provider quota 無輸出，Gemini 成功輸出；這是 degraded single-model
  fallback，不是完整雙模型審查。Gemini 的 BOM finding 已用 byte-level evidence 反證；其 source-test
  脆弱性 warning 已接受為本機 static-composition contract 的刻意限制。
- capacity、CE parity 或 deployment evidence 缺失時，gate 維持 false；這不阻擋下一個獨立的
  P7.4 local-only child，但阻擋 P7.5 與 P8。

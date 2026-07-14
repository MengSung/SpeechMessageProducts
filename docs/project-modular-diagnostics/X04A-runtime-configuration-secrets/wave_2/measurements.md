# Wave 2 量測合同：X04A Runtime Configuration And Secrets

- Wave：Wave 2
- 工作區：`X04A-runtime-configuration-secrets`
- 議題：`X04A-SEC-001`、`X04A-SEC-002`
- 合同狀態：`CONTRACT_STATUS: WAVE_PLAN_APPROVED`

所有結果附加於本文件最後的「修復證據紀錄」，每一筆包含 UTC 時間、commit SHA（或工作樹基線）、命令、退出碼、redacted summary 與證據檔路徑。不得記錄或輸出 secret 值。

## X04A-SEC-001：提交的 runtime secrets

**Exact sensitive-key manifest（21）。** 基線與修復後必須使用下列完全相同、順序固定的 named manifest；不得加入、移除、合併或以 pattern 取代任一鍵。清單只含 key path，沒有值：

1. `LineMessaging:Jesus:ChannelAccessToken`
2. `LineMessaging:JesusBack:ChannelAccessToken`
3. `LineLogin:ChannelSecret`
4. `MiniApp:ChannelSecret`
5. `CrmConnection:Username`
6. `CrmConnection:Password`
7. `LinePay:ChannelSecret`
8. `Payment:Profiles:JesusTest:Credentials:ShopNo`
9. `Payment:Profiles:JesusTest:Credentials:A1`
10. `Payment:Profiles:JesusTest:Credentials:A2`
11. `Payment:Profiles:JesusTest:Credentials:B1`
12. `Payment:Profiles:JesusTest:Credentials:B2`
13. `Payment:Profiles:JesusTest:Credentials:XKeyId`
14. `Payment:Profiles:MyPayProduction:Credentials:Key`
15. `Payment:Profiles:MyPayProduction:Credentials:IV`
16. `Sinopac:A1`
17. `Sinopac:A2`
18. `Sinopac:B1`
19. `Sinopac:B2`
20. `Sinopac:XKeyID`
21. `MyPay:Key`

**觀測與基線。** issue.md 指向上述 21 個 `appsettings.json` non-empty sensitive-key 位置。基線使用前述 exact manifest 掃描 committed `appsettings.json`；單位為 `SecretLiteralCount`，每一 named key 最多計 1，預期基線為 `21`。掃描只回報鍵名、行號與總數。

**可重現程序。** 修復代理先執行下列 redacted scanner，將輸出寫入工作樹外的受控 CI log 或本文件的 redacted summary；不可把結果另存至未授權 repository 路徑：

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationSecretScanTests" --no-restore
```

修復前，此測試可因舊基線失敗；必須以這 21 個 named key 記錄 `SecretLiteralCount=21`。修復後以同一 manifest 執行同一測試，必須通過並回報 `SecretLiteralCount=0`。測試 fixture 必須使用明顯 synthetic 的字串，只驗證掃描規則與 redaction，且不得包含任何 repository 或部署憑據。

**無回歸觀測。** `appsettings.json` 的 sensitive-key 區段與非敏感 metadata 必須仍可由 `IConfiguration` 以原有鍵名解析；掃描器不得掃描或輸出 unrelated config values。`dotnet build` 必須成功。

**證據位置。** `docs/project-modular-diagnostics/X04A-runtime-configuration-secrets/wave_2/measurements.md` 的修復證據紀錄，以及修復 commit 的 `git diff --check`／`git diff --name-only` redacted summary。

**本機證明限制。** 本機只能證明 repository 沒有已列舉 key 的 committed literal，不能證明外部 secret store、CI 或 Production 環境已注入任何值，也不能證明憑據已輪替或仍有效。

## X04A-SEC-002：Production 繼承不安全 base 設定

**可重現基線程序與八個條件。** 在修改任何產品設定前，修復代理先於 `RuntimeConfigurationSafetyValidatorTests` 建立 `CurrentRepositoryProductionOverlayMeasurement`。該測試以 repository root 為 base path，分別用 `ConfigurationBuilder().AddJsonFile("appsettings.json")` 與 `ConfigurationBuilder().AddJsonFile("appsettings.Production.json")` 建立兩個 `IConfigurationRoot`，再以相同順序建立第三個 effective root；對每一 case 同時讀取 Production provider 的 `TryGet` 結果與 effective root，不能只看最終值。所有輸出一律 redacted。

修復前與修復後均執行同一命令，並保存 detailed test output 的 redacted summary：

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~CurrentRepositoryProductionOverlayMeasurement" --logger "console;verbosity=detailed" --no-restore
```

逐一評估下列八個 named unsafe/inherited conditions，並以 `UnsafeOrInheritedConditionCount / 8` 計數：

1. `SEC002-01`：`Security:EnforceGlobalAuthorization` 的 Production overlay 缺失，effective value 繼承 base 的 permissive value，而非 `true`。
2. `SEC002-02`：`Security:AllowSessionIdentityFallback` 的 Production overlay 缺失，effective value 繼承 base 的 permissive value，而非 `false`。
3. `SEC002-03`：`LinePay:IsSandbox` 的 Production overlay 缺失，effective value 繼承 base 的 sandbox value，而非 `false`。
4. `SEC002-04`：`Cash_Environment` 的 Production overlay 缺失，effective value 繼承 base 的 test/sandbox classification。
5. `SEC002-05`：`PAY_PROVIDER` 的 Production overlay 缺失，effective provider selection 繼承 base，且未由 Production 顯式選擇可解析到 production profile 的 provider。
6. `SEC002-06`：`Payment:DefaultProfile` 的 Production overlay 缺失，effective value 繼承 base 的 test profile，而非 production profile。
7. `SEC002-07`：被 `Payment:DefaultProfile` 選取的 `Payment:Profiles:<effective-default-profile>:Environment` 未被 Production overlay 安全覆寫，effective value 繼承 `Sandbox`，而非 `Production`。
8. `SEC002-08`：`TSPG:TestMode` 的 Production overlay 缺失，effective value 繼承 base 的 test mode，而非 `false`。

預期修復前：八個 case 全部命中，`UnsafeOrInheritedConditionCount=8/8`、`SafeEffectiveConditionCount=0/8`、`ProductionOverlayPresenceCount=0/8`。預期修復後：八個 case 均不命中，`UnsafeOrInheritedConditionCount=0/8`、`SafeEffectiveConditionCount=8/8`、`ProductionOverlayPresenceCount=8/8`。

每次量測以固定格式寫入本文件的修復證據紀錄，且絕不寫 value：

```text
UTC=<timestamp>; Issue=X04A-SEC-002; Source=base+Production-overlay
Case=<SEC002-01..SEC002-08>; Key=<configuration-key-path>; Overlay=<missing|present>
EffectiveClass=<unsafe|safe>; Result=<unsafe-inherited|safe-explicit>; Value=REDACTED
Summary; UnsafeOrInheritedConditionCount=<n>/8; SafeEffectiveConditionCount=<n>/8; ProductionOverlayPresenceCount=<n>/8
```

**Fixture、validator case 與命令。** repository baseline 使用上述實際兩檔合併程序。修復後的 validator 邏輯另以 `ConfigurationBuilder().AddInMemoryCollection(...)` 測試：base dictionary 提供八個不安全 defaults，Production dictionary 覆寫八個安全 controls，secret dictionary 僅提供 synthetic non-placeholder 值。另有一個安全 Production fixture 通過、一個 missing/placeholder sensitive-key Production fixture 被拒絕，以及一個 Development fixture 不啟用 Production gate。synthetic fixture 不載入真實 `appsettings`、不使用 process environment，也不讀取外部 secret source。

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationSafetyValidatorTests" --no-restore
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --no-restore
```

預期：修復後第一個命令通過八個 named safety/inheritance cases、safe Production case、missing/placeholder rejection case 與 Development bypass case；第二個命令成功。validator 的錯誤輸出僅可含鍵名與分類，不可含有效設定值。

**無回歸觀測。** `GlobalAuthorizationFilterTests` 仍使用其既有 in-memory config 並通過；Production 以外不應因新 Production-only validator 拒絕啟動。驗證器必須在任何 `Startup.ConfigureServices` 之前執行，防止不安全有效設定進入 service registration。

**證據位置。** 同一 `measurements.md` 的修復證據紀錄、測試輸出 redacted summary、以及 `Program.cs` 啟動接線 diff 摘要。

**本機證明限制。** synthetic fixture 只證明有效設定合併後的 fail-fast 邏輯；它不證明 Production 部署當下有 secret、雲端／IIS environment variable 名稱正確、secret store 可連線，或外部金流/LINE/CRM 可成功認證。這些是部署 runtime 證據，必須由 X04B／部署所有者在受管環境另行取得。

## 修復證據紀錄

修復代理只能在此標題下附加實測結果；不得修改以上基線、case 數、目標、範圍或限制。

## 審查終止證據

- Claude 無可用輸出：`.ccg/dual-model-runs/20260714-154429-wave2-x04a-contract-reviewer/summary.json`；依流程改由控制器安排唯讀備援複審。
- `WAVE_PLAN_APPROVED`：Codex 唯讀備援複審確認 X04A-SEC-001 與 X04A-SEC-002 合約已具完整範圍、量測、目標、無回歸與回復界線，且無未解決的 Critical 或 Warning。

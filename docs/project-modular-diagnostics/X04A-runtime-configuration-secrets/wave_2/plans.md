# Wave 2 實施合同：X04A Runtime Configuration And Secrets

- Wave：Wave 2
- 工作區：`X04A-runtime-configuration-secrets`
- 工作樹：`D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.1.EvenVersion`
- 正式議題：`X04A-SEC-001`、`X04A-SEC-002`
- 合同狀態：`CONTRACT_STATUS: WAVE_PLAN_APPROVED`

本文件是後續修復代理的不可變更範圍。修復代理只能依此文件修改列入 allowlist 的產品檔案，並只能在本波三份合同中附加量測證據；不得修改目標、驗收值、範圍或回復條件。

## Allowlist

後續修復代理只可建立或修改下列路徑：

- `SpeechMessageProducts.ChurchReport/appsettings.json`
- `SpeechMessageProducts.ChurchReport/appsettings.Production.json`
- `SpeechMessageProducts.ChurchReport/Program.cs`
- `SpeechMessageProducts.ChurchReport/Configuration/RuntimeConfigurationSafetyValidator.cs`（新建）
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationSecretScanTests.cs`（新建）
- `ChurchReport.MemberInfo.Tests/Configuration/RuntimeConfigurationSafetyValidatorTests.cs`（新建）
- `docs/project-modular-diagnostics/X04A-runtime-configuration-secrets/wave_2/plans.md`、`measurements.md`、`goals.md`（只可附加實測證據，不可改寫合同）

## 明確排除

- 未選議題：`X04A-SEC-003`、`X04A-PERF-001`、`X04A-EXT-001`。
- `SpeechMessageProducts.ChurchReport/appsettings.Development.json`、`web.config`、所有 `.csproj`、solution、部署腳本、CI、文件以外的診斷產物。
- 所有 ad-hoc `ConfigurationBuilder` 消費端，包括 `Services/ChurchReportLineAdminNotificationService.cs`、`Services/PaymentNotificationService.cs`、`Tools/*.cs`、`Models/DonationPaymentManager.cs`、`WebServiceConnector/LineNotifyUtility.cs` 與 `WebServiceConnector/DonationPaymentProcessor/DonationPaymentProcessor.Core.cs`。其 host 設定生命週期遷移屬 `X04A-PERF-001`，本波不得藉由此修復碰觸。
- OAuth state 的移除或行為調整；該項屬 `X04A-SEC-003`。
- 可重用掃描器／跨模組抽取、部署注入、憑據輪替、金流或 LINE/CRM 商業流程變更；其分別屬 `X04A-EXT-001` 或外部所有者責任。

## 最小修復步驟

### X04A-SEC-001：提交的 runtime secrets

1. 僅在 `appsettings.json` 移除 issue.md 已列舉之 runtime secret 位置的非空字面值；保留區段、非敏感 metadata、端點與既有設定鍵名稱，避免改變既有繫結路徑。
2. 對應的正式值一律由部署期外部設定提供，使用 .NET 階層式環境變數鍵名（以 `__` 取代 `:`）；不得在任何提交檔、測試 fixture、命令輸出、量測或 Claude prompt 寫入真實值。
3. 新增純文字、紅遮罩的提交檔掃描契約測試。掃描器只輸出檔案、設定鍵與計數，並以 `measurements.md` 的「X04A-SEC-001 exact sensitive-key manifest（21）」作為唯一輸入；修復前與修復後不得改用不同 manifest。判定 committed `appsettings.json` 中 21 個 named key 均沒有非空字面值。
4. 不在本波執行憑據輪替。已暴露憑據的停用與替換是外部憑據所有者的必做後續動作，且不得以新值回填 repository。

### X04A-SEC-002：Production 繼承不安全 base 設定

1. 在 `appsettings.Production.json` 顯式覆寫 issue.md 指出的八個安全／繼承控制：`Security:EnforceGlobalAuthorization` 必為 `true`、`Security:AllowSessionIdentityFallback` 必為 `false`、`LinePay:IsSandbox` 必為 `false`、`Cash_Environment` 不可為 test/sandbox classification、`PAY_PROVIDER` 必須是 Production 顯式選擇且可解析到 production profile、`Payment:DefaultProfile` 指向 production profile、被選 profile 的 `Environment` 必為 `Production`、`TSPG:TestMode` 必為 `false`。不得在 Production 檔置入任何 secret 值。
2. 建立 `RuntimeConfigurationSafetyValidator`，輸入 `IConfiguration` 和 host environment name，輸出不含值的設定鍵錯誤清單。只在 Production 檢查上列有效安全值，並檢查 `X04A-SEC-001` exact sensitive-key manifest 的有效設定均為非空且非已知 placeholder；非 Production 不套用 Production fail-fast 規則。用於檔案層級繼承量測的測試還必須確認八個控制皆在 Production overlay 顯式出現，因 `IConfiguration` 的有效值本身不保留來源 provider 的 provenance。
3. 在 `Program.Main` 的 `WebApplication.CreateBuilder(args)` 後、任何 `Startup.ConfigureServices` 前，若 `builder.Environment.IsProduction()` 為真則執行 validator，發現錯誤即拋出單一啟動例外。錯誤訊息只列鍵名與失敗類別，禁止列出設定值。
4. 將 validator 以 in-memory configuration 測試：安全 Production fixture 通過；每個不安全 inheritance case 分別拒絕；Production 缺失／placeholder secret 拒絕；Development fixture 不因 Production 規則失敗。測試用 synthetic sentinel 值只證明驗證邏輯，不可聲稱部署期 secret 已存在。

## 本機驗證與預期證據

修復前後均執行下列命令；輸出只能保存紅遮罩結果：

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~RuntimeConfigurationSecretScanTests|FullyQualifiedName~RuntimeConfigurationSafetyValidatorTests" --no-restore
```

預期：修復後兩個新測試類別全部通過；輸出只含通過／失敗數與測試名稱，沒有設定值。

```powershell
dotnet build .\SpeechMessageProducts.ChurchReport\SpeechMessageProducts.ChurchReport.csproj --no-restore
```

預期：成功，無新增編譯錯誤。

```powershell
git diff --check
git diff --name-only
```

預期：無空白錯誤，且產品變更只在本文件的 allowlist。

另以 `RuntimeConfigurationSecretScanTests` 所用的同一 sensitive-key manifest 產生 `X04A-SEC-001` 紅遮罩計數；修復後必為 `0`。本機驗證不得啟動真實 Production、聯絡外部服務或宣稱外部 secret store／環境變數已在部署環境可用。

## 整波回復邊界

本 Wave 的回復單位是 allowlist 內的單一 config/startup-validation commit：還原 `appsettings.json`、`appsettings.Production.json`、`Program.cs`、validator 與兩個測試的同一修復變更。回復只撤銷 repository 內的行為，不撤銷外部憑據輪替或部署 secret 注入；若回復後必須恢復服務，部署所有者必須以受管 secret source 補回有效設定，絕不可把秘密重新提交至設定檔。

## 審查終止證據

- Claude 無可用輸出：`.ccg/dual-model-runs/20260714-154429-wave2-x04a-contract-reviewer/summary.json`；依流程改由控制器安排唯讀備援複審。
- `WAVE_PLAN_APPROVED`：Codex 唯讀備援複審確認 X04A-SEC-001 與 X04A-SEC-002 合約已具完整範圍、量測、目標、無回歸與回復界線，且無未解決的 Critical 或 Warning。

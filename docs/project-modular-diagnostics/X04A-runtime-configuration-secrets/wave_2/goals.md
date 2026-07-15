# Wave 2 完成目標：X04A Runtime Configuration And Secrets

- Wave：Wave 2
- 工作區：`X04A-runtime-configuration-secrets`
- 議題：`X04A-SEC-001`、`X04A-SEC-002`
- 合同狀態：`CONTRACT_STATUS: WAVE_PLAN_APPROVED`

本文件是本 Wave 的完成權威。後續修復代理只能附加證據，不得降低下列數值、替換驗證方式或擴張議題範圍。

## X04A-SEC-001：提交的 runtime secrets

**可量測完成目標：** `RuntimeConfigurationSecretScanTests` 對 committed `SpeechMessageProducts.ChurchReport/appsettings.json` 的 `measurements.md`「X04A-SEC-001 exact sensitive-key manifest（21）」產生 `SecretLiteralCount=0`；基線的 21 個 issue-evidenced 非空位置全數不再含提交字面值。修復前與修復後必須使用相同 named manifest。

**必須保留的授權行為：** 原有設定鍵、區段結構、非敏感 metadata 與 endpoint 設定仍存在，讓既有 `IConfiguration` consumer 可使用相同 key path 取得部署期外部設定。秘密值只可由部署期外部 source 提供；憑據輪替仍由外部 owner 執行，不是本地程式碼或設定檔動作。

**成功所需本機證據：** secret scanner test 通過、主專案 build 成功、`git diff --check` 無錯誤，且 `git diff --name-only` 僅含 plans.md allowlist 的修復檔。測試／輸出／證據不得含秘密值。

**失敗與回復條件：** 任一 sensitive-key literal 被掃描到、修復需要提交新 secret、既有 key path 被刪除而無相容的外部設定路徑、或 allowlist 外檔案被修改，均使本議題未完成；回復整個 allowlist commit，並由部署 owner 以受管 secret source 維持必要設定。

## X04A-SEC-002：Production 繼承不安全 base 設定

**可量測完成目標：** repository base+Production overlay 量測的 `UnsafeOrInheritedConditionCount` 必為 `0 / 8`、`SafeEffectiveConditionCount` 必為 `8 / 8`、`ProductionOverlayPresenceCount` 必為 `8 / 8`；八個 named conditions 定義於 `measurements.md`。`RuntimeConfigurationSafetyValidatorTests` 必須對八個 controls 的不安全 Production fixture 全部 fail-fast，安全 Production fixture 通過，且 missing/placeholder sensitive-key fixture 被拒絕。

**必須保留的授權行為：** Development 與其他非 Production environment 不套用 Production fail-fast 規則；既有 `GlobalAuthorizationFilter` 的授權行為不變，並以其既有測試通過為證。Production validator 只檢查 host 的 effective `builder.Configuration`，不修改 ad-hoc config consumers，因後者是排除的 `X04A-PERF-001`。

**成功所需本機證據：** 用實際 `appsettings.json` 加 `appsettings.Production.json` 的兩 provider 合併程序，記錄 `0/8`、`8/8`、`8/8` 的 redacted 摘要；八個固定 validator case 全部通過、`GlobalAuthorizationFilterTests` 通過、主專案 build 成功，並由 `Program.cs` diff 證明 validator 在 `Startup.ConfigureServices` 前且只於 Production 接線。所有錯誤只列鍵名與分類。

**失敗與回復條件：** 任一不安全 case 被接受、安全 Production fixture 被拒絕、非 Production 被新 gate 阻斷、錯誤訊息揭露設定值、或啟動接線晚於 service registration，均使本議題未完成。回復整個 allowlist commit；不可藉回復把秘密重新寫回 committed config，部署端仍須提供外部設定。

## 不可替代的部署證據

本機成功不降低任何部署目標。Production 上線前，部署所有者必須獨立證明 external secret source 可用、每個 required key 已受管注入、已暴露憑據已依事件程序輪替，且啟動 validator 在真實 Production effective configuration 通過。缺少該證據時，不得宣稱 Production secrets 可用，也不得將本 Wave 標示為已完成部署驗證。

## 審查終止證據

- Claude 無可用輸出：`.ccg/dual-model-runs/20260714-154429-wave2-x04a-contract-reviewer/summary.json`；依流程改由控制器安排唯讀備援複審。
- `WAVE_PLAN_APPROVED`：Codex 唯讀備援複審確認 X04A-SEC-001 與 X04A-SEC-002 合約已具完整範圍、量測、目標、無回歸與回復界線，且無未解決的 Critical 或 Warning。
## 修復證據追加（2026-07-15T12:39:23+08:00）

- X04A-SEC-001 目標對照：committed base 設定的 frozen manifest 從 baseline `21/21` non-empty literals 修復為 `0/21`；sections、key paths 與非 secret metadata 保留，未提交替代 secret literal。
- X04A-SEC-002 目標對照：Production overlay 明確覆蓋 8 個 safe controls，repository 測試結果為 overlay `8/8`、safe effective `8/8`、unsafe/inherited `0/8`；Production validator 對 missing/placeholder secrets fail-fast，Development bypass。
- 驗證摘要：目標測試 `RuntimeConfigurationSecretScanTests|RuntimeConfigurationSafetyValidatorTests` 通過 `16/16`；ChurchReport build 通過，`0` warning、`0` error。所有摘要均為 redacted count/key/class 級別。
- Claude-only final review 尚未開始；完成 allowlist/format/diff 檢查後執行並追加 artifact。

## 修復阻擋證據（2026-07-15）

- 更正先前「Claude-only final review 尚未開始」的時點性敘述：其後已執行 Claude-only runner，但 `.ccg/dual-model-runs/20260715-124709-wave2-x04a-runtime-config-secrets-reviewer/summary.json` 記錄為無可用輸出；此補充不改寫先前證據。
- 一次唯讀 Codex 備援複審回報 `CHANGES_REQUIRED`：排除於本 Wave allowlist 的 `Services/ChurchReportLineAdminNotificationService.cs` 與 `Tools/LineUtilityClass.cs` 自行只載入 `appsettings.json`，未加入環境或 Production provider。
- 所以 X04A 的原完成目標不能以 host-only validator 取代所有 consumer 的相容性；在不修改排除 consumer 的範圍下，沒有安全的產品修復提交。
- 已撤回本次未提交的產品、設定、validator 與測試變更。`X04A-SEC-001` 與 `X04A-SEC-002` 維持未解決，直到另行核准的合同納入 `X04A-PERF-001` consumer migration，或核准另一個安全相容性設計。

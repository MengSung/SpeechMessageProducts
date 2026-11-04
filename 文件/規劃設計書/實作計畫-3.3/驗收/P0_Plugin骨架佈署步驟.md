# P0-13　Plugin 骨架最小驗收

> 使用者 2026-11-04 核定。清理完成後預留約 1 小時；全部由使用者執行，Codex 不連線 D365／LINE。
> **只連 goodshepherdback；不要貼出密碼、token、金鑰、User Secrets 或追蹤全文。**

> **LINE 憑證分離補充（2026-11-04）：** ChurchReport 的 `appsettings.json` 已清空 LINE token；正式環境改由專用環境變數提供，Development 的 `DevTest` token 只接受 User Secrets。
>
> P0-13 使用 trace，不需要 LINE channel、`TestLineUserId`、`TestChannelToken` 或任何 LINE token。Deployment 的 User Secrets 只填下面三個 Dataverse 值；本次 `secureConfig` 保持空白。
>
> **程式設定分離不等於 goodshepherdback 資料清理完成。** 舊 Plugin／工作流程相關資料中的 LINE token 與會友 `new_lineid` 仍須由使用者確認清理；尚未回報的項目保持未勾選。

本次只確認：以佈署專案註冊、啟用 net48 Plugin，在沙箱看到版本與雙語文字，最後停用並清理。
不需要測試 LINE channel，也不需要 Six Labors 授權：Plugins 與 Deployment 的直接、間接相依均無 ImageSharp。
推播移至 03 的「P2-04 前置驗收」；Plugin 自身 FileVersion 追蹤、更新與回復移至 04 階段 5，現在不改 Plugin。

所有命令都在 **P0 worktree 的方案根目錄**以 PowerShell 執行。每行結束碼必須為 0 才執行下一行；出錯停止下一項，仍按第 6 步收尾。

## 0. 前置確認

**做什麼：** 全部確認後才執行部署命令；以下依使用者 2026-11-04 回報更新，尚未確認的項目維持未勾選，整體清理仍未完成。

- [x] goodshepherdback 的舊自訂 Plugin 步驟已停用（使用者 2026-11-04 回報）。
- [x] goodshepherdback 的自訂工作流程已停用（使用者 2026-11-04 回報）。
- [x] 等候中、可重試、排程中的舊自訂流程系統作業已取消（使用者 2026-11-04 回報取消篩選後的 3 筆）。
- [ ] 資料中的 LINE token 已清除或換成測試 channel。
- [ ] 會友 `new_lineid` 已清空。P1-12 仍用此組織，不能省略上述清理。

**停用確認依據：** 使用者提供的截圖可見 4 筆 `TwoWingsPlugIn` 步驟皆為「已停用」；其餘舊自訂 Plugin 步驟與自訂工作流程依使用者回報記錄。截圖只涵蓋部分步驟，不代表 Codex 已逐筆核對全部 105 筆，也未由 Codex 連線查驗；Microsoft 系統內建項目維持原狀。

- [ ] 開一個**環境變數設定後才開的全新 PowerShell 視窗**，執行下列檢查，顯示 `True`；之後在這個視窗執行建置與部署命令。只確認檔案存在，不讀取、複製或貼出金鑰內容。

```powershell
Test-Path $env:SPEECHMESSAGE_PLUGIN_KEYFILE
```

已開著的 PowerShell 視窗與 Visual Studio 看不到後來新增的環境變數；請重新開啟。

- [ ] 在 Visual Studio 對 **SpeechMessage.Dynamics.Deployment** 選「管理使用者密碼」，填好以下三個值；填完關閉檔案，不提交。

```json
{
  "Deployment": {
    "Organizations": {
      "goodshepherdback": {
        "OrganizationUrl": "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
        "Username": "<帳號>@speechmessage.com.tw",
        "Password": "<自行填入>"
      }
    }
  }
}
```

Username **必須含網域**，建議使用上方 UPN。部署工具以 `ClientCredentials.UserName` 登入，與 ChurchReport 的 Domain＋NetworkCredential 不同，不可只填不含網域的帳號。也可用以下 JSON 欄位寫法：

```json
"Username": "<網域>\\<帳號>"
```

JSON 中的反斜線必須寫**兩條** `\\`，解析後才是一條；只寫一條會導致設定讀取失敗。
依據：[CreateOnPremiseClient 快速參考：使用者名稱格式](../../../../ToolUtility/文件/CreateOnPremiseClient快速參考.md#使用者名稱格式)。

**預期看到什麼：** 組織為 goodshepherdback，UserSecretsId 是 `SpeechMessage.Dynamics.Deployment.P0`。trace 只需要這三個 Dataverse 值，不要求 `TestLineUserId`、`TestChannelToken`，不要求任何 LINE token，也不讀 ChurchReport 設定。

**不對時怎麼辦：** 清理未完成就不連線。金鑰無法使用、User Secrets 找不到或要求額外 LINE 值時，回報固定錯誤型別／代碼，不填假值、不換金鑰。

### 0.1 系統作業清理（使用者已回報完成）

**完成紀錄（2026-11-04）：** 使用者將狀態篩選為「等待資源、等待中、進行中、暫停、正在取消」，清單剩下 2 筆「等待計時器」與 1 筆「因錯誤而正在等候重試」的舊自訂工作流程作業；使用者回報已取消，後續截圖顯示該篩選清單已無資料。此紀錄依據使用者回報與截圖，Codex 未連線查驗；「成功、失敗、已取消」的歷史記錄保留，不要求刪除。

**操作紀錄：** 由使用者逐步操作，每次看清畫面後才往下：

1. 保留目前「預設解決方案／SDK 訊息處理步驟」視窗，切回原本 goodshepherdback 的 Dynamics 365 主畫面。
2. 點主畫面左上方「設定」旁的下拉箭頭，找「系統作業」。找到後開啟；找不到就提供遮住帳號與個資的選單截圖，先不修改任何作業。
3. 先核對作業名稱、類型與狀態，找出舊自訂流程留下的等候中、可重試或排程作業，再由使用者執行取消。Microsoft 系統維護作業與無法辨識來源的項目先不動，不全選整份清單；已完成的歷史記錄不必刪除。

**預期看到什麼：** 目前先看到系統作業清單；只有在清理範圍核對完、相關作業均已取消並由使用者回報後，才勾選上方「系統作業已取消」。

**不對時怎麼辦：** 找不到清單、無法判斷來源或取消失敗就停在該畫面回報。取消後無法恢復該作業，不改用暫停或延後冒充取消，也不刪除歷史記錄。
操作依據：[Microsoft：監視及管理系統作業](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/admin/monitor-manage-system-jobs?view=op-9-1)。

### 0.2 待確認：舊 LINE token 與會友資料清理

**做什麼：** 先確認舊 Plugin／自訂流程的 LINE token 當初在哪個畫面或檔案設定，再依實際位置引導清理。只回報畫面名稱、欄位名稱或檔名，不貼出 token 或設定內容；位置尚未確認前，不猜測資料表、不修改正式頻道或正式組織。

**已完成的程式工作：** `86ca8bf83` 已完成 ChurchReport LINE 設定分離；離線測試 ChurchReport.MemberInfo.Tests 597／597、Line.Messaging.Tests 32／32 通過。這只證明程式設定與守門，不代表曾連線確認 goodshepherdback，不能據此勾選資料清理或 P0-13 實機項目。

**完成條件：** 使用者在 goodshepherdback 清除或替換相關資料中的 token，並回報完成後，才勾選上方 token 項目。目前 token 與會友 `new_lineid` 均尚未回報清理完成，整體清理仍未完成。

### 0.3 現在先做的一小步：檢查本機金鑰是否備妥

**做什麼：** 按 Windows「開始」，輸入 `PowerShell`，開啟一個全新視窗；只貼上並執行 `Test-Path $env:SPEECHMESSAGE_PLUGIN_KEYFILE`。這個檢查可在等待資料清理確認時先做，不會連線 D365／LINE，也不會讀取金鑰內容。

**預期看到什麼：** 顯示 `True`，保留這個視窗。只回報 `True`，不要貼路徑或金鑰內容；收到結果後再進行下一小步，不要一次貼上第 1～6 步的全部命令。

**不對時怎麼辦：** 顯示 `False` 或紅色錯誤就先停在這一步，只回報 `False` 或遮罩後的錯誤代碼；不產生替代金鑰。即使顯示 `True`，仍須完成第 0 步所有未勾選項目，才開始第 1～6 步的實機驗收。

## 1. 開啟 Plugin 追蹤

**做什麼：** 在 goodshepherdback「設定 → 系統管理 → 系統設定 → 自訂」，把「啟用外掛程式追蹤記錄」設為「全部」。

**預期看到什麼：** 儲存成功；稍後可在「Plugin 追蹤記錄」與「系統作業」查看本次執行。階段 0～4 的 Plugin 失敗由這兩處查看。

**不對時怎麼辦：** 無權限或找不到選項就停下回報，請管理員確認；驗收後改回「例外狀況」。

## 2. 建置與打包檢查

**做什麼：** 逐行執行，每行成功才執行下一行。

```powershell
dotnet build .\SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true
powershell -NoProfile -File .\tools\PluginPackaging\Test-PluginAssembly.ps1 -AssemblyPath .\artifacts\plugins\SpeechMessage.Dynamics.Plugins.dll
dotnet build .\SpeechMessage.Dynamics.Deployment -c Release
```

**預期看到什麼：** 建置成功，已簽章 DLL 通過檢查，AssemblyVersion 固定 `1.0.0.0`，目標為 net48。

**不對時怎麼辦：** 回報失敗命令與錯誤碼；不產生替代金鑰、不跳過簽章檢查，也不為此提供 Six Labors 授權。

## 3. 一次規劃：trace 模式、建立時就啟用

**做什麼：** 開啟 `d365\registration\plugins.registration.json`，保留唯一的 DiagnosticsPingPlugin：Create／task／PostOperation／Asynchronous／rank 1。第一次 plan **之前**，把此檔的 `enabledByOrganization.goodshepherdback` **從 false 改為 true**，並確認以下三個欄位片段。**驗收期間不要提交清單為 true 的版本。**

```json
"unsecureConfig": "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
"secureConfig": "",
"enabledByOrganization": { "goodshepherdback": true }
```

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- plan --org goodshepherdback
$plan = Join-Path (Get-ChildItem .\artifacts\d365\goodshepherdback -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).FullName 'plan.json'
notepad (Join-Path (Split-Path $plan) 'plan.md')
```

plan 成功後，**立刻執行取得 `$plan` 與開啟 plan.md 的兩行，在下一個部署指令之前完成**；apply 也會建立新目錄，不能等 apply 後才找最新目錄。plan 失敗就不要執行後兩行。審核開啟的 plan.md，內容不符預期就**不要 apply**。

**預期看到什麼：** 全新安裝只列 CreateAssembly／CreateType／CreateStep，沒有秘密佔位。CreateStep 會直接建立為啟用狀態，不必先停用註冊再跑第二輪；trace 不發出 LINE HTTP 請求。若已安裝同一骨架，可能只有啟用狀態更新或無差異，須確認為預期的同一個測試步驟。

**不對時怎麼辦：** 出現其他組織、步驟、push 模式或未預期更新就不套用，回報操作種類。不得手改 plan hash；清單或 DLL 改過就重新 plan。

## 4. 套用並確認 verify=0

**做什麼：** 確認第 3 步 plan.md 內容符合預期，再在**同一個 PowerShell 視窗**逐行執行，沿用剛取得的 `$plan`。

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $plan
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
```

**預期看到什麼：** apply 結束碼 0、測試步驟已註冊並啟用；verify 結束碼 **0**、無差異。apply 先在**該次 apply 目錄**保存 export.json；留在本機 artifacts，不上傳。

**不對時怎麼辦：** 連線、權限、簽章或沙箱錯誤就停下回報並做第 6 步，不改回手動註冊。trace 的 verify=1 或 3 都未通過，不能略過。

## 5. smoke trace，人工看五項追蹤

**做什麼：**

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- smoke --org goodshepherdback --scenario p0-ping-trace
```

開啟「**設定 → 自訂 → 外掛程式追蹤記錄**」，依建立時間找最新一筆 **DiagnosticsPingPlugin**，打開查看「**訊息區塊**」。以本次 smoke 時段核對，必要時對照「系統作業」，不要把舊記錄當成本次成功；確認：

- [ ] `P0-PING OK`。
- [ ] `Assembly=` 為 SpeechMessage.Dynamics.Plugins，版本 `1.0.0.0`。
- [ ] `FrameworkFileVersion=` 為 **4.8.x**（mscorlib）；不能用 `EnvironmentVersion=` 代替。
- [ ] `zh-TW=連線測試成功`。
- [ ] `qps-ploc=［連線測試成功ǂǂǂ］`。

**預期看到什麼：** 結束碼 0、`Smoke scenario passed.`，五項人工確認符合；工具最多等 3 分鐘，結束時嘗試刪除自己建立的 `[smoke]` task。

**不對時怎麼辦：**

| 結果 | 處理 |
| --- | --- |
| FrameworkFileVersion 不是 4.8.x | 依 D21 回報，由使用者決定是否評估改 4.6.2，不自行降低框架 |
| smoke 逾時或沒有追蹤 | 請使用者確認沙箱處理服務、非同步服務已啟動及追蹤設定 |
| 其他內容不符或清理失敗 | 記下固定錯誤型別／代碼並回報，不貼追蹤全文或資料內容 |

## 6. 收尾並回報

**做什麼：** 成功或中途失敗都要收尾：清單維持 trace、secureConfig 空白，把 `enabledByOrganization.goodshepherdback` 改為 **false**。

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- plan --org goodshepherdback
$plan = Join-Path (Get-ChildItem .\artifacts\d365\goodshepherdback -Directory | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).FullName 'plan.json'
notepad (Join-Path (Split-Path $plan) 'plan.md')
```

同第 3 步：plan 成功後立刻取得 `$plan` 並開啟 plan.md，必須在下一個部署指令前完成，因為 apply 也會建立新目錄。**預期只有一項 UpdateStepState，對象為同一個測試步驟**；內容不符就不要 apply，不沿用第 4 步舊 plan。審核符合後，在同一個視窗執行：

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $plan
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
```

把追蹤改回「例外狀況」，確認本次 smoke task 已清除。最後再次確認 `plugins.registration.json` 的 `enabledByOrganization.goodshepherdback` 已回到 **false**，執行：

```powershell
git status --short -- .\d365\registration\plugins.registration.json
```

預期沒有輸出，表示此檔沒有已暫存或未暫存的未提交變更。若有輸出，先核對清單是否確實恢復原設定；不要為了消除輸出而提交 true 的版本，也不要覆寫不屬於本次驗收的修改。

**預期看到什麼：**

- [ ] 同一測試步驟已停用、清單回到 false，收尾 verify=0；git 顯示清單沒有未提交變更。
- [ ] Plugin 追蹤設定已改回「例外狀況」。
- [ ] smoke 資料已清除，沒有觸發其他舊流程或通知。

**不對時怎麼辦：** 無法停用或清理就立即回報，由使用者處理 D365 停用／清理，不再建立測試資料。

回覆下表即可，不貼任何秘密或會友資料：

| 項目 | 請填成功／未通過及固定錯誤碼 |
| --- | --- |
| 第 0 步清理與準備 | 進行中：舊自訂 Plugin 步驟、自訂工作流程已回報停用，3 筆待處理系統作業已回報取消；其餘待確認 |
| 追蹤設定、建置與打包檢查 | 待使用者執行 |
| 一輪 plan／apply／verify=0 | 未驗證 |
| trace smoke 與五項人工追蹤 | 未驗證 |
| 停用、收尾 verify=0、追蹤復原及資料清除 | 未驗證 |

同一事件重送不再做實機驗收：使用者接受既有「Retry Key＝來源記錄 ID」單元測試，加上 LINE 官方同 Retry Key 在 24 小時有效期內抑制重複受理的契約作為依據；這不保證訊息一定送達，也不涵蓋超過有效期的重送。
來源：[LINE 官方重試說明](https://developers.line.biz/en/docs/messaging-api/retrying-api-request/)。

GitHub CI 的 23／26 專案過渡範圍已由使用者 2026-11-04 接受；DevExpress 來源在階段 1 開始後優先處理，本機 §8 仍驗證全方案。
**文件已備妥就停下，等使用者回報清理與最小驗收成功；P0-13 和階段 0 目前尚未完成，也不開 P1 分支。**

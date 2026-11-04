# P0-13　Plugin 骨架最小驗收

> 使用者 2026-11-04 核定。前置備妥後預留約 1 小時；全部由使用者執行，Codex 不連線 D365／LINE。
> **只連 goodshepherdback；不要貼出密碼、token、金鑰、User Secrets 或追蹤全文。**

> **LINE 憑證分離補充（2026-11-04）：** ChurchReport 的 `appsettings.json` 已清空 LINE token；正式環境改由專用環境變數提供，Development 的 `DevTest` token 只接受 User Secrets。
>
> P0-13 使用 trace，不需要 LINE channel、`TestLineUserId`、`TestChannelToken` 或任何 LINE token。Deployment 的 User Secrets 只填下面三個 Dataverse 值；本次 `secureConfig` 保持空白。
>
> **使用者 2026-11-04 追加核定：** 本次 P0-13 trace 驗收保留資料中的舊 LINE token 與會友 `new_lineid`，免除這兩項清理前置，不再等待清空才繼續。這記錄為使用者核定保留資料，不能宣稱資料已清理；舊 Plugin／工作流程維持停用。此例外限本次 trace 驗收，後續階段另依該階段規則執行。

本次只確認：以佈署專案註冊、啟用 net48 Plugin，在沙箱看到版本與雙語文字，最後停用並清理。
不需要測試 LINE channel，也不需要 Six Labors 授權：Plugins 與 Deployment 的直接、間接相依均無 ImageSharp。
推播移至 03 的「P2-04 前置驗收」；Plugin 自身 FileVersion 追蹤、更新與回復移至 04 階段 5，現在不改 Plugin。

所有命令都在 **P0 worktree 的方案根目錄**以 PowerShell 執行。每行結束碼必須為 0 才執行下一行；出錯停止下一項，仍按第 6 步收尾。

## 0. 前置確認

**做什麼：** 以下依使用者 2026-11-04 回報與追加核定更新；舊步驟／流程停用、待處理作業取消及本機準備均已有回報，可繼續第 1 步追蹤設定。token 與會友 LINE ID 資料保留，免除本次清理前置。

- [x] goodshepherdback 的舊自訂 Plugin 步驟已停用（使用者 2026-11-04 回報）。
- [x] goodshepherdback 的自訂工作流程已停用（使用者 2026-11-04 回報）。
- [x] 等候中、可重試、排程中的舊自訂流程系統作業已取消（使用者 2026-11-04 回報取消篩選後的 3 筆）。
- 本次免除：清除或替換資料中的舊 LINE token（使用者 2026-11-04 追加核定保留）。
- 本次免除：清空會友 `new_lineid`（同上）。這兩項不標記為清理完成，亦不自動放寬後續階段。

**停用確認依據：** 使用者提供的截圖可見 4 筆 `TwoWingsPlugIn` 步驟皆為「已停用」；其餘舊自訂 Plugin 步驟與自訂工作流程依使用者回報記錄。截圖只涵蓋部分步驟，不代表 Codex 已逐筆核對全部 105 筆，也未由 Codex 連線查驗；Microsoft 系統內建項目維持原狀。

- [x] 使用者已在新 PowerShell 執行下列檢查並回報 `True`；只確認檔案存在，未讀取金鑰內容。之後沿用該視窗執行命令，重開視窗時再確認一次。

```powershell
Test-Path $env:SPEECHMESSAGE_PLUGIN_KEYFILE
```

已開著的 PowerShell 視窗與 Visual Studio 看不到後來新增的環境變數；請重新開啟。

- [x] 使用者已在 **SpeechMessage.Dynamics.Deployment** 的「管理使用者密碼」儲存以下三個值；內容未由 Codex 讀取，尚未驗證登入成功。

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

**不對時怎麼辦：** 舊步驟／流程未停用或待處理舊作業未取消時，先停止部署。金鑰無法使用、User Secrets 找不到或要求額外 LINE 值時，回報固定錯誤型別／代碼，不填假值、不換金鑰。

### 0.1 系統作業清理（使用者已回報完成）

**完成紀錄（2026-11-04）：** 使用者將狀態篩選為「等待資源、等待中、進行中、暫停、正在取消」，清單剩下 2 筆「等待計時器」與 1 筆「因錯誤而正在等候重試」的舊自訂工作流程作業；使用者回報已取消，後續截圖顯示該篩選清單已無資料。此紀錄依據使用者回報與截圖，Codex 未連線查驗；「成功、失敗、已取消」的歷史記錄保留，不要求刪除。

**操作紀錄：** 由使用者逐步操作，每次看清畫面後才往下：

1. 保留目前「預設解決方案／SDK 訊息處理步驟」視窗，切回原本 goodshepherdback 的 Dynamics 365 主畫面。
2. 點主畫面左上方「設定」旁的下拉箭頭，找「系統作業」。找到後開啟；找不到就提供遮住帳號與個資的選單截圖，先不修改任何作業。
3. 先核對作業名稱、類型與狀態，找出舊自訂流程留下的等候中、可重試或排程作業，再由使用者執行取消。Microsoft 系統維護作業與無法辨識來源的項目先不動，不全選整份清單；已完成的歷史記錄不必刪除。

**預期看到什麼：** 目前先看到系統作業清單；只有在清理範圍核對完、相關作業均已取消並由使用者回報後，才勾選上方「系統作業已取消」。

**不對時怎麼辦：** 找不到清單、無法判斷來源或取消失敗就停在該畫面回報。取消後無法恢復該作業，不改用暫停或延後冒充取消，也不刪除歷史記錄。
操作依據：[Microsoft：監視及管理系統作業](https://learn.microsoft.com/en-us/dynamics365/customerengagement/on-premises/admin/monitor-manage-system-jobs?view=op-9-1)。

### 0.2 本次核定：保留舊 LINE token 與會友 LINE ID

**已找到的來源（本機舊程式，2026-11-04）：** 舊 webhook 會把 LINE token 複製到「信件」資料表（`letter`）的 `new_channel_access_token` 欄位，舊 LineMessagePlugIn 再從該欄位讀取。因此清空網站設定，並不會清掉已存在資料庫中的值。小組週報舊 Plugin 也會讀取 `new_group_present_weekly_report` 的同名欄位。這是本機原始碼的證據，尚未確認 goodshepherdback 的實際資料或已部署 DLL 版本。

**來源檔：** 上層工作區 `.其他解決方案` 內的 `LineApiWebhook\Crm2011Processor\Dynamics365ProcessorClass\Dynamics365ProcessorClass.MessageStore.cs`、`Dynamics365Line\LineMessagePlugIn\LineMessagePlugInClass.cs`、`Dynamics365Line\WeeklyReportLinePlugIn\WeeklyReportLinePlugInClass.cs`。只記錄檔名與欄位名稱，不記錄 token 值。

**保持停用：** 舊 `Dynamics365Line\LineUtility\LineUtilityClass.cs` 還有程式內預設 token 的回退邏輯；清空資料欄位不能證明舊程式不會發送。先前停用的舊 Plugin／工作流程必須保持停用，本次不修改、重建或重新啟用它們。

**使用者 2026-11-04 追加核定：** 本次保留既有資料，不再進行資料查詢、清空或替換的引導，直接續行 trace 驗收。舊步驟／流程保持停用；本次只註冊清單內的 DiagnosticsPingPlugin，維持 trace 與空白 secureConfig。

**已完成的程式工作：** `86ca8bf83` 已完成 ChurchReport LINE 設定分離；離線測試 ChurchReport.MemberInfo.Tests 597／597、Line.Messaging.Tests 32／32 通過。這只證明程式設定與守門，不能據此勾選 P0-13 實機項目。

**完成條件：** 本次兩項資料清理已核定免除。P0-13 仍須由使用者完成追蹤設定、plan／apply／verify、trace smoke 與停用收尾，不能以核定免除取代實機驗收。

### 0.3 目前進度與下一小步

**已完成：** 使用者已確認金鑰存在、儲存 Deployment User Secrets，並完成第 2 步的三個離線命令；本次資料清理前置已核定免除。接著進入第 1 步追蹤設定，尚未執行部署或 smoke。

**現在只做：** 回到 goodshepherdback 主畫面，點「設定 → 系統管理」，先停在該畫面；回報「已開啟系統管理」後，再繼續找到系統設定中的 Plugin 追蹤選項。

**不對時怎麼辦：** 找不到「系統管理」就停下，回報看見的選單名稱；如需截圖，先遮住帳號與個資。追蹤設定完成後，再依第 3 步進行 plan。

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

**使用者執行結果（2026-11-04）：** Plugin Release 打包成功，0 錯誤、0 警告；組件檢查 `ok=true`，各項檢查均為 true，AssemblyVersion=`1.0.0.0`，大小 723456 bytes。Deployment Release 建置成功，0 錯誤、1 個 `CS1572` 警告（`Line.Messaging/LineMessagingClient.cs:840` 的 `chatId` XML 註解）；此處只記錄使用者提供的輸出，不將該警告宣稱為已驗證的既有基準。

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
| 第 0 步前置與準備 | 使用者已回報舊步驟／流程停用、3 筆作業取消、金鑰存在及 User Secrets 儲存；舊 token 與會友 LINE ID 核定保留，免除本次清理前置 |
| 建置與打包檢查 | 已由使用者執行通過；Deployment 有 1 個 CS1572 警告，詳第 2 步 |
| Plugin 追蹤設定 | 待確認 |
| 一輪 plan／apply／verify=0 | 未驗證 |
| trace smoke 與五項人工追蹤 | 未驗證 |
| 停用、收尾 verify=0、追蹤復原及資料清除 | 未驗證 |

同一事件重送不再做實機驗收：使用者接受既有「Retry Key＝來源記錄 ID」單元測試，加上 LINE 官方同 Retry Key 在 24 小時有效期內抑制重複受理的契約作為依據；這不保證訊息一定送達，也不涵蓋超過有效期的重送。
來源：[LINE 官方重試說明](https://developers.line.biz/en/docs/messaging-api/retrying-api-request/)。

GitHub CI 的 23／26 專案過渡範圍已由使用者 2026-11-04 接受；DevExpress 來源在階段 1 開始後優先處理，本機 §8 仍驗證全方案。
**文件已備妥，依使用者回報逐步續行最小驗收；P0-13 和階段 0 目前尚未完成，也不開 P1 分支。Codex 不連線 D365／LINE。**

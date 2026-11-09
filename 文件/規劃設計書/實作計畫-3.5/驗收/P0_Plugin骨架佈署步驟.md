# P0-13　Plugin 骨架最小驗收

> **P0-13 已完成（2026-11-04，台灣時間 20:41 收尾）。** 使用者先完成註冊與 verify=0，隨後全權授權 Codex 自動執行剩餘驗收、必要調查及收尾；本次授權取代先前僅由使用者操作的限制。不發送 LINE。
>
> 本頁保留可重現步驟與歷史問題摘要；本輪已完成，不需再次執行。最新結果見 §0.3 及文末結果表。
> **只連 goodshepherdback；不要貼出密碼、token、金鑰、User Secrets 或追蹤全文。**

> **LINE 憑證分離補充（2026-11-04）：** ChurchReport 的 `appsettings.json` 已清空 LINE token；正式環境改由專用環境變數提供，Development 的 `DevTest` token 只接受 User Secrets。
>
> P0-13 使用 trace，不需要 LINE channel、`TestLineUserId`、`TestChannelToken` 或任何 LINE token。Deployment 的 User Secrets 只填下面三個 Dataverse 值；本次 `secureConfig` 保持空白。
>
> **使用者 2026-11-04 追加核定：** 本次 P0-13 trace 驗收保留資料中的舊 LINE token 與會友 `new_lineid`，免除這兩項清理前置，不再等待清空才繼續。這記錄為使用者核定保留資料，不能宣稱資料已清理；舊 Plugin／工作流程維持停用。此例外限本次 trace 驗收，後續階段另依該階段規則執行。

本次只確認：以佈署專案註冊、啟用 net462 Plugin，在沙箱看到版本與雙語文字，最後停用並清理。
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

- [x] 使用者已在 **SpeechMessage.Dynamics.Deployment** 的「管理使用者密碼」儲存以下三個值；內容未由 Codex 讀取。後續第 3 步 plan 已成功讀取組織狀態，但不代表具有註冊 Plugin 的寫入權限。

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

**完成條件：** 本次兩項資料清理已核定免除。P0-13 的追蹤設定、plan／apply／verify、trace smoke 與停用收尾均已實機完成，證據見 §0.3；核定免除並非實機通過的依據。

### 0.3 本輪實機驗收已完成

**最新授權（使用者 2026-11-04）：** 使用者回報 verify=0 後，明確授權 Codex 自動執行 PowerShell、本次 D365 驗收及必要的 D365-9.1 調查，不再要求逐步貼指令。本次只操作 goodshepherdback 的 DiagnosticsPingPlugin、其 smoke task 與追蹤設定；未發 LINE，未修改其他組織、舊 Plugin／流程或伺服器。

| 已完成項目 | 證據與結果 |
| --- | --- |
| 註冊並啟用 | 使用者完成 net462 plan／apply，並回報 `Deployment state matches the registration manifest.`、結束碼 0 |
| trace smoke | Codex 20:36:38～20:37:18 執行 `p0-ping-trace`，回報 `Smoke scenario passed.`、結束碼 0 |
| 五項追蹤 | 依本次時窗及完整 Plugin 型別限定查詢，唯一一筆追蹤五項皆符合；不輸出或保存訊息區塊全文 |
| 執行環境 | 本次沙箱實際回報 `FrameworkFileVersion=4.8.4795.0`；Plugin 目標仍為 net462 |
| 停用 | 新 plan 只有同一步驟一項 `UpdateStepState`；apply=0，收尾 verify=0 |
| 追蹤復原及清理 | 20:41:18 回讀確認步驟已停用、追蹤設定為「例外狀況」（1）、本次 smoke task 殘留 0 |
| 清單與版控 | 清單已回到 false，另以新提交 `f664c685` 保存；提交後該檔 git status 無輸出，未改寫使用者的 true 提交 |

同次 correlation 僅查到 DiagnosticsPingPlugin，沒有 Plugin 例外或推播標記。這是本次限定範圍的結果，不代表已全面稽核其他歷史作業。

**已解決的阻礙：**

- net48 apply 曾回報 `0x80040216`；伺服器事件 18176 的 `TargetFrameworkVersionValidator.ValidateInternal()` 出現 FormatException，ObjectModel 為 `9.1.49.21825`。依 D21 改用 net462 後，實機註冊、verify 及 smoke 均通過。原始拒絕訊息被遮住的細節仍屬推論，不能據此認定伺服器接受框架的完整上限。
- apply 成功後 verify 曾回報 `0x80041103`。`a4649e3b` 已將錯誤查閱欄位 `secureconfigid` 改為 `sdkmessageprocessingstepsecureconfigid`，保留安全設定內容欄位 `secureconfig`；離線回歸及實機 verify 均通過。
- PowerShell 中文輸出僅部分改善，先前還原開頭兩行仍有亂碼；本次自動命令使用程序層英文 CLI 摘要，沒有宣稱已永久修復使用者視窗的編碼。

詳見[部署失敗調查報告](P0-13_部署失敗調查報告_供Claude複核.md)、主任務的 P0-13 回報及去識別化驗收證據。原始 export、追蹤全文、User Secrets、金鑰與授權內容均不進回報或版控。

**完成判定：** 最小 P0-13 實機驗收通過；搭配既有完整 00 §8、階段審查無未處理 Critical／High，及使用者已接受的 GitHub CI 過渡範圍，階段 0 為 18／18 工作包完成。額外的本輪收尾文件審查逾時、無可用輸出，已備妥審查包；任務暫留 review，取得結果後才正式封存。本次未建立 P1 分支，未開始階段 1。

## 1. 開啟 Plugin 追蹤

**做什麼：** 在 goodshepherdback 開啟「設定」，於「系統」區塊點「管理 → 系統設定 → 自訂」，把「啟用外掛程式追蹤記錄」設為「全部」。實際選單名稱是「管理」。

**預期看到什麼：** 儲存成功；稍後可在「Plugin 追蹤記錄」與「系統作業」查看本次執行。階段 0～4 的 Plugin 失敗由這兩處查看。

**完成結果（2026-11-04）：** 使用者先設為「全部」並儲存；Codex 收尾時已改回「例外狀況」，並以 API 回讀確認值為 1。

**不對時怎麼辦：** 無權限或找不到選項就停下回報，請管理員確認；驗收後改回「例外狀況」。

## 2. 建置與打包檢查

**本步已完成。** 以下保留各次建置的歷史回報；當時「尚待」項目均以 §0.3 的最新實機結果為準。

**第 10 份審查修正與複審（2026-11-04）：** 初審沒有 Critical，但發現 facade 參考可能被 System.* 規則放行的 High。已用合成 DLL 先紅後綠修復，PluginPackaging 27／27；合併前與部署 DLL 檢查皆新增獨立 `noNetStandardFacade` 守門，其他檢查保留。修正後完整 00 §8 再次通過；同一自癒入口的獨立複審為 Critical 0、High 0、Medium 0、Low 0。兩次均為單模型（使用者指定 ClaudeOnly），`reviewMode=claude-only`、`dualModelComplete=false`、`quotaBlocked=false`；現在可由使用者重新驗收，仍不代表 D365 已接受 net462。

**做什麼：** 逐行執行，每行成功才執行下一行。

**中文亂碼的處理（2026-11-04）：** 使用者回報建置訊息與路徑中的中文出現亂碼，JSON 檢查仍全數通過；目前推測為 PowerShell 與原生建置工具間的編碼不一致，尚未取得使用者視窗的實際碼頁，不能宣稱已修復。在目前視窗先執行以下設定，再繼續尚未完成的 Deployment 建置，以新輸出確認；設定只影響目前視窗及後續子程序，未修改永久環境變數或產品檔案。依據：[Microsoft：CLI console output uses UTF-8](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/8.0/console-encoding)。

```powershell
chcp 65001 > $null
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Console]::InputEncoding
$OutputEncoding = [Console]::InputEncoding
$env:DOTNET_CLI_UI_LANGUAGE = 'zh-TW'
```

```powershell
dotnet build .\SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true
powershell -NoProfile -File .\tools\PluginPackaging\Test-PluginAssembly.ps1 -AssemblyPath .\artifacts\plugins\SpeechMessage.Dynamics.Plugins.dll
dotnet build .\SpeechMessage.Dynamics.Deployment -c Release
```

**預期看到什麼：** 建置成功，已簽章 DLL 通過檢查，AssemblyVersion 固定 `1.0.0.0`，Plugin 與合併輸入符合核定的 `net462` 目標契約；Deployment Release 建置成功。這次使用者重新打包會更新部署工具預設讀取的 artifacts/plugins/SpeechMessage.Dynamics.Plugins.dll；先前 net48 plan 不可重用。

**使用者執行結果（2026-11-04）：** Plugin Release 打包成功，0 錯誤、0 警告；組件檢查 `ok=true`，各項檢查均為 true，AssemblyVersion=`1.0.0.0`，大小 723456 bytes。Deployment Release 建置成功，0 錯誤、1 個 `CS1572` 警告（`Line.Messaging/LineMessagingClient.cs:840` 的 `chatId` XML 註解）；此處只記錄使用者提供的輸出，不將該警告宣稱為已驗證的既有基準。

**使用者 net462 重新打包結果（2026-11-04）：** 第一條命令成功，0 警告、0 錯誤，ILRepack 顯示 Merge succeeded，已更新預設部署 DLL。四個合併輸入的 targetFramework／noNetStandardFacade 均為 true：Plugins、Localization、Core 為 v4.6.2，Newtonsoft.Json 為 v4.5。這是合併前檢查；合併後 DLL 的完整檢查與本輪 Deployment 建置尚待執行，不能據此宣稱 D365 註冊成功。

**使用者 net462 合併後 DLL 檢查結果（2026-11-04）：** 第二條命令成功，`ok=true`；readable、strongName、targetFramework、allowedReferences、noNetStandardFacade、noDrawing、diagnosticsPlugin、size 全為 true，AssemblyVersion 為 `1.0.0.0`，大小 723456 bytes。已通過本輪部署 DLL 的完整離線檢查；Deployment 重建與 D365 實機註冊仍待執行。

**使用者 Deployment 重建與編碼調整結果（2026-11-04）：** 已在同一視窗套用上述編碼設定並執行第三條命令，Release 建置成功，0 個警告、0 個錯誤。中文路徑、「建置成功」及結尾統計恢復正常，開頭兩行還原訊息仍有亂碼；這只證實部分輸出改善，不能宣稱編碼問題已完全修復。第 2 步現已全部通過，接續重新 plan；D365 註冊與 smoke 仍未驗證。

**Codex 離線驗證（2026-11-04）：** net462 正式簽章打包及組件檢查成功，0 錯誤；AssemblyVersion=`1.0.0.0`、FileVersion=`1.0.0.37`、大小 723456 bytes，各項檢查均為 true。產物位置為 `artifacts/plugins/net462-p0-13-20261104/SpeechMessage.Dynamics.Plugins.dll`。Release 全方案建置 0 警告、0 錯誤；細項與套件資產記錄於[部署失敗調查報告第 8 節](P0-13_部署失敗調查報告_供Claude複核.md)。尚未連線 D365；net462 能否註冊待使用者重驗。

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

**使用者執行結果（2026-11-04）：** 清單已改為 true 並儲存；plan 成功，只列 CreateAssembly、DiagnosticsPingPlugin 的 CreateType，以及 Create／task／PostOperation／Asynchronous／rank 1 的 CreateStep，秘密佔位為無。此為首次 plan 結果；第 4 步 apply 失敗後須重新 plan，不保證新 plan 仍為三項建立。

**重新 plan 核對結果（2026-11-04）：** 使用者回報 UTC `2026-11-04T06:14:02.6994698+00:00`（台灣時間 14:14）的新計畫，目標仍為 goodshepherdback，只有同一骨架的 CreateAssembly／CreateType／CreateStep，秘密佔位為無；組織指紋、清單與組件雜湊均與首次一致。新 Plan SHA-256 為 `C7C599656278D8E1A6BFB06894D5EC7D16BA182427FA6BBE55C9749D4B84DDDA`，核對符合預期；使用者已執行這份新 plan 的 apply，但仍失敗（見第 4 步），不能將 plan 成功當成註冊成功。

**net462 重新驗收規則（使用者 2026-11-04 核定）：** 舊計畫綁定 net48 組件雜湊，不能重用。待本機 net462 Release 打包檢查通過並由使用者取得新部署 DLL 後，重新執行第 3 步 plan，立即取得該次 `$plan` 並審核 `plan.md`。預期仍只有 CreateAssembly／CreateType／CreateStep；如果出現其他操作或新 plan 的內容與 trace 骨架不符，停止，不要 apply。

**net462 新 plan 核對結果（2026-11-04）：** 使用者提供 UTC `2026-11-04T12:06:26.9763980+00:00`（台灣時間 20:06）的新計畫，組織為 goodshepherdback，只有 CreateAssembly、DiagnosticsPingPlugin 的 CreateType，以及 Create／task／PostOperation／Asynchronous／rank 1 的 CreateStep；User Secrets 佔位為無，符合本次 trace 骨架預期。清單與組織指紋沿用既定值，組件 SHA-256 已改為 `4F70115499412056FD84A2DDCFE0A853CEED11E55050594266A877D785E74B11`；本次 Plan SHA-256 為 `CC89D4891576FA9579257C22583D60C10002A0CFF6A88BD38BF14D5E1D7D2E51`。本項依使用者貼出的計畫審核；此份 plan 後續 apply 已成功（見第 4 步），後續 verify 已由使用者回報 0，並由 Codex 完成 smoke 及停用收尾；詳見 §0.3。

**不對時怎麼辦：** 出現其他組織、步驟、push 模式或未預期更新就不套用，回報操作種類。不得手改 plan hash；清單或 DLL 改過就重新 plan。

## 4. 套用並確認 verify=0

**做什麼：** 審核第 3 步 plan.md 後，沿用該次已固定的 `$plan`。

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $plan
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
```

**預期看到什麼：** apply=0；verify 輸出 `Deployment state matches the registration manifest.`，結束碼 **0**。apply 的 export.json 只留本機 artifacts。

**本輪結果（2026-11-04）：** 使用者已回報 net462 apply 成功；`a4649e3b` 修正查詢欄位後，使用者重新 verify 成功，結束碼 0。因此未重複 apply，也未重建已通過檢查的 Plugin。

**已修正的 verify 缺陷：** 步驟安全設定查閱必須用 `sdkmessageprocessingstepsecureconfigid`，原 `secureconfigid` 導致 `0x80041103`。獨立 schema 回歸先取得 5 個功能紅燈，修正全部 8 處後 Deployment 56／56；覆蓋率 84.95%。`a4649e3b` 前完整 00 §8 為 Release 0 警告／0 錯誤、方案 1125／1125（ChurchReport 596／596）、Debug tracer 7／7，編碼／重複碼／I18n 均通過。其獨立審查無缺陷：單模型（使用者指定 ClaudeOnly），reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=true。

**不對時怎麼辦：** 只記固定摘要與數字錯誤碼，不貼原始 fault、export 或設定；verify 非 0 不算通過。需要再次寫入時先重新 plan 並審核，不重用已套用或失敗的 plan，不改回手動註冊。

## 5. smoke trace，核對五項追蹤

**做什麼：**

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- smoke --org goodshepherdback --scenario p0-ping-trace
```

開啟「**設定 → 自訂 → 外掛程式追蹤記錄**」，依建立時間找最新一筆 **DiagnosticsPingPlugin**，打開查看「**訊息區塊**」。以本次 smoke 時段核對，必要時對照「系統作業」，不要把舊記錄當成本次成功；確認：

- [x] `P0-PING OK`。
- [x] `Assembly=` 為 SpeechMessage.Dynamics.Plugins，版本 `1.0.0.0`。
- [x] `FrameworkFileVersion=` 為 **4.8.x**（mscorlib）；不能用 `EnvironmentVersion=` 代替。
- [x] `zh-TW=連線測試成功`。
- [x] `qps-ploc=［連線測試成功ǂǂǂ］`。

**預期看到什麼：** 結束碼 0、`Smoke scenario passed.`，五項確認符合；工具最多等 3 分鐘，結束時嘗試刪除自己建立的 `[smoke]` task。

**本輪結果（2026-11-04）：** Codex 依全權授權執行 smoke=0，並透過部署工具既有固定端點 connector 查核本次時窗的唯一 DiagnosticsPingPlugin 追蹤，五項全部通過；FrameworkFileVersion 實值為 `4.8.4795.0`。原始訊息區塊只在程序記憶體中比對，未輸出或存檔；查核連線在 finally Dispose。smoke task 殘留 0；沒有 Plugin 例外或推播標記。

**不對時怎麼辦：**

| 結果 | 處理 |
| --- | --- |
| FrameworkFileVersion 不是 4.8.x | 這是伺服器 mscorlib 執行環境欄位，不是 Plugin 目標；記錄固定資訊並回報，不據此修改已核定的 net462 目標 |
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

預期沒有輸出，表示此檔沒有已暫存或未暫存的未提交變更。**本次例外：** 使用者的 `714465fd` 已提交 true，因此改回 false 時會先出現修改；完成伺服器停用與收尾 verify=0 後，須核對只有本次預期的 false 修改，再以**新提交**保存 false，最後重跑這項檢查。保留原提交與歷史，不為消除輸出而還原成 true，也不覆寫其他修改。

**預期看到什麼：**

- [x] 同一測試步驟已停用、清單回到 false，收尾 verify=0；`f664c685` 提交後 git 顯示清單沒有未提交變更。
- [x] Plugin 追蹤設定已改回「例外狀況」，API 回讀為 1。
- [x] 本次 smoke task 已清除；同次 correlation 僅有 DiagnosticsPingPlugin，trace 無例外或推播標記。

**不對時怎麼辦：** 無法停用或清理就立即回報，由使用者處理 D365 停用／清理，不再建立測試資料。

回覆下表即可，不貼任何秘密或會友資料：

| 項目 | 請填成功／未通過及固定錯誤碼 |
| --- | --- |
| 第 0 步前置與準備 | 使用者已回報舊步驟／流程停用、3 筆作業取消、金鑰存在及 User Secrets 儲存；舊 token 與會友 LINE ID 核定保留，免除本次清理前置 |
| 建置與打包檢查 | 第 2 步全部通過：使用者 net462 Release 打包、合併後 DLL 完整檢查及 Deployment Release 重建成功；兩次建置皆 0 警告／0 錯誤，DLL AssemblyVersion 1.0.0.0、723456 bytes |
| Plugin 追蹤設定 | 已完成：先設全部（2），收尾回讀為例外狀況（1） |
| 一輪 plan／apply／verify=0 | 已完成：使用者 net462 註冊成功，欄位修正後實機 verify=0 |
| trace smoke 與五項追蹤 | 已完成：smoke=0，五項全部符合，FrameworkFileVersion=4.8.4795.0 |
| 停用與收尾 verify=0 | 已完成：新 plan 僅一項 UpdateStepState，apply=0、verify=0，伺服器步驟已停用 |
| 清單與資料清除 | false 已提交 f664c685，該檔無未提交變更；本次 smoke task 殘留 0；舊 token／會友 LINE ID 依核定保留 |

同一事件重送不再做實機驗收：使用者接受既有「Retry Key＝來源記錄 ID」單元測試，加上 LINE 官方同 Retry Key 在 24 小時有效期內抑制重複受理的契約作為依據；這不保證訊息一定送達，也不涵蓋超過有效期的重送。
來源：[LINE 官方重試說明](https://developers.line.biz/en/docs/messaging-api/retrying-api-request/)。

GitHub CI 的 23／26 專案過渡範圍已由使用者 2026-11-04 接受；DevExpress 來源在階段 1 開始後優先處理，本機 §8 仍驗證全方案。
**P0-13 實機驗收已完成，階段 0 工作包為 18／18。使用者先回報啟用註冊 verify=0；Codex 依最新全權授權完成 smoke、五項追蹤、停用與清理。本次未建立 P1 分支、未執行 LINE 推播、未推送、未合併或改寫歷史。**


### 本輪收尾文件審查待續

2026-11-04 透過同一自癒入口呼叫本輪補充審查，結果逾時且沒有可用輸出（exitCode=124、timedOut=true）。reviewMode=claude-only、dualModelComplete=false、quotaBlocked=false、ok=false；不能當成配額降級、雙模型或已通過的審查。依逾時規則保存審查包後停止，不再新增線上操作。既有產品審查結果保留，本輪只改清單停用值、文件及任務紀錄；任務保留 review，尚未正式封存。完整輸入與摘要見 `.ccg/dual-model-runs/20261104-204858-p013-live-acceptance-closeout-reviewer/`，不含原始追蹤或秘密內容。

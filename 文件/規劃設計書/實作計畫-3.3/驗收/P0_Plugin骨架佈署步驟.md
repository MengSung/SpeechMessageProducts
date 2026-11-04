# P0-13　Plugin 骨架最小驗收

> 使用者 2026-11-04 核定。清理完成後預留約 1 小時；全部由使用者執行，Codex 不連線 D365／LINE。
> **只連 goodshepherdback；不要貼出密碼、token、金鑰、User Secrets 或追蹤全文。**

本次只確認：以佈署專案註冊、啟用 net48 Plugin，在沙箱看到版本與雙語文字，最後停用並清理。
不需要測試 LINE channel，也不需要 Six Labors 授權：Plugins 與 Deployment 的直接、間接相依均無 ImageSharp。
推播移至 03 的「P2-04 前置驗收」；Plugin 自身 FileVersion 追蹤、更新與回復移至 04 階段 5，現在不改 Plugin。

所有命令都在 **P0 worktree 的方案根目錄**以 PowerShell 執行。每行結束碼必須為 0 才執行下一行；出錯停止下一項，仍按第 6 步收尾。

## 0. 前置確認

**做什麼：** 全部確認後才執行部署命令；目前未收到清理回報，維持未勾選。

- [ ] goodshepherdback 的舊 Plugin 步驟、自訂工作流程已停用。
- [ ] 等候中、可重試、排程中的系統作業已取消；資料中的 LINE token 已清除或換成測試 channel；會友 `new_lineid` 已清空。P1-12 仍用此組織，不能省略。
- [ ] `SPEECHMESSAGE_PLUGIN_KEYFILE` 已設定且目前程序可使用原金鑰；只確認檔案存在，不讀取、複製或貼出內容。
- [ ] 在 Visual Studio 對 **SpeechMessage.Dynamics.Deployment** 選「管理使用者密碼」，填好以下三個值；填完關閉檔案，不提交。

```json
{
  "Deployment": {
    "Organizations": {
      "goodshepherdback": {
        "OrganizationUrl": "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
        "Username": "<自行填入>",
        "Password": "<自行填入>"
      }
    }
  }
}
```

**預期看到什麼：** 組織為 goodshepherdback，UserSecretsId 是 `SpeechMessage.Dynamics.Deployment.P0`。trace 只需要這三個值，不要求 `TestLineUserId`、`TestChannelToken`，也不讀 ChurchReport 設定。

**不對時怎麼辦：** 清理未完成就不連線。金鑰無法使用、User Secrets 找不到或要求額外 LINE 值時，回報固定錯誤型別／代碼，不填假值、不換金鑰。

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

**做什麼：** 開啟 `d365\registration\plugins.registration.json`，保留唯一的 DiagnosticsPingPlugin：Create／task／PostOperation／Asynchronous／rank 1。第一次 plan **之前**確認以下三個欄位片段：

```json
"unsecureConfig": "{\"mode\":\"trace\",\"culture\":\"zh-TW\"}",
"secureConfig": "",
"enabledByOrganization": { "goodshepherdback": true }
```

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- plan --org goodshepherdback
```

查看 `artifacts\d365\goodshepherdback\<本次時間與GUID>\plan.md` 與 plan.json，記下**剛審核過**的 plan.json 路徑。

**預期看到什麼：** 全新安裝只列 CreateAssembly／CreateType／CreateStep，沒有秘密佔位。工具支援 CreateStep 時直接啟用，不必先停用註冊再跑第二輪；trace 不發出 LINE HTTP 請求。若已安裝同一骨架，可能只有啟用狀態更新或無差異，須確認為預期的同一個測試步驟。

**不對時怎麼辦：** 出現其他組織、步驟、push 模式或未預期更新就不套用，回報操作種類。不得手改 plan hash；清單或 DLL 改過就重新 plan。

## 4. 套用並確認 verify=0

**做什麼：** 尖括號換成第 3 步剛審核過的實際目錄，再逐行執行。

```powershell
$tracePlan = '.\artifacts\d365\goodshepherdback\<本次plan目錄>\plan.json'
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $tracePlan
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
```

**預期看到什麼：** apply 結束碼 0、測試步驟已註冊並啟用；verify 結束碼 **0**、無差異。apply 先在**該次 apply 目錄**保存 export.json；留在本機 artifacts，不上傳。

**不對時怎麼辦：** 連線、權限、簽章或沙箱錯誤就停下回報並做第 6 步，不改回手動註冊。trace 的 verify=1 或 3 都未通過，不能略過。

## 5. smoke trace，人工看五項追蹤

**做什麼：**

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- smoke --org goodshepherdback --scenario p0-ping-trace
```

在「系統作業」找本次 UTC 時段的骨架作業，對照「Plugin 追蹤記錄」確認：

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
$disablePlan = '.\artifacts\d365\goodshepherdback\<本次停用plan目錄>\plan.json'
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $disablePlan
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
```

先審核本次停用 plan 再填路徑，不沿用第 4 步舊 plan。把追蹤改回「例外狀況」，確認本次 smoke task 已清除。

**預期看到什麼：**

- [ ] 測試步驟停用、清單 false，收尾 verify=0。
- [ ] Plugin 追蹤設定已改回「例外狀況」。
- [ ] smoke 資料已清除，沒有觸發其他舊流程或通知。

**不對時怎麼辦：** 無法停用或清理就立即回報，由使用者處理 D365 停用／清理，不再建立測試資料。

回覆下表即可，不貼任何秘密或會友資料：

| 項目 | 請填成功／未通過及固定錯誤碼 |
| --- | --- |
| 第 0 步清理與準備 | 未回報 |
| 追蹤設定、建置與打包檢查 | 待使用者執行 |
| 一輪 plan／apply／verify=0 | 未驗證 |
| trace smoke 與五項人工追蹤 | 未驗證 |
| 停用、收尾 verify=0、追蹤復原及資料清除 | 未驗證 |

同一事件重送不再做實機驗收：使用者接受既有「Retry Key＝來源記錄 ID」單元測試，加上 LINE 官方同 Retry Key 在 24 小時有效期內抑制重複受理的契約作為依據；這不保證訊息一定送達，也不涵蓋超過有效期的重送。
來源：[LINE 官方重試說明](https://developers.line.biz/en/docs/messaging-api/retrying-api-request/)。

GitHub CI 的 23／26 專案過渡範圍已由使用者 2026-11-04 接受；DevExpress 來源在階段 1 開始後優先處理，本機 §8 仍驗證全方案。
**文件已備妥就停下，等使用者回報清理與最小驗收成功；P0-13 和階段 0 目前尚未完成，也不開 P1 分支。**

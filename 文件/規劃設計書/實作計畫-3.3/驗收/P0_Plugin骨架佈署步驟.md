# P0-13：Plugin 骨架佈署與使用者驗收步驟

文件日期：2026-11-04（Asia/Taipei）。唯一目標組織：**goodshepherdback（開發測試組織）**。
依據：01 的 P0-13、90 附錄 G、使用者 2026-11-03／11-04 核定；程式核對基準 e3f89eb6。

**目前狀態：文件已準備，尚未執行。第 0 步全部未勾選。**
Codex 沒有連線 D365／LINE，也沒有執行本文件中的佈署、推播、重送或回復操作。
原 11 份及獨立第 09 份外部審查已通過，為「單模型（使用者指定 ClaudeOnly）」，不代表真實環境驗收通過。

**停止事項：01 的 P0-13 第 8 項要求追蹤顯示新的 Plugin FileVersion，但目前 Plugin 沒有輸出此欄位。**
現有 `Assembly=` 是固定的 AssemblyVersion，`FrameworkFileVersion=` 是 mscorlib 的檔案版本，兩者皆不能代替 Plugin FileVersion。
依 00 §9.6 保留原驗收要求並回報；尚未補修或取得使用者核定前，不得把第 8 項標為通過。
本文件保留其餘待執行步驟供審閱，現在不開始線上驗收、不宣稱階段 0 完成。

## 0. 前置確認：全部確認後才可開始

以下每項由使用者實際完成後填寫；目前沒有收到 goodshepherdback 清理完成的回報，全部維持未勾選。

- [ ] 已確認登入的是 goodshepherdback，沒有指向任何正式組織。
- [ ] 等候中、可重試及排程中的舊系統作業已取消，避免啟用測試時重播舊通知。
- [ ] 舊 Plugin 步驟已停用；舊自訂工作流程已停用。
- [ ] 資料中的 LINE token 已清除或換成測試 channel 的 token。
- [ ] 會友 `new_lineid` 已清空，只保留使用者自己的測試聯絡人／測試帳號。
- [ ] 使用者已明確回報上述清理完成；回報前，任何工具或測試不得連線。
- [ ] 已建立測試 LINE channel；測試手機為使用者自己的帳號，已可接收該測試 channel 的訊息。
- [ ] 正式強式名稱金鑰位於 repo 外，當次程序可讀到 `SPEECHMESSAGE_PLUGIN_KEYFILE`；只確認檔案存在，不顯示或複製內容。
- [ ] 當次程序具備有效 Six Labors 一般金鑰設定；不輸出授權內容，不把本機路徑寫入 repo。
- [ ] Deployment 專案自己的 User Secrets 已設定，且具有測試組織所需的註冊、追蹤與測試資料權限。
- [ ] 已處理 Plugin FileVersion 追蹤缺口並完成相應驗證，或取得使用者對驗收方式的明確核定；目前尚未具備。
- [ ] 完整含 ChurchReport 的 GitHub CI 已全綠，或使用者已明確核定接受目前過渡狀態；目前兩者皆未確認。

### 帳密只填在 Deployment 的 User Secrets

在 Visual Studio 方案總管對 **SpeechMessage.Dynamics.Deployment** 選「管理使用者密碼」，
在使用者電腦的 secrets.json 填寫下列結構。此專案固定 UserSecretsId 為 `SpeechMessage.Dynamics.Deployment.P0`。
以下為格式範本，尖括號內容必須由使用者自行填入，不能把範本當成有效設定。

```json
{
  "Deployment": {
    "AllowedOrganizations": ["goodshepherdback"],
    "Organizations": {
      "goodshepherdback": {
        "OrganizationUrl": "https://goodshepherdback.speechmessage.com.tw/XRMServices/2011/Organization.svc",
        "Username": "<使用者自行填寫>",
        "Password": "<使用者自行填寫>",
        "TestLineUserId": "<使用者自己的測試LINE帳號>",
        "TestChannelToken": "<測試channel的token>"
      }
    }
  }
}
```

此工具不讀 ChurchReport 的 appsettings 或 User Secrets；沒有獨立的 Domain 鍵，Username 依既有地端登入格式填寫。
本階段只支援 User Secrets，Windows 認證管理員是後續選項。不得以命令列傳入密碼或 token，
不得提交 secrets.json，也不要貼出設定檔、畫面中的真實值或原始連線例外。

所有後續命令均在本 P0 worktree 的方案根目錄執行。此文件中的 `apply`、`smoke`、`rollback` 及 D365 畫面操作全部由使用者執行。
不使用 Plugin Registration Tool 註冊或更新；P0 只支援 `plan`、`apply`、`verify`、`export`、`rollback`、`smoke`。

## 1. 開啟 Plugin 追蹤

完成第 0 步後，在 goodshepherdback 的「設定 → 系統管理 → 系統設定 → 自訂」，
把「啟用外掛程式追蹤記錄」改為「全部」。記錄原始設定，驗收完改回「例外狀況」。

階段 0～4 的 Plugin 失敗只從 **系統作業** 與 **Plugin 追蹤記錄** 查看；
net48 沙箱不寫本機 Exception.log、不直接通知管理者 LINE。階段 5 才有 D365 失敗彙總通知。

## 2. 建置並檢查簽章 DLL

```powershell
dotnet build .\SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true -p:SpeechMessageBuildNumber=1
powershell -NoProfile -File .\tools\PluginPackaging\Test-PluginAssembly.ps1 -AssemblyPath .\artifacts\plugins\SpeechMessage.Dynamics.Plugins.dll
dotnet build .\SpeechMessage.Dynamics.Deployment -c Release
```

依序執行，每一行結束碼都必須為 0。若首版已用過建置序號 1，改用已確認未使用且遞增的序號。
打包檢查應確認簽章、固定 AssemblyVersion 1.0.0.0、net48／TargetFramework 與核定合併參考清單。
一般 Release 建置不會打包；這裡必須明確指定 `SpeechMessagePackagePlugin=true`。
若缺金鑰、金鑰路徑無效或授權檢查失敗，停下回報錯誤碼，不產生替代金鑰、不關閉檢查。

在本機另存第一版已合併 DLL 至自己選定的 repo 外驗收資料夾，供第 9 步比對；只保存 DLL，不複製金鑰。
可在檔案內容的「詳細資料」查看本機 FileVersion；這只能證明本機產物，不能當作沙箱已載入新版的證據。

## 3. 產生第一份 plan：先註冊為停用

檢查 `d365\registration\plugins.registration.json`：只含 `DiagnosticsPingPlugin`，
Create／task／PostOperation／Asynchronous／rank 1，trace 模式、zh-TW，secure 設定空白。
目前 `enabledByOrganization.goodshepherdback` 為 **false**；先保持停用。

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- plan --org goodshepherdback
```

查看 `artifacts\d365\goodshepherdback\<本次UTC時間與GUID>\plan.md` 及同目錄 plan.json。
全新、尚未部署本骨架的組織預期只有 CreateAssembly／CreateType／CreateStep；已存在時可能是無操作或更新。
若有未預期操作、其他組織、簽章或主次版本不符，停下回報，不手改 plan 的 hash 或指紋。
清單、DLL、組織狀態改變後必須重產 plan，不能沿用舊檔。

每個命令都建立不同的 UTC＋GUID 目錄。以下變數須由使用者換成**剛審核過的那一份**實際路徑，不能照抄尖括號。

```powershell
$tracePlan = '.\artifacts\d365\goodshepherdback\<本次plan目錄>\plan.json'
```

## 4. 套用、核對，再明確啟用測試步驟

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $tracePlan
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
```

trace 模式沒有秘密佔位，預期 `verify` 為 0，表示清單、產物與公開設定一致。
`apply` 會在任何寫入前把現況存成該次 **apply 目錄** 的 export.json；它不在前一份 plan 目錄。
主控台目前只顯示檔名，請依開始／完成 UTC、目錄與 export 的組織／版本確認配對，在本機記下實際位置。
不要依「最新 export」推測是哪一份；尤其 apply 失敗時，先確認正確備份再決定後續。

接著只把清單的 `enabledByOrganization.goodshepherdback` 改為 **true**，保持 trace 模式。
重新執行第 3 步 plan，審核應只變更同一筆測試步驟的啟用狀態，再以新 plan 執行本節 apply／verify。
不要直接繼續使用 `$tracePlan` 舊路徑。只有明確啟用後，Create task 才會觸發骨架。

在 D365 的測試步驟資料列確認並於本機記下其資料庫 ID；此後變更模式與更新 DLL 應保留同一 ID。
ID 只在自己驗收紀錄中比對，不貼出會友或業務記錄內容。

## 5. smoke：追蹤模式

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- smoke --org goodshepherdback --scenario p0-ping-trace
```

預期結束碼 0、`Smoke scenario passed.`。工具建立自己的 `[smoke]` task，最多等待 3 分鐘，
成功、失敗或取消皆會嘗試刪除該 task；若清理失敗仍須回報，不能只看前面的成功文字。

在對應 UTC 時段的「系統作業」找本骨架作業，再從 correlation 對照「Plugin 追蹤記錄」，只查看本次測試。
目前 smoke 判定只檢查固定標記，以下版本及精確文字要由使用者人工確認：

- [ ] `P0-PING OK`。
- [ ] `Assembly=` 顯示 SpeechMessage.Dynamics.Plugins 與固定 AssemblyVersion 1.0.0.0。
- [ ] `FrameworkFileVersion=` 符合伺服器 mscorlib 4.8.x；`EnvironmentVersion=` 不能代替此檢查。
- [ ] `zh-TW=連線測試成功`。
- [ ] `qps-ploc=［連線測試成功ǂǂǂ］`。
- [ ] 本次測試 task 已刪除，沒有意外觸發其他舊流程或通知。

若版本不符、追蹤沒有資料或 smoke 逾時，停下回報；使用者確認非同步服務、沙箱處理服務及追蹤設定。
不得自行降低目標框架、改回手動註冊或解除防護。

## 6. smoke：測試 channel 推播模式

只修改清單中同一個步驟的以下兩個欄位，其他身份欄位不變，且保持 goodshepherdback 為 true：

```json
"unsecureConfig": "{\"mode\":\"push\",\"culture\":\"zh-TW\",\"lineUserId\":\"${secret:TestLineUserId}\"}",
"secureConfig": "${secret:TestChannelToken}"
```

這是 JSON 欄位片段，須放回原清單步驟中；只存佔位名稱，不把實值貼入清單。
重新 plan，確認是同一筆步驟設定更新，plan／Markdown 沒有解析後的 token 或 lineUserId；審核後 apply 新 plan。

```powershell
$pushPlan = '.\artifacts\d365\goodshepherdback\<本次push-plan目錄>\plan.json'
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- apply --org goodshepherdback --plan $pushPlan
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- smoke --org goodshepherdback --scenario p0-ping-push
```

含秘密佔位的 `verify` **預期為 3（未完成）**，表示公開 metadata 一致、秘密未比對；不是完整成功，也不能當作錯誤忽略。
若為 1 表示公開設定仍有差異，停下。確認為固定的未比較秘密訊息後，由本節真實投遞另行驗證，記錄結果。
plan 不偵測或套用相同模板的秘密輪替；只改 User Secrets 不保證已部署的秘密跟著更新，需要輪替時另行核定，不能改 plan 欺騙守門。

- [ ] 手機收到一則「P0 推播測試 …」，收件者是使用者自己的測試帳號。
- [ ] smoke 結束碼 0，追蹤有 `Outcome=Accepted`、`Status=200` 與有效 `RequestId=`（來自 x-line-request-id）。
- [ ] 追蹤沒有 Token、訊息內文或 lineUserId；回報只提供固定分類、狀態碼及已遮蔽的驗收結果。
- [ ] 測試 task 已清除；此項不代表重送去重已通過。

若 D365 沙箱無法連到 LINE API，請使用者處理防火牆／Proxy；直接推播尚未驗證可行前，不進階段 2 推播工作。

## 7. 同一事件重送：不能用新 task 代替

Retry Key 取自 `context.PrimaryEntityId`。必須重試**同一筆系統作業／同一個 task 事件**，保持清單與測試 channel 不變。
在 D365「系統作業」確認該作業是否真的允許 Resume／Retry；只有使用者執行這項寫入操作。
預期仍使用同一個 Retry Key，手機不會再收到第二則，追蹤可對照去重結果。

**目前驗收限制：** smoke 結束會刪除它自己的 task，而且已成功的系統作業可能沒有可用的重試操作。
若無法重試原作業／原記錄，記「未驗證」並停下回報，另行核定可重現的測試方式；
不得另跑一次 smoke、複製／新建 task、重建步驟或只靠單元測試，當作真實重送已通過。

- [ ] 確認重試的是同一事件，而非新事件。
- [ ] 同一事件重試後沒有第二次投遞；若不可執行，維持未勾選。

## 8. 只更新 FileVersion：目前阻擋，待核定補修

原驗收要求為：AssemblyVersion 維持 1.0.0.0，只遞增 FileVersion，更新內容、不重建步驟，追蹤顯示新的 FileVersion。
目前 Plugin 沒有輸出自己的 FileVersion，故**本節不能完成，也不能把本機檔案版本或 FrameworkFileVersion 當作通過證據**。
依 00 §9.6 回報此差異；此文件不變更原計畫，也沒有加入未經核定的 Plugin 實作。

待此缺口修正並完成測試／必要複審後，使用者才依序操作：

1. 暫時切回 trace 模式（unsecureConfig 與第 3 節相同、secureConfig 空白），保持 enabled=true；plan → 審核 → apply → verify=0。
2. 保存此時第一版 DLL 與清單，記錄原步驟 ID；不更換簽章金鑰、組件名稱或 AssemblyVersion。
3. 把 SpeechMessageBuildNumber 改為比第 2 節大的有效整數，例如首版為 1 時使用 2：

```powershell
dotnet build .\SpeechMessage.Dynamics.Plugins -c Release -p:SpeechMessagePackagePlugin=true -p:SpeechMessageBuildNumber=2
powershell -NoProfile -File .\tools\PluginPackaging\Test-PluginAssembly.ps1 -AssemblyPath .\artifacts\plugins\SpeechMessage.Dynamics.Plugins.dll
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- plan --org goodshepherdback
```

4. 審核新 plan 應只有 UpdateAssembly；若要建立步驟／型別或變更設定，停下檢查，不套用。
5. apply 前記下本次新的操作目錄，套用新 plan；保留該次 **apply 前的 export.json**，供第 9 節使用。
6. verify=0 → smoke trace；確認原步驟 ID 沒變、沙箱追蹤顯示更新後的 Plugin FileVersion（待補修後的實際欄位）。

- [ ] 新版 DLL 打包檢查通過且只有 FileVersion／建置資訊遞增。
- [ ] plan 只有 UpdateAssembly，步驟資料庫 ID 未變。
- [ ] 沙箱顯示新的 Plugin FileVersion；目前沒有該追蹤欄位，維持未勾選。

## 9. 回復上一版：只測組件內容與步驟啟用狀態

本節在第 8 節具備可執行條件且完成後才進行。使用**第 8 節更新 apply 前產生的 export.json**，不是初次安裝前的空組織備份。
rollback 只復原受管理組件內容與既有步驟啟用狀態，**不復原 unsecure／secure 設定或秘密**。
因此第 8、9 節都保持同一 trace 設定；不要混入 token 輪替或模式切換。

```powershell
$beforeUpdateExport = '.\artifacts\d365\goodshepherdback\<第8節apply的目錄>\export.json'
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- rollback --org goodshepherdback --from $beforeUpdateExport
```

先確認 rollback 結束碼 0。再把本機 `artifacts\plugins\SpeechMessage.Dynamics.Plugins.dll` 換回第 8 節保存的第一版已合併 DLL，
保持清單為同一 trace 設定，再執行 verify；否則工具仍拿新版本機 DLL 比對，出現 UpdateAssembly 是正確結果，不能誤稱 rollback 失敗。

```powershell
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- verify --org goodshepherdback
dotnet run -c Release --no-build --project .\SpeechMessage.Dynamics.Deployment -- smoke --org goodshepherdback --scenario p0-ping-trace
```

- [ ] verify=0，原步驟 ID 不變且狀態符合備份。
- [ ] smoke 通過，沙箱顯示第一版 Plugin FileVersion（同樣待第 8 節缺口補修）。
- [ ] 沒有把 rollback 當成秘密或所有設定的完整還原工具。

## 10. 收尾：停用、恢復追蹤、確認清理

成功或中途停止後都要由使用者確認測試步驟的停用狀態，避免稍後建立 task 意外推播。
把清單恢復 trace 模式、secureConfig 空白、`enabledByOrganization.goodshepherdback=false`，
重新 plan → 審核 → apply 新 plan → verify=0；確認同一筆步驟已停用。
若佈署工具故障而無法停用，立即回報並由使用者處理 D365 的停用操作，不再建立測試資料。

- [ ] goodshepherdback 的本次測試步驟已停用；清單也維持 false。
- [ ] 「啟用外掛程式追蹤記錄」改回「例外狀況」。
- [ ] smoke 測試資料已清除；如有清理失敗，記錄固定錯誤型別並回報，不貼資料內容。
- [ ] plan／export 留在本機 ignored artifacts，沒有提交秘密、會友資料或追蹤全文。
- [ ] 測試使用的 User Secrets 及測試 channel 由使用者依自己的保管方式處理；不將其值寫入驗收回報。

## 使用者回報表（只填結果，不填秘密或會友資料）

| 項目 | 目前結果 |
| --- | --- |
| goodshepherdback 清理與第 0 步 | 未回報，未執行 |
| 追蹤設定／簽章打包／Deployment 建置 | 待使用者執行 |
| 初次 plan／apply／trace verify=0 | 未驗證 |
| trace smoke／精確雙語／伺服器 4.8.x | 未驗證 |
| push metadata verify=3（未比較秘密）／測試手機實收 | 未驗證 |
| 同一事件重送不重複 | 未驗證；原事件須可重試，不可另建事件代替 |
| 只更新 FileVersion／原步驟不重建 | 阻擋：Plugin 尚未輸出自身 FileVersion |
| rollback 組件／啟用狀態 | 未驗證；不涵蓋設定／秘密復原 |
| 測試步驟停用／追蹤改回例外狀況 | 待使用者確認 |
| 完整 GitHub CI 或明確接受過渡 | 待使用者決定 |

所有必要項目經使用者回報成功後，才可判定階段 0 是否通過。文件完成、本機測試全綠或審查放行均不代表真實環境已驗收。
目前依「寫好步驟後停下」及 00 §9.6 停止，不開階段 1 分支、不進行 D365／LINE 操作。

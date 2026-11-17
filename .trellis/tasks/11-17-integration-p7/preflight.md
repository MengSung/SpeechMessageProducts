# 階段7 前置缺項清單

2026-11-17 12:44 Asia/Taipei (UTC+08:00)：本次僅打包與唯讀連線 goodshepherdback，D365 writes=0、LINE sends=0；未連正式組織。

| 項目 | 給哪個單元 | 實際狀態與證據 | 後續處理 |
| --- | --- | --- | --- |
| C:/smv 僅有 guard | 守門 | 符合；無多餘資料夾 | 無須處理 |
| 程序提升權限／VM憑證 | 前置A | Elevated=False；test-vm-admin.clixml 存在；服務名稱未核對 | 使用者見下方三選一；Codex不自行提升 |
| 兩DLL打包與檢查 | 所有實機 | 本次打包成功；Plugins 1147904 bytes、Workflows 1131008 bytes；Test-PluginAssembly各ok=true | 新產物保留artifacts/p7/repacked-start；未部署 |
| verify與新舊狀態 | 所有實機 | verify=0；22個新步驟停用；31個舊組件、18舊步驟，舊步驟/流程啟用皆0 | 以雜湊等於組織現況的P5已驗收DLL核對；見下方差異說明 |
| D34 | C-b、P7-02 | 測試contact綁定1、其他0；Channel測試1、空0、其他0；權威身分matched | 每輪Live前再核對；本次未改資料 |
| User Secrets完整鍵名 | C-b、P7-02 | 下列九鍵均存在，只檢查存在且非空、不輸出值 | 無缺鍵 |
| 待辦起點 | C-b、P7-02 | new_outboundtask總數0（因此各類型Pending皆0） | 演練收尾回0 |
| 引用舊活動的流程 | P7-01b、P7-02 | 23個流程全部停用，引用14種舊活動型別，型別/筆數見下表 | 活動是否可複製與執行留P7-02驗證；未啟用舊流程 |
| PaymentsLive排除/啟動 | C-b | PaymentsLive.runsettings預設Category!=PaymentsLive；csproj僅無VSTestTestCaseFilter時載入；顯式--filter Category=PaymentsLive啟動 | P7Live比照辦理，實機測試另行啟動 |
| 追蹤起點 | A、P7-02 | plugintracelogsetting=1 | 改動後還原1 |

## 秘密鍵名（均已有）

共同前綴 `Deployment:Organizations:goodshepherdback:`；後綴：`OrganizationUrl`、`Username`、`Password`、`TestLineUserId`、`TestChannelToken`、`TestChannelSecret`、`TokenKey`、`ShadowTestChurchId`、`ShadowTestContactId`。只記鍵名，不提供或輸出值。

## 基準DLL差異（已釐清，未寫入）

本次從P7重新打包，初次verify為1；唯讀plan只有兩筆UpdateAssembly，沒有步驟差異。export後解碼組織DLL算SHA，與同層P5已驗收artifacts逐位元組雜湊相同：Plugins `3770b1d67780c805cccd5614ac007ff4b61fa2332b1dd53650dd7b713820e997`、Workflows `5f17136058be5b65ec2fd455e406b09c07c912ce0703bb51b876a7527a682bda`。本地借用這兩個P5原產物，離線檢查通過後verify=0。新打包另存，沒有套用plan、沒有修改組織；後續C-b照計畫部署新產物。不能把基準核對稱作新打包已在沙箱驗收。

## 舊活動型別/引用流程筆數

| 型別 | 筆數 |
| --- | --- |
| `AllotWorkflow.AllotWorkflowClass` | 2 |
| `AppointmentWorkflow.AppointmentWorkflowPlugin` | 2 |
| `CalculateQrCodeScanNumberWorkflow.CalculateQrCodeScanNumberWorkflow` | 1 |
| `EquipmentWorkflow.EquipmentWorkflowClass` | 1 |
| `FeePushLineWorkflow.FeePushLineWorkflowClass` | 1 |
| `HappyBirthdayWorkflowNamespace.HappyBirthdayWorkflowPlugIn` | 5 |
| `JoinMainSmallGroupList.JoinMainSmallGroupListPlugIn` | 1 |
| `NewPersonCareWorkflow.NewPersonCareWorkflowClass` | 1 |
| `PersonalQrCodeWorkflow.PersonalQrCodeWorkflowClass` | 1 |
| `QrCodeWorkflow.QrCodeWorkflow` | 1 |
| `RecursiveWorkflowNameSpace.RecursiveWorkflowPlugIn` | 1 |
| `StorLessonSigningWorkflow.StorLessonWorkflowPlugin` | 3 |
| `SundayQrCodeWorkflow.QrCodeWorkflow` | 1 |
| `WeeklyReportWorkflowNameSpace.WeeklyReportWorkflow` | 2 |

名稱只為CLR型別，沒有流程名稱、教會名稱、資料ID或XAML。原始export保留ignored artifacts，不進repo。

## 給使用者的一次性清單

1. **前置A需要權限**：目前未提升，沒有嘗試自行提升。建議在「系統管理員PowerShell」執行07A附錄丙指令（服務名稱未核對）：

```powershell
Invoke-Command -VMName 'D365-9.1' -Credential (Import-Clixml "$env:USERPROFILE\.speechmessage\test-vm-admin.clixml") -ScriptBlock { Restart-Service -Name 'MSCRMAsyncService','MSCRMAsyncService$maintenance','MSCRMSandboxService' -Force; iisreset }
```

此為07A明列的測試服務重啟，不重新部署或修改IIS/Node設定。也可改由提升權限的Codex執行；若不處理，依前置A預先核定退路先做B/C-a，到P7-02a仍未處理則標追蹤不可用，以Live實機證據繼續，不阻擋本目標。

2. **未來正式組織作業**：組織清單/主機/切換順序/維護時段，以及User Secrets鍵名，尚未提供。本目標不連任何正式組織、不把真實正式名稱或主機寫入repo；這一項不阻擋本目標，留階段回報列待提供，不要求此刻貼秘密。
3. **提案**：P7-PROP-001～006已登錄；未回覆即照07A預設分級/退路繼續。

## 外部分析狀態

自癒入口run 20261117-123707-p7-start-analysis-analyzer兩次嘗試，Claude有可用輸出，Gemini 403/503無輸出；health通過，dualModelComplete=false、quotaBlocked=false、degradedFallback=false。這不是雙模型成功，也不是配額降級。保留結果、以本地查證進行紀錄單元；後續仍須再由自癒入口重試外部分析/階段審查。

## 本機證據SHA-256

- `artifacts/p7/package-inspection.log`：`40870bba71a1e7eaa7b9bade30808369459fde6a03691a275e21503f1f95dc91`。
- `artifacts/p7/verify-baseline-matched.log`：`ca0262dcc68601a726404ba0bc102910c4b70a1fdcf432a80b1a447fb2e3a4dd`。
- `artifacts/p7/preflight-probe.log`：`26469178186e62d625b0bb774d69520c2bc02fb2730e409ef04977577f98af24`。
- `artifacts/p7/export-legacy-baseline.log`：`1ffbca26621578c53fdc988b9b7485c10e3db6b2965ea42f0cc176561da24dca`。

## 2026-11-17 12:53 Asia/Taipei (UTC+08:00) 更新

使用者已回報重啟服務；依序單次診斷成功，PluginFileVersion=1.0.0.17、smoke=0、off=0、trace restore=1、verify=0。前置A權限等待解除，不需再次重啟。只啟用新diagnostics，沒有舊程式執行或LINE推播。使用者已明確指定本階段ClaudeOnly，後續外部分析/階段審查使用自癒入口-ClaudeOnly，標「單模型（使用者指定 ClaudeOnly）」，不再重試Gemini。

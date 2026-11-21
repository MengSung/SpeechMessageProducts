# L2 執行紀錄

**2026-11-21 22:32 Asia/Taipei (UTC+08:00)：依 08A 修訂 1 縮減結案。** 21:34 是範圍核定時間，不是完成時間；舊時間戳結論已於 22:37 更正。成果限可編譯、有離線測試且全方案 Release 通過的 PE 輸入契約、shape projector、strong-name／輸入驗證與相關 CLI／測試。Workflow XAML 產生及 goodshepherdback 實機移至好牧人 9.1 上線前作業，七活動維持 dormant。

## 執行與證據

- 保留未完成 `WorkflowTemplateBuilder.cs` 的原始檔與差異於 `%USERPROFILE%\.speechmessage\evidence\p8-l2-wip\`；不提交該 builder。保留的實作改以 `ActivityInputContractValidator` 明確表示其實際責任。
- `LegacyDispositionTests.Source_NoLineNotifyEndpoint`：先紅（tracked source 被本輪刪除，Git 列為 deleted），調整後綠 1/1。紅／綠輸出為 `C:\smv\guard-p8-evidence\p8-l2-source-no-endpoint-red-20261121-2123.log`、`p8-l2-source-no-endpoint-green-20261121-2130.log`。
- `SourceSecretsTests.Source_NoHardcodedKeys`：完整方案首次重跑紅於同一 deleted-path 原因；修正後單測綠 1/1，輸出 `C:\smv\guard-p8-evidence\p8-l2-source-secrets-green-20261121-2145.log`。兩掃描仍對存在的 tracked source 執行原有檢查；只有 Git 明確回報 deleted 的路徑可略過，其他缺檔仍失敗。
- 全方案 Release 最終重跑 exit 0，23 個測試目標合計 7,535/7,535：`C:\smv\guard-p8-evidence\p8-l2-full-solution-release-final-20261121-2150.log`，SHA-256 `DABF2986589CC372881E00881A402B1160DA90C3EF017CED87FD815FEC5BEC31`。
- 完整 §8.1 守門於 2026-11-21 22:21:24 對乾淨 checkout `477c8136147d7168ebc8faf869a8e1761004db93` 通過；該 checkout 含本單元程式提交 `2ace3199581ba0479233fea66fd5a63b331b430f`。保存的逐命令輸出於 22:32:15 完成，23 個目標合計 7,542/7,542、Debug tracer 7/7、ProgressPage 28/28、文件說明 1,891 檔零失敗。守門紀錄 `C:\smv\guard-p8-evidence\guard-477c8136-full.log` SHA-256 `D7A98FADFCB88D0BE86C9A9B022890DEBC3DD779F3D9D85FE8AC141DEBA20ED5`；完整輸出 `C:\smv\guard-p8-evidence\guard-477c8136-full-output.log` SHA-256 `7D1003DF99428E214E5FB91FC8CFB3BE5CDA6532408F79FD41DFC0D649941F23`。此完整守門在提交 `2ace3199` 之後、目前紀錄收尾提交之前完成；`477c8136` 只另含 L1 封存任務 JSON 修正。
- build 22 PE 核對：六個既有核定情境符合輸入契約；`AppointmentSigningActivity` 無核定 Workflow 情境，不猜測補值。完整細節見 `p8-l2-pe-contracts-build22-20261121-191330.log`。
- 最新可核證組織 verify 仍是 2026-11-21 20:15 exit 1、唯一 `UpdateAssembly`；本單元未做組織寫入，故不聲稱目前 `off`、`verify=0` 或殘留 0。

## 明確移至上線前作業

真實脫敏 XAML golden、完整十項原名測試、XAML builder、PREP-09／10、新流程 Create／Activate／Execute／finally cleanup、Appointment 情境、七活動完整契約與 goodshepherdback 實機均未完成。這些不是本次離線縮減驗收內容；上線前必須補驗。`LiveActivityProbe` 及 registration tier/evidence 本次不變。

## 品質及交付

- [x] 全方案 Release 通過，保存完整當次輸出。
- [x] 更新一頁 L2 回報、父子 Trellis、決策紀錄與進度 DATA。
- [x] 完整守門：已通過並保存原始 transcript 與完整逐命令輸出。
- [ ] 單元紀錄收尾提交、分支推送及遠端 SHA 核對：待本次收尾程序。

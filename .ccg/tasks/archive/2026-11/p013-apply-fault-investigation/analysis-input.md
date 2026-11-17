# P0-13 apply fault：安全錯誤碼診斷分析

只分析，不修改任何檔案，不連線任何外部服務或 D365／LINE，不讀取設定、User Secrets、token、金鑰或授權檔。不掃描其他 worktree。

目前事實：
- 使用者自己執行 plan 成功，plan 僅 CreateAssembly、CreateType、CreateStep，目標 goodshepherdback，trace、無秘密佔位。
- apply 輸出 `Command failed (FaultException`1). Sensitive details are not shown.`
- 本機 export.json 已先完成，匯出前此組件不存在。不能藉此判定是否已有部分寫入。
- Exception.log 只留 OrganizationServiceFault 型別及 WCF 前五個 stack frame、一般 HResult；沒有 SDK Detail.ErrorCode，無法判斷伺服器根因。

擬作最小補修：
- 僅在 DeploymentCli 的既有 catch 中辨識 `FaultException<OrganizationServiceFault>`。
- 保留原始 exception 經既有 report 委派上報一次，仍 return 1；不得新增 notifier 或改 ToolUtility。
- 額外只印 Detail.ErrorCode 的固定 8 位十六進位數字；有 InnerFault 時最多 4 層。拒絕列印 Message、Reason、TraceText、ErrorDetails、Data、原始 ToString 或 stack。
- 不改連線、SDK 寫入、Plugin、清單、重試或驗證行為；不自動重試 apply。
- 離線測試注入帶假秘密的 Fault 及內層 Fault，驗證輸出含數字碼且沒有秘密、原始報告 exception 同一物件、連線確定釋放、深度有界（含 cycle）；原有連線例外遮罩測試全部保留。
- 使用者會在新版本準備後重跑 plan 供核對，然後才 apply 收集數字錯誤碼；Codex 自己不執行線上命令。

請指出方案中的 Critical／High 或必要的更小修法，尤其錯誤碼安全、取消分類、原始例外保留與資源釋放。不需要探索其他模組，也不要推測這次 D365 伺服器的根因。

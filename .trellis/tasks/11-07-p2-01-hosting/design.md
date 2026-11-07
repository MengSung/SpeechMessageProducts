# P2-01 技術設計

## 模組邊界

`SpeechMessage.Hosting` 是 net10 上層組合模組，可參考 `ToolUtility`、Localization
與 Tenancy，但雙目標核心不得反向參考 Hosting。既有 `ToolUtility.Diagnostics`
是目前唯一通知 owner；本任務透過 Hosting registration、guard 與 adapter
暴露共用能力，避免複製第二個 queue／writer。

## 元件

- `SpeechMessageHostingServiceCollectionExtensions`：註冊單一 `ExceptionDiagnostics`
  owner、logger provider、middleware／guard 所需服務；owner 由 Host 建立與 Dispose。
- `StartupGuards`：純函式驗證 ICU、LocalizationOptions、Development CRM／LINE
  設定；不持有組態 provider、不建立背景工作。
- `ExceptionReportingAdapter`：只把 Host 已擁有的 exception owner 注入 MVC／
  middleware；不保存 HttpContext、Session、tenant 或 credential。
- `ArchitectureRules`：沿用 `tests-shared/ArchitectureTests` 的 XML／原始碼掃描，
  在 Hosting.Tests 驗證主機與 Plugin 的公開邊界。

## 生命週期

建立順序：組態快照 → guard → exception owner → optional LINE consumer → DI／Host。
停止順序：停止接收 → owner 完成有限 drain → sender/client Dispose → registration
解除 → configuration root Dispose。任何逾時後不重用不確定傳輸狀態。

## 失敗處理

四種輸出開關保持既有契約。寫檔與通知故障只產生固定狀態碼；不把原始例外
Message、request body、token 或個資放入 Exception.log／LINE。正常 caller cancellation
不通知；內部 timeout 仍視為失敗。

## 整合與回復

先保留 `ToolUtilityNameSpace.Diagnostics` 相容型別與 ChurchReport 呼叫點，透過
Hosting API 接入；只有在完整 solution 參考圖證明可安全搬移時才改 namespace。
任何搬移失敗可回復至 P1 owner，避免在 P2-01 引入雙 owner 或下層反向參考。

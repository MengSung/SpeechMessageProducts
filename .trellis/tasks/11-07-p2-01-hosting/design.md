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

## 2026-11-07 實際抽取修正（原規劃03優先）

舊設計只保留adapter不足以滿足P2-01的實際搬移。已確認三個diagnostics型別不依賴
ToolUtility其他程式，Tenancy僅依賴Localization與LineMessagingProcessor.Core，故將
ExceptionDiagnostics／ExceptionOutputOptions／ExceptionReporting移入Hosting唯一組件，
保留原namespace與ToolUtility的TypeForwardedTo確保來源及既有組件身分相容；不是複製實作。
ToolUtility改參考Hosting，Hosting移除ToolUtility參考、改參考Tenancy。此為net10基礎
抽取，R4圖將Hosting置於Tenancy之上、既有ToolUtility／Processor之下（32層），並以
正反圖與實際方案評估同時禁止Hosting反向參考ToolUtility、雙目標／Plugin引用Hosting。
未來worker透過Core契約或DI handler使用依賴，不把舊工具反向拉進Hosting。

註冊快照先完整驗證再加入描述子；相同目錄／開關或相同外部owner／adapter重入冪等，
不同快照、外部adapter與owner不符、已存在非本入口的保留服務均拒絕且不部分發布。
DI僅Dispose自己建立的owner／adapter；外部instance一律借用。全域registration先解除，
再drain診斷，最後釋放sender與configuration。此步不替代後續sender搬移、Program接入、
CultureCompleteness生成、R2／R3／R9與完整主機驗收，P2-01保持進行中。

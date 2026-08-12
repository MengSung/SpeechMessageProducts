# P7.2 operation-local CRM service propagation：架構與安全分析

請只做靜態架構分析，不執行 CRM/CE、fixture、ledger、feature flag、流量切換或任何外部寫入。

已知風險：Session ID 快取的 `ListManager` 最長存活 30 分鐘，持有 `DownloadIntegrateData`；後者建構時取得 process-static `ToolUtilityClass`，其中含可變 `IOrganizationService`。若某個 operation 借用的 service 被存回此鏈，後續 request/profile 可能重用它。

目前 `ListManager.SetupIntegrateData(string, IOrganizationService)` 和對應 `DownloadIntegrateData` overload 在 CRM I/O 前 fail closed。下一步計畫把 service 僅作為同步 method parameter 傳給所有下載 partial 的 CRM I/O，絕不寫入 field/static/cache/AsyncLocal/ToolUtility/Factory，且內層絕不 Dispose 借用 service；legacy UI path 不切換。

請審查下列方向的安全性、相容性與測試缺口：

1. 以 `IOrganizationService` parameter 取代 service-aware path 中的 ToolUtility CRM call；attribute mapping 可保留靜態/純 Entity helper。
2. 以 operation-local `IdentityConverter` 或純 metadata helper 取代 session-long `_identityConverter` service retention。
3. 同一 session 的 `ListManager` 可變 output 必須 fail closed 或以 request-local output 避免交錯污染。
4. timeout/fault/cancellation 的 service 不得由內層重用或釋放；外層 lease owner 有唯一 finally cleanup。

輸出：Critical / Warning / Info，且每項指出可驗證的本機測試。不回顯敏感資料。

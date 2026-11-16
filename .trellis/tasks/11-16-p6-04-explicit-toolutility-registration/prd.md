# P6-04 AddToolUtility 舊多載移除

2026-11-16 19:04 Asia/Taipei (UTC+08:00) 開始，時間盒半天，依 06A §5 P6-04。
只移除無參數多載及 ToolUtility 的三個例外型別轉送；Hosting 專案參考保留。

## 指定測試與驗收清單

- [ ] `AddToolUtility_ExplicitChurchReport_ConnectionKeyHasChurchReportProduct`：明確註冊後的實際池鍵必須為 ChurchReport；合成連線，釋放租約並由容器 Dispose。
- [ ] `AddToolUtility_ParameterlessOverload_NoLongerExists`：反射確認無只收 IServiceCollection 的多載，先紅後綠。
- [ ] `ToolUtilityAssembly_HasNoExceptionTypeForwarders`：確認無三個例外診斷型別的 CLR 轉送，先紅後綠。
- [ ] 先證明 Startup.Configuration 與容器 IConfiguration 同一實例；若不相同依 §7-3 停止，不另建組態。
- [ ] 刪無參數多載後依編譯錯誤逐項修呼叫點；測試保留原名並傳原容器的同組態與 ChurchReport。
- [ ] 刪 null 組態相容分支；無需隱藏容器或延後組態解析。
- [ ] 檢查 ChurchReport LoggerProvider 相容性不是 ToolUtility 三型別轉送的消費者；只刪 ToolUtility 轉送檔。
- [ ] ToolUtility.Tests、ToolUtility.Dataverse.Tests、ChurchReport.MemberInfo.Tests Release 全通過。
- [ ] 程式提交後固定 guard 完整 00 §8.1（起點 9b7ac0a0）。
- [ ] 無新增專案、無實機；一頁回報、進度 DATA、驗收時間與提交齊全，驗收後推送並核對遠端 SHA。

不改 NoWarn、產品名稱、有效連線鍵、資源所有權、Hosting 參考、duplicate-baseline.json。

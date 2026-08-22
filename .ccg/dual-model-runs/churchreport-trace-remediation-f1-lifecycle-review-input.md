請審查目前工作樹中 F1 ChurchReport SaveIntegrate 背景上傳隔離修正，特別聚焦 request scope/session/resource lifecycle 與跨使用者隔離。

變更重點：
- ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs 新增 AsyncLocal 背景 IServiceProvider override；背景 override 優先於 IHttpContextAccessor request services，Dispose 還原前值。
- ToolUtility/Factory/ToolUtilityFactory.cs 新增 BeginBackgroundScope(IServiceProvider) 轉發 API。
- SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Save.cs 在新背景 DI scope 內套用 ToolUtilityFactory.BeginBackgroundScope，並保留 DataverseTrace background scope 與巢狀 Task.Run。
- SmallGroupDataList/ListSmallGroupWeeklyReport/Member 使用深拷貝背景快照；背景只清理快照、不回寫 Session；回應 requiresRefresh=true。
- 新增 ToolUtility.Dataverse.Tests 的背景 scope nested Task.Run 與 trace context regression tests；新增模型快照並行測試、Factory static 測試 collection serialization。

請檢查：
1. 背景 CRM 呼叫是否一定使用背景 scope，是否可能在 request 結束後使用 disposed RequestServices。
2. AsyncLocal override 的巢狀、平行、例外、取消、Dispose 還原與資源 ownership 是否正確；是否破壞 F4 DataverseTrace ExecutionContext 關聯。
3. SaveIntegrate closure 是否仍保留 controller/Session/HttpContext/credential 不必要參考，或發生跨使用者/跨租戶資料洩漏。
4. 快照是否完整深拷貝且不會 stale publish/lost update；測試是否確實注入並行競態。
5. C# XML/繁中註解、UTF-8 無 BOM/CRLF/final CRLF、編譯與測試風險。

輸出格式：Critical / Warning / Info；每項附檔案與行號、根因、可重現性與最小修正建議。不要修改檔案。

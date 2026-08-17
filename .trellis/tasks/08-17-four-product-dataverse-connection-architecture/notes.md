# 執行備註

## 單位 2：SKIPPED

- 原因：品質門檻 G2 連續三次無法完成。第一次為 `ToolUtility.Tests` 的 `net8.0` 與被測 `ToolUtility net10.0` 不相容；將測試專案框架與 Logging 套件對齊後，仍發現多個既有測試檔以已淘汰的 `ICrmClient`、`IEntityCrudService` 與舊建構式呼叫目前 API。
- 最後錯誤：`ToolUtility.Tests/LineMessaging/LineMessageServiceTests.cs(33,69): error CS1503: 引數 2: 無法從 'ToolUtilityNameSpace.EntityOperations.IEntityCrudService' 轉換成 'Microsoft.Xrm.Sdk.IOrganizationService'`；另有 Attachment、Contact、Entity、List 與 Facade 舊測試的同類型編譯錯誤。
- 範圍判斷：修復這些既有測試需要變更多個非本單位新增的測試契約，與「只加四個測試」的範圍衝突；依失敗處理程序不再擴張。
- 回復：已還原本單位修改的 Startup、CrmConnectionPool、OnPremiseClient 與測試專案設定，並逐一刪除新增的 PooledOrganizationService 與其四個測試。

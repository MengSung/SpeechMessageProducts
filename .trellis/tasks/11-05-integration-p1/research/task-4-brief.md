# Worker 共通契約
先完整讀AGENTS.md、此brief、design.md、02中所屬WP及相關spec。不得spawn，不git commit、不改共用檔，不讀secrets、不連D365／LINE。不改別人變更。每新增／修改C#含完整繁中XML、UTF8無BOM、CRLF／final CRLF。先特性綠或TDD紅再實作，不NoWarn、不降分析器、不刪原測試。方案／中央套件／CI／task主檔只能controller改；需要變更以report通知。

### Task 4: P1-04 ToolUtility.Core
Files: ToolUtility.Core/**、ToolUtility/**、ToolUtility.Tests/**、wp-reports/P1-04-inventory.md，worker exclusive（ToolUtility DI由同worker改product overload）。
- [ ] 逐型別列ToolUtilityPartials、Operations、Core、Extensions、Constants，標可搬／不可搬與依賴原因；唯讀比對父工作樹LineApiWebhook來源。
- [ ] 每可搬Operations 2～3 query/result golden特性先綠；git mv保歷史；host Facade／連線／ILogger不搬不複製。
- [ ] Core雙目標strict、原namespace，SDK條件private；依賴host型別用最小純介面adapter，不引入額外同功能copy。
- [ ] 四方法GetNotYetPaidFee、GetPaidFee、RetrieveFeeByFetchXmlOrderNumber、RetrieveEntityNoCache；各≥3case及XML特殊字元，既有API不變。
- [ ] AddToolUtility(services,configuration,string product)及Obsolete舊overload→ChurchReport；不同product connection key隔離。
- [ ] tests雙目標：純Core兩target，既有host tests net10保留，不為net48刪測試。借用IOrganizationService不Dispose、不保存userstate。
```csharp
Assert.Equal("a<&\"", parsedValue);
Assert.NotEqual(productAConnectionKey, productBConnectionKey);
```

報告寫research/task-4-report.md，記檔案、命令、redgreen、未完項及風險。回覆精簡，附改檔清單。

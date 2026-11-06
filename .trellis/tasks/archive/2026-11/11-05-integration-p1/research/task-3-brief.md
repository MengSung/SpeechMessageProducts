# Worker 共通契約
先完整讀AGENTS.md、此brief、design.md、02中所屬WP及相關spec。不得spawn，不git commit、不改共用檔，不讀secrets、不連D365／LINE。不改別人變更。每新增／修改C#含完整繁中XML、UTF8無BOM、CRLF／final CRLF。先特性綠或TDD紅再實作，不NoWarn、不降分析器、不刪原測試。方案／中央套件／CI／task主檔只能controller改；需要變更以report通知。

### Task 3: P1-03 LINE SDK
Files: Line.Messaging/**、Line.Messaging.Tests/**，worker exclusive；controller更新solution packages若必要。
- [ ] 完整讀取csproj／序列化models，先六種golden且舊net10跑綠，fixture不含個資。
- [ ] 兩產品net462;net10.0，tests net48;net10.0，修documentation output避免覆寫；缺API用NETFRAMEWORK，>30檔停並交controller裁決。
- [ ] Sender(name 1..20,https icon)、Background linear gradient、WebhookEventId／DeliveryContext.IsRedelivery，完整繁中XML與無狀態序列化。
- [ ] LineSignature.Verify(byte[] body,string signatureHeader,string channelSecret)，空secret丟、invalid base64／wrong digest／tamper false，HMAC deterministic Dispose。
- [ ] 每新功能紅綠，兩target六golden逐字相同，negative vectors與framework載入驗證，回報改檔、測試、風險。
```csharp
Assert.False(LineSignature.Verify(body, tamperedSignature, secret));
```

報告寫research/task-3-report.md，記檔案、命令、redgreen、未完項及風險。回覆精簡，附改檔清單。

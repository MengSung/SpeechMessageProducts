# Worker 共通契約
先完整讀AGENTS.md、此brief、design.md、02中所屬WP及相關spec。不得spawn，不git commit、不改共用檔，不讀secrets、不連D365／LINE。不改別人變更。每新增／修改C#含完整繁中XML、UTF8無BOM、CRLF／final CRLF。先特性綠或TDD紅再實作，不NoWarn、不降分析器、不刪原測試。方案／中央套件／CI／task主檔只能controller改；需要變更以report通知。

### Task 5: P1-05 Localization
Files: SpeechMessage.Localization/**、SpeechMessage.Localization.Tests/**（不改csproj；controller處理連結），worker exclusive。新增snapshot介面可由Task8直接消費。
Interfaces: TextContext(Guid churchId,string textCulture,string formatCulture, string organization="default",string product="ChurchReport") immutable；WithCultures preserves boundary；TextArg(name,object?)；ITextCatalog.GetText(context,key,args)；IChurchTextOverrideSnapshotProvider.GetSnapshot(TextContext context)；snapshot.TryGetText(culture,key,out text)；ChurchTextOverride有database Guid Id、ChurchId、Key、Culture、Text。source.ReadAsync(TextContext,CancellationToken)回IReadOnlyList<ChurchTextOverride>，不持SDK。
- [ ] 保留P0 public契約與43case，增加SystemTextTable.TryGetOriginalText分開預填fallback。
- [ ] 五層每層測、全缺丟；命名參數完整比對／format culture，無global culture。
- [ ] resolver released/ enabled intersection、en未開放、pseudo production fail startup；Keyword NFKC／空白／case／跨語系衝突。
- [ ] resx唯一來源，en僅既有keyword；generated由controller的I18nCheck重生，worker不得手改generated資源導致verify漂移。
- [ ] A/B 1000並行、scope不隨語言改變、snapshot深不可變／duplicate ID rejection；兩targettests。
```csharp
Assert.Equal(original.ChurchId, original.WithCultures("en", "en-US").ChurchId);
```

報告寫research/task-5-report.md，記檔案、命令、redgreen、未完項及風險。回覆精簡，附改檔清單。

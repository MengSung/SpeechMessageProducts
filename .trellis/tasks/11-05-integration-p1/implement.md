# P1 基礎層與國際化地基 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 完整完成 P1-01～12，維持既有行為並交付可驗證的共用國際化與租戶地基。
**Architecture:** 純雙目標 Core 與 net10 host adapters 分離；完整不可變快照與有限生命週期。架構測試先行，按依賴整合。
**Tech Stack:** C#、net462／net10.0、net48 test harness、xUnit、Newtonsoft、CrmSdk、Extensions 10。
**Spec:** 本任務 design.md 及 文件/規劃設計書/實作計畫-3.3/02_階段1_基礎層與國際化地基.md、90_附錄_規格與範本.md。

## Global Constraints
- Framework產品輸出只能net462；另一產品net10.0，test host可net48但確認載入net462。
- 新專案SpeechMessageStrictCode=true、GenerateDocumentationFile=true；CPM，不增加NoWarn。
- 每個修改區繁中XML／UTF8無BOM／CRLF／final CRLF；不含hardcoded secrets。
- 不搬業務流程；不改父工作樹；不推送或合併；goodshepherdback only、no LINE。
- 每包紅綠／先綠特性、review、00§8、新模組每target≥80% coverage。

## 執行模式
2026-11-05 最新developer規定inline：主會話直接實作與檢查，不派implement/check子代理。前輪worker均已停止，僅留下LINE golden測試草稿；由主會話完整讀取後接續。CCG Gemini／Claude仍經自癒入口做唯讀分析與審查。全部共用檔及工作包由主會話依序管理，保留既有未辨識修改，不混入提交。

### Task 1: 首批 smoke 與 DevExpress／P1-01
Files: DeploymentEngine.cs、Deployment.Tests新增smoke節奏tests、CI yaml／nuget.config（只在必要時）；Trace、LinePayCSharp、兩個_Net10.csproj、空worker lock資料夾；ChurchReport.MemberInfo.Tests/Performance/CollectionQueryServiceAsyncTests.cs；SpeechMessageProducts.sln。
- [ ] 逐項搜尋sln/csproj/Compile／symbol並記P1-01-report；任何有效參考停止刪該項，不刪測試草稿。
- [ ] 在既有smoke注入可控TimeProvider／delay，測初次立即、後續2秒、180秒、cancel及finally task delete；先紅再改100ms。
- [ ] 檢查本機NuGet package來源metadata（不印feed URL/key），以安全CI secret或可公開正版source解決還原，不改全域設定，未真正hosted run不聲稱成功。
- [ ] 特性／duplicates基準已綠；git mv效能測試、補deterministic fixture／Trait，再確認可編譯及原全部案例有效。
- [ ] 加ToolUtility.Tests至sln，刪確定無引用孤兒，跑00§8並記前後數字。

### Task 2: P1-02 R4
Files: tests-shared/ArchitectureTests/ArchitectureRules.cs、ArchitectureRuleTests.cs、SpeechMessage.Localization.Tests.csproj（net10連結）。
Interfaces: Validate(project model,evaluated target dependency graph)→violations；測試合成csproj不呼叫外部服務。
- [ ] 每規則正例／反例先測：dual→net10、Extensions/ASP.NET/Dataverse禁用、Core net10 Dataverse例外、plugin→net48、host-host、DAG反向、conditional target。
- [ ] 用每target的評估project／XML，不誤算未啟用PackageReference；solution中未知分類fail closed。
- [ ] 讀取實際solution驗證0違規、測試host反射TargetFrameworkAttribute net462；保留正例證明合法Core依賴。
```csharp
Assert.NotEmpty(ArchitectureRules.Validate(invalidFixture));
Assert.Empty(ArchitectureRules.Validate(validFixture));
```

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

### Task 6: P1-06 時鐘
Files: Localization/Time/**、Localization.Tests/ChurchClockTests.cs、tools/I18nCheck/data/windowsZones.xml及generator；在Task5後同owner接續。
- [ ] 固定UTC測Taipei+8、London 2027-03-28／10-31、LA03-14、八zone、生日date-only；invalid／ambiguous local、unknown id先紅。
- [ ] IClock／TimeProvider adapter、IChurchClock含ToUtc(LocalDateTimeOfChurch)，明確Kind；net462 map和net10 IANA保持結果。
- [ ] CLDR釘選版本／license／原XML，generate-timezones deterministic regeneration；ICU啟動驗證helper。

### Task 7: P1-07 Money
Files: Localization/Money.cs、MoneyFormatter.cs及tests；Task5後。
- [ ] readonly Money(decimal,string)白名單／default invalid；TWD產品顯示0、其他2；加法檢查currency並checked。
- [ ] Format(m,CultureInfo)輸出ISO幣別且格式只改separator／symbol，en-US保留TWD；同值、invalid／overflow測試。

### Task 8: P1-10 Tenancy resolver（先於P1-08）
Files: SpeechMessage.Tenancy/**、SpeechMessage.Tenancy.Tests/**、既有九處ChannelAccessToken解析及其csproj references；controller ownership。
- [ ] 先測現有Jesus fallback，TenantId server-scope／LineChannelCredential不印秘密；provider immutable settings。
- [ ] IConfiguration resolver集中現有key，缺org才Jesus且safe warning，空token fail closed；A/B parallel1000。
- [ ] rg盤點所有active callsites，逐一替換並保留現有constructor API相容（只在legacy adapter創建共用resolver，不複製解析），測試不得剩rawfallback。
- [ ] host DI登記集中resolver；net10共用layers依賴Tenancy，無circular；全方案回歸。

### Task 9: P1-08 source／cache
Files: ToolUtility.Core/Localization/D365ChurchTextOverrideSource.cs（待Task4 worker結束才controller接手）、Tenancy/Localization/**及tests。
- [ ] source query new_accountid filter／paging／stableId duplicate；error不吞掉、不存client。
- [ ] Task5純interface，MemoryCache限2000／30minidle／10minreload；bounded flight、Lazy<Task> singleflight、generation race，caller cancellation不cancel共享load。
- [ ] 驗candidate重複ID／keyculture、unknown key／parameter／length，錯誤保留old snapshot並notifier一次原Exception；failedflight移除nextretry。
- [ ] fake-time expiry／invalidate／disable、1k A/B、mutation／loaderfailure／dispose/draincounterbaseline、capacity stress測試；無timers或fire-forget ownerless工作。

### Task 10: P1-09 I18nCheck v2
Files: tools/I18nCheck/**（controller，等Task6 sourcegenerator協調）及tests／limits.json。
- [ ] 每規則positive/negative fixture：unknownkey、duplicate key跨resource（同key不同culture合法）、placeholder set、sender20／chatbar14及pseudo、跨culture keywords、en coverage nonblocking。
- [ ] 保留原scanner／P0checks，resx生成deterministic、timezone生成idempotent、verify repo全綠。

### Task 11: P1-11 schema deployment
Files: Deployment/Schema/**、DeploymentCli.cs及tests、d365/schema/speechmessagecore.schema.json、d365/snapshots metadata。
- [ ] schema完整白名單及phase，fixed test metadata fixture；existing matching no-op／type/length/date/security conflict reject／disallowedorg reject先紅。
- [ ] export-metadata只定義、無business RetrieveMultiple／secure configs；限制metadata projection且bounded ownership。
- [ ] plan包含schemahash/current fingerprint；apply --schema --plan重核hash／lock／before-export；只create，solution membership，key Active verify；第二apply0write。
- [ ] crash／partialapply以現況重plan安全續行，不rollback刪既有結構；所有query不印rawfault，正常cancel不告警。

### Task 12: P1-12 live goodshepherdback／收尾
Files: 驗收/P1_D365結構建立步驟.md、wp-reports/P1-12.md、進度頁、task review與spec。
- [ ] 全offline tests／外部雙模型review無CriticalHigh後，唯讀HyperV／固定endpoint連線metadata，plan逐操作與schema比對並記hash。
- [ ] 僅核定新增apply、verify無差異、key Active；合成localizedtext duplicate insert必被alternate key拒絕，finally只刪本次ID；不發LINE。
- [ ] 全方案Release／Debugtracer／duplicate／encoding／i18n／architecture／package／per-targetcoverage／packaging重跑，保留結果不洩漏秘密。
- [ ] 外審經Start-CcgDualModelRun、核dualModelComplete；修CriticalHigh後重審。
- [ ] 更新spec與進度、按WP工作提交；最後task封存／journal另提交，僅實際驗證完才標completed。

## 計畫自審
需求02每包映射Task1～12；P1-10先於08。共用檔僅controller寫；Task5→6/7→9接口一致，Task4→9 source不並寫。Task3不需要Task4。Task2可在controller實作時三worker各自先測，最終整合R4為關卡。任何外部model提示只是待驗證風險，不能當既有bug斷言。

## 2026-11-05 使用者審查方式覆寫
使用者最新明確要求「不需要雙模型REVIEW了」。後續不再啟動外部reviewer，由主會話直接逐項審查、修正及實際驗證並保存review.md；已取得的歷史外審證據保留原狀，不冒稱新增雙模型完成。需求、安全／測試／覆蓋率門檻不降低；開發規範所需雙後端分析仍經自癒入口及如實揭露額度降級。這只更改執行協作方式，不修改正式3.3計畫需求，無Word修正報告需求。


## 2026-11-06 最新執行方式
使用者明確表示「不需要雙模型了」，後續分析與審查均由主會話直接完成，不再等候外部模型。20261106-085049歷史分析曾嘗試雙後端：Gemini HTTP403、Claude週額度，無可用輸出，不能稱為雙模型或單模型分析成功；原始證據保留。需求／測試／安全／覆蓋率與實機證據門檻不變，不修改正式3.3計畫，無Word需求修正報告。

## P1-04 Contact切片執行決策（2026-11-06）
- 原host三查詢golden與舊登入/資訊文字7/7先綠；安全24全紅、空密碼2全紅。
- 可選cache的舊鍵缺完整伺服器已授權scope且共享mutable Entity，停用所有Contact cache I/O，不把密碼/雜湊加key；兩建構式保留於薄host adapter，不保存cache/logger。
- 借入SDK的全部資料操作集中ContactDataService，保留namespace/19介面方法/3Task入口與舊文字；八個legacy文案移中立resx，不引入host相依或全域culture。
- 在原host位置修正後43/43通過；搬前暫時Compile link同一SdkDataQuery來源供internal helper使用，非副本；git mv前已移除link並逐字核對host csproj回HEAD。
- 完整集合最多5000列/1000頁/64Ki cookie，穩定contactid排序，完整候選跨頁驗重後發布；超預算/後頁故障拒絕，不回部分集合。
- 搬後補外部SDK/ref/null、三Task取消/內部timeout、Dispose/drain/weak-reference/1000A/B，跑雙目標coverage/strict/全方案/00§8並保存本地證據。不改正式3.3計畫。

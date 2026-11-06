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
- [x] 逐型別列ToolUtilityPartials、Operations、Core、Extensions、Constants，標可搬／不可搬與依賴原因；唯讀比對父工作樹LineApiWebhook來源。
- [x] 每可搬Operations 2～3 query/result golden特性先綠；git mv保歷史；host Facade／連線／ILogger不搬不複製。
- [x] Core雙目標strict、原namespace，SDK條件private；依賴host型別用最小純介面adapter，不引入額外同功能copy。
- [x] 四方法GetNotYetPaidFee、GetPaidFee、RetrieveFeeByFetchXmlOrderNumber、RetrieveEntityNoCache；各≥3case及XML特殊字元，既有API不變。
- [x] AddToolUtility(services,configuration,string product)及Obsolete舊overload→ChurchReport；不同product connection key隔離。
- [x] tests雙目標：純Core兩target，既有host tests net10保留，不為net48刪測試。借用IOrganizationService不Dispose、不保存userstate。
```csharp
Assert.Equal("a<&\"", parsedValue);
Assert.NotEqual(productAConnectionKey, productBConnectionKey);
```

### Task 5: P1-05 Localization
Files: SpeechMessage.Localization/**、SpeechMessage.Localization.Tests/**（不改csproj；controller處理連結），worker exclusive。新增snapshot介面可由Task8直接消費。
Interfaces: TextContext(Guid churchId,string textCulture,string formatCulture, string organization="default",string product="ChurchReport") immutable；WithCultures preserves boundary；TextArg(name,object?)；ITextCatalog.GetText(context,key,args)；IChurchTextOverrideSnapshotProvider.GetSnapshot(TextContext context)；snapshot.TryGetText(culture,key,out text)；ChurchTextOverride有database Guid Id、ChurchId、Key、Culture、Text。source.ReadAsync(TextContext,CancellationToken)回IReadOnlyList<ChurchTextOverride>，不持SDK。
- [x] 保留P0 public契約與43case，增加SystemTextTable.TryGetOriginalText分開預填fallback。
- [x] 五層每層測、全缺丟；命名參數完整比對／format culture，無global culture。
- [x] resolver released/ enabled intersection、en未開放、pseudo production fail startup；Keyword NFKC／空白／case／跨語系衝突。
- [x] resx唯一來源，en僅既有keyword；generated由controller的I18nCheck重生，worker不得手改generated資源導致verify漂移。
- [x] A/B 1000並行、scope不隨語言改變、snapshot深不可變／duplicate ID rejection；兩targettests。
```csharp
Assert.Equal(original.ChurchId, original.WithCultures("en", "en-US").ChurchId);
```

### Task 6: P1-06 時鐘
Files: Localization/Time/**、Localization.Tests/ChurchClockTests.cs、tools/I18nCheck/data/windowsZones.xml及generator；在Task5後同owner接續。
- [x] 固定UTC測Taipei+8、London 2027-03-28／10-31、LA03-14、八zone、生日date-only；invalid／ambiguous local、unknown id先紅。
- [x] IClock／TimeProvider adapter、IChurchClock含ToUtc(LocalDateTimeOfChurch)，明確Kind；net462 map和net10 IANA保持結果。
- [x] CLDR釘選版本／license／原XML，generate-timezones deterministic regeneration；ICU啟動驗證helper。

### Task 7: P1-07 Money
Files: Localization/Money.cs、MoneyFormatter.cs及tests；Task5後。
- [x] readonly Money(decimal,string)白名單／default invalid；TWD產品顯示0、其他2；加法檢查currency並checked。
- [x] Format(m,CultureInfo)輸出ISO幣別且格式只改separator／symbol，en-US保留TWD；同值、invalid／overflow測試。

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
- [x] 每規則positive/negative fixture：unknownkey、duplicate key跨resource（同key不同culture合法）、placeholder set、sender20／chatbar14及pseudo、跨culture keywords、en coverage nonblocking。
- [x] 保留原scanner／P0checks，resx生成deterministic、timezone生成idempotent、verify repo全綠。

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

## 2026-11-06 進度歷程固定位置
- 使用者指定所有本任務修改歷程寫入正式進度頁的「一般資訊」；使用既有 DATA.alert(level=info).body/steps，不只更新 timeline 或工作包 note。每輪追加時間、修改範圍、實際紅/綠驗證、證據、本地commit與未完成邊界；保留既有歷史及他任務版面。
- 本次將契約/Contact既有提交與Fetch原host9/9先綠、安全20案17失敗/3通過寫入；HTML渲染器未變，現有工作樹進度頁測試14/14，產品Fetch仍未修改，不標P1-04完成。

## P1-04 Fetch切片執行決策（2026-11-06）
- 既有五個非Fee完整XML golden與四Fee原host特性9/9先綠；安全矩陣拆為七入口獨立案例，62案51失敗/11通過，產品修改前取得真紅。
- 先於原host修補後再git mv；暫時Compile link同一SdkDataQuery來源供internal helper，不複製。七入口沿用Execute、top1000/100/500、原欄位/排序/link/差異條件；lookup用XElement與非空GUID驗證。
- 共用SdkDataQuery集中Execute錯型別/整頁穩定ID/top預算；Fee Query委派同一實作，不保留兩套驗證。無背景排程/結果cache/logger引用；Dispose清SDK，不關借入連線，舊工作可drain。兩Fee入口逐次借SDK至局部FeeService，不留額外owner。
- 兩個中文業務謂詞保持原義，移分層resx並由正式I18n工具生成，不能按語言翻譯資料庫條件。一般資訊持續記錄修改/驗證/未完成。
- 工具層要求的外部分析經自癒入口20261106-101015嘗試，Gemini403、Claude週額度，兩者無可用輸出；不是雙模型成功，依規則續做本地驗證，不重試相同供應商阻擋。

## P1-04 Relationship切片執行決策（2026-11-06）
- 六入口完整QueryExpression投影golden原host6/6先綠；安全53案52失敗/1通過。保留SDK直接RetrieveMultiple、link方向/根與link filter差異、bool/Guid/字串型別、欄位/排序及PageInfo缺省。
- 純化反射logger後抽Core；五/七動態metadata名稱共用既有logical-name驗證規則，非空GUID在I/O前驗；單頁最多5000列，先拒超量再配置ID集合，同名異ID/metadata保留。
- 原週報日期使用sunday.ToString，保留明確CurrentCulture字串型別而不偷改為DateTime/時區；日期地基仍依後續P1-06，不改正式計畫。借SDK/Dispose/drain/1000A/B/weak-reference與故障重試均實測。

## P1-04 Present／Query切片執行決策（2026-11-06）
- Present八入口完整投影特性8/8原host先綠；安全矩陣產品修改前63紅／2綠，修後搬前72綠再git mv至Core；net48 887/887、net10 1017/1017，Core strict 0警告／錯誤。保留兩個UTC界線、父課程唯一日期link、Fetch Execute回應型別、所有條件順序/值型別、分頁metadata及同名異ID。動態父表/欄位在I/O前驗logical name及正式GUID；單頁上限5000，borrowed SDK Dispose/drain、1000 A/B與弱引用證據在artifacts/p1-04-present-20261106。
- Repository QueryService五入口原host完整golden 5/5先綠；新增錯表、null/empty/duplicate/over-budget、DTD/多根Fetch、取消/失敗重試、Dispose race、1000 A/B與弱參照安全矩陣，原實作修改前37紅／3綠。修後Combined Query／Present／產品路由131/131；證據artifacts/p1-04-repository-query-20261106。
- QueryService已改純Core候選，保持舊namespace、constructor與五API；共享安全串流Fetch根表解析由SdkDataQuery唯一維護，EntityQueryService移除重複parser。QueryService移除輸出欄位值／例外本文的Trace；失敗只固定operation包裝並保留原InnerException。原present使用DateTime.Now不符合Core strict，故新增無scope狀態IClock、注入LocalNow以保留舊Local DateTime.Kind及月份算法，測試可固定時鐘；未自行改成教會時區或補未使用list filter，待P1-06定義時鐘遷移。
- 上述QueryService尚未完成搬移後雙目標／strict／方案驗證及 coverage；P1-04仍進行中。尚須處理CrmAsyncExtensions、ComplexQuery host adapter、ListService／MarketingListService、四方法與product DI之整包核對，不能把已綠切片宣告為整包交付。


## P1-04 最終整包核對（2026-11-06）
- Fetch／Relationship／Present／Query／Complex／CrmAsync／List及Marketing全部完成原host特性、安全紅綠與搬移，原namespace／67多載／預設值及必要host adapter保留。四方法專屬案例7/7/8/8、product8案由實際TRX核對，沒有未處理候選。
- 全方案Release16TRX3906/3906、ChurchReport623、架構67；換行修復後實際重建兩宿主1214/1345，Core strict零警告／錯誤，獨立coverage95.45%/95.81%。Debug7、I18n108、PE27、Plugin metadata10/build6、CI閉包6、套件34/158零違規及I18n/duplicates通過。原始輸出及完整Changed守門沿用既定基準，未放寬斷言。
- 審查由主會話逐項完成，使用者免除雙模型；Marketing舊例外wrapper回歸已修並12案重測，原Fault保真、snapshot／完整頁／caller取消／drain／弱參照及1000 A/B均有證據。
- 驗收矩陣：wp-reports/P1-04-acceptance.md；可追蹤證據：.ccg/tasks/integration-p1/evidence/p104-final/。只封存P1-04收尾子任務，主P1保持in_progress；下一包P1-05，後續P1-10先於P1-08。沒有更改正式計畫，無Word修正報告需求；未push／部署／連VM或D365／發LINE。

## P1-05 最終驗收（2026-11-06 13:00）
完整五層／具名參數／語言resolver／關鍵字與完整scope快照通過；Localization92/116、方案16TRX4004、coverage96.61%/97.70%、strict雙目標零警告／錯誤。兩階段格式化及重疊前綴priority真紅後修正，A/B1000、故障保真重試與百次弱參照回收通過。驗收wp-reports/P1-05-acceptance.md及evidence/p105-final/。主會話直接審查，未恢復外部雙模型；主P1保持in_progress，下一P1-10。

## P1-06 最終驗收（2026-11-06）
正式時間四入口、TimeProvider／純日期與CLDR49完整445映射完成；Word8.2／E.5重新核對後，補實際ChurchReport.Main ICU守門，非僅未來Hosting helper。正規化後方案17TRX4127/4127、Localization124/150（clock32/34）、coverage98.11%/98.47%、strict0/0；I18n119及coverage96.77%、Debug7、PE27、Plugin metadata10/build6、CI6。ICU／NLS／Invariant各一案與真實NLS啟動拒絕通過。缺來源verify、缺主機守門、CLDR清單空白oracle及分析器真失敗均保留修正證據。正式計畫未變，無Word修正報告需求；主會話自審，無外部reviewer／第二人。執行層analyzer曾經自癒入口嘗試雙後端，皆無可用輸出，不能稱成功。不歸檔主P1，依使用者要求續作已解鎖P1-07／09。

## P1-07 最終驗收（2026-11-06）
Money兩宿主35/35、完整Localization159/185，方案17TRX4197/4197；coverage98.17%/98.52%、strict0/0。七幣/default/精度/負零/溢位、千次A/B、百次文化弱引用及故障重試通過。主會話自審無外審；只封存p107-money，主P1保持8/12並續P1-09。

## P1-09 最終驗收（2026-11-06）
完整key/參數/跨組唯一、20/14與pseudo、跨文化關鍵字及英文資訊報告完成；工具170（v2 51）、coverage97.54%、strict0/0，方案17TRX4203/4203、Localization162/188及coverage98.17%/98.52%。原完整14字教會名保留，只縮sender固定後綴助手使pseudo21→19；正式需求/上限/公式/英文發布政策不改，無Word修正報告需求。孤兒/大小寫、顯式group budget、JSON高surrogate、舊精確golden及錯namespace參數均有修正與證據。主會話自審無外審；只封存p109子任務，主P1 9/12，續P1-08/11/12。

## P1-08 最終本機驗收（2026-11-06）
完整來源/validator/有界single-flight完成；回收48/200未釋放根因以同步內容計數真紅0/2確認，改ForceYielding後Tenancy75/75及五輪10/10。交付重建17TRX4317/4317、Localization182/208、ToolUtility1231/1369、ChurchReport630；五份coverage97.84%/98.17%、Core95.50%/95.86%、Tenancy96.96%，strict0/0，全部必要守門通過，Changed1133檔0問題。dfb82a7e驗收交付，原實作b74a311d、UI封存f2c7bfbc；矩陣wp-reports/P1-08-acceptance.md及evidence/p108-final。主會話自審，外部兩後端自癒入口run20261106-161834均無可用輸出，不能稱雙模型或單模型完成。主P1保持active/in_progress，10/12，續P1-11/12，未連VM/D365/LINE、未push。

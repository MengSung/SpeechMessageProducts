# P1 執行帳本（2026-11-05）

## 本輪接續（2026-11-05 午間）
- Goal 保持原 P1，不覆蓋；工具回報 `usageLimited`，不假稱已重啟 Goal 或仍在背景自動運作。當前依使用者「繼續」在本輪執行。
- 最新原全方案 Release build 退出 0、0 錯誤／18 警告；15份 TRX 合計 1,403 案，1,399 通過／4 失敗／0 略過。失敗並非效能偶發：三案期待遠端原始 LINE message，另一案反射舊 `_client` 欄位。證據 `artifacts/p1-resume-full-{build,tests}.log`、`artifacts/p1-resume-full-tests/`。
- 維持既有安全修補，不回傳原始 provider body。三個整合測試精確斷言安全訊息、保留 ProviderRejected／標準例外，另外注入私密合成標記並斷言不外洩。生命週期測試改讀真正 owner `_transport.client`，仍驗自有 HttpClient 已 Dispose，另驗 SDK 停止新操作及替換實例；未刪斷言或放寬標準。
- 首次修補回歸有一個測試筆誤：FieldInfo 取值仍傳舊 SDK 而非 transport，導致 ArgumentException；修正後定向生命週期 1/1 通過。此筆誤不是產品缺陷，原始失敗紀錄保留 `artifacts/p1-regression-repair-tests/`，定向結果 `artifacts/p1-regression-repair-lifecycle/`。最新全方案重跑中，不預先宣告通過。
- P1-04 既有 Fee 4案特性通過；日期 golden 的 ambient culture 僅在當案固定 InvariantCulture 並 finally 還原，不設定 DefaultThreadCurrentCulture。XML helper 正反例6案（縮排／引號、欄位變更、缺少節點、順序、DTD、1MiB）全部通過。
- P1-04 新增現有 FetchXmlQueryService 的認獻／收費黃金查詢與原例外測試4案；合計14/14通過，產品尚未修改。證據 `artifacts/p1-04-pre-extraction.log`／`artifacts/p1-04-pre-extraction/p1-04-pre-extraction.trx`。
- 確認 Facade 有兩套認獻／收費名稱路徑：`RetrieveDedicationBookingByFetchXml`／`RetrieveFeeByFetchXml` 目前經 Fee 空方法，`RetrieveDedicationBooking`／`RetrieveFee` 則經有效 FetchXmlQueryService。有效查詢的條件已鎖 golden，可以收斂單一 Fee 實作，不能帶空殼進 Core。
- P1-04 雙模型分析 run `20261105-122655-integration-p1-04-extraction-analysis-analyzer`：dualModelComplete=true、quotaBlocked=false、degradedFallback=false。為去識別化摘要風險分析，非 code review。採納先分類／溯源／特性／安全修補後才搬移；模型提出必須修計畫之建議尚無充分依據，現行計畫已有安全抽取邊界，暫不修改正式計畫。
- 進度頁已記錄原全方案紅燈及最新有條件 VM 授權，區分歷史 D24 與最新對話；未確認隔離、適當備份與可驗證復原前仍不寫入，亦不把不可逆 schema 操作視為當然可執行。

## 持續目標與執行邊界
沿用 active P1 Goal；主會話 inline 實作／檢查，不派 implement/check worker。前輪三個 worker 已停止，無背景代理。CCG 只經自癒入口作唯讀分析／審查。不 push、不 merge、不部署正式環境；VM 預設只讀，必要寫入先確認隔離／備份／復原。

## 已取得證據
- 基準全方案1125、ToolUtility孤兒63、Debug tracer7、encoding536無問題、duplicates26projects879files0groups、I18nCheck及進度3通過（artifacts/p1-bootstrap-20261105）。
- smoke：100ms→2秒，可控時鐘紅2/綠62；180秒90輪，caller取消／原例外／finally自己的task清理。artifacts/p1-smoke-red.log、p1-smoke-green.log。
- P1-01：51個tracked孤兒檔無有效外部參考後刪除；完整清單 wp-reports/P1-01-reference-audit.md。效能15原案例全部保留，補fixture：15紅→15綠（artifacts/p1-01-performance-*）。ToolUtility.Tests加入方案。
- P1-02：模型反例紅10→綠12；真實XML反例紅9→net10綠22（含原方案與reflection）／net48載入net462綠1。新增無消費者net48產品反例紅1，修正後待完整重驗。已加NuGet Framework實選assets檢查。
- P1-03：舊net10捕獲六類golden（修正草稿錯誤強制轉型，不改產品），改成內嵌逐字比對；dual各6綠。產品net462/net10，test宿主net48/net10；無新增產品API polyfill。
- P1-03新功能：Sender 1..20/https、Flex Background、webhook id/redelivery、HMAC原始byte固定時間驗簽：行為紅18→六golden與功能net10 64/64、net48 59/59（含既有tests）。RFC向量改case2（case1的全vertical-tab密鑰會被空白設定守門拒絕，不更改產品檢查）。
- P1-03隔離／資源：實際重現共享HttpClient預設header跨channel及request/response滯留，23紅；修成per-request credential、operation-owned response／stream owner、Dispose停止新請求、borrowed upload stream不關閉、401不帶raw body/userURL：兩宿主各23綠。待全套與外審。

## 審查
分析run 20261105-084222-integration-p1-focused-analysis-analyzer雙方完成，非code review。
Bootstrap review run 20261105-090525-integration-p1-bootstrap-reviewer dualModelComplete=true，無quota降級。Claude指出審查包diff空白為Critical證據缺口：產生包前HEAD已被外部提交前進，故不能用當時HEAD當基準。將使用固定P0基準cb932c01補送完整engine/fixture與sln，再重審。R4無消費者net48產品反例及fixture原子計數已採納；原粗效能預算保留，不放寬既有斷言。SDK實作審查未執行。

## 版控觀測
09:01外部程序／使用者建立f5c69cde及82515aab，已包含原暫存刪除、smoke、P1-01/02與SDK retarget/golden等修改，並混入先前未辨識.turns檔。不是本主會話執行的commit；保留、不重設或改寫。後續本地主動提交只含可明確歸屬P1的新diff；審查以固定P0起點取碼，避免HEAD前進造成空包。

## 環境
Visual Studio (devenv) 同時開啟此solution，其設計時還原曾將Line.Messaging.Tests assets只留下net48，造成NETSDK1005；完整--force restore後兩目標正常。未關閉使用者IDE或修改全域設定；每次final gate先明確restore並記actual targets。
DevExtreme23.1.5本機來自已授權local source，公開NuGet固定版本404且未找到vendor環境變數；未取得可用hosted來源／CI證據，沒有洩漏source/key。繼續其他工作。

## 待完成
P1-01/02/03完整00§8、byte-level、duplicates、package、打包驗證與分階段commit。P1-02 最新定向雙模型複審已完成，但全階段 gates／逐包驗收仍待完成。P1-04～12尚未完成，不得標完成。先完成 ToolUtility 型別盤點與可搬 Operations 特性測試，再抽 Core；P1-10先於08。整階段任務只在全部必要證據通過後封存。

## 接續檢查點（2026-11-05）
- 使用者要求每有進度即更新 `文件/規劃設計書/實作計畫-3.3/實作進度.html`，並逐包列出 P1-01～P1-12。頁面已新增12包清單，P1-01～03標為驗收收斂中，P1-04～11未開始，P1-12依D24標示使用者執行apply；總計0/12驗收完成、3包進行中。
- 進度頁新增渲染測試；先紅（缺少P1列表renderer），實作共用renderer後4/4通過。
- 2026-11-05 CCG analyzer run `20261105-103725-integration-p1-02-warning-resolution-analyzer`：`reviewMode=dual`、`dualModelComplete=true`、`quotaBlocked=false`。確認 `ArchitectureProjectReader.Evaluate` 的終止等待及pipe drain沒有期限；`QueryFixture.LastQuery` 欄位並行發布不安全。Gemini所提 `AdvancingClock` 競態依實際使用圖不成立（clock per fixture，輪詢序列await，`OnDelay`無呼叫端）。
- 計畫本身已有有界等待／資源釋放規則；此為實作缺口，故不改正式計畫、不產Word修正報告。後續依TDD加入程序期限及雙管線輸出測試，並修正有界讀取與 LastQuery 發布。

## P1-02 修正里程碑（2026-11-05）
- RED：新增程序期限與雙管線超額輸出故障測試後，首次執行因缺少 `BoundedProcessRunner` 而編譯失敗（兩處 CS0103）；測試明確依賴尚未提供的 runner 行為。
- GREEN：`ArchitectureProcessRunnerTests` 的真實子程序測試通過 2/2。500ms期限測試在8秒斷言範圍內回報逾時；stdout與stderr各輸出約2MB時，runner持續排空、每條只保留4,096字元並回報截斷。
- 實作以同一30秒總期限涵蓋程序退出及兩條pipe drain；每條最多保留1,048,576字元，輸出逾限時架構讀取器拒絕解析截斷JSON；逾時後終止程序樹，最多2秒回收，關閉串流並有限等待reader完成。清理失敗會與主要例外一起保留，不讓錯誤被覆蓋。
- `QueryFixture.LastQuery` 已改用 `Volatile.Read/Write` 明確發布；64路並行回歸完成。最新版 `SpeechMessage.Localization.Tests` net10 全套架構／MSBuild 67/67、`CollectionQueryServiceAsyncTests` 16/16、進度頁 4/4 均通過；Changed-scope encoding 3檔0問題，7個異動文字檔均通過UTF-8無BOM／CRLF／final CRLF 位元組檢查，`git diff --check` 無錯誤。
- MSBuild 評估命令增加 `-maxcpucount:1` 與 `-nodeReuse:false`，維持不執行targets的單程序讀取契約。API的 `WaitForExit` 只證明根程序退出；runner 文件限制不得套用於會自行建立子程序的工作負載，若 tree termination 回報不完整即明確失敗，不假稱後代皆已退出。
- P1-02仍未整包驗收：最新版定向雙模型 review 已完成且無 Critical／Warning；仍需重跑全套 gates、逐包證據與階段級檢查。

## P1-02 最新雙模型複審（2026-11-05）
- 經專案自癒入口執行 reviewer run `20261105-113854-integration-p1-02-process-lifecycle-review-reviewer`；`summary.json` 確認 `reviewMode=dual`、`dualModelComplete=true`、`quotaBlocked=false`、`degradedFallback=false`，Gemini 與 Claude 均有輸出。
- Gemini：Critical 0、Warning 0；認可單一總期限、雙管線有界保留／排空、截斷 JSON 拒絕、失敗時合併清理錯誤及 LastQuery 安全發布。其 Info 建議 BOM 與專案 UTF-8 無 BOM 規範相衝，未採納。
- Claude：Critical 0、Warning 0；Info 指出 StopAndReap 清理例外型別篩選可收斂、測試 Arguments 使用舊 overload、未覆蓋「根程序退出但子程序持有 pipe」情境，且明確將後者定位為目前單程序 MSBuild 評估契約以外的未來工作負載限制。現有註解已禁止擴用至會自行建立子程序的命令；本輪不為非必要 Info 改動受審程式。
- Claude 輸出含一段稱本身未經 CCG 的文字，與 runner `summary.json` 的實際成功紀錄矛盾；原始 stdout/stderr 及摘要均保留，按自癒 runner 的機器摘要判定雙模型完成，並如實記錄此輸出內部矛盾。
- 本次 review 僅涵蓋 P1-02 程式切片，不等於 P1-02 全包或階段 1 驗收。局部既有測試證據仍為架構／MSBuild 67/67、runner fault injection 2/2、query 16/16、進度頁 4/4；全階段 gates 與 P1-01／03 完整回歸尚待重跑。

## P1-04 接續檢查點（2026-11-05）
- 正式計畫要求 ToolUtility 逐型別盤點、每個待搬 Operations 的特性測試先在原實作上通過，再搬到雙目標 Core。
- 已找到舊來源：父工作樹 `.其他解決方案\\LineApiWebhook\\ToolUtility`；初次誤查的 `Dynamics365Line\\LineApiWebhook` 路徑不存在。後續逐方法比對舊版行為，並以目前產品版本與實際消費者為相容權威；舊來源若與核定規格衝突，不盲目複製。
- 目前初步分類：純 `IOrganizationService`／`Microsoft.Xrm.Sdk` 型別才可能搬；ServiceClient、Microsoft.Extensions、檔案、logger、連線／cache／host DI 與 facade 明確留在 host。逐檔清單與特性測試結果待補入 `wp-reports/P1-04-inventory.md` 及此帳本。
- 總階段驗收維持 0/12；Hyper-V／D365 預設只讀，不因 P1-04 離線工作擴大權限或做實機寫入。

## P1-04 有效消費路徑與舊方法初核（2026-11-05）
- 僅搜尋 ChurchReport 實際 `.cs` 原始碼（排除 `bin/obj`），未找到 `OrganizationServiceProxy`、`ServiceClient` 或 `.ServiceClient` 使用；確認 7 個 `RetrieveMemberListCollectionByListIdDynamics365`／`RetrieveDynamicMemberListDynamics365` 呼叫點，皆經現有 `ToolUtilityClass` facade，以 `IOrganizationService` 進入 ListService。正式計畫的「非 `IOrganizationService` 大量消費」停止條件目前未觸發；文件範例及註解不計入有效呼叫。
- 四個舊來源已核實：`Services/QueryService.cs` 的 `GetPaidFee`、`GetNotYetPaidFee`；`ToolUtilityClass/ToolUtilityClass.FetchXmlAndString.cs` 的 `RetrieveFeeByFetchXmlOrderNumber`；`Core/ToolUtilityFacade.cs` 的 `RetrieveEntityNoCache`。
- 三個舊 FetchXML 方法把呼叫值直接插入 XML 屬性；這是可重現的 XML/query injection 風險，不按舊字串寫法搬移。特性測試只使用合成識別值，對 `<`、`&`、`"`、`'` 證明 XML 解析後保留原值且條件樹不被注入；識別碼欄位另依其業務型別驗證。
- `ToolUtility.Tests` 已在 solution，並有獨立 Moq `IOrganizationService` helper；目前缺 FeeOperations、QueryOperations 專屬測試，待新增搬移前特性測試。此處尚無程式實作或 P1-04 驗收通過。
- AGENTS 指定的 `.trellis/spec/backend/cross-user-isolation-and-performance.md` 與 `.trellis/spec/guides/cross-user-isolation-and-performance-review.md` 在此工作樹不存在；已讀取 AGENTS 內完整隔離條款及現有 duplicate-row、error-handling、logging、encoding、CI 規範，並將缺檔列為審查限制。

## 接續回歸與專案現況（2026-11-05）
- 重現 `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs`：4 個案例中 2 個失敗，均因斷言仍要求舊進度文案「P1-01～03」及「最新版雙模型複審尚未完成」；目前頁面已正確顯示 P1-02 `dualModelComplete=true` 並接續 P1-04。修正測試為按單一工作包列定位當前狀態，避免跨列文字誤匹配，保留 12 個 ID 與 P1-12 D24 apply 邊界斷言；結果 4/4 通過。
- P1-04 實際產品專案是 `ToolUtility/ToolUtility.csproj`（net10.0），測試專案是 `ToolUtility.Tests/ToolUtility.Tests.csproj`（net10.0）；現有 `ToolUtility/Core/IToolUtilityCore.cs` 是既有程式碼，不代表 `ToolUtility.Core` 雙目標專案已建立。盤點範圍仍依正式計畫列出的各 partial、Operations、Core、Extensions、Constants，不能以路徑名推定可搬。
- 最新進度頁已記錄 renderer 回歸 4/4、階段仍 0/12 驗收完成、P1-04 未驗收；P1-01／03 全階段 gates 與 P1-12 實機寫入界線均未改變。


## 回歸與盤點收斂
- 全方案最新重跑退出0，15份TRX合計1409/1409、0略過，ChurchReport612/612；證據artifacts/p1-regression-green-tests/。其後新增FetchXmlFee4案另跑綠，最後整體仍須重驗。
- 逐型別盤點已寫wp-reports/P1-04-inventory.md：實際62檔／76宣告列，先前61為計數錯誤，並非需求擴張。候選前置／host依賴仍明列，不代表抽取完成。
- 三個新Fee API測試先因未存在而CS1061，artifacts/p1-04-fee-contract-red.log；這是API缺口紅燈，不宣稱已重現所有行為缺陷。
- 局部review run20261105-124121雙模型完整成功、無Critical；反射耦合Warning保留以實際驗自有HttpClient Dispose，不為測試擴大產品API。資源命名Warning採用固定EmbeddedResource.LogicalName並待變更RootNamespace驗證；完整回歸覆蓋Warning已有1409結果並待最後重跑。


## 13:00 Core 搬移檢查點
- Fee 安全實作／Query委派搬移前正常建置54/54通過，證據 artifacts/p1-04-before-move/；既有1409全方案綠燈早於此產品異動，不能作為最新版驗收。
- FeeService／IFeeService已移入Core，雙產品net462/net10及net48/net10測試配置建立，尚未搬後編譯成功。
- restore被NU1903阻擋：Pkcs6.0.1（GHSA-555c-2p6r-68mm）及System.Text.Json6.0.8（GHSA-8g4q-xg66-9fp4）；artifacts/p1-04-core-restore.log。將沿用中央安全版本直接引用，不關audit／不新增NoWarn。
- 本輪HEAD已外部前進至dbd785ec，保留他人提交／變更，不將外部提交宣稱為本輪操作。沿用P1 Goal，工具仍usageLimited；本輪依使用者繼續執行，不宣稱Goal已重啟。


## 13:08 Fee Core 雙目標綠燈
- 初次安全直接引用JSON於net10觸發NU1510；檢查assets確認弱點JSON6.0.8實際在Framework圖。改為net462/net48直接中央JSON6.0.11，net10僅直接Pkcs10.0.14並使用內建JSON；restore成功，不關audit／不加NoWarn。官方NuGet PackageReference文件確認net10 pruning行為。
- Core初次strict build出CA1861六錯，將三個固定結構欄位陣列改有限且唯讀ReadOnlyCollection，不共享使用者資料。最新版雙目標build0警告／0錯誤（artifacts/p1-04-core-strict-build-green.log）。
- Framework測試首次缺LangVersion造成CS8630；設定latest後net48 51/51、net10 118/118、無略過。反射測試驗net48實際載net462。證據artifacts/p1-04-core-dual-green/。
- 每target新鮮Core覆蓋率：net48載net46295.60%、net10 100%，均通過Test-Coverage ≥80%；artifacts/p1-04-core-coverage/。
- net10既有host依賴帶入System.DirectoryServices9，直接SDK帶Permissions6，產生MSB3277；待查正確相依修復，不抑制。局部綠燈不是全P1驗收。


## 13:27 Entity／產品隔離與最新版整合證據
- Product DI先CS1501紅（API缺口），新overload後8/8綠；實際子池鍵讀取證明ProductA/B區分、相同註冊冪等、衝突拒絕且集合不變、lease歸零及容器Dispose只關自己client。舊無參數API保留ChurchReport且依計畫Obsolete。
- EntityQuery三個搬前characterization3/3；NoCache API先CS1061；先還原舊來源直接SDK別名後安全測試12失敗/4通過，真實重現XML注入／缺ID／重複ID／Dispose後仍可操作；修復後搬前含原2案21/21，才git mv兩檔至Core。
- Entity借入SDK不Dispose，GetService取得操作局部參照；Dispose停止新操作並清除欄位，已開始同步呼叫可完成，host須先drain再關其SDK。無背景工作或cache。保留optional ctor未初始化的原明確失敗語義。
- Core net10首次CA1510／CA1513，依既有跨目標模式採NETFRAMEWORK分支與現代ThrowIfNull/ThrowIf，不新增NoWarn。兩target strict build0警告0錯誤。
- 最新完整ToolUtility測試net48 77/77、net10 152/152；Core覆蓋率97.63%／100%，每目標新鮮報告及Test-Coverage通過；artifacts/p1-04-current-{dual,coverage}/。
- Permissions6與host DirectoryServices9衝突：首次試10.0.14造成Drawing.Common9/10衝突，未採；中央Permissions9.0.0與既有9系列對齊後MSB3277=0、restore安全稽核通過（artifacts/p1-04-permissions-aligned-*）。不升整個圖、不抑制警告。
- 新版全方案Release build退出0、0錯誤／4個本次依計畫Obsolete提示（增量建置）；16份TRX1565/1565、0略過、ChurchReport612/612。artifacts/p1-04-integration-{restore,build,tests}.log及tests/。所有16個組件／目標均有TRX，無覆寫警告。
- CI精確依賴閉包守門先紅4/5；新增Core coverage矩陣守門後紅4/6。修復filter補Core與ToolUtility.Tests且仍只排除原核定三專案，coverage加入net48/net10兩target；綠6/6。舊硬編碼23改精確比較實際eligible集合，保留固定三項排除與全差集，不降低檢查。沒有hosted證據。
- 編碼Changed(73b6e6cf)先20檔紅，都是前輪P1產物LF／缺檔尾；只正規化換行且比較文字內容不變，重跑633檔0問題／排除488份raw模型產物。duplicates28專案921檔0groups/new/stale。新異動16文字檔另已做位元組斷言。
- Fee run20261105-130623 reviewer雙模型完整、無Critical/High。Gemini兩項Warning實為已處理風險提醒；Claude三項Warning為來源可獨立核對、期數空白相容、是否host還有舊副本。已核對active host無三新API副本、唯一現行期數固定001；補舊來源切片與分頁Same/MoreRecords/Cookie，連同Entity與DI送run20261105-132016重審。尚不預判結果。
- P1仍0/12，未修改正式計畫、無Word修正報告需求；VM未操作。

## 13:34 審查回應
- run20261105-132016雙模型完整完成，無quota降級、無Critical/High；Claude兩Warning為FetchExpression略過實體驗證與反向日期分支未測。
- 核對後採納：先補22案例，11失敗/11通過；跨實體回應確實未拒絕。補串流根實體解析、I/O前驗證、禁DTD與外部解析；不重寫query、單操作reader Dispose；Fee日期反向測試屬既有行為補證。定向93/93通過，待雙宿主/coverage/完整回歸及再審。
- 送Fetch/CI固定快照補審；保留P1 0/12。Goal工具仍usageLimited，本輪依使用者繼續，未聲稱已重啟。

## 13:48 三組Operations抽取
- Appointment/Lessons/MeetingStatistics固定舊來源47ab85f8，12份合成XML加ByDate型別化查詢先13/13綠；source SHA artifacts/p1-04-query-source-goldens.json。安全測試81失敗/18通過，修後搬前112/112。
- 六檔git mv到Core；新增共同internal SdkDataQuery只負責XML/日期/整頁識別，不新建連線或通知器。借入SDK不Dispose，logger不保存，原exception交host。保留原欄位、top、順序、status、alias及舊未filter ByDate／weekly uitype提示。
- Core雙目標strict0警告0錯誤，ToolUtility net48(實net462)207/207、net10 282/282。coverage初次TRX attachment副本使精確一份守門拒絕，未刪證據；分離coverage run後新鮮單份98.78/100通過。
- 前Fetch/CI review20261105-133329雙模型完整，無quota、Critical/High；Claude三Warning均快照/證據缺，本輪Operations外審已包含缺件。CS8603曾擋net462，等價顯式null判斷修正，無NoWarn。
- package34專案158refs0錯、analyzer26/26、I18nCheck及CItransition6、TracerCI、SixLaborsCI、package/coverage契約皆通過；Debug tracer與離線Plugin測試執行中。P1仍0/12。

## 2026-11-05 14:01 驗收與審查收斂
- 最新產品與純測試增補後，全方案16TRX1836/1836，ChurchReport612/612；CI14TRX1145/1145。原摘要誤算CI為13份(錯把被排除ChurchReport產品當測試)被assert拒絕，改精確比較兩集合；沒有改/略過任何測試。
- Core net48實net462211/211、net10 289/289；獨立最新coverage98.78%/100%。CI全部10模組/目標各單份報告≥80%。Debug tracer7及正TRX、Plugin10/6、Pester39、JS20、工具108/27、CCG6全過。
- reviewer20261105-134544/135446兩輪均dualModelComplete=true、quotaBlocked=false、degradedFallback=false，無Critical/High；nullable Warning以evaluated設定和實際metadata證明誤報，ByDate4故障與完整Golden helper補證均確認。末輪僅「零產品改動」稽核Warning已補13檔與前審快照SHA完全相同證據，沒有新產品行為修改。
- P1-02正式需求逐項、雙模型及00§8均有證據，首次標為正式完成，階段1/12；其餘未擅自標完成。P1-04還有CRUD/Owner/AttributeUtility/Constants/Extensions及其餘純契約候選待處理，P1-03補審仍未結，05～12未完成。

## 2026-11-05 14:25 接續與本地主動提交
- 沿用P1 Goal，get_goal仍usageLimited，沒有重啟/覆蓋或宣稱背景執行；依本輪使用者繼續工作。
- SDK最終20261105-140350雙模型完整、無quota降級／Critical／Warning，舊C1/W2關閉；本地29快照核對，最新SDK96/96、101/101，P1-03正式驗收。P1目前2/12。
- 進度卡片/階段表舊done=0被新增測試實際拒絕(0≠1)，同步後5/5；不是用一致性測試證明真實驗收。增量審查20261105-141440完整雙模型，Claude因未附alert原文推論未同步，但實際title/body已提03；已補直接render斷言、P1-03固定里程碑、P1-12既有is-you與total.active sum。#if三檔清單已補。
- 文字換行無語義正規化後Changed(73b6e6cf)672檔/548 raw排除/0問題；duplicates28專案924檔0group/new/stale。git diff --check在quoted source snapshot抓到原來源尾端空白，保留證據不改raw；code staged check0問題。
- 本主會話明確執行本地commit abbbba3b：44檔Core查詢/DI/CI安全切片。提交前最新ToolUtility雙宿主211/211、289/289，未push。之前47ab85f8是外部提交，不冒稱本輪建立。
- CRUD/Owner舊來源固定abbbba3b與SHA，7/7特性先綠；22安全案14失敗/8通過，修後搬前29/29再git mv四檔。真實SDK例外原樣交host，不再把ID/Message拼入owner外層；保留alternate-key更新，不推測業務冪等鍵。
- 最新補四ctor及並行1000Create+1000Assign後，雙宿主244/244、324/324；Core strict0警告0錯誤，獨立新鮮coverage98.91%/100%。review經自癒入口執行中，不預判；尚未用這輪結果替代完整方案再驗證。

## 2026-11-05 15:10 P1-04 Constants 與相容性接續
- 接續時原 P1 Goal 狀態仍為 `usageLimited`；依使用者本輪「繼續」執行，沒有宣稱背景仍在跑或另行建立 Goal。
- 檢查到外部 HEAD 前進為 `a7fba28f7` 且與遠端追蹤分支一致；這不是本主會話建立或 push 的提交。保留完整歷史與內容，不回退、不改寫；本輪僅留進度 HTML 與後續 P1-04 變更。
- 進度頁回歸最新 5/5。P1-04 mutation reviewer run `20261105-142426` 機器摘要為 dual、兩個 backend 均完成、無配額降級與 Critical；Claude 提出兩項 Warning：Delete 空／空白名稱相容差異需明載、AssignOwner callers 需核對 Entity logical name。
- 35 處有效 ChurchReport `AssignOwner` literal callsites 已逐項核對；目標 Entity 來自同名 Retrieve 或同 logical-name 組裝，未找到 mismatch。整個 ChurchReport 原始碼未找到 `catch (ArgumentNullException)` 或相同型別專用過濾，因此 Delete 舊型別無專一攔截相依。
- 補 `Delete_NameValidation_PreservesEmptyExceptionAndRejectsWhitespace` 精確案例：空字串維持 `ArgumentNullException("entityName")`，純空白為 `ArgumentException`；net10 定向 1/1。原實作已符合此預期，因此只鎖回歸行為，沒有改產品語義。
- 首次 net48 全套在編譯期 CS0234，原因是新增 `ColumnContractTests` 已依 Framework host 不引用 ToolUtility 的架構，只能從 Core 找 `CrmEntityColumns`；不是測試略過或 NuGet/環境問題。正式 P1-04 本身要求盤點／抽取 `Constants`，故將現有純固定欄位目錄移至 `ToolUtility.Core/Constants/`，未排除 Framework 測試。
- 抽取後 ColumnContractTests net48（宿主實載 net462）23/23；完整 ToolUtility net48 270/270、net10 350/350；Core Release strict `--warnaserror` 雙目標 0 警告／0 錯誤。TRX 位於 `artifacts/p1-04-columns-core-net48/`、`p1-04-current-dual-net48/`、`p1-04-current-dual-net10/`；net48 首次 CS0234 保留於本輪工具執行紀錄。
- `CrmEntityColumns` 維持 21 組欄位及順序；類別文件修正原先指向不存在 `ContactColumns` 的範例，記載私有固定模板、每次讀取複本、caller 所有權及無租戶／請求狀態。覆蓋率仍須以抽取後各目標新鮮報告重跑；增量雙模型審查尚未執行，P1-04仍未驗收。
- 已將此狀態寫入進度頁與 P1-04 inventory／core-extraction 報告。下一步：獨立 net48/net10 coverage、更新 P1-04 相容性與 review evidence、透過 `Start-CcgDualModelRun.ps1` 進行增量雙模型複審，再跑 changed-scope encoding／duplicates／回歸。

## 2026-11-05 15:57 階段 2～8 進度頁工作包登錄
- 依使用者要求，把正式計畫 03／04 的階段 2～8 共 53 個工作包逐包登錄到 `文件/規劃設計書/實作計畫-3.3/實作進度.html`；各包包含名稱、實作範圍、指定驗收／測試與前置／安全界線，階段摘要另列依賴順序及外部操作停止條件。
- 數量對照為階段 2～8：12／9／6／13／6／2／5；ID 共 53 且唯一，全數 `todo`；不改 P0 18/18、P1 2/12 或全計畫 20/83 統計，也不宣稱後續階段已開始。
- 對照 03／04 原文核實 P2-04 測試憑證保密與使用者沙箱實收、P3-04 平台 SQL 未提供即停止、P3-09 測試站操作、P5 舊來源唯讀及使用者切換、P7 禁止 Codex 連正式組織、P8 同名不同 ID 保留／重複 ID 拒絕、角色中文比對停止、伺服器端教會授權、人工登入不保存帳密、v3.3 已完成 Word 修正。P8-01 已改為計畫原文「拒絕發布並診斷」；P8-03 明列教會由伺服器依登入者判定。
- 初次重新跑 `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 先抓到 P8-01 用語斷言不同，隨後抓到 P8-03 摘要沒有明載伺服器依登入者判定的正面規則；補齊原文契約並同步斷言後，當時結果 7/7 通過、0 略過。`git diff --check` 通過。進度頁快照及時間軸已記錄此驗證。
- 階段 2～8 內容分析 CCG run `20261105-152344-integration-progress-stages-2-8-analysis-analyzer` 的 `summary.json` 為雙模型完成、無配額降級；其輸出曾基於過期頁面快照，故內容結論以現行 03／04 與本機資料／測試為準。針對目前變更的雙模型 reviewer 尚待經專案自癒入口執行。
- 本工作僅登錄未來計畫，沒有修改正式計畫文件、不需 Word 修正報告；Hyper-V／D365 未連線或寫入。P1-04 仍進行中，不代表 P1 Goal 完成。既有其他未提交修改均保留。

## 2026-11-05 16:12 階段工作包複審與展開提示修正
- CCG reviewer run `20261105-160221-integration-progress-stages-2-8-review-reviewer` 經 `Start-CcgDualModelRun.ps1` 完成；`summary.json` 確認 `reviewMode=dual`、`dualModelComplete=true`、Gemini／Claude 均成功、`quotaBlocked=false`、`degradedFallback=false`。
- 兩模型一致確認 53 IDs、階段數、todo 狀態、20/83 已驗收統計、03／04 安全界線與動態欄位 `esc()` 跳脫均正確；核實後沒有有效的 Critical 或 Warning。Gemini 提出的 `<title>` 編碼 Warning 與 Claude 位元組核實相矛盾；本機 UTF-8 解碼及 `Get-Content -Encoding utf8` 均實際讀得 `<title>實作進度</title>`，因此判為誤報，沒有改動正確標題。
- Claude 唯一 Info 指出 `<details>/<summary>` 隱藏原生標記卻沒有替代視覺提示。先新增回歸測試並確認紅燈，再以 `aria-hidden` 可見加號／減號提示補齊；專項測試 1/1 通過，完整 `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 8/8 通過、0 略過。`git diff --check` 通過。
- 進度頁於 16:12 更新快照與時間軸：初次雙模型審查完成、Info 已修正、最終複審待完成。此後需再依自癒入口複審最終頁面／測試；未更動正式計畫，無 Word 修正報告需求。D365／Hyper-V 未操作，P1-04 仍在進行中。

## 2026-11-05 16:25 階段 2～8 工作包登錄驗收
- 第二輪 `20261105-161352-integration-progress-stages-2-8-final-review-reviewer` 雙模型完成、無 Critical／Warning；唯一 Info 為 P5-03 內 FileVersion／rollback 關卡歸屬已明標「階段 5 共同關卡，非 P5-03 專屬驗收」，不改原規則。
- 真實 Edge／Playwright 在 1440×1000 明色、390×844 明色、320×800 暗色驗證 7 個階段、53 個 todo 包、Enter／Space 展開／收合、可見加減號、頁面錯誤 0、溢出 0。首次 320px 全頁寬 362，定位為既有 P0 長名稱／時間軸；修正最小欄寬後剩 322，再定位警示內長 API 文字並補換行，最終三尺寸精確等於視窗寬，未提高容許值或隱藏內容。工具及 JSON／截圖保存在 `artifacts/progress-stages-2-8-browser/`，每個 context／browser 均在 finally 關閉，HTTP 外連已阻擋。
- 自行核對並修正兩處摘要精確度：P4-05 回呼按金流供應商契約驗簽／冪等，不套 LINE webhook 契約；P5-01 影子模式讀真實服務、僅記錄寫入／推播，只有離線測試使用合成資料。正式 03／04 原文不變，無 Word 修正報告需求。
- 上述最後增量經 `20261105-162250-integration-progress-stages-2-8-closeout-reviewer` 雙模型複核：`dualModelComplete=true`、`quotaBlocked=false`、`degradedFallback=false`，Gemini／Claude 均無 Critical／Warning。
- 最新頁面測試 8/8、0 略過；兩份正式原文擷取 ID 與頁面比較為 53 對 53、無缺漏或額外項；P0 18/18、P1 2/12、全計畫 20/83 保持不變。進度頁快照／時間軸已更新最終結果；本次獨立文件任務紀錄歸檔位置為 `.ccg/tasks/archive/2026-11/integration-progress-stages-2-8/`。主 P1 任務與 Goal 未宣告完成，後續接回 P1-04 檢查點。
- 提交前重跑頁面 8/8、三種尺寸瀏覽器驗證及 53 個來源 ID 比對，結果一致；測試紀錄、瀏覽器程式／JSON／截圖另保存於本次獨立任務的 `evidence/`，原始外部模型輸出保持原樣。只提交本次頁面、測試與紀錄；既有付款測試及應用設定修改維持工作樹原狀。
- 本地頁面與驗證提交 `48964ee5`；原始 runner health JSON 的檔尾空行另保留 ZIP 原件與 SHA-256，Git 複本只做格式整理且 JSON 值相等，暫存 diff 檢查已通過。沒有 push，未封存主 P1。

## 2026-11-05 18:21 P1-04 接回實作與輸出新鮮度檢查
- 接續正式 P1，不回到已封存的 P0；目前 Goal 工具仍 usageLimited，沒有覆蓋或宣稱背景持續執行。沿用 Trellis 已核定計畫與 inline 模式。
- 起始 `--no-build` coverage 只發現 net48 244 案、net10 349 案，與現行來源應有 270／350 不符，判定輸出過期；該輪 `artifacts/p1-04-columns-coverage-20261105-181843/` 不列抽取後驗收證據。沒有略過測試或降低覆蓋標準。
- 重新實際建置後測試 net48 270/270、net10 350/350，無略過；`artifacts/p1-04-resume-columns-rebuild/`。後續覆蓋率須使用本輪建置及全新目錄。
- 下一切片 EntityAttributeUtilityService 與 OrganizationServiceExtensions 的唯讀 Gemini＋Claude 分析已經專案自癒入口啟動；先特性、再安全紅綠、最後 git mv，不帶入 logger／連線 owner。主 P1 與 P1-04 尚未驗收。

## 2026-11-05 18:44 P1-04 屬性工具與 SDK 相容入口
- 原host特性12/12；安全31案28失敗/3通過，修後搬前81/81才git mv三檔到Core。新增null欄位名及完整Query/PageInfo斷言，最新雙宿主314/314、395/395，無失敗或略過。
- 兩target各獨立且恰一份coverage，Core99.36%/100%；strict雙目標0警告/0錯誤。證據artifacts/p1-04-attribute-extension-final-{dual,coverage}/及review-evidence.json。
- 固定16來源SHA與完整安全快照經自癒入口啟動reviewer，涵蓋Constants及mutation兩Warning補證；00§8完整方案另行執行中。尚不預判結果，P1仍2/12，P1-04未驗收。
- 本輪不操作Hyper-V/D365/LINE，不修改正式計畫。Goal仍usageLimited，未覆蓋或宣稱背景執行。

## 2026-11-05 19:09 P1-04 切片驗證檢查點
- 最後實際完整方案16TRX2056/2056（已含caller11案），ChurchReport623/623、ToolUtility314/395，無失敗略過；Core coverage99.36%/100%、strict0警告錯誤。Debug tracer7與正TRX、duplicates28/930/0、I18n verify、Plugin metadata10/build6、工具108/27、package34/158/0全過。
- reviewer20261105-184322採納caller可執行補證；185302抓到盤點8種而非7種，補週報InlineData並做8對8精確集合比較，11/11綠。185904最終雙模型完整、無Critical/High/Warning、無quota降級；一項fixture Dispose雙重故障診斷Info已揭露。
- Encoding守門抓到13份前輪文字換行不符，僅正規化CRLF／final CRLF且canonical內容逐份相同，沒有更改timestamp raw證據。正式changed-scope須在本輪明確自有新檔暫存後再跑；沒有放寬守門。
- 進度HTML與wp報告已更新；下一步是本地分離提交本切片，再依盤點續Repository/Activity/Attachment/其他候選，P1-01與05～12待完成，主P1保持2/12不封存。現有Goal仍usageLimited，未覆蓋或宣稱背景執行。

- 2026-11-05 19:16 已完成本地主動提交 `0169e079`（85檔；屬性／SDK抽取、離線caller補證、雙模型與完整回歸證據）；未push，另外兩檔hash不變。接續剩餘Repository與資料操作，主P1與Goal未宣告完成。

## 2026-11-05 19:28 Repository切片接續
- P1-04 Repository 分析191821已完成Gemini／Claude雙模型、無配額降級；原host六入口特性12/12。目前補安全紅綠，保留Update非空ID及SDK原InnerException，記錄固定訊息／拒絕錯誤識別等安全差異。Repository尚未搬移或驗收，整個P1仍2/12；上一切片2056回歸是既有證據，不當成本切片通過。
- 分析建議不直接照單全收：原有參數檢查仍在try前；adapter新增拒絕沿用操作失敗包裝。不能catch ArgumentException一律重拋，因SDK本身也可能拋此型別，故加入12案保護真InnerException。
- Repository目前僅有QueryService／MarketingListService六個RetrieveMultiple消費點；無產品直接new或AssignOwner caller。新增名稱、Assign非空ID／一致性及查詢根表驗證屬安全差異，並非額外授權機制。

## 2026-11-05 19:36 Repository搬移後局部驗證
- P1-04 Repository原host特性12/12、安全30紅/4綠、overload2紅；修後搬前含六個消費點98/98後git mv。搬後雙宿主362/362、456/456，Core獨立coverage99.42%／100%、strict零警告／錯誤。完整方案回歸與CCG雙模型審查進行中，尚未預判通過，P1仍2/12。

## 2026-11-05 19:47 Repository再驗檢查點
- Repository初輪完整方案16TRX、2165/2165，ChurchReport623/623、架構67/67；193656雙模型無Critical／High，有兩個文件Warning，已補聚合無ID拒絕與PageInfo原地改寫文件／案例。I18nCheck曾抓13個搬入Core的中文診斷常值，已改英文固定訊息、Core回0且verify通過，未改基準。英文化及補證後的最後回歸／複審進行中，尚未列最終驗收。P1仍2/12、P1-04整包未完成。

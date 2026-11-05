# P1 執行帳本（2026-11-05）

## 本輪接續（2026-11-05 午間）
- Goal 保持原 P1，不覆蓋；工具回報 `usageLimited`，不假稱已重啟 Goal 或仍在背景自動運作。當前依使用者「繼續」在本輪執行。
- 最新原全方案 Release build 退出 0、0 錯誤／18 警告；16份 TRX 合計 1,403 案，1,399 通過／4 失敗／0 略過。失敗並非效能偶發：三案期待遠端原始 LINE message，另一案反射舊 `_client` 欄位。證據 `artifacts/p1-resume-full-{build,tests}.log`、`artifacts/p1-resume-full-tests/`。
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

# P1 執行帳本（2026-11-05）

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
P1-01/02/03完整00§8、byte-level、duplicates、package、打包驗證及適用雙模型審查／修正／分階段commit。P1-04～12尚未實作，不得標完成。下一個工作包先盤點ToolUtility並鎖住可搬Operations查詢特性，再抽Core；P1-10先於08。整階段任務只在全部必要證據通過後封存。

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
- P1-02仍未驗收：需完成最新版雙模型review、依 findings 修正並重跑，另需全套 gates 與階段級檢查。

# P1-04 Repository 抽取與驗證

日期：2026-11-05。此為 P1-04 的一個切片，主 P1 仍 **2/12**；不宣告整包完成。

## 需求對應與目前狀態

| 需求 | 實作 | 測試與證據 | 狀態 |
| --- | --- | --- | --- |
| 純 SDK 型別保持 namespace 並 git mv | `ToolUtility.Core/EntityOperations/EntityRepository.cs`，介面與類別同檔 | 原 host 六入口特性 `p1-04-repository-before/before.trx` 12/12；修後搬前連 Query 契約及實際 caller 98/98，之後才移動 | 此切片通過，整包未完成 |
| 原驗證與 SDK 包裝相容 | 同上六方法 | 既有參數型別／ParamName／順序、Update 純替代鍵拒絕；12 案實際注入 SDK TimeoutException／ArgumentException，保留同一 InnerException、單次 I/O | 局部通過 |
| 欄位選擇不變 | `EntityQueryService.RetrieveEntity` 三參數 overload | 新 API 缺口 2 紅；null／省略 AllColumns、指定 ColumnSet 原參照、原二參數仍存在 | 局部通過 |
| 單筆與整頁穩定 ID | 共用既有 CRUD／Owner／Query | 空 Create ID、錯單筆 ID／表名、空／重複／跨表頁拒絕，原候選不裁剪，同名不同 ID 完整保留 | 局部通過 |
| 原消費點可運作 | `RepositoryCallerRouteTests.cs` | QueryService 五入口與 MarketingListService 一入口，每入口正常／重複 ID，共12案，實際產品方法→Repository→SDK；不讀設定／憑證 | 離線路徑通過 |
| 隔離及生命週期 | Repository 與操作局部 query adapter | A/B 1000次、59次故障後明確重試、2000個成功頁、active=0、借入Dispose=0；弱參照Query／頁／Entity可回收；listener finally釋放 | 局部通過 |
| 雙目標與strict | Core與`CoreTargetFrameworkTests` | net48實載net462 363/363；net10 457/457；獨立各一份coverage 99.42%／100%；strict雙目標0警告／0錯誤 | 通過 |
| 完整方案及00§8 | 全方案、架構、打包、encoding、i18n等 | 本輪16份TRX2167/2167、623 ChurchReport、67架構；完整守門見下方 | 通過 |
| Gemini＋Claude審查 | 專案自癒入口 | 分析191821、初審193656、末審195337均雙模型完整且無quota降級；前Warning關閉、無新增阻斷缺陷 | 通過 |

## 行為決策與分析核實

- 原始基準 `b9f84d01`；原檔 SHA256、搬前TRX保存在
  `artifacts/p1-04-repository-before/baseline.json`，先特性綠再新增安全紅綠。
- 安全紅測34案中30失敗／4通過；另2個公開overload缺口紅測。未以編譯失敗、略過測試或放寬斷言冒充紅綠。
- 保留既有明確參數檢查在try前。Claude分析建議「catch ArgumentException一律重拋」不採：
  原host沒有檢查的新邊界原本即由SDK失敗包裝，SDK自身也可能拋ArgumentException。
  新測試對六操作各注入這種真SDK例外，確保仍包裝同一例外；不是依型別猜故障來源。
- 安全差異明列：移除含名稱／ID／原訊息的Trace；外層固定英文操作訊息；
  Create／Update缺名稱、Assign空ID／名稱不一致在SDK前拒絕；
  讀取缺失／錯識別、整頁空／重複／跨表ID拒絕，FetchXML必須有唯一合法根實體且禁DTD。
  新adapter拒絕仍沿用操作失敗包裝，InnerException保留可診斷型別。
- Update繼續要求非空資料庫ID，即使Core CRUD支援純替代鍵也不放寬舊Repository契約。
- 搜尋目前消費鏈只有QueryService五個及MarketingListService一個RetrieveMultiple呼叫，
  沒有產品直接new EntityRepository或經Repository指派的caller。十二案執行這六個實際消費點；
  不將此結果宣稱全部業務分支、DI、授權或實機D365已通過。
- 三參數overload加在具體QueryService上，不改既有IEntityQueryService要求，避免破壞外部實作者。
  QueryExpression.PageInfo缺失才補5000／第一頁；已指定的PageInfo／cookie與Query原參照保留。
  補值會直接寫入caller的可變QueryExpression，XML文件已明載，呼叫中不得跨操作共用。
- 記錄入口不支援缺乏正式資料庫ID的聚合投影；不得生成暫存GUID讓聚合列混入持久化記錄集合。
  已另加合成count列測試，驗證拒絕且不改ID／候選；目前六個消費點的形狀均為一般記錄查詢。
- Repository只限host已授權SDK scope，CRUD／Owner借用同一SDK、無狀態marker不保留。
  Query adapter每次using，沒有新增既有caller必須Dispose的Repository生命週期；
  不建立static SDK、池、cache、背景工作、通知器或隱藏重試。
- 真正故障以真InnerException交host共用通知邊界；Core不建立LINE或記錄故障本文。
  呼叫端仍須先做伺服器授權，表名與ID驗證不代表跨租戶授權。
- 首次strict搬後建置遇4個CA1510，按既有雙目標模式：
  net10使用ThrowIfNull、Framework保留顯式ArgumentNullException；沒有NoWarn或降低分析器。
  舊host測試原有17個warning保留，新增測試的1個nullable warning已補NotNull並消除。
- 首次I18nCheck抓到搬入Core的13個舊中文診斷常值，未更新或放寬掃描基準；
  改固定英文診斷後Core為0、I18nCheck verify通過。例外型別、驗證位置／ParamName及InnerException不變。
- 第一輪193656 reviewer為完整雙模型、無quota降級、無Critical／High。
  Claude兩個Warning分別為聚合投影與PageInfo原地改寫的文件清晰度，已補上述XML／報告及聚合案例。
  Gemini的marker／介面建議、Claude的pragma／配置成本為非阻斷Info，維持刻意相容與局部所有權設計。
  當時快照仍是中文診斷；最終英文化與補證將另外經雙模型複審，不冒稱舊review覆蓋新來源。

## 交付界線

本輪沒有操作VM／D365／LINE，沒有push或正式部署，沒有修改正式計畫，因此不需Word修正報告。
保留使用者原有付款測試及appsettings修改，僅比對雜湊、不輸出設定內容。
目前Goal工具仍usageLimited，未覆蓋，也不宣稱在背景持續執行。


## 最終驗證、審查與提交界線

- 最新完整方案 `artifacts/p1-04-repository-final-section8/`：精確16組件／目標集合、
  TRX counters與結果列一致，2167/2167，0失敗／略過；ToolUtility363/363、457/457，
  ChurchReport623/623、架構／MSBuild67/67。沒有採用過期DLL；新增聚合案後再次完整建置測試。
- Core strict雙目標0警告／0錯誤，獨立coverage每目標恰一份99.42%／100%。
  全方案重編有17個原有host測試warning，沒有NoWarn；不採Gemini「所有測試零警告／完整覆蓋率」的泛稱。
- Debug tracer7/7加正TRX；I18nCheck verify及Core CJK=0；工具測試108/108、27/27；
  package34專案／158references／0failures；duplicates28專案／933檔／0group／new／stale；
  Plugin metadata10/10、一次性測試金鑰build6/6、analyzer26/26、CI transition6/6。
  誤以Pester執行獨立CI腳本只發現0案，守門明確拒絕，改執行原腳本才取得6/6；失敗日誌保留。
- 進度頁8/8；新無登入Edge在1440／390／320像素、明暗色及鍵盤操作通過，
  documentWidth精確等於viewport、頁面錯誤0。HTTP外連禁止，context及browser均finally關閉。
- 最終 `20261105-195337-integration-p1-04-repository-final-reviewer`：
  dualModelComplete=true、quotaBlocked=false、degradedFallback=false。
  兩模型明確關閉W1／W2，無新增Critical／High／Warning。Gemini仍將舊項列在Warning標題下，
  但結論明確CLOSED；不把舊項標題說成未修缺陷或隱藏此格式差異。
  Claude兩Info為委派防禦文件歸屬及nullable pragma風格，無需改產品行為。
- 原始來源SHA、驗證摘要、瀏覽器結果及三輪CCG原始ZIP／逐檔SHA保存在
  `.ccg/tasks/integration-p1/evidence/repository/`，ZIP解壓逐份核對原bytes相同。
- 執行期間觀察到非本執行建立的本地提交 `b64d3224`、`93377c7f`；
  前者保存搬移，後者納入本輪程式及兩份原有付款測試／appsettings修改。
  不撤回或重寫外部提交，也不將兩份原有修改宣稱為本任務實作；它們的原始雜湊仍完全相同。
  本執行後續提交僅收可分離的收尾文件／證據。主P1任務不封存，其他候選仍須繼續。

- 提交前Changed encoding：747 checked／678 timestamp raw excluded／0 issues；來源及文件whitespace通過，完整diff唯一診斷為刻意保留raw health JSON檔尾空行，其原bytes另有ZIP／SHA可還原。

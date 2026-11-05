# P1-04 屬性工具與 SDK 相容入口抽取

日期：2026-11-05。本報告只涵蓋此切片，**不代表 P1-04 或 P1 完成**。

## 正式需求對照

| 需求 | 實作 | 驗證與證據 |
|---|---|---|
| 純 SDK 屬性操作雙目標、維持 namespace | `ToolUtility.Core/AttributeOperations/EntityAttributeUtilityService.cs`、`Interfaces/IEntityAttributeUtilityService.cs` | 先在原 host 的 12 個特性案例通過，再修安全故障、搬移；後補 null 名稱及完整 Query 分頁斷言 |
| 純 SDK extension 可由 Plugin／網站共用 | `ToolUtility.Core/Extensions/OrganizationServiceExtensions.cs` | 原簽章不變，委派現有 `EntityQueryService`，不複製 Fetch parser／穩定 ID 驗證 |
| SDK 回應完整性、整頁識別與原始故障 | `ToolUtility.Core/EntityOperations/EntityQueryService.cs` | null／錯型回應明確拒絕；既有 QueryByAttribute／QueryExpression／FetchXML 共用根表及整頁驗證 |
| 不保留無用 logger／Entity、不跨操作共享 | 屬性服務無欄位、extension 僅操作局部查詢器 | 弱引用回收、1,000 次 A/B 並行、SDK 活動數排空、借入 Dispose=0 |
| Framework 實際載入核定 net462 產品 | `ToolUtility.Tests/CoreTargetFrameworkTests.cs` | 反射三個新抽取型別及 Constants 均屬 Core；net48 只引用 Core，新增測試在既有 `DataOperations/*.cs` 白名單內 |

## 行為與相容性裁決

- 三個屬性方法簽章、原 Entity 就地更新、普通／一層 `AliasedValue` 轉文字、真正缺值回空字串保持不變。`GetAttributeValue` 的 nullable 欄位名稱註記描述既有 null 行為，未改 CLR 簽章。
- `RemoveAttribute` 是移除屬性；`SetEntityAttributeToNull` 即使原本缺欄也保存明確 null。這兩種 SDK 更新語義不能互換。
- 保留舊 `InvalidOperationException` 外層型別與真實 `InnerException`。只把原本回顯欄位、實體及 `Exception.Message` 的外層訊息改為固定文字；host 不得序列化／記錄原 Message 或 ToString。
- 保留舊建構式對 null logger 的拒絕，但不保存 logger；新增無參數建構式讓純資料工具無須配置主機診斷依賴。
- SDK extension 的 null SDK／request／Query 以及異常 response 以前可被當成空資料，現在明確失敗。限定目前 repo 的 C# 搜尋沒有找到依賴該 extension 的產品呼叫點；同名測試原來呼叫的是 `EntityQueryService` instance method，不能混為同一 API。repo 外部消費者未知，故仍明載此安全差異。
- extension 每次建立一個小型局部 `EntityQueryService` 並確定性 Dispose，以重用完整公開查詢入口；Dispose 只清借入 SDK 參照，不 Dispose 原連線。沒有新增 parser、cache、連線、重試或通知器，也沒有為內部 helper 開放 `InternalsVisibleTo`。
- 實體與 Query 只屬當次已授權操作；同一 Entity 的並行寫入仍須 caller owner 協調。通用工具不把 Entity／Query 名稱當授權，也不推測租戶條件。

## 分析發現與核實

CCG 自癒入口 run `20261105-181935-integration-p1-04-attribute-extension-analysis-analyzer`：
`dualModelComplete=true`、`quotaBlocked=false`、`degradedFallback=false`。

- 採納兩模型的未使用 logger、固定安全外層訊息、缺參數不可偽裝空結果及借入所有權建議。
- Claude 的 Critical「保留舊 wrapper 必定違反 Core」不成立：沒有跨產品契約禁止這種相容包裝，既有 `EntityCrudService`／Owner 也保留 logger null 拒絕，不能從另一類型的 optional 建構式推導所有 API 必須改行為。已用精確型別／InnerException 測試及產品 remarks 明載理由。
- 「ToString 不會失敗」屬錯誤前提；測試以真實欄位物件注入 `FormatException`，證明原例外必須保存。
- 分析提示的 `MutationContractTests.cs` 路徑不存在，正確範本為 `MutationOperationsCharacterizationTests.cs`／`MutationOperationsSafetyTests.cs`，主會話已讀實檔。
- net48 測試遺漏提醒已核實：新測試刻意放進既有 `DataOperations/*.cs`，無須擴大或修改白名單；完整 net48 執行數與 target 反射提供實際證據。
- 不從 host 呼叫 internal `SdkDataQuery`，而委派 Core 公開查詢器後搬移 extension；因此沒有跨 assembly 可見性缺口。

## 紅綠與輸出新鮮度

- 原 host 成功特性：`artifacts/p1-04-attribute-extension-before/before.trx`，12/12。
- 安全故障重現：`artifacts/p1-04-attribute-extension-red/red.trx`，31 案中 28 失敗／3 通過，沒有編譯錯誤或略過。失敗包含 logger 保留、外層訊息回顯、缺失輸入／回應及資料列拒絕缺口。
- 修正後搬前，連同既有 EntityQuery 契約：`artifacts/p1-04-attribute-extension-safe-before-move/`，81/81；通過後才執行三檔 `git mv`。
- 起始 `--no-build` 偶然載到舊 244／349 案輸出，**不採為現況驗收**。重新建置後 270／350、Constants 後 coverage 99.33%／100%；記錄保留於 `artifacts/p1-04-resume-columns-*`。
- 抽取後補測的最終雙宿主／覆蓋率在 `artifacts/p1-04-attribute-extension-final-{dual,coverage}/`；正式數字由 TRX counters 及恰好一份每目標 coverage 原檔核對，不挑選較佳結果。

## 最終驗證與審查處置

- 最終 ToolUtility 雙宿主 314/314、395/395，無失敗／略過；Core 獨立覆蓋率
  99.36%／100%，兩目標各恰一份原始報告，Release strict 雙目標 0 警告／0 錯誤。
- 最新完整方案（已含全部 11 個 caller 補證）16 TRX、2056/2056，無失敗／略過；
  ChurchReport 623/623、架構／MSBuild 67/67。原始執行與機器摘要位於
  `artifacts/p1-04-attribute-extension-closeout-section8/`。
- 00 §8 Release build、完整 test、Debug tracer 7/7 與正 TRX 守門、重複掃描
  28 專案／930 檔、0 group／new／stale、I18nCheck verify 皆通過。
  最後 incremental build 0 警告／0 錯誤；前次重編舊 host 測試的 17 個既有 warning
  另完整保留，不因此宣稱整個舊專案已 strict-clean，也未加 NoWarn 或放寬檢查。
- Plugin 離線 metadata 樣本 10/10、一次性測試金鑰 build 樣本 6/6；
  PluginPackaging.Tests 27/27、I18nCheck.Tests 108/108、中央套件守門
  34 專案／158 references／0 failures。未使用正式金鑰、未部署。
- 第一輪 reviewer `20261105-184322` 完整雙模型、無 Critical／High。
  Delete 精確例外補證已關閉；AssignOwner caller 除文字稽核外仍需可執行證據。
- 已新增 `ChurchReport.MemberInfo.Tests/Integration/ContactOwnerRouteTests.cs`，
  真正執行已編譯 ContactService 方法及 ToolUtilityClass→Facade→Core 鏈，
  只替代 SDK I/O，注入錯表及真正逾時例外，驗證不重試與借入 SDK Dispose=0。
  為避免無關 LINE 建構式讀憑證，只初始化該方法唯一使用的 dependency，
  **不證明建構式、DI、授權或實機 D365**，也不宣稱執行全部 35 個業務分支。
- 第二輪 `20261105-185302` 正確抓到初次補證漏一種週報表名：實際 35 處共 8 種，
  不是先前誤記的 7 種。已補 `new_group_present_weekly_report`；現在 3 Fact＋8 Theory
  共 11/11。從 35 處盤點擷取的 8 名稱與測試 InlineData 集合比較完全相等，
  `artifacts/p1-04-owner-route-name-coverage.json`，沒有降低斷言或隱藏遺漏。
- 最終 `20261105-185904-integration-p1-04-attribute-extension-closeout-reviewer`：
  `dualModelComplete=true`、`quotaBlocked=false`、`degradedFallback=false`；
  Gemini／Claude 均無 Critical／High／Warning，前輪名稱缺件明確關閉。
  Claude 留一項非阻斷 Info：fixture Dispose 的所有權斷言遇到另一個同時失敗時可能
  遮蓋原測試失敗，尚未在本輪重現；保留此測試診斷限制，不影響產品故障傳遞契約。
- Gemini 對舊 logger 簽章與局部 query 配置的建議按刻意相容／操作所有權保留，
  沒有新增產品 stub caller，也沒有為無量測的 GC 推測建立跨 scope 池或共用 SDK。
  Nullable 的全域 targets 與反射均補證，既有 pragma 是合法冗餘，不改受審產品。
- 前次 reviewer 的 15 份程式／設定／golden 與現在 canonical 內容完全相同；
  三份先前提交的測試及四份 mutation golden 只有 UTF-8／CRLF 格式正規化。
  另六份舊 root CCG task prompt 補 CRLF；13 份修改前後 canonical 相同，
  timestamp raw 外模輸出不改 bytes。相關 manifest 及安全摘要歸入任務 evidence。

## 提交前文件與證據檢查

- 進度頁渲染測試 8/8；全新無登入資料 Edge 在 1440×1000、390×844、320×800
  明／暗色驗證 7 個未開始階段、53 個 todo 包、鍵盤切換與頁面錯誤 0。
  各尺寸 documentWidth 精確等於 viewport，沒有放寬溢出容許值。HTTP 外連被拒絕，
  每個 context／browser 都在 finally 關閉。首次 Node 未載入 bundled Playwright 的
  診斷已保留，改用現有 bundled runtime／當次 NODE_PATH 後通過，沒有安裝或改全域環境。
- 正式 Changed encoding（基準 73b6e6cf）727 份、raw 排除 648、0 問題；
  自有新增 C#／HTML／文件另做嚴格 UTF-8 無 BOM／CRLF／final CRLF byte check。
- 原始 CCG 40 份檔案完全保留；另外 ZIP＋SHA256 逐份解壓核對相同，避免 Git
  文字換行正規化使機器原始 bytes 無法恢復。來源／文件 whitespace check 無錯；
  完整 diff 的少數 whitespace 診斷只來自刻意保留的 timestamp raw health JSON
  檔尾空行與原始 prompt 中的空白行，不改寫 raw 證據或壓低檢查設定。

## 交付界線與下一步

此切片已取得實作、特性／安全紅綠、雙宿主、完整方案與適用雙模型審查證據。
主 P1 仍為 **2/12**；P1-04 還有 Repository、Activity／Attachment、其他屬性、
Contact／Collection／List／Query 與必要純契約候選需按逐型別準入繼續，不縮減範圍。
P1-01 尚待來源／審查收斂，P1-05～12 保持未完成，主任務不封存。
本輪不修改正式計畫，不需 Word 修正報告；不操作 Hyper-V／D365、不推播、不 push。
Goal 工具仍為 usageLimited，保留現有 Goal，不另建或覆蓋；本報告不表示背景工作持續執行。

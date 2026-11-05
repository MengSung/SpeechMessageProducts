# P1-04 Core 抽取切片驗收紀錄

日期：2026-11-05。**本文件不是整個 P1-04 或 P1 完成宣告。**
正式範圍及尚待處理型別見 `wp-reports/P1-04-inventory.md`。

## 需求、實作與證據

| 正式需求 | 實作位置 | 測試與實際證據 | 狀態 |
| --- | --- | --- | --- |
| 逐型別盤點 | `wp-reports/P1-04-inventory.md` | 62 原檔、76 宣告列；包括 partial 與巢狀型別；不是以 namespace 名稱猜可搬性 | 已盤點；搬移狀態持續更新 |
| 雙目標純 SDK Core | `ToolUtility.Core/ToolUtility.Core.csproj` | 產品 net462/net10.0、strict、XML 文件；SDK 各目標 PrivateAssets=all；`p1-04-operations-core-strict.log` 0 警告／0 錯誤 | 此切片通過 |
| Fee 舊查詢及三個新增方法 | `ToolUtility.Core/FeeOperations/` | 搬前特性、舊來源獨立切片、golden、特殊字元、故障、穩定 ID、同名資料及分頁測試 | 此切片通過，非全包完成 |
| RetrieveEntityNoCache | `ToolUtility.Core/EntityOperations/` | 搬前 3 特性；API 缺口紅、行為安全 12 紅／4 綠，修後搬前 21 綠；兩入口 FetchXML 混實體補強先重現失敗 | 此切片通過 |
| Appointment／Lessons／MeetingStatistics | Core 同名三資料夾及 `DataOperations/SdkDataQuery.cs` | 12 份原查詢 golden＋ByDate 型別化查詢搬前 13/13；安全 81 紅／18 綠；修後搬前 112/112；再 git mv 六檔 | 已搬移並取得雙目標證據 |
| product 參數化與舊多載 | `ToolUtility/DependencyInjection/ServiceCollectionExtensions.cs` | `ProductRegistrationTests` 8 案：真正子池鍵分隔、同配置冪等、衝突拒絕且集合不變、舊 ChurchReport、租約歸零及 Dispose owner | 此切片通過 |
| 全部 host 測試保留 | `ToolUtility.Tests/ToolUtility.Tests.csproj` | Framework 宿主只編純 Core 契約；net10 原 host 測試完整保留；反射確認 Core 組件及真實 net462 產品目標 | 此切片通過 |
| CI 不遺漏新模組 | `.github/workflows/solution-ci.yml`、`SpeechMessageProducts.CI.slnf` | CI 精確閉包先紅，再加入 Core 與 ToolUtility.Tests；仍固定原三項排除、完整差集、雙目標 coverage；`Test-CiTransition` 6/6 | 本機契約通過；沒有 hosted 證據 |
| 固定欄位模板隔離 | `ToolUtility.Core/Constants/CrmEntityColumns.cs` | 先以 net10 23/23 驗證 golden 與 caller mutation；net48 曾以 CS0234 證明型別仍留 host；抽取後 net48 23/23、完整雙宿主 270/270 與 350/350，Core strict Release 雙目標 0 警告／0 錯誤 | 此切片通過 |
| 其他可搬型別與必要純化 | 原盤點中剩餘候選 | 尚未取得完整特性、純化與抽取證據 | **未完成，不縮減範圍** |

## 舊來源與相容性

- 原三組查詢以固定基準 `47ab85f8` 核對，捕獲來源 SHA 見
  `artifacts/p1-04-query-source-goldens.json`。只使用合成名稱及 GUID。
- 保留欄位、順序、top、distinct、alias、狀態白名單及分頁旗標。
  `RetrieveByDate` 舊版實際不過濾日期，刻意不在搬移時改變；
  週報舊 `uitype` 提示值亦不憑推測修正。
- 明確安全差異：非空 GUID 驗證、反向日期拒絕、固定西曆日期、XML 安全編碼、
  完整頁面非空／唯一 ID／根實體驗證。不以空集合、去重或吞例外取得綠燈。
- FetchXML 通用入口在 SDK I/O 前辨識唯一根表；DTD 禁止、resolver=null，
  字元上限為原輸入長度，串流 reader 由單次操作確定性釋放，不重寫原 Query。
- `CrmEntityColumns` 保留 21 組欄位名稱及順序；模板留在 Core 私有靜態陣列，
  每次公開 getter 回傳操作自有複本。原先 net48 編譯錯誤證明該型別仍在 host；
  抽取後 23 案涵蓋完整 golden、caller mutation 與 1,000 次並行讀取。
- `DeleteEntity` 空字串維持舊 `ArgumentNullException` 與參數名；純空白名稱明確改為
  `ArgumentException` 並在 SDK 前拒絕。精確回歸案例已在 net10 通過 1/1；
  ChurchReport 搜尋未發現對 `ArgumentNullException` 的專一捕捉。
- ChurchReport 35 處有效 `AssignOwner` 呼叫均使用明確 logical name；逐一沿 Entity
  建立／同名 Retrieve 路徑核對，沒有發現傳入名稱與目標 Entity 不一致。

## 隔離、資源與通知

- 五組服務不保存任意 logger、查詢結果、HttpContext、Session 或 credential。
  靜態資料只含有限且唯讀的結構名稱；候選、XML 與 ID 集合只屬當次操作。
- SDK 由 host 已授權 scope 借入，host 擁有 timeout／drain／Dispose；
  Core 不 Dispose 借入連線，不建 cache、queue、timer、Task.Run 或隱藏 retry。
- EntityQuery Dispose 停止新操作並清借入參照；已取得參照的同步 I/O 可完成，
  host 必須先 drain 再關其 SDK owner。
- A/B 有界 1000／1200 次並行、caller mutation、故障後重試、active 歸零及借入
  SDK 未 Dispose 皆有測試。這是離線契約，不冒充真實 D365 整合。
- 原始 SDK Exception 直接交回 host 最終共用通知邊界；Core 不自行送 LINE、
  不記錄原始訊息或敏感識別值，也不把真正失敗偽裝為成功空資料。

## 機器驗證位置

- 搬前：`artifacts/p1-04-operations-before/`、`p1-04-operations-red/`、
  `p1-04-operations-safe-before-move/`。
- 搬後初輪：`artifacts/p1-04-operations-dual/`，net48 207/207、
  net10 282/282；獨立 coverage 98.78%／100%。
- 後續只增測試：ByDate 四種故障及三介面 nullability 中繼資料；
  `artifacts/p1-04-review-proof/` 8/8。最新最終全量結果以
  `artifacts/p1-04-final-solution-summary.json` 為準：16 TRX、1836/1836，
  Core 211/211、289/289；不能把前輪數字當最新。
- 所有 10 個 CI coverage 模組／目標：`artifacts/p1-04-ci-all-coverage/`，
  每份獨立且 ≥80%；Core 的最新補測後報告另在 `p1-04-final-core-coverage/`。
- Debug tracer 7/7、正 TRX 守門、Plugin metadata 10/10、
  一次性測試金鑰打包 6/6，均只離線、不讀正式金鑰、不部署。
- 編碼：基準 `73b6e6cf`，新文字逐位元組 UTF-8 無 BOM／CRLF／final CRLF；
  timestamp raw 外模證據依既有規範保留原 bytes。重複掃描未新增。
- 初次同收 TRX／coverage 產生 attachment 副本，嚴格一份報告守門確實拒絕；
  改用獨立新 coverage run，沒有刪除失敗證據或挑選較佳報告。
- 本輪抽取後最新 ToolUtility 測試：net48（實載 net462）270/270、
  net10 350/350；結果分別見 `artifacts/p1-04-current-dual-net48/` 與
  `artifacts/p1-04-current-dual-net10/`。Core Release strict build 兩目標
  0 警告／0 錯誤。抽取後新鮮覆蓋率與增量雙模型複審尚待完成。

## 審查與未結事項

審查詳細處置及機器摘要見 `.ccg/tasks/integration-p1/review.md`。
五輪切片／增量補證審查均為完整雙模型、無配額降級，沒有 Critical／High；
最後補證為 `20261105-135446`。非阻斷 Warning 逐項有處置與產品 SHA 比對證據，
不是忽略模型意見或把局部審查宣稱為整個 P1-04 完成。

其他候選 Operations、純契約及 Extensions 尚未全部抽取；
P1-05～12 尚未完成。本輪未操作 VM，也未修改正式實作計畫，不需要 Word 修正報告。

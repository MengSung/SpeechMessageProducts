# P1-04 Contact 純資料抽取與快取隔離（2026-11-06）

狀態：本切片實作、主會話審查、末端整合與守門已完成；不是整包P1-04完成。P1仍3/12。

## 需求 → 實作 → 決定性驗證
| 需求 | 實作 | 驗證/證據 |
|---|---|---|
| SDK純資料操作抽Core，host相依不硬搬 | Core/ContactOperations/ContactDataService；host ContactService薄adapter | 原host7/7特性，安全24全紅/空密碼2全紅，修後搬前43/43綠才搬Core |
| 公開API/舊文字/查詢不變 | 同namespace/19同步/3Task入口/兩host ctor；8個legacy文案resx＋generated表 | 三完整query golden、四種登入結果與資訊標籤；反射實載Core net462 |
| 零跨使用者可變cache污染 | 所有Contact入口不讀寫缺完整scope的舊cache，不保存cache/logger | 污染cache12案，含正/錯/變更/空密碼、取消、1000A/B；不將密碼加key |
| 正式ID與完整發布 | 同名異ID保留；無top集合5000列/1000頁/64Ki cookie上限，contactid穩定排序 | null/空/錯表/重複/超top/跨頁衝突、後頁故障/重試、cookie/page精確斷言 |
| 取消/生命週期 | 同步SDK前後檢查caller；內部取消faulted；Dispose只清借用SDK | 三Task入口五情境、外借SDK/ref/null、1000A/B排空active=0、Dispose競爭/weak reference |
| XML安全與舊OR語義 | 兩Fetch用XElement；保留top3/100、欄位、排序、空/提示值略過 | 六欄特殊字元精確roundtrip、條件數不變、SDK超量拒絕 |

## 設計與邊界
- 原ICrmCacheService仍在host，不抽到Core；新adapter接受但不使用/保存/Dispose它。重新啟用cache前需完整伺服器授權scope與不可變快照，不能只補一段caller提供的tenant文字。
- 停用不安全cache會使LINE/姓名每次重新查SDK，是刻意安全成本；未製造昂貴runtime或全域鎖。1000次A/B離線壓力只證明隔離/drain，不冒稱實機效能。
- 相同SDK scope服務不能跨租戶singleton；host擁有授權、timeout、transport併發及drain。輸出Entity和資訊字串含業務個資，只屬原caller，不應直接紀錄/通知/共享。
- 無top集合現在完整有界讀取，後頁故障/超限明確拒絕；top查詢仍是原語義的受限頁，保留SDK分頁metadata，不冒充整個全表。
- 先在原host位置修補，僅暫時Compile link同一SdkDataQuery來源供internal helper使用；沒有建立副本，搬Core前移除link，逐字核對ToolUtility.csproj回HEAD。
- I18n守門實際拒絕非分層資源key；改Contact.Legacy.*並用正式工具產生.g.cs，改用私有唯讀中立編譯表，不放寬規則或手寫假generated。
- 正式3.3計畫未修改，沒有Word需求修正報告。未操作VM/D365/LINE，沒有新增實機或hosted CI證據。

## 審查
使用者免除雙模型；主會話逐方法核對查詢/API、候選發布、取消分類、owner/drain、cache不留存與固定資源表。無已知本切片Critical/High。主P1/P1-04不封存；List/MarketingList、其餘Query與CrmAsync仍未完成。

## 原始證據
artifacts/p1-04-contact-20261106/：characterization-before、safety-red、missing-secret-red、safe-before-move、net48/net10、strict-first/strict、coverage、section8及final-section8；最終值以末端摘要為準。

## 最終實際驗證
- ToolUtility net48 688/688（實載Core net462）、net10 818/818；單獨補強既有測試null斷言後，重新build及net10全818案通過，正TRX守門確認非零測試。
- 全方案Release16TRX 2853/2853，0失敗/略過；ChurchReport623/623、架構67/67；完整產品重建22個warning包含既有測試及一個新nullable提示（已加更強Assert.NotNull解決），最後增量17個既有測試warning，沒有新增NoWarn。Core strict雙目標0警告/錯誤。
- Core每目標各唯一一份最終coverage：net48實載net462 92.77%、net10 93.42%，正式80%守門通過；Contact本體兩目標100%。
- Debug tracer7/7＋TRX守門、duplicates954來源0新重複、I18n verify/generated3檔及Core CJK=0、進度頁11/11。編碼與本地保存詳evidence/contact。
- 原始紅/綠、第一次I18n資源key拒絕及修正後通過均保留；不降低資源規則、測試斷言或覆蓋率門檻。

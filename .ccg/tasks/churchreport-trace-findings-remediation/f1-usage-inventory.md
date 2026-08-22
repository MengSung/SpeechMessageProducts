# F1.1 `Members` 使用點盤點

## 範圍與方法

- 掃描時間：2026-08-22 11:23:04 +08:00。
- 指令：`rg -n -F --hidden --glob '!**/bin/**' --glob '!**/obj/**' --glob '!**/.git/**' <literal> .`。
- 三個精確字面量：`m_SmallGroupData.Members`、`m_NewPersonFollowUpData.Members`、`m_AllMemeberData.Members`。
- 本文件本身已由掃描排除，以免盤點結果因寫入報告而自我增加。`.ccg/dual-model-runs/` 由平行分析工作產生，因而其命中數會隨產物新增而變動；下列清單以掃描時間點為準。
- 為避免 `?.Members` 逃過精確字串搜尋，另以 `m_(?:SmallGroupData|NewPersonFollowUpData|AllMemeberData)\?\.Members` 進行補充掃描；其結果不併入「精確字面量」總數。

## 總數

| 精確字面量 | 全 repo | C# | Markdown |
| --- | ---: | ---: | ---: |
| `m_SmallGroupData.Members` | 45 | 7 | 38 |
| `m_NewPersonFollowUpData.Members` | 32 | 5 | 27 |
| `m_AllMemeberData.Members` | 63 | 39 | 24 |
| **合計** | **140** | **51** | **89** |

C# 的 51 個命中包含 34 個產品位置（33 個可執行、1 個註解掉的舊碼）與 17 個新增的快照隔離測試位置。Markdown 的 89 個命中全部是歷史文件、任務文件或 CCG 執行產物，沒有執行期行為。

## 產品 C#：精確字面量的分類

| 類別 | 檔案與行號 | 集合 | 行為 | lock / snapshot 判定 |
| --- | --- | --- | --- | --- |
| 讀取 | `Controllers/SmallGroupController/SmallGroupController.DataApi.cs:124` | SmallGroup | 將集合交給 `DataSourceLoader.Load`。 | **取得快照**；不得只在取參考時短暫持鎖，因為 loader 會在鎖外列舉。 |
| 讀取 | `Controllers/NewPersonController.cs:119` | NewPersonFollowUp | 將集合交給 `DataSourceLoader.Load`。 | **取得快照**，理由同上。 |
| 讀取 | `Controllers/PersonalController.cs:179` | AllMember | 將集合交給 `DataSourceLoader.Load`。 | **取得快照**。 |
| 讀取 + 深層改寫 | `Controllers/PersonalController.cs:408`（後續 412--446） | AllMember | 對集合作 LINQ、`foreach`，並透過 `ApplyMaintainContactFields` 改寫既有 `Member`。 | **深拷貝快照或以 `SmallGroupDataList.SyncRoot` 包住完整讀改寫臨界區**；只有複製 `List` 容器不足以隔離 Member 欄位。 |
| 讀取／列舉 | `Models/ListManager.cs:582` | AllMember | `foreach` 建立地圖標記。 | **取得快照**後再列舉。 |
| 讀取 | `Models/ListSmallGroupWeeklyReport.cs:158, 161, 164-171` | AllMember | 檢查及讀取第 0 個 Member 填入個人回報 ViewModel。 | 在**同一** lock 內完成 null／Count／index 檢查與讀取，或讀取隔離快照；現行 `Members[0]` 先於空值檢查。 |
| 直接改寫 Member 欄位 | `Models/ListSmallGroupWeeklyReport.cs:378, 380-385` | AllMember | 檢查後直接改寫第 0 個 Member 的出席與關懷欄位。 | **必須 lock**；若與長工作並行，將完整 Member 深拷貝後原子發布。 |
| 讀取 | `Models/SmallGroupDataList.cs:174` | SmallGroup | 讀取 `Count` 作為新 Member 的 Id。 | 與下方 Add 放在**同一 lock**，避免重複 Id／集合競態。 |
| 直接 Add | `Models/SmallGroupDataList.cs:167, 206, 212, 215` | AllMember / SmallGroup / NewPersonFollowUp | `AddNewPersonToMember` 對三個共享清單加入 Member。 | **必須 lock**；此方法也是背景 `Task.Factory.StartNew` 的間接目標（見背景章節）。 |
| 已註解，非執行期 | `Models/SmallGroupDataList.cs:164` | SmallGroup | 註解掉的 `Add`。 | 不需修改；不得將它計入可執行使用點。 |
| 讀取／列舉 | `WebServiceConnector/DownloadIntegrateData.Setup.cs:273, 315` | AllMember | 先取得集合、再兩處 `foreach`，用以分類與建立 HappyGroup。 | 若傳入的 report 已對前景可見，**snapshot 或 lock**；若該 report 尚未發布且僅由本建構流程擁有，無需額外同步。 |
| 直接 Add | `WebServiceConnector/DownloadIntegrateData.Members.cs:301, 351, 470, 654` | AllMember | 載入資料時對集合加入 Member。 | 同上：未發布建構物件可免鎖；共享快取圖則**必須 lock**。 |

## 產品 C#：`?.Members` 補充命中（精確搜尋不會找到）

| 類別 | 檔案與行號 | 集合 | 行為與處置 |
| --- | --- | --- | --- |
| 讀取／投影 | `Controllers/EquipmentController.cs:231, 251, 335` | AllMember | `Select`／`FirstOrDefault` 前取得 Session 快取集合；**先取快照**，否則重載或背景發布可與列舉交錯。 |
| 空值檢查／診斷 | `Controllers/PersonalController.cs:406, 455` | AllMember | 本身不列舉，但 406 守護 408 的讀改寫流程；同步策略應涵蓋 406--446，不可只包住 408。 |
| 直接原地改寫 | `WebServiceConnector/DownloadIntegrateData.Setup.cs:135-137` | AllMember / SmallGroup / NewPersonFollowUp | `List.Sort`。若 report 已共享，**必須 lock 或在快照上執行**。 |
| 直接原地改寫 | `WebServiceConnector/DownloadIntegrateData.Setup.cs:141-143` | AllMember / SmallGroup / NewPersonFollowUp | `RemoveNumericAndBlank` 會變更列表；處置同上。 |

補充搜尋共有 15 個命中，其中 11 個是產品 C#、4 個是歷史 Markdown。它使可執行的產品集合存取表達式從 34 個精確命中擴展為 **45** 個（含 1 個註解掉的舊碼）。

## 背景工作與非字面量但必須納入 F1 的路徑

這些呼叫不一定含有三個「完整」字面量，卻會取得其容器、把集合傳入背景工作，或透過 `SmallGroupData.Members` 泛型 API 改寫同一資料；不能因字串搜尋而被遺漏。

| 風險 | 檔案與行號 | 說明與要求 |
| --- | --- | --- |
| **F1 主競態** | `Controllers/SmallGroupController/SmallGroupController.Save.cs:73-74, 88, 142-158, 286-318` | `Task.Run` 捕獲 `weeklyReportRef`／`allMemberData`，並以局部變數呼叫 `RemoveTransferredMembers`，其 `RemoveAt` 在 312 行原地改寫 SmallGroup 與 NewPersonFollowUp 清單。**背景工作只能使用深拷貝的背景專屬圖；不得發布回原圖。** |
| 背景新增 | `Controllers/NewPersonController.cs:547-550` → `Models/SmallGroupDataList.cs:167, 206, 212, 215` | `Task.Factory.StartNew` 呼叫 `AddNewPersonToMember`，會改寫三個集合。若保留背景執行，工作應擁有快照並以受控方式發布，或改為前景短鎖寫入。 |
| 背景上傳持有共享容器 | `Controllers/PersonalController.cs:765-774, 824-833` | 兩個 `Task.Factory.StartNew` 將 `m_AllMemeberData` 傳入上傳，沒有複製；需確認 Upload 不改集合，否則改傳快照。即使 Upload 只讀，也應避免持有 Session 快取圖超過 request。 |
| 平行更新 | `Controllers/SmallGroupController/SmallGroupController.Crud.cs:79-85` | 兩個 `Task.Run` 同時呼叫 `UpdateMember`（SmallGroup、AllMember），均經 `SmallGroupData.Members` 泛型 API 改寫 Member。應由共同 `SmallGroupDataList.SyncRoot` 序列化，或以獨立副本替代平行共用寫入。 |
| 包裝器再開背景執行緒 | `WebServiceConnector/UploadIntegrateData.AsyncWrapper.cs:51-73` | `Task.Run` 將呼叫者傳入的 `SmallGroupData` 交給同步 Upload；若呼叫端傳 Session 快取物件，工作仍共享集合。F1 的背景複本必須一路傳到這裡。 |
| 上傳期間移除 | `WebServiceConnector/UploadIntegrateData.PresentRecord.cs:486-490` | 對傳入的 `aSmallGroupData.Members.RemoveAt(i)`。此處未含三個完整字面量，卻是背景快照必須深拷貝的直接改寫。 |
| 通用資料 API | `Models/SmallGroupData.cs:45-50, 52-116, 118-131` | `InsertMember`、`UpdateMember`、`DeleteMember` 都直接讀／改 `Members`。所有經 `m_SmallGroupData`、`m_NewPersonFollowUpData`、`m_AllMemeberData` 呼叫這些方法的來源都要使用同一同步所有權。 |

## 測試位置（精確字面量）

`ChurchReport.MemberInfo.Tests/Models/SmallGroupDataListSnapshotIsolationTests.cs` 包含 17 個精確命中：

- SmallGroup：29、85、106。
- NewPersonFollowUp：30、86、107。
- AllMember：31、45、51、52、74、76-79、87、108。

這些是獨立的 fixture／斷言與背景壓力測試資料，不是 Session 快取的產品存取點。它們是 F1 快照所有權與深拷貝的必要驗證，不應加入產品 lock。

## 非執行期 Markdown 位置（精確字面量）

以下清單完整涵蓋 89 個精確 Markdown 命中；均分類為「文件／分析產物」，不需要 lock 或 snapshot。

### `m_SmallGroupData.Members`（38 個 Markdown 命中）

- `.ccg/dual-model-runs/20260822-100708-churchreport-trace-remediation-analysis-retry-analyzer/gemini-analyzer-attempt-1.stdout.md:44, 48, 50`
- `.ccg/dual-model-runs/20260822-100708-churchreport-trace-remediation-analysis-retry-analyzer/gemini-analyzer-attempt-2.stdout.md:34`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/claude-analyzer-attempt-1.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/claude-analyzer-attempt-2.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-1.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-1.stdout.md:79, 148, 172`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-2.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-2.stdout.md:33, 50, 52, 112, 124`
- `.ccg/dual-model-runs/churchreport-trace-remediation-f1-analysis-analyzer.md:13`
- `.ccg/dual-model-runs/churchreport-trace-remediation-f1-analysis-input.md:7`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/codex-prompt.md:197`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/design.md:28, 73`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/implement.md:113`
- `SpeechMessageProducts.ChurchReport/文件/修正官網奉獻網頁/HomeController-南崁長老教會.md:904, 906, 908, 910, 912, 917, 919, 1130, 1132, 1136, 1138, 1140, 1142, 1636, 1638, 2528`

### `m_NewPersonFollowUpData.Members`（27 個 Markdown 命中）

- `.ccg/dual-model-runs/20260822-100708-churchreport-trace-remediation-analysis-retry-analyzer/gemini-analyzer-attempt-1.stdout.md:48`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/claude-analyzer-attempt-1.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/claude-analyzer-attempt-2.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-1.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-1.stdout.md:87`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-2.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-2.stdout.md:50, 58`
- `.ccg/dual-model-runs/churchreport-trace-remediation-f1-analysis-analyzer.md:13`
- `.ccg/dual-model-runs/churchreport-trace-remediation-f1-analysis-input.md:7`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/codex-prompt.md:197`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/design.md:29, 73`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/implement.md:114`
- `SpeechMessageProducts.ChurchReport/文件/修正官網奉獻網頁/HomeController-南崁長老教會.md:1151, 1155, 1157, 1159, 1161, 1318, 1727, 1729, 1731, 1733, 1735, 1740, 1742`

### `m_AllMemeberData.Members`（24 個 Markdown 命中）

- `.ccg/dual-model-runs/20260822-100708-churchreport-trace-remediation-analysis-retry-analyzer/gemini-analyzer-attempt-1.stdout.md:48`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/claude-analyzer-attempt-1.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/claude-analyzer-attempt-2.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-1.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-1.stdout.md:96`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-2.prompt.md:15`
- `.ccg/dual-model-runs/20260822-111346-churchreport-trace-remediation-f1-analysis-analyzer/gemini-analyzer-attempt-2.stdout.md:50, 63`
- `.ccg/dual-model-runs/churchreport-trace-remediation-f1-analysis-analyzer.md:13`
- `.ccg/dual-model-runs/churchreport-trace-remediation-f1-analysis-input.md:7`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/codex-prompt.md:198`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/design.md:30, 74`
- `.trellis/tasks/08-22-churchreport-trace-findings-remediation/implement.md:115`
- `SpeechMessageProducts.ChurchReport/文件/修正官網奉獻網頁/HomeController-南崁長老教會.md:1316, 1454, 1944`
- `SpeechMessageProducts.ChurchReport/文件/歷程記錄/林寬仁錯誤出現在瑀倢小組診斷報告.md:31`
- `SpeechMessageProducts.ChurchReport/文件/歷程記錄/Member-ContactId添加指南.md:94, 108, 154, 178, 246, 394`

## 結論：F1 的安全邊界

1. 產品的精確 C# 呼叫點已有 34 個，加上 `?.Members` 變體為 45 個；廣泛為每個讀端補 lock 容易遺漏，也不能安全地跨 `DataSourceLoader`、CRM I/O 或背景上傳持鎖。
2. 因此應採設計文件的**唯讀退路**：在 request 執行緒以短 lock 製作三組 `List<Member>` 與每個 `Member` 的深拷貝；背景上傳及清理僅操作這個獨佔快照，且**不回寫** Session／IMemoryCache 共用物件圖。
3. 仍需讓所有前景的直接結構或欄位改寫（本盤點列出的 Add、Sort、Remove、Update、Delete）遵守 `SmallGroupDataList` 的同一 `SyncRoot`，至少與「建立快照」這個短臨界區互斥。對 UI／資料載入端，應傳遞快照而不是持鎖後的原始 `List` 參考。
4. F1 驗收測試至少涵蓋：背景快照清理同時，前景 `DataSourceLoader`／`foreach` 不擲 `InvalidOperationException`；前景三集合與其 Member 欄位均保持不變；清理後不發生背景回寫。

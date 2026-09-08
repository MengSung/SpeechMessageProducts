# 分析報告：小組回報重複姓名問題 — 現行分支架構與擬採設計審查

## 0. 分析範圍與已核實的程式事實

已實際讀取以下檔案並以行號為證據基礎（非推測）：
`Models/ListManager.cs`、`WebServiceConnector/DownloadIntegrateData.{Core,Setup,Members}.cs`、`Models/SmallGroupDataList.cs`、`Models/InMemoryDataContextSmallGroup.cs`、`Extensions/ListManagerCacheExtensions.cs`、`Controllers/BaseChurchController.cs`、`Controllers/SmallGroupController/*.cs`（`LineLogin`、`IntegrateView`、`DataApi`、`Date`）、`ChurchReport.MemberInfo.Tests/Models/ListManagerIntegratePublicationTests.cs`、`ToolUtility/Factory/ToolUtilityFactory.cs`，以及 `文件/網頁上會出現重複的會友名字/duplicate-names-implementation-handbook-2026-09-08.md`（另一分支已完成的參考實作）。

**重要背景**：`ListManager.cs` 目前**尚未**實作 `EnsureAndGetIntegrateDetachedRead`／`ListManager(loader)` 建構式，但 `ListManagerIntegratePublicationTests.cs` 已經以這組 API 撰寫測試（32 併發同 key、exact-duplicate PresentRecordId fail-closed、caller-mutate 隔離）。這代表**本分支目前處於「先寫失敗測試」的紅燈狀態，尚未實作**，符合 `requirements.md` 的 TDD 要求，但也代表以下所有 Critical 發現目前都是「真實存在、尚未修正」的生產路徑。

---

## Critical

### C1. `LoadFlag` 在資料尚未完整前就被設為 true，失敗後無法重試（違反「失敗後重試」驗收條件）
`WebServiceConnector/DownloadIntegrateData.Setup.cs:50` 的 `SetupHeaderData` 在**尚未**呼叫 `SetupShepherdData`／`SetupWeeklyReportData`／`SetupWeeklyReportChartData`（`DownloadIntegrateData.Core.cs:124-133`）之前，就把 `aListSmallGroupWeeklyReport.LoadFlag = true` 寫回**共享**的 `m_ListSmallGroupWeeklyReport`。若後續任一階段的 CRM 呼叫拋例外（逾時、暫時性連線問題），例外會被 `SmallGroupController` 的外層 `catch(Exception e)` 吞掉並轉成 `HandleError`，但 `LoadFlag` 已經是 `true`。此後 `ShouldLoadIntegrateData`/`EnsureIntegrateDataLoaded`（`SmallGroupController.IntegrateView.cs:97,120`）只檢查 `LoadFlag`，會判定「已載入」而**永遠不再重試**，把半成品（部分成員清單）當成正式資料端出去。
**這與擬採設計沒有衝突，但擬採設計必須明確要求：LoadFlag 只能在候選報表通過完整驗證（含 row key 檢查）後，隨整個 snapshot 一起原子發布；任何階段拋例外都不得讓共享物件殘留 `LoadFlag=true` 或半數成員。**

### C2. 成員清單以「逐筆 append 到共享物件」的方式組裝，構成可重現的重複列根因
`DownloadIntegrateData.Members.cs` 的 `GetAllMemeberDataList` 先把 `aListSmallGroupWeeklyReport.m_SmallGroupDataList.m_AllMemeberData` 重設為新物件（第 69 行），但隨後 `ProcessPresentRecordEntityWithCache`／`GetAllMemberDataFromListOptimized` 是**逐筆**呼叫 `AddMemberToAllMemberData(member)`（`SmallGroupDataList.cs:172-176`），每次只在極短的 `ExecuteSynchronized` 臨界區內 add 一筆。這代表一次完整載入是「數十次獨立加鎖操作」而非「一次原子發布」。若同一 `m_ListSmallGroupWeeklyReport`（同 session、同物件）被兩個並行呼叫觸碰（例如 C4 的 LINE 三個 `Task.Run`，或 C5 的 `UpdateIntegrateDate` 與另一請求交錯），兩條執行緒會交替把各自查到的 CRM 出席紀錄 append 進**同一個** `m_AllMemeberData.Members`，產生「同一位會友被兩條路徑各建立一次 `Member`（不同或甚至相同 `PresentRecordId`）都寫進同一張表」的結果——這正是使用者看到「重複姓名」最直接、可重現的機制，**不需要**任何姓名比對即可解釋。
**擬採設計第 2、3 點（candidate 報表在鎖內完成、驗證後才發布；讀取端拿 detached snapshot）正是對症下藥，但必須明確要求：`GetAllMemeberDataList` 整個成員組裝過程只能寫入 operation-local 的候選物件，不得在完成前就寫進會被其他請求讀到的共享欄位。**

### C3. 讀取 API 直接把可變共享集合交給 `DataSourceLoader`，未加鎖也未深拷貝
`Controllers/SmallGroupController/SmallGroupController.DataApi.cs:123-124`（`LoadIntegrate`）與 `:202-203`（`GetChartDataList`）都是：
```csharp
var tasks = InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport.m_SmallGroupDataList.m_SmallGroupData.Members;
return DataSourceLoader.Load(tasks, loadOptions);
```
`SmallGroupDataList` 的寫入路徑（`UpdateSmallGroupAndAllMember`、`AddNewPersonToMember`、`RebuildSmallGroupAndNewPersonDataFromAllMembers` 等）都有透過 `_syncRoot` 加鎖，但**這兩個讀取路徑完全沒有進入 `_syncRoot`**，是直接讀取即時參考後交給 DevExtreme 做排序/分頁/過濾。若同一使用者連續點擊（Grid 自動刷新與儲存交錯）導致寫入與讀取重疊，`List<T>` 在無鎖列舉時可能拋 `InvalidOperationException`，或（更隱蔽）在 `RebuildSmallGroupAndNewPersonDataFromAllMembers` 換參考的瞬間讀到「舊列表的部分頁 + 新列表的部分頁」这類前後不一致的分頁結果，肉眼呈現就是「同一人出現兩次、或缺一筆」。
**這證實擬採設計第 3 點（detached snapshot 才交給 DataSourceLoader）是 Critical 等級的必要條件，而非效能優化選項。**

### C4. LINE 登入的三個 `Task.Run` 對同一 `InMemoryContext`／`ListManager`／`SmallGroupDataList` 無序並行寫入
`SmallGroupController.LineLogin.cs:66-80`：`setupDataTask`（寫 `SmallGroupDataList.SetupContactIdString`）、`setupViewBagTask`（讀 `ListManager` 狀態組 ViewBag）、`ensureDataTask`（可能呼叫 `SetupIntegrateData`）三者用 `Task.WhenAll` 平行執行，彼此沒有任何順序保證或鎖保護，且都作用在**同一個** request-scoped `InMemoryContext` 底下的可變物件圖。這與 C2 的機制疊加，會放大重複列出現的機率。**擬採設計第 5 點（移除平行 Task.Run，改依賴順序執行）方向正確，必須落實。**

### C5. `SetupIntegrateData` 之外，至少還有兩個未被擬採設計涵蓋的 `ListManager` 寫入入口，會與新 gate 競爭
擬採設計只提到把 gate 包在「載入」（`SetupIntegrateData`／`EnsureAndGetIntegrateDetachedRead`）周圍，但實際程式碼中至少還有兩處**直接呼叫 `ListManager.SetupListManager(...)`**，且完全不在擬採設計的保護範圍內：

1. `Controllers/BaseChurchController.cs:788-798`（`EnsureCorrectUserData`）：發現 Session 密碼與 `ListManager.m_Password` 不一致時，直接呼叫 `InMemoryContext.ListManager.SetupListManager(sessionAccount, sessionPassword, ...)`，此方法會覆寫 `m_MultiGroupList`、`m_MultiGroupChartDataList`、`LoginType`、`ActiveListId` 等欄位（見 `ListManager.cs:62-79` 內部呼叫 `m_DownloadListManager.GetListManager(...)`）。**這是幾乎每個 API 進來都會執行的前置檢查**（`LoadIntegrate`、`GetChartDataList` 等都先呼叫它），代表新 gate 若只包 `SetupIntegrateData`，`EnsureCorrectUserData` 觸發的身分重建仍可能與 gate 內的候選載入同時改寫同一個 `ListManager` 實例的欄位。
2. `SmallGroupController.Date.cs:132-150`（`UpdateIntegrateDate`）：依序執行「清快取 → 直接寫 `m_SelectDate`（無鎖）→ 呼叫 `SetupListManager`（無鎖）→ 呼叫 `SetupIntegrateData`（若擬採設計只包這一步，前兩步仍是裸寫）」。這是一個**跨三個步驟的複合操作**，若中途被另一併行請求插入（例如使用者快速連點日期切換，或另一分頁的 `LoadIntegrate` 同時打進來），會讀到「新日期＋舊小組資料」或「新小組資料＋舊日期」的半套狀態，這正好對應參考手冊（另一分支已完成實作的踏勘文件）troubleshooting 表中「只有 date/小組切換時出現」那一列的成因。

**結論：擬採設計必須把 gate（或其後繼的 generation/scope 版本）的保護範圍擴大到涵蓋 `SetupListManager` 的所有呼叫點，並把 `UpdateIntegrateDate` 的三步驟合併為 gate 內的單一原子操作，而不是只包住最後一步 `SetupIntegrateData`。**

### C6. LINE 登入把 LINE User ID 誤當成 `ListEntityId` 傳入，會在 `SetupHeaderData` 對非 GUID 字串呼叫 `new Guid(...)`
`SmallGroupController.LineLogin.cs:75-76`：
```csharp
var ensureDataTask = Task.Run(() => EnsureIntegrateDataLoaded(lineUserId), cancellationToken);
```
`EnsureIntegrateDataLoaded(string id)`（`IntegrateView.cs:116-124`）在 `weeklyReport == null || !LoadFlag` 時會呼叫 `InMemoryContext.ListManager.SetupIntegrateData(id)`，而這裡的 `id` 是 **LINE User ID**（例如 `U4af...` 格式字串），不是小組的 `ListEntityId`。`SetupIntegrateData` 最終會走到 `DownloadIntegrateData.Setup.cs:51`：`this.m_ToolUtilityClass.RetrieveEntity("list", new Guid(ListEntityId))`，對非 GUID 字串建構 `Guid` 會直接拋 `FormatException`。這代表**目前程式碼在特定時序下（`ListManager` 快取未命中、`ActiveListId` 尚未由其他流程設定）幾乎必定在 LINE 首次登入時炸掉**，被外層 `catch(Exception e)` 吞成一般錯誤頁。這不是併發問題，是純粹的參數誤傳，且已被另一分支的參考實作明確列為修正項（「LINE 流程不再把 LINE User ID 當成小組 ListEntityId」）。**擬採設計文字沒有提到這個 bug，必須一併納入修正範圍，否則單獨修好併發/快照隔離，LINE 登入仍會壞。**

---

## Warning

### W1. Gate 的 scope key 目前只看得到 `ListEntityId`，未涵蓋 `requirements.md` 明訂的「使用者、組織、日期」
`requirements.md` 明確要求：「同一 Session、使用者、組織、小組**與日期**的並行請求只能發布一份完整快照」，但目前唯一透露出目標 API 形狀的測試（`ListManagerIntegratePublicationTests.cs:32,69`）只用 `EnsureAndGetIntegrateDetachedRead("list-a")`／`("list-b")` 兩個字串當 key，完全沒有測試日期切換場景。而 `ListManager` 的 `m_SelectDate` 是單一可變欄位（`ListManager.cs:27`），若 gate key 只用 `ListEntityId`，同一小組、不同日期的兩個候選載入會被誤判為「同一份快照」而互相覆蓋或漏訂閱最新日期。**建議 gate/generation key 明確組合 Account（或已驗證的 ContactId）＋ ListEntityId＋ WeeklyReportEntityId／週次日期，並在測試矩陣中補上「A 慢載入舊日期、B 切換到新日期、A 完成後不得覆蓋 B」的案例（對應參考手冊 C02）。**

### W2. Semaphore 的釋放路徑與 Session 快取淘汰（eviction）之間沒有明確協議
`InMemoryDataContextSmallGroup.cs` 的 `SessionDataCacheTracker.GetOrCreate`（第 270-316 行）已經確保同一 session key 併發 miss 只會建立一個 `ListManager` 實例，這點對擬採設計第 1 點是有利的既有基礎。但若在 `ListManager` 上新增 `SemaphoreSlim` 欄位：
- `ListManager` 目前不是 `IDisposable`，若不主動處理，`SemaphoreSlim` 會在 30 分鐘 TTL 到期或容量上限（`SessionDataCacheMaximumEntries=4096`）觸發淘汰時，隨物件被 GC 回收——只要不呼叫 `.AvailableWaitHandle`，這是可接受的，但**需要在設計文件明確聲明「不使用 AvailableWaitHandle，不需要顯式 Dispose」**，避免日後有人加上 `IDisposable` 卻在仍有等待者時呼叫 `Dispose()`，那會讓等待中的 `WaitAsync` 丟出 `ObjectDisposedException`。
- 更重要的邊界情況：若載入耗時超過 TTL 或觸發容量淘汰，session 快取會在下一次 `.ListManager` getter 呼叫時建立**全新** `ListManager`（新的、獨立的 Semaphore）。此時舊實例上仍在跑的候選載入完成後，會把結果寫進一個**已經不被 session 快取引用**的孤兒物件，使用者實際讀到的是新實例的空/舊資料，等於「白跑一次 CRM 查詢」。雖然不會造成跨使用者污染，但會造成前端顯示「明明剛存完/剛切換完，資料卻沒更新」的困惑。**建議：候選載入在寫回前，先確認自己持有的 `ListManager` 實例仍是 session 快取目前指向的實例（identity 比對），不一致就視為過期並丟棄結果，而不是原地覆寫。**

### W3. 同步 CRM I/O 在 Semaphore 臨界區內執行，且既有程式大量用 `Task.Run` 包裝同步呼叫，有 ThreadPool 飢餓風險
`DownloadIntegrateData` 所有 CRM 查詢（`RetrieveEntity`、`RetrieveMultiple` 等，見 `Members.cs`）都是同步阻塞呼叫。擬採設計把整個候選載入包在 gate 內是對的（先前 C2 已說明必要性），但這代表：
1. 若 gate 用 `SemaphoreSlim.WaitAsync()`＋同步 CRM I/O，持有鎖期間會長時間佔用一個 ThreadPool 執行緒；
2. 目前 `SmallGroupController.LineLogin.cs` 已經展示了用三個 `Task.Run` 包裝同步呼叫的模式（C4），若日後在其他呼叫點沿用相同模式為了「不要卡 request 執行緒」而繼續加 `Task.Run`，會在高併發（多個小組長同時點名）下疊加造成 ThreadPool 排隊延遲。
**建議明確評估：CRM 查詢是否可搬出鎖外（例如：鎖只保護「候選物件組裝＋row key 驗證＋發布」這段純記憶體操作，CRM 查詢結果先在鎖外準備好再進鎖），並在效能驗收中加入 ThreadPool queue length／CRM 連線池等待時間的量測基準，而不是只驗證正確性。**

### W4. 失敗路徑（非重複 key 的一般例外，如 CRM timeout）沒有對應測試
`ListManagerIntegratePublicationTests.cs:68-85` 只驗證了「候選報表本身含重複 `PresentRecordId`」這一種失敗；沒有測試「loader 拋出一般例外（模擬 CRM 逾時）時，gate 是否正確釋放、`LoadFlag` 是否維持未完成、下一次呼叫是否能重新觸發載入並成功」。這正是 C1 描述的缺口在測試層級的對應——**若不補這個案例，即使實作了 gate，仍可能重現 C1 的「失敗後卡死」問題而測試卻是綠燈**。

### W5. `EnsureAndGetIntegrateDetachedRead` 目前簽章只接受 `listId`，難以同時滿足「單一入口」與「日期/週報 ID 也是 scope 一部分」
延續 W1，若最終實作維持 `EnsureAndGetIntegrateDetachedRead(string listId)` 這種單參數簽章，呼叫端仍需自行先把 `m_SelectDate`／`WeeklyReportEntityId` 寫進 `ListManager` 實例欄位再呼叫，這兩步之間依然存在競態窗口（類似 C5 的 `UpdateIntegrateDate` 情境）。**建議 API 設計把完整 scope（日期／週報 ID）作為參數的一部分傳入並在方法內一次性验證，而不是依賴呼叫端先寫欄位再呼叫方法。**

---

## Info

### I1. CRM 連線身分隔離機制本身沒有問題，不需要因本次修正而改動
`ToolUtility/Factory/ToolUtilityFactory.cs:26-30,136-165` 顯示 `ToolUtilityClass` 單例只是一個不持有連線狀態的 facade，實際 `IOrganizationService` 透過 `AmbientGatewayOrganizationService` 依「目前 request 的 DI scope」逐次解析，並非固定身分的共用連線。擬採設計第 2 點「每次使用新的 `DownloadIntegrateData`」不需要額外處理 CRM 連線身分隔離問題，這部分既有架構已經是安全的。

### I2. Session 隔離的 holder/cache 機制已經足夠支撐擬採設計第 1 點，不需要新增 static keyed dictionary
`InMemoryDataContextSmallGroup.ListManager` 屬性（`InMemoryDataContextSmallGroup.cs:812-833`）已經透過 `TryGetSessionCacheKey` + `GetOrCreateSessionDataGraph` 確保同一 Session（含已綁定使用者、指紋、建立時間）併發首次存取只會建立一個 `ListManager` 實例，且無 Session 時退回 scope-local 後備物件不寫入程序級快取。擬採設計「在 `ListManager` instance 上掛一個 `SemaphoreSlim`」的前提成立，方向正確。

### I3. 未發現任何以 FullName／電話／單獨 ContactId 做去重合併的建議或既有邏輯
已檢視 `SmallGroupDataList.cs`、`DownloadIntegrateData.Members.cs` 中所有成員組裝與比對邏輯，目前完全沒有任何依姓名/電話合併資料的程式碼或建議，UI row key 全部以 `PresentRecordId` 為準（如 `Member.PresentRecordId`、`DataApi.cs` 中的分頁欄位）。**本次審查也不建議引入姓名 Distinct 或類似掩蓋手法**，這與任務要求一致。

---

## 對擬採設計五點的逐項覆核

| # | 擬採設計 | 覆核結論 |
|---|---|---|
| 1 | ListManager instance 自帶 SemaphoreSlim，沿用既有 Session holder，不新增 static dict | **方向正確**（見 I2），但保護範圍必須擴大到 `SetupListManager`（見 C5），否則同實例仍有未受保護的寫入路徑。 |
| 2 | Gate 內用全新 `DownloadIntegrateData` 與 candidate report，完成驗證後才發布 | **正確且必要**（見 C1、C2 的根因分析直接支持此設計）。需明確定義「驗證」包含 row key 唯一性檢查與所有子階段（header/shepherd/weekly/chart）全部成功。 |
| 3 | 讀取 API 用深複製 detached snapshot 再交給 DataSourceLoader | **Critical 等級必要**（見 C3 的具體無鎖讀取證據），不是效能可選項。 |
| 4 | Exact duplicate `PresentRecordId` fail closed；同名不同 key 全部保留 | 現有測試（`ListManagerIntegratePublicationTests.cs:68-85`）已驗證此契約在單一 key 內成立，且失敗只影響該 key、不污染其他已發布 key，設計正確。**但尚缺「非 duplicate 的一般例外也要 fail closed 且可重試」的對應測試**（見 W4）。 |
| 5 | LINE 登入移除同一 request state 的平行 Task.Run，依依賴順序執行 | **方向正確**（見 C4），但必須同時修正 C6 的「LINE User ID 誤當 ListEntityId」參數錯誤，否則移除平行化後 LINE 登入仍會因型別/格式錯誤而失敗。 |

## 測試與效能缺口彙總（回應「請特別檢查」的項目）

- **Session/cross-user leakage**：既有 holder 機制已足夠（I2），但需補「不同 session 併發登入交錯操作」測試（對應參考手冊 C05），目前本分支未見此類測試。
- **Semaphore/Task/取消生命週期**：需補 gate 等待者在 request 被取消（`CancellationToken` 觸發）時，是否只 detach 自己而不取消其他等待者/winner 的候選載入（對應 C03）；目前無對應測試。
- **同步 CRM I/O／deadlock**：需評估鎖範圍是否可縮小到純記憶體操作（W3）；需確認 gate 不會與 `SmallGroupDataList._syncRoot` 形成巢狀鎖順序不一致（兩者目前是不同物件的鎖，若未來某條路徑同時持有兩者，需固定取鎖順序避免 deadlock）。
- **Scope key 完整性**：目前唯一可見的 API 形狀只鍵值化 `ListEntityId`，缺日期/週報 ID（W1、W5）。
- **失敗後重試**：C1（生產路徑）與 W4（測試缺口）已具體指出，必須同時修正程式與補測試。
- **效能配置量**：尚未見任何併發壓測或 ThreadPool/CRM 連線池指標基準（參考手冊建議的 100 concurrent × 100 generations、P50/P95/P99），建議納入驗收前的效能關卡，而不是只跑功能測試。

---
SESSION_ID: ec76e1dd-b46d-4574-8650-5d5194d82301

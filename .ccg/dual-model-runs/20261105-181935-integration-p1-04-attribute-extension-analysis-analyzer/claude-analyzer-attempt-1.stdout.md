# CCG 分析結果：EntityAttributeUtilityService / OrganizationServiceExtensions 抽取

**聲明**：本次僅以 Read/Grep/ls 靜態檢視下列檔案與其在 repo 內的實際呼叫點，未執行建置、測試、D365/LINE 連線，亦未讀取設定或機密。

## 1. 需求或相容重大衝突

**Critical — 「保留 InvalidOperationException wrapper」與既有 Core 遷移慣例衝突**
`EntityAttributeUtilityService.cs` 三個方法目前 `catch (Exception ex)` 會連「自己在同一 try 區塊內丟出的 `ArgumentNullException`」都一併重包成 `InvalidOperationException`。實際追蹤程式路徑後確認：三個方法在正常情況下唯一會被攔截到的例外，就是自己拋出的 `ArgumentNullException`（targetEntity/entity/propertyName 為 null 時）；SDK 層（`Attributes.Contains/Remove/Add`、`AliasedValue.Value.ToString()`）在既有呼叫模式下不會丟例外。
但已遷移到 `ToolUtility.Core` 的同類服務（`EntityQueryService`、`OwnerManagementService`、`SdkDataQuery`）**全部放棄 catch-rewrap**，改為「前置驗證直接拋 `Argument*Exception`，SDK 原始例外原樣上傳」（`EntityQueryService.cs:154` 註解：「不攔截或重新包裝 SDK 真實故障」）。若這次抽取刻意保留 wrapper，會是本次 slice 唯一違反既有 Core 慣例的例外策略，且是「呼叫端可觀察」的差異：目前呼叫 null 輸入會看到 `InvalidOperationException`（InnerException 才是 `ArgumentNullException`），若改走 Core 慣例則直接丟 `ArgumentNullException`。兩者對 catch 型別的呼叫端行為不同，需要明確決策，不能同時宣稱「兩者相容」。

**Warning — 「保留原 ctor(object) 之 null 拒絕」與 Core 的 logger-less 慣例衝突**
`EntityQueryService(object? logger = null, …)` 明確示範的慣例是：logger 參數**只為來源相容保留，不檢查 null、不保存**（避免 Core 持有未使用的任意物件延長生命週期）。但 `EntityAttributeUtilityService` 目前的 `ctor(object logger)` 會 `?? throw new ArgumentNullException`，且 `_logger` 欄位目前完全未被任何方法讀取（已核實，純死欄位）。任務計畫要「保留原 ctor 之 null 拒絕」+「新增無 logger ctor 供沙箱」，等於同時維持兩套 ctor 行為語意（舊的嚴格拒絕 null、新的完全不理 logger），這本身不衝突，但**嚴格 ctor 的拒絕行為沒有任何已知生產呼叫端依賴**：`ToolUtilityFacade.cs:90` 的 `_logger = logger ?? new object();` 保證永遠不會對 `EntityAttributeUtilityService` 傳 null（facade 是目前唯一建構點，已核實 repo 內無其他 `new EntityAttributeUtilityService(` 呼叫）。換言之，保留嚴格 ctor 只是防禦性保留，不是修復任何已知使用需求。

**Info — `ExecuteRetrieveMultiple` 的「失敗當成功」風險，在生產程式碼中找不到對應呼叫端**
已針對全 repo（非僅三檔範圍，但限定在驗證此風險是否真實存在）搜尋 `OrganizationServiceExtensions`／`ToolUtilityNameSpace.Extensions`／`.ExecuteRetrieveMultiple(` 字面呼叫。結果：
- `OrganizationServiceExtensions` 類別名稱只出現在它自己的定義檔。
- 唯一 import 了 `ToolUtilityNameSpace.Extensions` 命名空間的生產檔案是 `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Members.cs`，但該檔實際呼叫的是 SDK 原生 `organizationService.RetrieveMultiple(query)`，並未呼叫 `ExecuteRetrieveMultiple`——該 using 應是為了同資料夾的 `CrmAsyncExtensions.cs`。
- 測試碼中所有 `service.ExecuteRetrieveMultiple(...)` 呼叫（`EntityQueryContractTests.cs`）測試的都是 `ToolUtility.Core.EntityOperations.EntityQueryService.ExecuteRetrieveMultiple(RetrieveMultipleRequest)` 這個**不同的、已遷移且已有 fail-closed 驗證與測試**的 instance method，與 `OrganizationServiceExtensions.ExecuteRetrieveMultiple(this IOrganizationService, …)` 同名但完全是兩套程式碼、兩個命名空間。

**結論：目前沒有找到任何生產呼叫端依賴 `OrganizationServiceExtensions.ExecuteRetrieveMultiple` 的 null→空集合行為。** 這點應如實記錄為「查無對應生產依賴證據」，不應反過來捏造相容需求去保留空集合行為；但也不能排除未來/外部專案（非此 repo 範圍）呼叫它，只是本 repo 內無實證。

另：任務文字提到的 `ToolUtility.Tests\DataOperations\MutationContractTests.cs` 經核實**不存在**於目前路徑；該資料夾下實際檔案是 `MutationOperationsCharacterizationTests.cs` 與 `MutationOperationsSafetyTests.cs`。若後續要援引既有 Mutation 紅綠測試模式作範本，應以這兩個實際檔名為準。

## 2. 最小正確抽取與例外策略建議

- **EntityAttributeUtilityService**：維持三個公開方法簽章（含 `ref Entity`）與 namespace。建議採「Core 慣例」而非「保留 wrapper」：移除 catch-rewrap，前置驗證直接拋 `Argument*Exception`（null targetEntity/entity/propertyName），不再有 `InvalidOperationException` 外層。若業務上一定要保留 `InvalidOperationException` 契約（例如上層 host 有 catch 型別依賴，但此次抽取範圍已聲明不開全 repo 掃描，故未驗證），則至少要把這個「刻意違反 Core 慣例」的決定明文記錄在程式碼註解與 PR 說明中，不要悄悄分歧。`_logger` 欄位本身直接刪除（死欄位，無讀取者），新 ctor 不保存 logger，只為來源相容保留參數（比照 `EntityQueryService` 寫法）。
- **OrganizationServiceExtensions.ExecuteRetrieveMultiple**：目前 `internal` 驗證邏輯（`SdkDataQuery.RequireService`/`ValidatePage`）是 `internal static class`，`ToolUtility`（host, net10.0-only）要呼叫需確認組件是否互相可見（未見 `ToolUtility.Core.csproj` 有任何 `InternalsVisibleTo` 設定）。若要重用而不 copy，需要：(a) 把這個 extension method 本身 `git mv` 進 `ToolUtility.Core`（與其呼叫的 `SdkDataQuery` 同組件，天然可見，不需要新增 InternalsVisibleTo），或 (b) 在 `ToolUtility.Core.csproj` 補 `InternalsVisibleTo` 給 `ToolUtility`。選 (a) 更貼合本次「下一個小切片」extraction 的目的與既有 Fee/EntityQuery 等的搬遷模式。簽章 `EntityCollection ExecuteRetrieveMultiple(this IOrganizationService service, RetrieveMultipleRequest request)` 與回傳型別不變。由於查無生產依賴空集合的呼叫端（見上），可直接改為 fail-closed：null service/request 拋 `ArgumentNullException`，`request.Query == null` 拋 `ArgumentException`，回應/頁經 `SdkDataQuery.ValidatePage` 驗證（複用既有方法，不重寫）。

## 3. 必須測試的 fault / 隔離 / 生命週期契約

**特性（characterization，先綠鎖既有成功路徑）**
- `GetAttributeValue`：plain 值、`AliasedValue` 包裝值、屬性缺失回空字串、`attributeName` 為 null/empty 回空字串（這點是目前的「寬容」行為，抽取時必須先鎖住，否則悄悄變成拋例外就是破壞相容）。
- `RemoveAttribute`/`SetEntityAttributeToNull`：以 `ref Entity` 就地修改、屬性原本存在/不存在兩種分支都要各鎖一次（`SetEntityAttributeToNull` 對不存在的屬性會 `Add`，存在的則覆寫為 null——兩條路徑語意不同，都要各一條綠燈）。
- `ExecuteRetrieveMultiple`：合法 `RetrieveMultipleRequest`（含分頁 `PagingInfo`）成功路徑，確認回傳 `EntityCollection` 與分頁資訊未被竄改；合法空 `EntityCollection`（`MoreRecords=false`、`Entities.Count==0`）必須仍視為成功，不可被新加的驗證誤判為故障。

**安全紅燈（fail-closed，後補）**
- `GetAttributeValue(null, ...)` → 明確例外（型別依第 2 節決策，`ArgumentNullException` 或維持 wrapper 皆要有對應斷言，但兩者只能選一個鎖進測試，不能同時宣稱兩種契約）。
- `RemoveAttribute`/`SetEntityAttributeToNull`：entity 為 null、propertyName 為 null/empty/whitespace 各一條。
- `ExecuteRetrieveMultiple`：null service、null request、request.Query 為 null 必須在 I/O 前失敗（用 `Mock<IOrganizationService>(MockBehavior.Strict)` + `VerifyNoOtherCalls()` 證明沒打到 SDK，這是 repo 既有 `EntityQueryContractTests.cs:373-384` 的標準寫法，應照抄此驗證手法）；SDK 回應為 null、`EntityCollection` 為 null、內含跨實體 LogicalName、內含重複 Id 都要各一條紅燈，全部交由 `SdkDataQuery.ValidatePage` 判定而非另寫一套邏輯；同名不同 Id 的列必須保留（不可被誤判為重複而拒絕——`ValidatePage` 是以 `Guid` 去重而非名稱，已天然符合，只需補測試鎖住）；SDK 原生 Exception（如 `FaultException`）要原樣上拋，不可被新驗證邏輯吞掉或轉型。

**隔離 / 生命週期**
- `EntityAttributeUtilityService` 本身不持有 `IOrganizationService`、不快取 `Entity`，遷移後需用測試證明：同一個方法呼叫兩次、傳入不同使用者的 `Entity` 實例，彼此互不污染（純函式特性，用兩個獨立 `Entity` 物件各呼叫一次並斷言互不影響即可，不需要真正跨執行緒測試）。
- `ExecuteRetrieveMultiple` 若搬進 Core，要確認它**不**透過任何 Lazy/靜態欄位持有 `IOrganizationService`——目前是 extension method，天然無狀態，遷移後不可意外變成有狀態服務。
- 兩者都不建立 logger、不建立 LINE client、不攔截最終通知例外（沿用現有「真實故障原樣上拋」慣例）——這點用既有的 `client.Verify(...)`/`VerifyNoOtherCalls()` 模式即可同時驗證「沒有多打 I/O」與「沒有吞例外」。
- `git mv` 前務必同步修改 `ToolUtility.Tests.csproj` 的 net48 `ItemGroup`（目前第 54-66 行白名單**沒有包含 `AttributeOperations/*.cs` 或任何 Extensions 相關測試**）；若只加測試檔不補白名單，新測試只會在 net10.0 host 跑，net48 host 完全不會執行，違反任務本身強調的「test host net48 必載 net462」。

## 4. Critical / Warning / Info 彙總

- **Critical**：`EntityAttributeUtilityService` 的 catch-rewrap 成 `InvalidOperationException` 策略，與已遷移 Core 服務（`EntityQueryService`／`OwnerManagementService`／`SdkDataQuery`）全面採用的「前置驗證 + 例外原樣上拋」慣例直接衝突；兩者只能選一個當作新契約鎖進測試，目前任務描述同時要求「保留 wrapper」又要符合 Core 既有模式，是自相矛盾，需要先決策。
- **Warning**：任務文字指定要讀的 `ToolUtility.Tests\DataOperations\MutationContractTests.cs` 不存在，實際檔名是 `MutationOperationsCharacterizationTests.cs`／`MutationOperationsSafetyTests.cs`；若以此為範本需更新路徑。
- **Warning**：`ToolUtility.Tests.csproj` 的 net48 白名單目前未包含 `AttributeOperations/` 任何測試檔，若新增紅綠測試不同步補白名單，net48 host 會靜默跳過，不符合專案「test host net48 必載 net462」規則。
- **Warning**：`SdkDataQuery` 是 `internal static class`，`ToolUtility.Core.csproj` 未見任何 `InternalsVisibleTo`；`OrganizationServiceExtensions` 若不隨本次一起 `git mv` 進 `ToolUtility.Core`，將無法直接重用 `RequireService`/`ValidatePage`，會被迫 copy 邏輯或新增 InternalsVisibleTo（兩者都需要額外決策）。
- **Info**：已核實本 repo 內查無任何生產呼叫端使用 `OrganizationServiceExtensions.ExecuteRetrieveMultiple`（唯一同名方法的生產/測試使用，實際指向已遷移且已受測的 `EntityQueryService.ExecuteRetrieveMultiple`）；改為 fail-closed 在此 repo 範圍內不會破壞已知生產相容性，但不代表可以排除 repo 外部消費者。
- **Info**：`EntityAttributeUtilityService._logger` 欄位目前確認為死欄位（建構後從未被任何方法讀取），且唯一建構點 `ToolUtilityFacade.cs:164`（經 `_logger ?? new object()` 保證非 null）從未依賴 ctor 的 null 拒絕行為——保留嚴格 ctor 是防禦性而非修復已知需求。

---
SESSION_ID: 51bdfd91-2e4c-4643-8998-e08faa915916

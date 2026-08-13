# P7.4 Batch B 本機審查報告

## 驗證方法
- 完整 `dotnet build SpeechMessageProducts.sln`：建置成功，0 警告 0 錯誤。
- `SpeechMessage.Dynamics.Tests`（`Package01` 篩選）：70 通過。
- `SpeechMessage.Dynamics.Tests`（`FeeReadClient` 篩選）：11 通過。
- `ChurchReport.MemberInfo.Tests`（`StorLesson` 篩選）：9 通過。
- 逐檔比對 diff 與 `.trellis` design/implement.md 的授權範圍。

## Critical
無。已對照下列硬性限制逐項確認：feature gate 全數維持 false（`StorLessonQueryService` 舊建構式未變、DI 組合根未動）；Package01 typed path（`GetByContactViaPackage01Async` / `GetByDiscipleLessonViaPackage01Async`）未出現 `RetrieveEntity`、`GetAwaiter().GetResult()` 或跨 request 可變狀態；`GetEntityCollectionByContact/ByDiscipleLesson`、`FindStorLessonId`、`ToEntityCollection` 仍全數走 legacy `_utility` 路徑並有註解明確標示不可計入 migrated；`MapDtos` 對 null `ClassStartDate` 以 `row.ClassStartDate?.LocalDateTime ?? DateTime.MinValue` 短路，不會被本機時區偏移成看似有效日期，並有對應測試覆蓋（`StorLessonQueryServiceAsyncTests.Package01_contact_read_keeps_missing_class_start_date_as_legacy_minimum_date`）。

## Warning

**1. `new_class_start_date`／`new_now_stage_name` 缺乏「CRM 端合法為 null」情境的 connector 層測試**
檔案：`SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs:764-780`（`ReadOptionalAliasedUtcDateTime`，同一模式亦見於既有 `ReadOptionalAliasedString`，`Package01Data8ReadOperations.cs:743-756`）

`lesson` 現在是 inner join（保證有配對的 disciple lesson row），但該 row 上的 `new_class_start_date` 或 `new_now_stage_name` 欄位本身完全可能是合法未填。目前程式碼的邏輯是：
```
if (!entity.Attributes.TryGetValue(aliasAttributeName, out var value) || value is null) return null;
if (value is not AliasedValue { Value: DateTime dateTime }) throw new InvalidOperationException(...);
```
這假設「CRM SDK 對於連結表上合法為 null 的欄位，會直接不把該 alias key 放進 `entity.Attributes`」。但本批新增的兩組測試（`Created_client_projects_lesson_link_date_and_stage_for_each_stor_lesson_operation` 與 `Created_client_rejects_invalid_lesson_alias_types_for_each_stor_lesson_operation`，`SpeechMessage.Dynamics.Tests/OnPremiseData8ConnectorClientFactoryTests.cs:219-408`）只涵蓋「欄位存在且型別正確」與「欄位存在但型別錯誤（字串偽裝日期／日期偽裝字串）」兩種情境，並未涵蓋「欄位鍵確實存在、但 `AliasedValue.Value` 本身為 null」的情境。

如果實際 Data8/CRM 回應對合法 null 的連結欄位是回傳 `AliasedValue { Value = null }` 而非完全省略該 key，這段程式碼會走到 `throw new InvalidOperationException`，導致「沒有開課日期」的正常業務情境變成整個 request 失敗（500），直接違反本次審查明確列出的硬性要求「null 開課日期必須維持既有 UI 的 `DateTime.MinValue`」。既有 `ReadOptionalAliasedString` 的 XML 註解只描述「缺少 outer-join row 時回傳 null」（對應 `ContactAlias` 的 LeftOuter 情境），並未涵蓋「inner join 行存在、但該欄位本身為 null」這個新情境，代表這個假設此前沒有在生產路徑上被驗證過。

建議：在合入前補一個 connector 層測試，讓 fake `Entity` 對 `lesson.new_class_start_date` / `lesson.new_now_stage_name` 分別模擬「合法為 null」（依實際 Data8 行為決定用省略 key 或 `AliasedValue(Value: null)`），斷言回傳 `ClassStartDate == null` 而非拋例外，以鎖定這個硬性需求的真實行為。

## Info

**1. `AddDiscipleLessonLink` 的 XML 註解與本批新增的呼叫點不一致**
檔案：`SpeechMessage.Dynamics.Connectors.Data8/Package01Data8ReadOperations.cs:362-378`

方法上方註解寫「僅依 contact 查詢需要這個 inner join」，但本批 diff 已把 `AddDiscipleLessonLink(query)` 加入 `CreateStorLessonsByDiscipleLessonQuery`（同檔 316-321 行），該方法現在被兩個 query builder 共用。註解未同步更新，日後維護者可能誤以為 by-disciple-lesson 路徑不需要這個 join 而誤刪。建議更新註解描述。

**2. Red-test 遺留的反射讀取，現已可直接用屬性**
檔案：`SpeechMessage.Dynamics.Tests/OnPremiseData8ConnectorClientFactoryTests.cs:387-408`

`ReadRequiredStorLessonClassStartDate` / `ReadRequiredStorLessonStageName` 透過 `GetType().GetProperty(...)` 反射讀取 `Package01StorLessonRecord.ClassStartDate` / `StageName`，註解說明這是為了讓「屬性尚未實作前」測試仍可編譯的 red-test 手法。由於本批已直接在 `Package01StorLessonRecord` 加上這兩個公開屬性（`SpeechMessage.Dynamics.Abstractions/Operations/OperationResponseData.cs:1116-1122`），反射已無必要，可簡化為 `record.ClassStartDate` / `record.StageName` 直接存取。非阻塞，但屬多餘複雜度。

**3. `implement.md` Phase 3 checklist 未同步勾選已完成項目**
檔案：`.trellis/tasks/08-12-churchreport-productclient-cutover/implement.md`（Phase 3 區塊）

「先在 shared wire/ProductClient/connector tests 建立 fail-first contract」與「在 ChurchReport focused tests 建立 fail-first contract」兩項仍標記 `[ ]`，但對應測試（`OnPremiseData8ConnectorClientFactoryTests.cs`、`Package01FeeReadClientTests.cs`、`StorLessonQueryServiceAsyncTests.cs`、`StorLessonControllerProductClientContractTests.cs`）已在本次 diff 中新增並通過。建議同步勾選以維持 task 追蹤準確性，避免後續批次誤判進度。

---
總結：本批變更在架構邊界（no-SDK typed path、legacy-only SDK caller、cancellation 傳遞、A/B 隔離、UTF-8/CRLF）上落實良好，建置與既有測試全數綠燈；主要風險集中在 Warning 1 描述的「合法 null 開課欄位」測試缺口，建議合入前補測試驗證後再視為完成。

---
SESSION_ID: 35796579-13d5-4118-b0f2-4c91d4e46ae6

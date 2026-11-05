## 審查結論（P1-04 增量補證，窄範圍）

### Critical 🔴
無。

### Warning 🟡

- **「無產品行為變更」缺乏差異證據**
  本輪附件只有測試檔與（供佐證用的）既有介面／csproj 內容，沒有 `git diff --stat` 或等效差異清單直接證明這輪真的「零產品程式碼變更」。目前只能靠敘述與未重現的舊 1825 TRX（且明確早於本輪）間接推斷。建議結案證據補一份本輪變更檔案清單（哪怕只是 `git diff --stat` 文字），讓「僅新增測試」這個結論是可驗證的，而不是敘述性保證。這不影響本輪新增測試本身的正確性，純粹是結案稽核完整性問題。

### Info 🟢

1. **Nullable 誤報結論可採信，不需修改公開 API 或補 pragma**
   `ToolUtility.Core.csproj` 本身沒有顯式 `<Nullable>enable</Nullable>`，但 `artifacts/p1-04-nullability-net462.json` 與 `net10.0.json` 兩個目標的 **MSBuild 實際求值結果**都回報 `"Nullable": "enable"`，可見是透過 `SpeechMessageStrictCode=true` 間接帶出（machinery 不在本次閱讀範圍內，但求值結果本身已是比讀 props 檔更直接的證據）。三介面檔案都沒有 `#nullable disable` 覆寫，因此專案層級 enable 會對它們生效。`CoreInterfaces_EmitNonObliviousStringContracts` 用 `NullabilityInfoContext` 對三介面所有 `string` 參數做真實反射驗證、斷言 `ReadState == NotNull`，且有 `checkedParameters > 0` 防呆（避免反射找不到任何參數卻靜默通過）。這是比對 pragma 存在與否更可靠的運行時證據。**同意**：先前「無 `#nullable` 即 oblivious」的判斷是誤報，不需為此改公開 API 介面或疊加重複 pragma。
   （測試以 `#if !NETFRAMEWORK` 限制只在 net10.0 執行，因為 `NullabilityInfoContext` 本身是 .NET 6+ API；nullable metadata attribute 在 net462 編譯時一樣會內嵌，只是沒有該反射 API 可讀，這是合理的技術限制，不是漏測。）

2. **ByDate 四種故障注入與 Golden helper 均為語義檢查，非字串比對/自動更新**
   `AppointmentByDate_InvalidPage_RejectsEntireCandidate` 直接呼叫 `AppointmentService.RetrieveByDate(...)`（獨立於 `DataOperationFixture.Run` 共用派發路徑），對 duplicate/empty/entity/null 四種故障都斷言 `InvalidOperationException` 且候選集合筆數不變（2 筆）——符合「整頁拒絕、不可部分放行或靜默清空」的契約。
   `GoldenQueryAssertions` 用 `XDocument` 正規化比較（`DisableFormatting`），非純文字 diff；其自測 `GoldenQueryAssertionsTests` 覆蓋四種情境：僅空白/引號差異應通過、欄位改名／漏排序／元素順序互換均應擋下、DTD 直接拒絕、超過 1MiB 直接拒絕。這些都是對 helper 自身嚴格度的驗證，不是「能解析就放行」，也沒有自動回寫 golden 的邏輯。結論：此部分證據完整，無缺口。
   唯一理論上的邊界情況（非本輪缺陷，僅記錄）：`XElement.ToString` 序列化屬性時依原始解析順序而非排序，若未來兩份語義相同但屬性順序不同的 XML 被比較，可能被誤判為不相等；但目前 golden 檔由固定程式碼產生，不會自然出現此情況，不需本輪處理。

3. **8/8 執行結果與聲稱範圍完全對應**
   TRX 計數 `total=8 passed=8`，明細為 4 個 ByDate 故障（null/entity/duplicate/empty）+ 1 個既有 `AppointmentByDate_PreservesLegacyUnfilteredQuery` 正常案例 + 3 個介面 metadata 案例（`ILessonsService`/`IAppointmentService`/`IMeetingStatisticsService`），與原始碼中的 `[Theory]/[InlineData]` 參數組合逐一吻合，無虛報或遺漏案例。

4. **16 TRX / 1825 筆全案結果的時間先後聲明成立**
   `p1-04-extracted-solution-summary.json` 內各 TRX 檔案時間戳（13:47:12–15）早於本輪 8 項新測試的執行與附件產出時間，且摘要文字本身已明確標註「不冒充最新全部測試、待最後重跑」，未見文件內部矛盾或誇大宣稱。

### Summary
本輪補證內容與聲稱一致，無 Critical 問題。核心結論（nullable 誤報、ByDate 故障注入完整、Golden helper 為語義比較）**可採信**，不建議因此修改產品公開 API 或介面宣告。唯一建議補強項是附上本輪實際變更檔案清單以佐證「零產品行為變更」，屬結案稽核完整性的 Warning，不影響已附測試證據本身的有效性。P1-04 待最終整案重跑前，本窄範圍可視為通過。

---
SESSION_ID: 71429340-d804-497e-b944-133f76300017

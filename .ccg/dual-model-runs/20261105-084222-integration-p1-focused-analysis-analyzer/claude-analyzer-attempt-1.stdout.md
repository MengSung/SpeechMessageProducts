## P1 架構風險分析（依據提供之設計敘述分析，未讀取程式碼、未使用任何工具）

### Critical

**1.〔Framework 相依性選擇〕HMAC 固定時間比較在 net462 無對應 BCL API**
`CryptographicOperations.FixedTimeEquals` 僅存在於 netstandard2.1 / .NET Core 3+，net462（netstandard2.0）無法直接呼叫。若兩個 target 各自手寫比較邏輯，容易有一側退化為一般 `==`/`SequenceEqual`，使「transport-independent HMAC 驗證」在 net462 host 上失去時序攻擊防護。
**修正**：抽出一份不依賴該 API 的手寫固定時間比較函式（逐 byte XOR 累加後判斷），放入雙目標共用函式庫，兩端呼叫同一份實作，不要各自重寫。

**2.〔Framework 相依性選擇〕Dataverse.Client 必須嚴格以 TFM 條件隔離，且 R4 需驗證「還原後」的圖**
net462 用 CoreAssemblies、net10 用 Dataverse.Client、PrivateAssets=all，若條件寫在 PropertyGroup 而非逐一 PackageReference，或 R4 只檢查 csproj 文字宣告而非實際還原結果，net462 restore 仍可能嘗試解析 Dataverse.Client（需 net6+），導致建置失敗或載入錯誤版本組件。
**修正**：R4 對每個 TFM 個別執行還原並比對 `*.assets.json` 實際解析結果，而非僅靜態分析 csproj。

**3.〔Additive-schema 完整性〕Alternate Key 啟用時機早於整體驗證成功**
若「Active before verify success」指所有相依 create 完成驗證前就啟用 Alternate Key，一旦後續步驟失敗，Key 會指向尚未完整、可能衝突的 schema，而該啟用操作本身不易復原。
**修正**：改為所有 additive create 全部驗證成功後、最後一步才啟用 Alternate Key；若必須提前啟用，需同時定義補償/回滾步驟。

### Warning

**4.〔Stale generation race〕generation 比對與 atomic publish 未明確共用同一臨界區**
規格僅說明 generation fence「防止」republish，未說明比對與發布是否在同一鎖/CAS 內完成。若「驗證候選值通過」與「實際發布」之間存在時間差，invalidate 可能插入此空隙，使已失效的 snapshot 仍被發布。
**修正**：generation 檢查必須與 publish 在同一 critical section 內原子化完成，不得先查後發。

**5.〔Bounded task shutdown〕Dispose 的 drain 階段缺乏逾時與強制終止路徑**
若底層 in-flight 操作（如 IOrganizationService 呼叫）忽略 CancellationToken，drain 可能無限期阻塞 Dispose；同時「caller cancellation detaches waiter」與「Dispose 的 cancels owner」兩路徑的執行順序未定義，可能造成資源重複釋放或釋放後再存取。
**修正**：drain 加上有界逾時，逾時即強制放棄而非持續等待；detach 與 owner-cancel 需在同一同步原語下序列化，並確保 Dispose 冪等。

**6.〔Timezone 可攜性〕net462 pinned map 會隨時間與 net10 ICU 結果產生偏移**
Pinned CLDR/IANA-Windows 對照表是編譯期靜態快照，net10 的 TimeProvider 路徑則依賴即時系統/ICU 規則。當 DST 法規或 IANA 資料更新後，兩端對同一時區＋本地時間判斷「無效/模糊」的結果可能不一致，違反 transport-independent 假設；規格未提供更新流程。
**修正**：pinned map 版本化，建立 CI 比對測試對代表性時區/日期同時以兩個 target 執行，任何落差視為建置失敗；並訂定例行更新排程。

**7.〔Additive-schema 完整性〕多步驟 apply 缺少中途失敗的部分狀態政策**
已定義 export-before-mutation 與同 org OS lock 防雙重 apply，但未定義「N 個 additive 操作中途因 live-state 衝突中止」時，已完成步驟如何處理。
**修正**：明確定義為「中止並保留已完成步驟、回報可續跑 plan」（非自動回滾），plan 結果需記錄每個 operation 的完成狀態供下次 apply 做冪等判斷。

**8.〔Framework 相依性選擇〕obsolete ChurchReport 預設值可能被新呼叫點無意繼承**
保留舊 overload 的預設值以維持相容性合理，但未限制新程式碼依賴此預設，可能讓新功能在未指定 product 時被誤歸入 ChurchReport scope。
**修正**：保留舊 overload 供既有呼叫點使用，新增一個不含預設值的 overload，並以分析器規則禁止新呼叫點使用有預設值版本。

### Info

**9. 四個 fee/entity 查詢僅做 XML escape，未處理 `like` 萬用字元**
若這些值用於 FetchXML 的 `like` 條件，`%`、`_` 未轉義可能造成查詢範圍比預期寬（正確性風險，非資安漏洞）。
**建議**：對用於 `like` 運算的值額外做萬用字元轉義。

**10. Credential ToString redaction 需延伸至新 webhook 簽章密鑰型別**
規格未明確說明新增的 webhook secret/HMAC key 包裝型別是否套用與既有 Credential 相同的 ToString/例外訊息 redaction 規則。
**建議**：明確要求新型別實作相同的 redaction 介面/基底類別，避免例外堆疊或日誌意外洩漏密鑰。

---
**結論**：以上 10 項均為提供敘述中「設計層級的遺漏」，非臆測性缺陷；Critical 3 項涉及可能導致建置失敗、資安防護失效或資料損毀，建議優先處理後再進入 P1 實作驗收。

---
SESSION_ID: 3f812ea9-5f2f-41d4-8dc1-7d168f195dd2

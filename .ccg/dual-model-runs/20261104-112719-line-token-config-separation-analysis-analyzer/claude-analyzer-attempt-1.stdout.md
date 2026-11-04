## 分析:ChurchReport LINE 憑證設定分離變更(事前設計覆核)

### 現況基礎(已讀取程式碼佐證,未讀取任何憑證值)

- `ChurchReport.Configuration.ChurchReportDevelopmentConfiguration`(`SpeechMessageProducts.ChurchReport\Configuration\ChurchReportDevelopmentConfiguration.cs:58-92`)目前有兩種互相獨立的保護機制:
  1. **別名名稱比對**(第 78-80 行):`DefaultOrganization` 不可等於 `baselineAliases` 中任何既有別名的 **Key**(不論該別名 token 是否為空)— 此檢查與 baseline token 是否清空無關。
  2. **Token 數值比對**(第 67-77 行):若目前任一通道或 `LINE_CHANNEL_ACCESS_TOKEN` 的值,與 `baseChannels`(baseline 中 **非空** 的 token)**完全相等**,才拒絕。此檢查**依賴 baseline 存在非空值**。
- 既有測試 `DevelopmentGuard_UnsafeSetting_RejectsWithoutSecret`(`ChurchReport.MemberInfo.Tests\Contracts\DevelopmentSafetyTests.cs:80-99`)的 `("LineMessaging:Jesus:ChannelAccessToken","PROD-FAKE")` 案例,之所以會被拒絕,是因為測試夾具的 `appsettings.json`(第 246-248 行)本身就寫了 `"PROD-FAKE"` 這個值 — **驗證的是數值相等**,不是「任何非空值都擋」。
- 共用函式庫 `LineMessagingProcessor.LineMessagingProcessorClass.ResolveDefaultChannelAccessToken()`(`LineMessagingProcessor\LineMessagingProcessorClass.cs:96-136`)在**所有環境**都呼叫無前綴的 `.AddEnvironmentVariables()`(第 103 行),且 `DefaultOrganization` 未設定時 fallback 到 `"Jesus"`(第 132 行)。
- 11 個舊式讀取點之一(`SpeechMessageProducts.ChurchReport\Tools\QrCodeUtility.cs:77-80`)目前**完全沒有** `AddEnvironmentVariables()`,只有 `AddJsonFile` + `AddChurchReportDevelopmentOverrides()`。
- `Program.cs:68-74`:`WebApplication.CreateBuilder(args)` 建立後、`ValidateDevelopmentConfiguration` 呼叫前,尚未呼叫 `Build()`,`builder.Configuration` 仍可新增 provider。

---

### Critical

**C1. 清空 base token 後,既有的「數值比對」保護會靜默失效,新規則尚未落地取代它。**
- 檔案:`SpeechMessageProducts.ChurchReport\Configuration\ChurchReportDevelopmentConfiguration.cs:67-77`
- 一旦追蹤的 `appsettings.json` 兩個 token 被清空,`baseChannels` 在真實執行時會是空陣列,`baseChannels.Any(...)` 永遠為 `false`。這代表:「任何通道/`LINE_CHANNEL_ACCESS_TOKEN` 不可等於正式 token」這條防線,在正式部署中會變成**恆真(無效)判斷**,而不是測試裡用合成 baseline 驗證出的行為。
- 設計書要求的「拒絕非空正式別名與 `LINE_CHANNEL_ACCESS_TOKEN`」必須是**無條件**檢查(只要 Development 下該鍵非空就拒絕),而非目前「與 baseline 相等才拒絕」的邏輯。這是**必須新增的程式碼變更**,不是既有邏輯的副作用保障。
- 同理,`DevTest` 別名的「必須由 Host User Secrets 提供」檢查,目前程式碼中完全不存在,是全新機制,且在 base token 清空後,它是**唯一**防止非 secrets 來源灌入 `DevTest` token 的機制(別名名稱比對與 DevTest 無關,它防的是「選到正式別名當預設」,不是「DevTest 本身的值從哪裡來」)。

**C2. User Secrets provenance 以 provider 類型/路徑反查並非穩定公開契約,且本專案已存在「重複註冊」情境需納入判斷。**
- `ChurchReportDevelopmentConfiguration.AddChurchReportDevelopmentOverrides`(第 41-48 行)在 Development 手動呼叫 `.AddUserSecrets(secretsAssembly, ...)`;而 `WebApplication.CreateBuilder` 在 Development 且組件有 `UserSecretsIdAttribute`(已由 `HostAssembly_UserSecretsId_IsAvailableToDevelopmentLoader` 測試證實存在)時**也會自動加入一次**。結果是同一支 secrets.json 可能對應**兩個** `JsonConfigurationProvider` 實例。
- `UserSecretsConfigurationProvider` 底層就是 `JsonConfigurationProvider`,與讀 `appsettings*.json` 的 provider **類型相同**,無法僅憑 `GetType()` 區分;只能用 `Source.Path`(或 `Source.FileProvider` 根路徑)結尾為 `secrets.json` 且路徑含使用者的 UserSecretsId 來推斷,這是依賴 .NET 內部路徑慣例、非公開文件化契約,未來 SDK/Runtime 版本調整檔案位置規則時可能失準。
- 可靠做法:對 `ConfigurationRoot.Providers` **反向**走訪(覆寫順序,最後加入者優先),找到第一個 `TryGet(key)` 成功的 provider;再檢查該 provider 是否符合「`JsonConfigurationProvider` 且 `Source.Path` 檔名為 `secrets.json`」。因為可能有兩個重複的 secrets provider,判斷邏輯不可假設「正好一個」,應寫成「只要找到的那個(反向第一個命中者)符合路徑條件即可」,而非比對 provider 清單中是否「恰好存在」某個 secrets provider。

---

### High

**H1. `LINE__` 前綴對 Development 不構成實質隔離邊界 — 安全性必須完全靠驗證邏輯,不能靠「只在非 Development 加前綴 provider」。**
- `WebApplication.CreateBuilder` 已預設載入**無前綴** `AddEnvironmentVariables()`(題述已確認),`LineMessagingProcessorClass.ResolveDefaultChannelAccessToken()` 第 103 行亦是**不分環境**呼叫無前綴版本。.NET 的無前綴 `AddEnvironmentVariables()` 會把**任何**含 `__` 的環境變數轉成 `:` 分隔的設定鍵,不要求任何前綴。
- 因此,即使「新 `LINE__` provider 僅在非 Development 加入」,在 Development 下,只要有人設定環境變數 `LineMessaging__Jesus__ChannelAccessToken`(**不含** `LINE__` 前綴),既有的、一直存在的無前綴 provider 就會直接寫入 `LineMessaging:Jesus:ChannelAccessToken`,完全不受本次「只在非 Development 加前綴 provider」規則管轄。
- 結論:`LINE__` 前綴只是正式/非 Development 環境變數命名慣例,**不是** Development 下防止環境變數灌值的機制。C1 所述「無條件拒絕非空正式別名/`LINE_CHANNEL_ACCESS_TOKEN`」與「DevTest 值須驗證來自 User Secrets」這兩條,才是真正擋下此路徑的機制,必須覆蓋「最終合併後的值」,不能只檢查「是否加了 `LINE__` provider」。

**H2. Provider 加入順序必須與既有 11 個讀取點的現狀逐一核對,不可一概而論。**
- 已確認至少一個讀取點(`QrCodeUtility.cs:77-80`)目前**沒有任何** `AddEnvironmentVariables()` 呼叫,是 `static readonly` 欄位、型別初始化時期一次性建構(`.AddJsonFile(...).AddChurchReportDevelopmentOverrides()`,無 reload)。新前綴 provider 要插入這 11 處時,需個別確認:(a) 現有是否已有無前綴 `AddEnvironmentVariables()`(若有,新 provider 相對位置決定何者覆寫何者)、(b) 建構器是否為 `static readonly`(一次性快取,不會隨後續環境變數變更重新讀取,此為既有慣例,新 provider 應沿用同樣的「不 reload」語意,避免行為不一致)。
- `SourceBuilders_AllLineConfigurationPaths_UseDevelopmentLayer` 測試(`DevelopmentSafetyTests.cs:171-198`)目前鎖定「恰好 11 個」讀取點、共用函式庫需含 `AddDevelopmentOverrides(`、`Program.cs` 中 guard 需在 `ExceptionOutputOptions.FromConfiguration` 之前。本次變更若新增一個「加入 `LINE__` provider」的輔助函式,應讓此測試(或其姊妹測試)同步鎖定「所有 11 處 + 共用函式庫 + Program.cs 都接上新 provider」,否則容易漏接而不自知。

### Medium

**M1. Production 關閉憑證來源的時序風險(非程式邏輯問題,是上線排序問題)。**
- `LineMessagingProcessorClass` 第 132 行 `DefaultOrganization ?? "Jesus"`:若正式環境未設定 `LineMessaging:DefaultOrganization` 且未設定任何環境變數,共用函式庫會嘗試讀 `LineMessaging:Jesus:ChannelAccessToken`。清空 base token 後,此路徑會回傳空字串,`GetRequiredChannelAccessToken()`(第 138-147 行)在真正需要 token 時才擲例外,而非啟動時立刻失敗。
- 這代表:**合併清空 base token 的變更前,必須先確認正式環境已經備妥 `LINE__LineMessaging__Jesus__ChannelAccessToken` / `LINE__LineMessaging__JesusBack__ChannelAccessToken`(或既有部署 secrets)**,否則會出現「啟動成功、發送訊息時才炸」的延遲性正式環境失效,排查成本較高。

**M2. 既有測試對「數值比對」機制的覆蓋,在變更後語意會偏移,需要新增而非只依賴既有案例。**
- `DevelopmentGuard_UnsafeSetting_RejectsWithoutSecret` 的 `PROD-FAKE` 案例只在「合成 baseline 恰好相等」時成立,**不能**代表「無條件拒絕非空正式別名值」這條新規則已被驗證。需要新增測試:baseline 兩個別名皆為空字串(模擬真實清空後狀態)時,`LineMessaging:Jesus:ChannelAccessToken` 設成**任意**非空、**不等於**任何 baseline 值的假字串,仍應被拒絕 — 這才是 C1 要補的行為。

### Low

**L1. 文件註解需同步更新。**
- `ChurchReportDevelopmentConfiguration.cs` 第 9-17 行 XML 文件註解描述目前兩種檢查機制,一旦新增無條件檢查與 User Secrets 來源驗證,註解需同步說明新邏輯,避免未來維護者誤判「比對 baseline」仍是主要防線。

---

### 必要的設計修正(落地前必須明確)

1. `ValidateDevelopmentConfiguration` 需新增**與 baseline 無關**的無條件規則:Development 下 `LineMessaging:Jesus:ChannelAccessToken`、`LineMessaging:JesusBack:ChannelAccessToken`、`LINE_CHANNEL_ACCESS_TOKEN` 只要非空即拒絕(取代/補強現有的數值相等比對,現有比對可保留作為額外防線但不可是唯一防線)。
2. 新增 `DevTest` 別名 provenance 檢查:反向走訪 `ConfigurationRoot.Providers`,找到解析出該鍵值的 provider,確認其為 `JsonConfigurationProvider` 且 `Source.Path` 檔名為 `secrets.json`;需容忍重複註冊的 secrets provider(見 C2)。
3. 明確規範新 `LINE__` provider 在既有無前綴 `AddEnvironmentVariables()` 之前或之後插入(決定互相覆寫優先權),三個插入點(`Program.cs` host、11 個舊式讀取點、`LineMessagingProcessorClass`)採同一順位規則。
4. 上線前確認正式環境部署設定已備妥新環境變數名稱,避免 M1 的延遲性失效。

### 建議新增/調整的測試(對應 `DevelopmentSafetyTests.cs` 既有風格)

- baseline 兩別名皆空字串 + Development 下該別名 token 設為任意非空假值 → 必須拒絕(對應 C1)。
- baseline 兩別名皆空字串 + `LINE_CHANNEL_ACCESS_TOKEN` 設任意非空假值(不等於任何 baseline 值)→ 必須拒絕。
- `DevTest` token 由**非** User Secrets 的 provider(例如一般 JSON 檔或記憶體 provider)提供非空值 → 必須拒絕;由 User Secrets provider 提供 → 必須通過(對應 C2)。
- Development 下,透過**無前綴**環境變數(模擬 H1 情境,非 `LINE__` 前綴)設定 `LineMessaging:Jesus:ChannelAccessToken` → 仍必須被 C1 的無條件檢查擋下,證明防線不依賴「有沒有加 `LINE__` provider」。
- 非 Development 下,`LINE__LineMessaging__Jesus__ChannelAccessToken` 與既有無前綴 `LineMessaging__Jesus__ChannelAccessToken` 同時設定不同值 → 鎖定最終優先權(對應 H2 的順序決策一旦拍板,需有測試鎖住,避免未來调整順序時無聲改變行為)。
- 擴充 `SourceBuilders_AllLineConfigurationPaths_UseDevelopmentLayer`,斷言 11 處讀取點與共用函式庫皆已接上新 provider 的呼叫片段。

以上為設計覆核結果,未實作任何程式變更、未讀取或輸出任何憑證數值。

---
SESSION_ID: 6b7e3b15-599b-436b-b745-e75652327922

Confirmed:此對話即為 CCG 派給 Claude reviewer 的 attempt-2 執行。已完成讀取視覺化 HTML/PNG,並對照實際程式碼(`OnPremiseClient.cs`、`CrmConnectionPool.cs`、`AdfsOAuthTokenProvider.cs`、`DynamicsExecutionMode.cs`、`ProductDynamicsOptions.cs`、`OrganizationAdmissionManager` 等)逐項驗證,現提出審查結論。

## Review: Central/Local Gateway with Dynamics CE 8.2 and 9.1 視覺化(`dynamics-central-local-82-91.html`)

### 摘要判定
整體架構敘事與 12 條決策**基本一致**,層級劃分(Central 集中 vs Local 個別 Process、8.2/9.1 各自獨立 Profile Runtime、Data8 暫留與退場路徑)清楚且無明顯技術性錯誤。未發現任何一項可判定為「圖表明確主張跨版本共用同一可變連線/Session」或「明確過度宣稱官方 ServiceClient/Web API 已在目前 CE 8.2 IFD 環境中可用」的錯誤敘述——圖表用語(「打通後」「ADFS OAuth 可用時」)刻意保留條件語氣,符合決策 #8、#9。

但比對現有程式碼後,找到 1 項 Critical 與 3 項 Warning,均屬「圖表未揭露的生命週期/命名一致性風險」,並非圖表文字本身寫錯。

---

### 🔴 Critical

**[PowerPlatform.Dataverse.Client/OnPremiseClient.cs:33]** Data8/CE 8.2 legacy client 未實作 `IDisposable`,連線池的 Dispose 呼叫是靜默空操作
- **問題**:`OnPremiseClient : IOrganizationService`(第 33 行)沒有實作 `IDisposable`,類別內唯一的 `IDisposable` 是私有巢狀類別 `OrgServiceScope`(僅用於每次呼叫的 SOAP header scope,第 38~64 行),不是用來關閉底層 WCF channel。而 `ToolUtility/ConnectionOperations/CrmConnectionPool.cs:415` 的 `DisposeConnection` 是用 `(connection?.Service as IDisposable)?.Dispose()` 釋放連線——對 `OnPremiseClient` 而言這個轉型永遠是 `null`,`Dispose()` 永遠不會被呼叫,底層 WCF `ChannelFactory`/`IClientChannel` 不會被 `Close`/`Abort`。在連線驗證失敗、idle 清理、pool 收縮等情境(該檔案第 137、394、461 行都會觸發 `DisposeConnection`)下會持續累積未關閉的 WCF channel/socket,長時間跑等於 socket 耗盡風險。
- **與圖表的關係**:圖表(`data-view-panel="central"` 區塊,第 499~505 行)明確寫「每個 Runtime 都要有明確 Dispose 與 Drain」作為集中式設計原則,但這個原則目前**尚未在 crm82 legacy 路徑的既有實作上成立**。圖表也沒有把這條已知缺陷標示成「Data8 暫留期間的已知風險」。
- **範圍澄清(重要)**:目前 `SpeechMessage.Dynamics.*` 系列專案(新 Gateway/WebApi/Embedded 架構)**尚未引用** `OnPremiseClient`/`PowerPlatform.Dataverse.Client`——已確認搜尋無結果。也就是說這個缺陷目前只存在於舊 `ToolUtility/ConnectionOperations` 路徑,尚未被新 Gateway 架構「繼承」。這正是修正它的最佳時機:一旦圖表所規劃的 crm82 legacy adapter 把 `OnPremiseClient` 接入新 Central/Local Connection Runtime,若不先修好 `IDisposable`,新架構的 pool 會原封不動繼承同一個洩漏風險,且會直接違反決策 #11「Data8 必須通過實機測試才能移除」的驗收條件(帶著洩漏缺陷的元件無法真正通過長時間 soak test)。
- **建議**:在把 `OnPremiseClient` 接進新 Gateway 之前,先讓它實作 `IDisposable` 並正確 `Close`/`Abort` 底層 channel;圖表可加一條「Data8 暫留期間已知限制」的註記,提醒維護者這不是零風險的純轉接層。

---

### 🟡 Warning

**[HTML 第 510~514、561~565 行]** 圖表範例 JSON 的 `ExecutionMode` 值與目前程式碼 enum 不一致,可能誤導實作
- 圖表在 Central 與 Local 面板都各自秀出範例 appsettings.json:`"ExecutionMode": "CentralGateway"` 與 `"ExecutionMode": "LocalGateway"`。但實際型別 `SpeechMessage.Dynamics.Abstractions/Execution/DynamicsExecutionMode.cs` 目前只定義兩個值:`Gateway = 0`、`Embedded = 1`,並沒有 `CentralGateway`/`LocalGateway` 這兩個字面值。`ProductDynamicsOptions.ExecutionMode` 標了 `[Required]`,若有人依圖表範例原樣貼進真實 `appsettings.json`,選項繫結/驗證會直接失敗(enum 無法解析該字串),導致啟動失敗。
- Central vs Local 目前在程式碼層面應該是「同一個 `ExecutionMode.Gateway`,靠 `GatewayModeOptions.Endpoint` 指向不同網址(central 內部 DNS vs `https://localhost:<port>`)」來區分,而不是兩個獨立 enum 值。圖表若要維持「CentralGateway/LocalGateway」這種語意清楚的用詞,建議明確加註「這是部署角色標籤,不是目前 `DynamicsExecutionMode` 的字面值」,或反過來推動程式碼真的新增這兩個 enum 值以符合圖表契約——兩者擇一,但目前二者不同步。

**[HTML 第 456~463、581~596 行 vs `DynamicsExecutionMode.cs` 第 25~28 行]** 「LocalGateway」與既有已實作的 `Embedded` 模式的關係未在圖表中說明清楚
- 決策 #12 明講「Embedded remains deferred」,但目前 `DynamicsExecutionMode.Embedded` 已經是一個**功能完整、已實作**的模式(`EmbeddedModeOptions` 涵蓋 CE 8.2/9.1、Windows/ADFS OAuth、manifest 驗證等,`SpeechMessage.Dynamics.Embedded` 專案也已存在),其官方註解明寫用途是「方便 Visual Studio 本機除錯」——這與圖表現在賦予「LocalGateway」的角色(VS 開發/隔離部署便利性)高度重疊。
- 圖表沒有回答:LocalGateway 是要取代 Embedded 模式(即 Embedded 未來會被棄用/移除),還是兩者並存服務不同情境(例如 Embedded 用於免額外進程的單元測試、LocalGateway 用於需要獨立健康檢查的整合開發)?這個歧義如果不釐清,維護者可能會同時投入心力維護兩條路徑,或誤刪已經寫好的 `EmbeddedModeOptions` 相關程式碼。建議圖表補一句明確關係說明。

**[HTML 第 588 行 vs `AdfsOAuthTokenProvider.cs` 第 4~11、73~80 行]** 「Web API v8.2:ADFS OAuth 打通後」未區分「已驗證的互動式流程」與「尚未驗證的正式環境非互動流程」
- 目前程式碼與其教學註解確認:此環境 ADFS **已實測拒絕 password grant**,目前唯一走得通的是瀏覽器 authorization_code + refresh_token,且明確標示為 `LocalDevTokenStorePath`/`AllowLocalDevPasswordGrant`——即「僅限 local-dev」。正式環境所需的非互動式服務身分驗證(client credentials 或憑證)**目前程式碼裡沒有已驗證的實作**,註解只寫「正式環境應走非密碼服務流程」作為待辦方向。
- 圖表第 588 行僅寫「必須先完成 ADFS OAuth Client／Redirect URI,並驗證功能差異」,容易讓讀者以為只差「跑通一次互動登入」這一步,但 Central Gateway(無人值守生產服務)真正需要的是非互動式流程,這是難度不同、目前完全沒有 PoC 的另一個問題。建議圖表把「互動式本機驗證通過」與「非互動式正式環境身分」拆成兩個獨立里程碑,避免低估 8.2 Direct Web API 路徑的正式化成本。

---

### 🔵 Info

1. **既有的 `.ccg/dual-model-runs/.../gemini-reviewer-attempt-2.stdout.md`(先前 Gemini 審查結果)內含不準確描述,不應直接採信其評分或部分敘述**——該份報告提到「互動式 Mermaid 圖表」「鍵盤導航焦點框」等,但實際上這份視覺化是純手寫 HTML/CSS + 5 顆 `<button>` 切換面板(第 381~387 行),並非 Mermaid;`.dg-node` 內容區塊是純展示用 `<div>`,沒有 `tabindex`/`href`/click handler,不存在「互動節點缺焦點框」的問題;唯一可互動元素是頂端 5 顆 `<button>`,已有正確的 `role="group"`、逐一 `aria-pressed` 切換(第 626~636 行),且 CSS 未見任何 `outline:none` 抑制焦點框的規則。Gemini 版本附帶的「89/100」評分套用了一份看起來像是通用 UI/UX rubric(User Experience/Browser Compatibility 等),與純架構決策視覺化的審查目標不太吻合,建議不要直接把該分數當作此圖表的品質基準。
2. **Organization Admission Coordinator 的敘述有實際程式碼佐證**:`SpeechMessage.Dynamics.WebApi/Capacity/OrganizationAdmissionManager.cs`、`SqlRuntimeHostSlotCoordinator.cs` 與對應測試(`OrganizationAdmissionManagerTests.cs`、`OrganizationAdmissionLeaseLifecycleTests.cs`)已存在,證實決策 #5「Central 與所有 Local pool 物理獨立但共用組織級併發預算」不是空話,已有落地機制,這點在圖表中呈現得清楚且可信,值得肯定。
3. 無障礙標記整體良好:面板均有 `aria-label` 描述資料流向,`role="img"`/`role="list"`/`role="listitem"` 使用得當(第 477、528、580、599 行),小螢幕版面也有對應的 `@media (max-width: 760px)` 重排,無需額外修正。

---

### 回答審查問題清單

- **架構是否技術正確且與 12 項決策一致?** 是,核心敘事一致;差異僅在「圖表範例值與現行 enum 不同步」「LocalGateway 與既有 Embedded 模式關係未講清楚」兩處命名/邊界層級的落差(見 Warning)。
- **是否有文字誤導成『跨版本共用同一可變連線』?** 沒有——圖表明確反向強調 8.2/9.1 各自獨立 Runtime、獨立 Token Cache、不可共用可變 Session。
- **是否有過度宣稱官方 ServiceClient/Web API 已可用於現行 CE 8.2 IFD?** 沒有明顯過度宣稱,但 Warning #3 指出「打通後」的措辭低估了正式環境非互動驗證的難度。
- **Central vs Local 的擁有權邊界是否易懂?** 是,顏色分軌、標籤(集中管理/個別 Process)、獨立 pool 敘述都清楚。
- **Data8 保留/移除與官方 worker 遷移邊界是否清楚?** 是,v82 面板的時間軸與「立即決策」判斷語句清楚劃出邊界,並正確禁止 Data8 成為永久共用核心。
- **是否有圖表遺漏的隔離/憑證/連線池/生命週期風險?** 有,即 Critical 項(Data8 legacy client 的 Dispose 缺陷)與 Warning #3(ADFS 非互動驗證缺口),兩者都建議在圖表或後續設計文件中明確標註為已知風險/待驗證項目。

**結論**:無法整體判定為「無 Critical/Warning」——存在 1 項 Critical(需要在把 Data8 legacy client 併入新 Gateway 架構前修正,否則會把既有洩漏缺陷帶入新設計)與 3 項 Warning(圖表範例與現行程式碼 enum 不同步、LocalGateway/Embedded 邊界未言明、ADFS 正式環境非互動驗證難度被低估)。建議在下一版視覺化或設計文件更新時補上這些澄清,再進入下一階段實作。

---
SESSION_ID: 65dfdce9-2cd0-435f-8287-620da3fe81d6

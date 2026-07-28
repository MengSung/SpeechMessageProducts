# 審查報告:Dynamics Access Gateway 架構規格 (Final Completeness Review)

## 審查範圍
- `.trellis/tasks/07-23-dynamics-connection-compatibility/prd.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/design.md`
- `.trellis/tasks/07-23-dynamics-connection-compatibility/implement.md`
- `docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md`

僅審查上述規劃文件,未修改任何程式碼或未列出的工作區變更。

## 總體結論
四份文件內容彼此一致,且已涵蓋任務要求清單(regression checks)中列出的每一項機制:`RuntimeHostSlotLease` + `AdmissionEpoch`、單一受控操作端點、`AggregateMaxInFlight/MaximumRuntimeHosts` 驗證、`OrganizationAdmissionKey` 跨代/跨別名/跨藍綠共享、冪等性帳本的原子鍵與 `OutcomeUnknown` 不自動重放、`CanonicalKeyV1` 版本化編碼、Windows `HostIdentity`/`SecretReference` 嚴格聯集、FetchXML 禁止呼叫端傳入、`OrganizationAdmissions` 集中設定、審計保留的原子預留機制、`LocalQueueCapacity`/`MaxDispatchEnvelopeBytes` 邊界、CE 8.2/9.1 用語的證據安全性,以及 Phase 0 覆蓋矩陣、CI 反退回閘門、ADR 前置條件、公平佇列等治理要求。這是一份經過多輪收斂、內部高度自洽的規格。以下為本輪新發現、先前review未提出的具體問題。

沒有發現 **Critical** 等級問題(即會導致安全/資料外洩/資源洩漏零容忍紅線被違反的缺陷)。

---

## Warning 🟡

### W1. `OrganizationAdmissionKey` 未涵蓋「不同環境指向同一實體 Dynamics 組織」的風險
- **檔案/章節**: `design.md` §7.1 (line 555):`OrganizationAdmissionKey = tuple(deploymentEnvironment, expectedOrganizationId)`
- **問題**: 此鍵刻意排除 profile generation、endpoint、產品身分等,只用「部署環境 + 預期組織 ID」界定共享的准入預算,理由是讓同一環境下的藍綠/金絲雀版本共用預算。但它同時假設「不同 `deploymentEnvironment`(如 staging vs production)必定對應到不同的實體 CE 組織」。CE on-premises 常見情境是 UAT/staging 與 production 共用同一台實體伺服器上的組織(或設定錯誤導致 staging 指向了與 production 相同的 `ExpectedOrganizationId`)。一旦發生,兩個環境會各自建立獨立的 `OrganizationAdmissions` 預算計畫並各自要求 `RuntimeHostSlotLease`,兩者互不知情地同時對「同一顆實際的 Dynamics 組織」送出流量,實際併發量會是兩個預算之和,超出該組織的服務保護上限——這正是本規格在別處(如 blue/green 共用同一 `OrganizationAdmissionKey`)極力避免的「預算被意外加倍」情境。
- **佐證**: 規格本身在其他地方(產品 JSON 的 Development guard,`design.md` §4.1)已經意識到「非正式環境誤連正式組織」是需要主動防範的真實風險,但該防護只做在產品層 JSON,沒有延伸到 Gateway 內部的 profile/`OrganizationAdmissions` 層。
- **建議修正**: 在 §6.1.1 / Phase 0 驗證清單中新增一條規則:當兩個不同 `deploymentEnvironment` 的 profile 解析出相同的 `ExpectedOrganizationId`(且/或相同的 `OrganizationBaseUri` origin),必須視為設定衝突並拒絕啟動,除非該組織的 `OrganizationAdmissions` 條目明確標示為「跨環境共享」並合併其預算計算,否則視為配置錯誤。

### W2. Gateway 與 Embedded host 的本地並發配額為齊頭式均分,未反映負載型態差異
- **檔案/章節**: `design.md` §7.2.1、§6.1.1;`docs/.../2026-07-23-dynamics-access-gateway-design.md` 規則 9
- **問題**: `LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumRuntimeHosts)` 對所有 host(不論是服務 5–10 個產品的共用 Gateway replica,或只服務單一產品的 Embedded host)套用同一個上限。文件中的「公平性(fairness)」機制(deficit/weighted queue、per-workload cap)只作用於「同一 host 內、依 workload 的佇列排程」,並未對「Gateway host vs Embedded host 應獲得的本地並發配額」做角色加權。隨著更多產品選擇 Embedded 模式、`MaximumRuntimeHosts` 增加,所有 host(包含承載多產品聚合流量的 Gateway)的本地配額會被同步壓縮,可能使 Gateway 在高產品數情境下相對吃緊,即使整體預算仍安全。這與 PRD 要求的「high performance using safe connection reuse and bounded concurrency」目標存在效率上的落差(非安全性問題,但屬於效能/可擴充性設計缺口)。
- **建議修正**: 在 §7.2.1 或 `OrganizationAdmissions` schema 中新增可選的「per-host-role 權重」(例如 Gateway 權重 > Embedded 權重的加權配額分配),或至少明確記錄「目前選擇齊頭式均分是刻意的保守簡化,待有真實負載資料前不做角色加權」,避免日後被誤讀為遺漏。

### W3. Embedded host 的簽章/中央註冊表驗證,未定義「驗證來源不可用時」的失效行為
- **檔案/章節**: `design.md` §4.1 (line 214-218);`docs/.../design.md` line 100-106
- **問題**: 文件要求「Embedded mode must load a signed deployment manifest or verify … against the same central registry before resolving any secret, runtime, or queue slot」,但整份規格對「密鑰/租約/審計」等其他信任邊界都明確寫出「若無法驗證則 fail-closed / NotReady」的規則(例如 lease TTL 到期、審計儲存不可用、HMAC 驗證金鑰不可用等),唯獨對「簽章清單或中央註冊表在 Embedded 啟動時不可達」這個情境沒有明確落筆。若實作理解為「驗證失敗才拒絕,驗證來源不可達則略過驗證直接信任本地 JSON」,將直接破壞「product JSON is not the source of authorization truth」這條核心前提。
- **建議修正**: 在 §4.1 / Phase 1 明確補一句:「Embedded host 在簽章清單或中央註冊表不可達、逾時或驗證失敗時一律維持 NotReady/啟動失敗,不得回退為信任本地 `ProductProfileBinding`/`OrganizationAdmissionCoordinatorRef` 內容」,並將此情境加入 §11 測試矩陣(目前的 fault-injection 清單中沒有「registry/manifest 不可達」這一項)。

---

## Info 🟢

### I1. `SpeechMessage.Dynamics.Embedded` 在方案圖中的定位敘述不一致
- **檔案/章節**: `design.md` §3 的 ASCII 方案樹只列出 5 個專案,隨後文字寫「`Embedded` is added **beside** these projects」;但 `implement.md` Phase 1.1 與 `docs/.../design.md` 專案表格都把 Embedded 直接列為 `SpeechMessage.Dynamics.sln` 內的第 6 個專案。內容並無矛盾(兩者都同意 Embedded 屬於同一方案),純粹是 §3 的 ASCII 圖漏畫、文字用詞「beside」易被誤讀成「另一個獨立方案」。
- **建議**: 把 Embedded 加進 §3 的 ASCII 樹狀圖,與 `implement.md` Phase 1.1 的清單保持圖文一致,消除「另立方案」的誤解空間。

### I2. 「Discovery-service instance/release data」的取得方式未界定是否落在無 SDK 邊界內
- **檔案/章節**: `design.md` §6.2 point 1、§11.3;`docs/.../design.md` Compatibility 段
- **問題**: 規格多次提到「onboarding 需另外記錄 Discovery-service 的 instance/release 資料以證明確切 CE 版本」,但 Discovery Service 傳統上是 SOAP (`Discovery.svc`)介面。規格已明確排除連接器程式碼呼叫任何 SOAP/WCF,但沒有說明「操作人員在 onboarding 階段如何取得這份資料」——若透過內含 CRM SDK 的工具(如 XrmToolBox)取得,只要該工具不屬於本方案原始碼/專案圖,並不違反 §12.3 的「no project in the solution」掃描規則,但值得在文件中明確排除疑慮。
- **建議**: 在 §6.2 或 §12.3 補一句,說明此資料蒐集屬一次性、方案原始碼之外的人工作業,不得以任何形式引入方案內的 SDK 相依性。

### I3. 「RuntimeHostSlotLease 協調器」與「可選的分散式 permit limiter」用詞相近,易混淆
- **檔案/章節**: `design.md` §7.2.1 末段 vs §7.2.2
- **問題**: 兩個機制都用「coordinator/limiter」字眼描述,但性質不同——前者(`IRuntimeHostSlotCoordinator`)是強制性的 host 數量租約機制,失效會讓整個路徑 NotReady;後者(distributed permit limiter)是可選的精細請求級限流,失效時退回固定保守配額而非讓 host 下線。這個區分在文中其實已寫清楚,但因兩節都用「coordinator」稱呼,實作者快速讀過可能誤把兩者當同一元件的兩種狀態。
- **建議**: 建議統一為 `RuntimeHostSlotCoordinator`(強制)與 `OrganizationPermitLimiter`(選用)兩個明確不同名詞,並在 Mermaid 圖上分開標示,降低實作誤讀風險。

---

## 12 項審查問題逐項確認

1. **Gateway + 私有無 SDK 函式庫是否技術上合理,且 Library/透明代理替代方案是否有具體拒絕理由?** 合理。`design.md` §2.2 的選項比較表對 Library(A)、透明代理(B)、Gateway(C)、Embedded(D)各給出具體的、可驗證的拒絕/採用理由,非空泛假設。
2. **HTTP handler/憑證/OAuth cache/metadata cache/retry/queue/reload 是否以足夠的不可變 profile-generation key 隔離?** 是,`ProfileRuntimeKey`(§7.1)與獨立的 `OrganizationAdmissionKey` 分離「憑證隔離」與「跨代共享的容量控管」兩種需求,設計正確;但見 **W1** 的邊界情境。
3. **是否留有跨 profile 路由、密鑰外洩、呼叫端指定 endpoint/header/profile 逃逸、留存洩漏、過期執行期突變、不安全自動重試的路徑?** 未發現;唯一新的邊界情境是 W1/W3。
4. **CE 8.2/9.1 API 版本與驗證限制描述是否安全,不假設 on-prem client-secret / WS-Trust 回退?** 是,§6.3 明確排除該假設,IFD 僅作 feasibility gate。
5. **效能與高可用宣稱是否有界、可測、且與 Dynamics service protection 相容?** 是,目標值排除 CRM 伺服器自身執行時間,且明確聲明「不得以削弱隔離/生命週期防護換取效能」;但配額分配的角色加權缺口見 **W2**。
6. **遷移範圍、無 SDK 檢查、測試/發布閘門是否足夠具體?** 是,Phase 0-6 具體到 CI 掃描指令與排除清單(`no-sdk-source-roots.json`)。
7. **是否有矛盾、缺失的明確決策、或危險假設?** 見 W1(環境/組織隔離假設)、W3(Embedded 信任來源不可達時的行為未定義)。其餘決策已用 feasibility gate 方式妥善遞延,不需強行要求產品現在決定。
8. **Gateway/Embedded host-mode JSON 設計是否維持核心安全性、允許 VS 開發、禁止動態切換、並正確協調跨模式容量?** 是,`design.md` §4.1 完整覆蓋;但 Embedded 的信任驗證失效行為需補強(W3)。
9. **安全 warm-up 是否在不留存使用者資料的前提下加速冷啟動?** 是,warm-up 走與一般請求相同的 admission/audit/lease 路徑,且明確禁止以使用者/LINE ID/token 作為 key。
10. **是否要求 Organization-call coverage matrix?** 是,PRD、design、implement 三處皆要求,且 implement.md 已列出約 200 個 SDK 匯入來源作為遷移清單依據。
11. **CI/啟動閘門是否足以防止透過 `ICrmConnectionPool`、`ToolUtilityFactory`、CRM 套件或原始連線字串繞過?** 是,§12.2 point 8 與 Phase 0.2 列出具體禁止清單與例外(temporary-legacy matrix)。
12. **產品 JSON 信任邊界是否足夠明確,使可編輯 JSON 無法授予授權,且 Embedded 綁定須簽章/註冊表驗證?** 基本足夠,但如 W3 所述,驗證來源不可達時的 fail-closed 行為需要補上,否則此邊界在故障情境下可能被繞過。
13. **耐久協調器/帳本/審計 ADR、佇列公平演算法、容量所有權文件是否具體到可讓效能設計可測試?** 是,implement.md 明確要求 Phase 2 前完成 ADR,並列出需記錄的具體技術要素(store、clock source、fencing token 等)。

## 建議下一步
以上 W1–W3 建議在下一輪修訂中以最小文字補丁方式處理(各約 1–3 句話的規則補充),不需要重新設計既有章節結構;Info 項目屬純文件澄清,可視編輯資源決定是否處理。除此之外,本規格已達到可支撐 Phase 0 啟動的完整度。

---
SESSION_ID: e0c70b65-6b5a-4c50-833a-a480f653d26e

我已完整讀取四份規劃文件（prd.md、design.md、implement.md、docs/superpowers spec.md），並針對文件中引用的「現況證據」（Startup.cs 單例註冊、ChurchReport.csproj 的 HintPath）直接對照原始碼進行了獨立查證，而非僅信任文件敘述。以下為審查結論。

---

## 審查問題回覆（簡述）

1. **Gateway + 私有 no-SDK 庫是否技術上合理**：是。design.md §2.2 對「各產品自帶庫」與「透明代理」兩個替代方案都給出了具體、可驗證的拒絕理由（憑證/連線狀態多份拷貝的漂移風險；CRM schema 洩漏與審計不可判定），不是空泛主張。
2. **隔離鍵是否足夠**：是。`ProfileRuntimeKey = profileId + configurationGeneration + apiVersion + origin + authMode + secretVersionFingerprint`（design.md §7.1）涵蓋了 HTTP handler/HttpClient、Windows 憑證、OAuth token 快取、metadata 快取、重試/併發狀態、request context 六個面向的隔離規則，且明確禁止任何 static/global/AsyncLocal 共享狀態。
3. **跨 profile 路由、洩漏、殘留、危險自動重試**：規格已封閉這些路徑（Operation Registry 取代通用查詢面、憑證僅以 reference 解析、replace-and-drain 清理、寫入禁止盲目重試）。唯一實際缺口見下方 Warning。
4. **CE 8.2/9.1 驗證假設是否安全**：是。明確拒絕在 CE on-premises 宣稱 client-secret/ROPC/WS-Trust 回退，且以「未證實則封鎖該 profile」處理，符合「可安全遞延的決策才遞延」原則。
5. **效能/HA 聲明是否有界且可測試**：是，且明確要求以真實 8.2/9.1 伺服器基準驗證後才能定案 SLO，未做無依據的絕對承諾。
6. **遷移範圍與 no-SDK 閘門是否具體**：是，並列出約 200 個 SDK 相關來源檔案與具體 API（Assign/SetState/ExecuteMultiple 等），沒有假裝這是單一 DLL 替換。
7. **矛盾/遺漏/危險假設**：見下方 Warning 與 Info。

---

## 🔴 Critical
無。核心零容忍隔離模型、no-SDK 邊界、機密處理與版本路由規則彼此一致且有具體實作與測試閘門支撐，未發現會直接違反使用者列出之「hard quality requirements」的設計缺陷。

---

## 🟡 Warning

### W1 — Idempotency Ledger 未定義副本間共享機制，與強制多副本拓撲及「絕不盲目重放寫入」承諾矛盾
**檔案/章節**：`design.md` §7.2（"profile runtime objects are process-local and disposable"）、§8.1、§10（idempotency ledger）；`docs/superpowers/specs/2026-07-23-dynamics-access-gateway-design.md` 非負規則第 4-7 條。

design.md §9.2 要求生產環境「至少兩個 Gateway 副本」，而 §4 明確聲明 profile runtime 物件是 process-local（不跨副本共享）。§8.1／§10 又依賴一個「product+profile+operation+idempotency-key ledger」來保證「A write is never blindly replayed」。文件全文未說明此 ledger 是否為分散式/持久化儲存，也未提及 sticky session 路由。若沿用 §4 的 process-local 假設，當客戶端重試請求被負載平衡器路由到與第一次不同的副本時，第二個副本完全不知道第一次嘗試已存在，等同於在多副本拓撲下「盲目重放寫入」——直接牴觸文件自己宣稱的寫入安全保證。

**修正建議**：在 design.md §7.2 或 §10 明確補一條規則：idempotency ledger 必須是跨副本可見的（例如共享儲存，或明確採用 sticky session 並說明副本失效時的冪等性風險與降級行為），並將此列入 §11 驗證章節的多副本測試項目。

### W2 — `MaximumGatewayReplicas` 缺乏與實際部署副本數綁定的技術強制機制
**檔案/章節**：`design.md` §7.2.1；`docs/superpowers/specs/...design.md` 非負規則第 7 條。

`LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumGatewayReplicas)` 的安全性完全建立在「實際運行副本數 ≤ 設定值」這個假設上。文件僅以文字要求「must not autoscale beyond MaximumGatewayReplicas without recalculating」，但未描述任何技術執行點（例如：啟動時向編排平台查詢實際副本數並在超出時 fail-closed、或將 HPA/副本數上限與此設定值綁定的部署規則）。若維運人員因與 CRM 無關的理由（CPU/記憶體）調高副本數，各副本仍各自套用舊的 `LocalMaxInFlight`，加總後即可能超出 `AggregateMaxInFlight`，在分散式限制器同時失效的情況下就會超出 Dynamics 服務保護預算——這正是文件自己定義的「zero-tolerance」風險之一（memory/resource 之外的服務保護違規）。

**修正建議**：在 §7.2.1 補充具體強制手段，例如副本啟動時自我核對實際副本數並於超標時降級/告警並限流，而不是只依賴書面約束。

### W3 — Linux 容器部署與 Windows/IWA 驗證的可行性未列為 Phase 0 硬性關卡
**檔案/章節**：`implement.md` Preconditions 與 Phase 0。

規格要求「Windows/IWA for CE on-premises AD deployments」並要「Validate hosting OS and network/IWA support against the real target」，但 Preconditions 只列出要確認 Windows/AD FS 的「可行性」，未把「Gateway 實際部署平台（若為 Linux 容器）是否能原生支援 IWA/Kerberos（keytab 或 gMSA）」列為明確的 Phase 0 硬性可行性關卡。這是一個常見的實務阻礙點，若延後才發現會影響整個 Windows-profile 的路線。

**修正建議**：在 Preconditions 或 Phase 0 增列一項：確認 Gateway 部署目標 OS/容器平台與 Windows/IWA 驗證機制（keytab、gMSA 或必須改用 Windows 主機部署）的相容性，未通過則該 profile 的 Windows 驗證模式視為不可行。

### W4 — PRD/Design/Spec 所指的「禁止 DLL 目錄」與程式碼中實際引用路徑不一致
**檔案/章節**：`prd.md`「SDK-removal end state」段（"D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL"）；`design.md` §12.3；`docs/superpowers/specs/...design.md`「Final dependency rule」段。

已直接查證程式碼：`SpeechMessageProducts.ChurchReport.csproj:109` 的 HintPath 為
```
..\..\..\..\DevExpressDevExtreme-23.1.5版本\響應式\主要版本\ChurchReport.RazorPages\Dynamics 365 SDK DLL\Microsoft.CrmSdk.CoreAssemblies.9.0.2.52\lib\net462\Microsoft.Crm.Sdk.Proxy.dll
```
此相對路徑解析後落在一個完全不同的 `DevExpressDevExtreme-23.1.5版本` 目錄樹下，並不在文件反覆引用的絕對路徑 `D:\音訊科技產品\系統平台\Dynamics 365 SDK DLL` 之下。目前 design.md §12.3 的 `rg` 掃描是用資料夾葉節點名稱字串比對（"Dynamics 365 SDK DLL"），所以功能上仍能抓到這個實際參照——閘門本身不會失效；但文件敘述的「禁止目錄」與程式碼實際位置不符，未來若有人依文件字面路徑做目錄存在性檢查或人工稽核，會找不到真正的違規來源，造成誤導。

**修正建議**：在 prd.md 與 design.md 中改用「任何路徑中包含 `Dynamics 365 SDK DLL` 資料夾名稱的參照」這種以字串特徵而非絕對路徑為準的描述，或直接註明目前唯一已知違規位於 `SpeechMessageProducts.ChurchReport.csproj:109`（相對路徑指向 DevExpressDevExtreme 樹），避免與使用者提供的絕對路徑混淆。

---

## 🟢 Info

### I1 — Idempotency ledger 未明確納入資料分類/保留政策
**檔案/章節**：`design.md` §10。
§9.3 對 telemetry/audit 有清楚的「禁止儲存原始 body/token/PII-rich URL」與保留期規則，但 idempotency ledger 儲存「request fingerprint and result」時未同樣被要求套用資料分類與保留規則。建議在 §10 明確聲明 ledger 內容需比照 §9.3 的敏感資料處理標準。

### I2 — 範例設定中 `MaxConnectionsPerServer`（8）小於 `LocalMaxInFlight`（12）
**檔案/章節**：`design.md` §6.1 JSON 範例。
若非刻意作為 socket 層的額外背壓機制，此差距未被說明，可能讓效能量測（§10 的 p95/p99 目標）混入 handler 層排隊延遲。建議加一句話說明此為刻意分層背壓，或使兩者的預設範例值一致以避免誤讀。

### I3 — no-SDK 掃描 glob 未涵蓋 `packages.config` / `*.vbproj` / `*.fsproj`
**檔案/章節**：`design.md` §12.3、`implement.md`「Validation commands」。
已查證目前 repo 中與 CRM SDK 相關的參照（`ToolUtility.Tests.csproj`、`ChurchReport.csproj`）皆為 SDK-style `PackageReference`/`HintPath`，現有 `*.csproj` glob 足以涵蓋，此項不影響目前遷移範圍。但作為未來防禦深度建議，可將 `packages.config` 一併納入掃描 glob，避免舊式專案格式的殘留參照被遺漏。

---

## 總結
四份文件彼此一致，且對「集中化是否必要」「隔離鍵是否足夠」「版本路由是否安全」等核心硬性需求都給出了有證據支撐、可測試的答案，沒有發現會推翻整體架構方向的 Critical 問題。四項 Warning 集中在「多副本拓撲下的狀態一致性」（W1 idempotency ledger、W2 aggregate 併發預算）與「部署可行性/文件精確度」（W3 容器化 IWA、W4 DLL 路徑不符）；建議在進入 implement.md Phase 1 之前，於 design.md 補上 W1、W2 的具體技術強制手段說明，並將 W3 列為 Phase 0 硬性關卡，其餘三項可視為進入實作階段時一併修正的文件精確度問題。

---
SESSION_ID: 68c20ee0-ce6b-4006-b761-b5e228a4aa19

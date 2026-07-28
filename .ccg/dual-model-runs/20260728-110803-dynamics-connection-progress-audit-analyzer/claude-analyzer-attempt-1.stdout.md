# Dynamics 365 連線分割（no-SDK Gateway）進度稽核報告

*(唯讀稽核；未修改任何檔案。分支：`1.0.0.3.Gateway&Embedded.Worktree`，HEAD 基於已合併的 isolate-connector 成果 `f9e544e0` + 後續 CCG 封存/日誌提交)*

---

## 一、結論摘要（Executive Conclusion）

規劃文件（PRD/design/implement plan）品質**優異且經雙模型嚴格審查**，達到近乎生產級的嚴謹度。但**實際落地進度落後於文件的野心**：Phase 0–2 已完成並有測試佐證，Phase 3 已完成大部分程式碼但**卡在一個外部、非程式問題的硬阻塞**——`sunnyvalechback`（CE 9.1 IFD）的 ADFS OAuth 尚未由 ADFS 管理員完成用戶端註冊，導致 `ID3242` 權杖驗證失敗，Package 1 讀取路徑（`Package01FeeReadsEnabled`）**仍為 `false`**。Phase 4（soak/fault/perf 驗證）**完全尚未開始**，Phase 5（strangler 遷移）、Phase 6（SDK 移除）均未啟動。

三個「階段」概念不應混為一談：

| 概念 | 狀態 |
|---|---|
| Trellis 工作流階段 | `.trellis` task.json `status=in_progress`；`.ccg` task.json `currentPhase="implementation"` |
| implement.md 任務內 Phase 0–6 | **實際落在 Phase 3 中段，被外部 ADFS 阻塞**；Phase 4–6 尚未開始 |
| design.md 架構落地階段（12.2） | Foundation 完成、Gateway/控制平面部分完成（單機/記憶體版）、Prove 未開始、遷移/移除未開始 |

**這是本次稽核最重要的單一事實**：文件宣稱的「規劃完成、實作進行中」是準確的，但若只看 `.ccg/tasks/dynamics-connection-compatibility/review.md`，會得到「尚未開始正式實作」的**過時且錯誤**印象（見第四節）。

---

## 二、階段對照表

| Phase (implement.md) | 對應 design.md 12.2 rollout | 狀態 | 證據 |
|---|---|---|---|
| Phase 0 基線盤點 | Foundation 前置 | **大致完成**（owner 已 accept） | 70/~200 個 SDK 呼叫點已 normalize（`phase0-organization-call-matrix.json`）；scanner report-only，1072 筆歷史命中 |
| Phase 1 專案骨架 | Foundation | **完成** | 6 個新專案已建於 `SpeechMessageProducts.sln`；邊界測試存在 |
| Phase 2 Profile runtime + admission | Gateway/控制平面 | **完成（單機版）** | `OrganizationAdmissionManager`、`CanonicalOrganizationCapacityKey` 已實作；`InMemoryRuntimeHostSlotCoordinator` 明確標注 `IsDurable=false`，僅供單機/開發用 |
| Phase 3 Gateway/Embedded 政策 + 受控操作 | Gateway/控制平面 | **部分完成，被外部阻塞** | Tier A–D 讀取路徑（fee/stor/poll/QR）已接線，`Package01FeeReadsEnabled=false`；ADFS OAuth 卡在 ClientId 未在 `adfsdev91` 註冊 |
| Phase 4 上線前驗證（soak/fault/perf/5-workload 併發隔離） | Prove | **未開始** | repo 內無對應測試證據；implement.md 明訂為任何消費者遷移前的硬性關卡 |
| Phase 5 Strangler 遷移 | 首個消費者/逐產品遷移 | **未開始** | Flag 預設關閉，未有任何生產流量切換紀錄 |
| Phase 6 SDK 最終移除 | Removal/Enforcement | **未開始** | `PowerPlatform.Dataverse.Client` 仍在方案中；scanner 仍為 report-only |

**真實現況一句話**：專案處於 **implement.md Phase 3 中段**，且在能進入 Phase 4 之前，還有一個文件本身承認「不能靠程式碼自行發明」的外部管理動作（ADFS 用戶端註冊）擋著。

---

## 三、SPEC/PRD/Design/實作計畫品質評估

- **完整性極高**：PRD 含明確驗收條件、design.md 1401 行涵蓋拓撲、資料流、設定驗證、runtime pool、傳輸相容性、安全/可用性/監控、效能、驗證策略、遷移邊界（12 大節）。implement.md 精確定義 Phase 0–6、CI gate matrix、回滾點。
- **審查紀律良好**：`.ccg/tasks/dynamics-connection-compatibility/review.md` 記錄 6 輪 Gemini+Claude 雙模型審查，最終無 Critical、無殘留 Warning，兩模型皆建議 PASS。這是本專案在**規劃階段**的真正強項。
- **可追溯性佳**：Phase 0–3 每個 checkpoint 都有獨立 verification/`.md` 檔與具體 `dotnet test` 輸出佐證，優於一般專案的「口頭完成」。
- **風險**：文件量體極大、語氣充滿「zero-tolerance」等最高規格詞彙，但**部分關鍵保證（如 durable coordinator、Phase 4 驗證）在文件裡被清楚列為「尚未完成」的非目標**——這是誠實揭露，但也代表文件的完備度與程式碼的完備度之間存在明顯落差，讀者若只看 PRD/design 摘要語氣，容易高估落地成熟度。

---

## 四、實作進度評估（依 Phase 0–6，程式碼與測試證據）

| Phase | 完成度 | 具體證據 |
|---|---|---|
| 0 | 完成（規劃層面），但**呼叫點盤點僅 70/~200（約 35%）已 normalize** | `normalizedCallSites.length = 70`；design.md 12.1 估計約 200 個含 SDK 的來源檔 |
| 1 | 完成 | `SpeechMessage.Dynamics.{Abstractions,WebApi,Gateway,Embedded,Tests,SmokeTests}` 皆存在並可建置 |
| 2 | 功能完成，**但明確非目標：無 durable 多機協調器** | 僅 `InMemoryRuntimeHostSlotCoordinator`（單機/dev-only），`IRuntimeHostSlotCoordinator` 介面存在但無正式後端實作 |
| 3 | 程式碼完成（Tier A–D 讀取接線、AdfsOAuth 傳輸層、審計/遙測骨架），**功能性阻塞於外部 ADFS** | `phase3-tier-a-ifd-auth-blocker.md`：password grant 被 ADFS 拒絕（`unsupported_grant_type`）；`authorization_code` 因 RP 不符失敗；`Package01FeeReadsEnabled` 現況仍為 `false`（本次稽核直接讀取 `appsettings.json` 確認） |
| 4 | **完全未開始** | repo 中無 soak/leak/fault-injection/5-workload 併發隔離測試證據；implement.md 將此列為進入 Phase 5 前的強制關卡 |
| 5 | **未開始** | 無任何生產流量切換紀錄，flag 恆為 false |
| 6 | **未開始** | `PowerPlatform.Dataverse.Client` 仍在 `SpeechMessageProducts.sln`；scanner 仍 report-only（1072 筆命中） |

---

## 五、文件/可追溯性問題（過時或矛盾）

1. **`.ccg/tasks/dynamics-connection-compatibility/review.md` 已嚴重過時**：內容停留在「規劃已備審、尚未開始正式實作」的 SPEC 審查階段結論，但實際上 Phase 0–3 已有 9 次程式碼提交、47 個單元測試、4 個煙霧測試通過。若他人（或未來的 agent）只讀此檔，會得出與現況相反的結論。
2. **`.ccg/tasks/dynamics-connection-compatibility/task.json` 的 `branch` 欄位仍指向舊的 `1.0.0.2.IsolateConnector.Worktree`**，與目前實際所在分支 `1.0.0.3.Gateway&Embedded.Worktree` 不符；`currentPhase="implementation"` 過於籠統，未反映「卡在 ADFS」這個更精確的子狀態。
3. **`.trellis` task.json 的 `notes` 欄位是三份狀態文件中最新、最準確的**（明確寫出「Package01 still false. Next: operator Invoke-AdfsTokenProbe.ps1 then fee parity 56」），但其 `base_branch` 欄位同樣仍是舊 worktree 名稱。
4. **目標組織/版本在 Phase 3 過程中發生真實變更**（`jesus` CE 8.2 → `sunnyvalechback` CE 9.1 IFD），這在 `phase3-*.md` 系列文件內部有前後修正紀錄（非隱瞞，但需跨檔案比對日期才能拼出正確現況），`assessment.md` 的相容性矩陣仍以通用 8.2/9.1 描述，未反映目前實際卡關目標為 `sunnyvalechback`。

**影響**：目前只有 `.trellis` 的 `task.json.notes`、`phase3-tier-a-ifd-auth-blocker.md`、`phase3-live-smoke-attempt.md` 三份文件真正反映「現在卡在哪裡」；`.ccg` 端的兩份狀態檔案已對不上現況，建議列為技術債更新項目。

---

## 六、已驗證的技術/發布阻塞（Critical / Warning / Info）

### Critical

1. **ADFS/IFD 即時驗證未過** — `sunnyvalechback`（CE 9.1 IFD）之 OAuth：password grant 被 ADFS 拒絕、`authorization_code` 因取樣用 ClientId（`2ad88395-b77d-4561-9441-d0e40824f9bc`）未在 `adfsdev91` 正確註冊為原生用戶端而在 Relying Party 層失敗，尚無 refresh token。**這是純外部管理動作阻塞，程式碼無法自行解決**（文件本身也如此聲明）。`Package01FeeReadsEnabled` 因此鎖在 `false`，本次稽核直接讀取現行 `appsettings.json` 確認無誤。
2. **Durable 多機協調器未實作** — `IRuntimeHostSlotCoordinator` 僅有 `InMemoryRuntimeHostSlotCoordinator`（非 durable、單機/dev-only）。implement.md 明訂此為 Phase 2 開始前必須完成 ADR、Phase 4 前必須完成容錯測試的硬性前提；目前**連 ADR 選型都尚未落地**，任何多副本/HA 部署皆不具備條件。
3. **Phase 4 驗證關卡完全未執行** — soak/GC/handle/socket 洩漏測試、fault injection（401/429/503/timeout/DNS reset）、五個工作負載併發 fake-server 隔離測試、FetchXML/OData 惡意注入測試等，在 repo 中查無執行證據。這是 implement.md「Review gates」明訂的、進入 Phase 5 前不可略過的關卡，目前狀態等同**尚未取得任何生產就緒的驗證證據**，與 ADFS 問題是否解決無關——即便 ADFS 修好，Phase 4 仍是獨立的強制阻塞。
4. **全方案基線債務掩蓋回歸偵測能力** — `ToolUtility.Tests` 因 `ToolUtility`(net10.0) 與測試專案(net8.0) 版本不符而**完全無法還原/建置**（本次重新驗證確認仍然如此）；`ChurchReport.MemberInfo.Tests` 22 個失敗 + `LineMessagingProcessor.RichMenus.Tests` 1 個失敗，合計 23 個基線失敗持續存在。在舊有 SOAP 路徑仍是生產預設路徑的遷移期間，這代表**傳統路徑本身的回歸網並不完整**。

### Warning

1. **SDK 移除掃描仍為 report-only**，尚未升級為失敗關卡；repo 全域仍有 1072 筆歷史 SDK 命中（Phase 0 統計）。Phase 6 距離現況仍相當遠。
2. **呼叫點盤點覆蓋率僅約 35%**（70/~200），意味著在規劃 Tier B–D（stor/present-record/付款寫入路徑）之前，仍有大量正規化工作待完成，目前僅 Tier A（fee 讀取）具備遷移就緒度。
3. **`appsettings.json` 仍含多組明文第三方密鑰**（LINE Channel Access Token、LinePay/Sinopac/MyPay/TSPG 金鑰）雖非本次 Dynamics Gateway 稽核範圍核心，但與 `DynamicsAccess` 設定同檔並存；PRD 對 CRM 密碼已建立「零容忍明文」標準（`CrmConnection:Password` 已正確改為 User Secrets 參照），同一檔案的其他金鑰尚未套用相同標準，屬於一致性缺口。
4. **`.ccg` 端兩份狀態檔（`review.md`、`task.json`）已與現況脫節**（詳見第四節），存在誤導後續協作者/agent 的風險。
5. **NU1903 高風險依賴警示**（`System.Security.Cryptography.Xml` 10.0.9，經由 `ToolUtility`/`PowerPlatform.Dataverse.Client`）持續存在，非本次遷移引入，但尚無修復排程。

### Info

- 規劃階段的雙模型（Gemini+Claude）審查紀律良好，且延續到 Phase 3 的 `sunnyvalechback` ID3242 診斷（`.ccg/dual-model-runs/sunnyvalechback-91-id3242-*`），具備良好可追溯性。
- 回滾機制設計簡單可靠：單一 feature flag `Package01FeeReadsEnabled`，關閉即完全回退至舊 SOAP 路徑，未變更寫入路徑，風險可控。
- 舊有合併驗證（`f9e544e0`）與本次重新執行結果**完全一致**：`SpeechMessage.Dynamics.Tests` 47/47 通過、`SpeechMessage.Dynamics.SmokeTests` 4/4 通過（live CRM 停用）、ChurchReport/Gateway/ProductClient Release 建置通過；全方案 23 筆基線失敗與 `ToolUtility.Tests` 還原失敗經本次獨立重跑**再次確認未變動**，合併後無新增回歸。

---

## 七、優先順序建議（Next Steps）

| 優先級 | 行動 | 理由 |
|---|---|---|
| P0 | 請 ADFS 管理員在 `adfsdev91` 上為 ChurchReport 註冊原生/公開用戶端（含 redirect URI）並授權 CRM/IFD 資源，取代目前未註冊的取樣 ClientId | 這是唯一能解除 Package 1 上線的硬阻塞，且非程式碼可解決 |
| P0 | ADFS 修復後，完成 Tier A 對照測試（`Package01FeeReadsEnabled=true`，同一聯絡人/區間應回傳 56 筆），並完成一次回滾演練 | 文件已備妥完整 checklist（`phase3-tier-a-enablement-checklist.md`），僅缺最後一步驗證 |
| P1 | 在啟用任何非本機環境的 Package01 之前，執行 implement.md Phase 4 的 soak/fault-injection/五負載併發隔離測試 | 這是文件自訂的強制關卡，與 ADFS 問題彼此獨立，目前完全空白 |
| P1 | 對 durable `IRuntimeHostSlotCoordinator` 做出明確決策：選型並落地 ADR，或明確記錄「僅限單機部署」作為暫時限制並知會利害關係人 | 避免在未決策情況下被誤用於多機生產環境 |
| P2 | 更新 `.ccg/tasks/dynamics-connection-compatibility/{task.json, review.md}` 使其反映目前分支與 Phase 3 卡點，避免狀態誤導 | 低成本、高可讀性收益 |
| P2 | 繼續 Organization-call 覆蓋矩陣正規化（現 70/~200），優先支援 Tier B 讀取範圍 | 為後續遷移包提供正規化基礎 |
| P3 | 修正 `ToolUtility.Tests` 目標框架（net8.0 → 對齊 net10.0）並著手清理 23 筆基線失敗 | 恢復舊路徑的回歸偵測能力 |
| P3 | 盤點並比照 `CrmConnection:Password` 的做法，將 `appsettings.json` 中其餘 LINE/金流明文金鑰移至 User Secrets 或機密管理 | 與專案自訂的零容忍密鑰標準保持一致 |

---

## 八、整體評等

| 面向 | 評等 | 說明 |
|---|---|---|
| 規劃/文件/審查紀律 | **A-** | 深度、完整度、雙模型審查證據皆屬上乘，且誠實揭露自身未達成項目 |
| Phase 0–2 落地與測試證據 | **B+** | 有測試佐證、有邊界稽核，唯覆蓋率與 durable 協調器仍是已知缺口 |
| Phase 3 現況（含外部依賴） | **C**（受外部因素拖累，非團隊執行力問題） | 程式碼就緒但卡在 ADFS 管理員動作 |
| Phase 4–6 / 生產就緒度 | **D（尚未開始）** | 沒有任何 soak/fault/perf 證據，SDK 移除仍遙遠 |
| **綜合評等** | **B- / C+**（規劃優秀，落地紮實但尚未接近可上線） | 下一個里程碑完全取決於一個非程式問題：ADFS 用戶端註冊 |

**一句話總結**：這是一個文件治理與測試紀律都相當出色的專案，目前唯一卡住整條 Phase 3→5 進度的並非程式碼問題，而是**一個需要 ADFS 管理員手動完成的外部註冊動作**；即便該動作立即完成，Phase 4 的驗證關卡仍完全空白，因此距離「可上線」仍有兩個獨立的關卡要過。

---
SESSION_ID: 7d1fc315-1fcc-461e-9836-f5881ff804e5

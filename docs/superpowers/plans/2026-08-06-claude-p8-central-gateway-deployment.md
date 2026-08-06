# [CLAUDE] P8 ChurchReport 雲端 Central Gateway 部署計畫

> **檔案標記：CLAUDE 系列第 3 份。** 由 Claude 撰寫。
> 總контракт在 `2026-08-06-claude-churchreport-master-plan.md`，**先讀它**。
> 不要修改非 CLAUDE 檔案（`2026-08-06-p6-p7-integrated-execution.md` 等由 Codex 擁有）。
>
> **For agentic workers:** REQUIRED SUB-SKILL: `superpowers:executing-plans`（inline 模式）。

**Goal:** 把已在 Lenovo Legion 驗收完成的 ChurchReport 部署到雲端機房，
透過 Central Gateway 連接 Dynamics 365 CE on-premises 8.2 或 9.1，成為第一個正式產品。

**Architecture:** 雲端 Central Gateway 集中的**不是連線，是管理責任**——
workload authentication、authorization、profile registry、secret reference resolution、
operation registry、retry／timeout／backpressure、audit／telemetry／health、
profile runtime generation、aggregate organization admission。
ChurchReport 只透過強型別 ProductClient 呼叫已註冊的 capability operation。

---

## ⛔ 啟動前置條件

**本計畫在下列條件全部成立前不得啟動：**

- [ ] P7.5 已封存
- [ ] `.trellis/tasks/08-05-gateway-purpose-and-positioning/p8-handoff.md` 已產出且內容完整
- [ ] ChurchReport 在 Lenovo Legion 已全量 Gateway 化並通過觀測窗
- [ ] 使用者提供**獨立的 Goal B 授權**（見第 8 節）

> P6／P7 的 Goal **不包含**本計畫。任何代理不得由 Goal A 自動進入 P8。

## 1. 為什麼 P8 需要獨立授權

P8 與 P6／P7 有本質差異：

| | P6／P7 | P8 |
|---|---|---|
| 環境 | Lenovo Legion 本機 | 雲端正式機房 |
| 可逆性 | 改壞了重跑就好 | 對外服務，影響真實使用者 |
| 資產 | 本機檔案 | DNS、TLS 憑證、雲端主機、正式 secret |
| 誰能操作 | 代理可自主 | **必須有使用者在場提供雲端存取與變更視窗** |

因此 P8 的每個階段都有明確的 operator 參與點，代理不得代為決定或代為取得雲端憑證。

## 2. 硬性部署約束（先確認，否則整個計畫要重來）

### 2.1 雲端主機必須是 Windows Server

CE 8.2／9.1 的 Official Worker 是 **.NET Framework 4.8（net48）**，Windows-only
（證據：`artifacts/dynamics-workers-p6.2/crm82/SpeechMessage.Dynamics.Crm82Worker.exe.config`
與 `Microsoft.Xrm.Tooling.Connector.dll`）。

**因此：**

- 雲端主機必須是 Windows Server，不能是 Linux 容器
- 若機房只提供 Linux，Worker 架構必須重新評估，**這會讓 P8 重新規劃**

### 2.2 IFD 是雲端能連上地端 CE 的關鍵

你的 CE 是 **IFD（Internet Facing Deployment）**，代表 CE 與 ADFS 已對外發佈。
這正是雲端 Gateway 能連回 CE 的前提——不需要 VPN 打洞。

**但必須驗證：**

- 雲端主機的出向網路可達 CE organization URL 與 ADFS home realm（兩者皆 HTTPS）
- CE／ADFS 端若有 IP allowlist，**雲端主機的出口 IP 必須先加入**
- ADFS 憑證鏈在雲端主機可信任
- 延遲與頻寬可接受（跨機房會比本機慢，效能基線要重測）

參考既有診斷資產：`docs/scripts/Invoke-AdfsTokenProbe.ps1`、
`docs/superpowers/plans/2026-08-01-crm-ifd-external-domain-diagnostic.md`。

### 2.3 Secret 儲存方式必須先決定

本機用 Windows Credential Manager（per-user）。雲端可行選項：

| 方案 | 條件 | 備註 |
|---|---|---|
| Credential Manager 於服務帳號下 | Windows VM、服務帳號可互動登入一次 | 與本機一致，改動最小 |
| 核准的 secret provider | 需 Gateway 支援對應 provider | 較適合正式環境，但要確認程式支援 |

**P8.0 必須明確選定其中一種並驗證，不得到 P8.2 才發現不支援。**

## 3. P8.0 — 雲端部署就緒

**Operator 參與點：使用者提供雲端主機存取與網路資訊。**

- [ ] **Step 1: 回答雲端環境問卷**

  1. 雲端主機作業系統與版本？（必須 Windows Server，見 §2.1）
  2. 雲端主機出口 IP？CE／ADFS 端是否需要加入 allowlist？
  3. ChurchReport 對外網域與 TLS 憑證來源？
  4. Central Gateway 是否與 ChurchReport 同機？（同機較簡單，分機需內部 TLS）
  5. Secret 儲存採 §2.3 哪一種？
  6. 正式變更視窗？回滾決策者是誰？
  7. 雲端是否已有既有 ChurchReport 執行中？（影響 aggregate capacity 與切換方式）

- [ ] **Step 2: 驗證雲端到 CE 的可達性**

  在雲端主機以 operator bridge script 驗證（唯讀、不寫入、不輸出 secret）：
  CE organization URL 可達、ADFS home realm 可達、憑證鏈可信、TLS 版本相容。

  任一不通即 **No-Go**，先解決網路／allowlist 再繼續。

- [ ] **Step 3: 確認部署包與 rollback package 可重現**

  以 P7.5 handoff 的 deployment package 在雲端主機還原，驗證雜湊一致。
  **rollback package 必須同時就位**，不能等出事再準備。

- [ ] **Step 4: 建立容量與資源基線**

  記錄雲端主機的 CPU、記憶體、handle、連線上限，作為 P8.4 比對基準。

  **缺任何一項即 No-Go。**

## 4. P8.1 — 主機、服務身分與 TLS

**Operator 參與點：使用者建立服務帳號與憑證。**

> ⛔ **禁止使用 `Administrator` 或任何人類帳號。**
> 本機 P6／P7 用 `LENOVO-LEGION\Administrator` 是開發便利，**不是部署範例**
> （master plan §3.2）。

- [ ] **Step 1: 建立最小權限服務身分**

  - ChurchReport workload identity
  - Gateway／Worker service identity
  - 兩者分離，各自最小權限
  - 皆為**非人類帳號**，不可互動登入使用

- [ ] **Step 2: 建立 secret 與 ACL**

  依 §2.3 選定方案建立 credential，ACL 限定只有 Gateway／Worker service identity 可讀。

  **若採 Credential Manager 方案：credential 必須建立在執行 Worker 的服務帳號下**
  （per-user 限制，master plan §3.2）。

- [ ] **Step 3: 建立並驗證 TLS**

  - 對外：ChurchReport 的公開 TLS 憑證
  - 對內：若 Gateway 與 ChurchReport 分機，內部通道也要 TLS
  - 驗證憑證鏈、到期日、自動更新機制

- [ ] **Step 4: 驗證未授權 workload 被拒絕**

  確認未授權 workload 在 **body parsing、Profile resolution 與 outbound work 之前**就被拒絕，
  而不是先做完工作才檢查。

## 5. P8.2 — Central Gateway 與 Worker 部署

- [ ] **Step 1: 以可重現部署包安裝服務**

  `ConnectionMode` 設為 `CentralGateway`。
  部署 Gateway 與 CE 8.2／9.1 Worker，驗證 executable 雜湊與 manifest 一致。

- [ ] **Step 2: 驗證啟動與生命週期**

  startup、health、ready、restart、drain、forced termination 全部驗過。
  確認 log／metric 已 sanitize——**不得洩漏 endpoint、OrganizationId、credential、token**。

- [ ] **Step 3: 驗證資源基線**

  process、pipe、handle 計數在 drain 後回到宣告基線。

- [ ] **Step 4: 設定啟動期驗證為 fail closed**

  CE／Connector／package-lock／profile 不相容或缺 secret reference 時，
  **在 host startup 期就拒絕**，不在 request path 嘗試修復或 fallback。

## 6. P8.3 — ChurchReport cutover

- [ ] **Step 1: 先做受控 smoke**

  在變更視窗內，先執行 allowlisted read-only operation 確認端到端可通。

- [ ] **Step 2: 通過 aggregate-capacity gate**

  若雲端與任何其他執行個體同時連到同一 Organization，
  必須共用 durable admission authority，或先 drain 舊路徑再啟用新路徑。

  **per-process in-memory coordinator 不構成跨主機容量保證。**

- [ ] **Step 3: 只變更 endpoint／routing**

  只改 ChurchReport 的 Central Gateway endpoint 與 deployment-owned routing。

  **同一次變更不得同時改** capability contract、Profile、ConnectorKind 或 CE version。
  一次只動一個變數，出事才知道是誰造成的。

- [ ] **Step 4: 逐步放量**

  從最小 tier 開始，觀察後再擴大。任何退步只回滾該 capability。

## 7. P8.4 — Live validation、監控、回滾與結案

- [ ] **Step 1: 取得完整 live evidence**

  功能正確性、p50／p95／p99、錯誤率、queue、permit、lease、connection、
  worker recycle、working set、handle、alert。

  與 P7 本機基線比對；跨機房延遲增加是預期的，但**必須量化並可接受**。

- [ ] **Step 2: 實際演練 rollback**

  **不是紙上流程，要真的跑一次。** 演練後確認服務恢復且資源回到基線。

- [ ] **Step 3: 確認監控與告警**

  健康檢查、錯誤率告警、資源告警、憑證到期告警都已設定且會真的通知到人。

- [ ] **Step 4: 通過觀測窗後結案**

  觀測窗通過才 commit／archive P8。

  結案輸出：部署 runbook、rollback runbook、監控儀表、已知限制清單。

---

## 8. Goal B 提示詞（P7.5 封存後才可使用）

```text
按照 Trellis Workflow，依照
docs/superpowers/plans/2026-08-06-claude-churchreport-master-plan.md 與
docs/superpowers/plans/2026-08-06-claude-p8-central-gateway-deployment.md，
執行 P8.0～P8.4：把已在 Lenovo Legion 驗收完成的 ChurchReport 部署到雲端機房，
透過 Central Gateway 連接 Dynamics 365 CE on-premises 8.2 或 9.1，成為第一個正式產品。

前置條件我確認已達成：P7.5 已封存，p8-handoff.md 已產出，
ChurchReport 本機已全量 Gateway 化並通過觀測窗。

這是 P8 的獨立授權。你可以建立 P8 Trellis parent 與 P8.0～P8.4 children，
規劃、實作、check、commit、archive 並逐階段銜接。

但下列事項必須先問我，不得自行決定或代為取得：
- 雲端主機存取憑證、DNS、TLS 憑證、正式 secret
- 正式變更視窗的時間
- 任何會影響線上使用者的切換動作

遵守 master plan 第 4 節執行契約。特別注意：
- 雲端服務身分禁止使用 Administrator 或任何人類帳號
- Worker 是 net48，雲端主機必須是 Windows Server
- P8.0 必須先驗證雲端到 CE／ADFS 的網路可達性與 IP allowlist
- rollback 必須實際演練，不是紙上流程

每個階段完成後回報證據摘要。遇到需要我提供雲端資源或做決定時，
寫清楚卡點與所需項目，然後停下來等我。
```

## 9. 已知風險

| 風險 | 影響 | 緩解 |
|---|---|---|
| 雲端機房只提供 Linux | Worker 是 net48，跑不了 | P8.0 Step 1 第 1 題先確認 |
| CE／ADFS 有 IP allowlist 未開 | 雲端連不上 CE | P8.0 Step 2 先驗證 |
| 跨機房延遲拉高 | 效能退步 | P8.4 量化比對，不可接受就回滾 |
| Secret provider 不支援 | P8.2 才發現要改架構 | §2.3 在 P8.0 先決定 |
| 雲端與本機同時連同一 Organization | 併發超出總容量 | P8.3 Step 2 aggregate-capacity gate |
| ADFS 憑證到期 | 服務中斷 | P8.4 Step 3 設到期告警 |

## 10. 完成定義

- ChurchReport 在雲端機房透過 Central Gateway 連上 CE 8.2 或 9.1 並正常服務
- 身分、TLS、secret ownership、監控、告警均已驗證
- **rollback 已實際演練過**
- 觀測窗通過
- 部署與 rollback runbook 已文件化

達成後，**ChurchReport 即成為第一個正式產品**。
第二、第三產品的 onboarding 另立獨立範圍，不在本計畫內。

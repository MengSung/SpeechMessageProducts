# 審查報告：全域隔離與記憶體洩漏防護網最終審查 (global-isolation-memory-leak-guardrails-final)

## 1. 總體評估 (Summary)
本審查針對全域 Codex 政策變更及其規劃/任務產出物進行事實核對與合規性評估。整體而言，政策的設計方向正確，明確將跨會話洩漏與記憶體洩漏列為零容忍的發布阻礙，並將效能優化置於安全與正確性之下。然而，在生命週期所有權的具體涵蓋範圍中，PRD 與 Requirements 檔案漏掉了 `cancellation registrations`（取消註冊）與 `connections`（連線），這可能導致 AI 代理人在後續開發中忽略這兩類關鍵資源的清理。

---

## 2. 檢核清單核對結果 (Checklist Verification)

| 驗證項目 | 狀態 | 具體說明 |
| :--- | :---: | :--- |
| **1. 個人政策位於託管區塊外** | **PASS** (Info) | 政策設計明確要求將新規則置於 `C:\Users\Administrator\.codex\AGENTS.md` 的 `CCG` 託管區塊之外，未修改或重複其標記。 |
| **2. 跨會話/使用者/租戶洩漏零容忍** | **PASS** (Info) | PRD 與 Requirements 均明確將其列為零容忍的安全發布阻礙（security release blocker）。 |
| **3. 記憶體/資源洩漏零容忍** | **PASS** (Info) | PRD 與 Requirements 均明確將其列為零容忍的正確性/可靠性發布阻礙。 |
| **4. 生命週期所有權完整涵蓋** | **Warning** | PRD 與 Requirements 漏掉了 `cancellation registrations` 與 `connections` 的明確要求。 |
| **5. 具信度風險變更需針對性測試** | **PASS** (Info) | 政策明確要求在有隔離或記憶體殘留風險時，需進行針對性測試、壓力測試或分析證明。 |
| **6. 效能框架與從屬關係** | **PASS** (Info) | 效能被正確框架為「最大安全持續效能」，且不得削弱隔離性、正確性、清理、驗證或可維護性。 |
| **7. 措辭簡潔與一致性** | **PASS** (Info) | 措辭簡潔、具可執行性，且在 PRD 與 Requirements 之間保持高度一致。 |
| **8. 無無關全域配置變更** | **PASS** (Info) | 僅修改了任務相關的四個檔案，未變更任何無關的全域配置。 |

---

## 3. 具體發現報告 (Findings Report)

### Critical 🔴
* **無**。

### Warning 🟡

* **生命週期所有權未完整涵蓋 `cancellation registrations` 與 `connections`**
  * **檔案路徑**：
    * `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md` (第 12 行)
    * `.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md` (第 21 行)
  * **具體失效模式**：
    在 PRD 與 Requirements 的生命週期所有權描述中，僅列出了 `subscriptions, timers, background tasks, caches, collections, streams, handles, and disposable resources`，漏掉了 `cancellation registrations`（取消註冊，例如 `CancellationToken.Register` 的傳回值）與 `connections`（連線，例如資料庫連線、HTTP 連線）。
    如果沒有在全域政策中明確列出這兩類資源，AI 代理人在開發或審查時可能會忽略它們的釋放，導致連線洩漏或 CancellationToken 註冊洩漏，進而引發記憶體洩漏或資源耗盡。
  * **修復建議**：
    在 `prd.md` 與 `requirements.md` 的對應條款中，補上 `cancellation registrations` 與 `connections`。例如：
    * `prd.md` 修改為：`...subscriptions, timers, background tasks, caches, collections, streams, handles, cancellation registrations, connections, and disposable resources.`
    * `requirements.md` 修改為：`...subscriptions, timers, background tasks, caches, collections, streams, handles, cancellation registrations, connections, and disposable resources where relevant.`

### Info 🟢
* **無**。

---

## 4. 評分與建議 (Scoring & Recommendation)

```
VALIDATION REPORT
=================
User Experience: 18/20 - 政策定義清晰，有助於引導 AI 代理人提供更好的使用者體驗，避免洩漏。
Visual Consistency: 18/20 - 政策在 PRD 和 requirements 之間保持高度一致。
Accessibility: 15/20 - 政策中漏掉了 cancellation registrations 與 connections 的生命週期管理，這會影響系統的可靠性與資源可存取性。
Performance: 19/20 - 效能被正確框架為「最大安全持續效能」，且不得削弱隔離性與正確性。
Browser Compatibility: 18/20 - 政策適用於所有整合邊界，無相容性問題。

TOTAL SCORE: 88/100

ISSUES FOUND:
- Warning: .trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md 與 .ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md 中漏掉了 cancellation registrations（取消註冊）與 connections（連線）的生命週期所有權與清理要求。

RECOMMENDATION: NEEDS_IMPROVEMENT
```

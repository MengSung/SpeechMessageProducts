## 審查報告：global-isolation-memory-leak-guardrails（規劃規格審查）

### 總體結論：**PASS**

規格文件（`.ccg/tasks/.../requirements.md` 與 `.trellis/tasks/.../prd.md`）在核心不變量（隔離零容忍、記憶體洩漏零容忍、生命週期清理範圍、風險驗證、規劃階段不涉及實作）上均已明確且互相一致，可進入下一階段。發現兩項文件間的措辞不一致，建議在撰寫最終 `AGENTS.md` 文字前收斂，但不構成阻擋發布的缺陷。

---

### 🔴 Critical
無。未發現違反零容忍原則、越權實作，或破壞既有配置的問題。

---

### 🟡 Warning

- **`.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md:11`** vs **`.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md:14`** — 「效能優化不可犧牲」清單在兩份來源文件中不一致，且都未涵蓋審查準則第 6 項要求的完整五項。
  - requirements.md 第 11 行：「must never weaken **isolation, correctness, cleanup, or verification**」— 缺少 *maintainability*。
  - prd.md 第 14 行：「subordinate to **security, correctness, cleanup, and maintainability**」— 缺少 *verification*，且用 *security* 而非審查準則所指的 *isolation*。
  - 審查準則要求同時涵蓋：isolation, correctness, cleanup, verification, maintainability（五項）。
  - 影響：若日後直接複製其中一份文字寫入全域 `AGENTS.md`，會遺漏 verification 或 maintainability 其中之一，弱化準則 6 的完整性。
  - 建議：在撰寫 `implement.md` 或最終 `AGENTS.md` 文案前，合併兩份清單為單一、涵蓋五項的完整句子。

---

### 🟢 Info

- **`.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md`（全篇）** — 未包含 prd.md 第 15 行「preserve all unrelated global and repository configuration」等價的明確保留條款，僅隱含於「Add content outside the existing CCG-managed block」。兩份文件目標一致，但 requirements.md 措辞較弱，建議之後同步補上明確保留語句，避免實作時遺漏。

- **`.ccg/tasks/global-isolation-memory-leak-guardrails/task.json:3`** vs **`.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/task.json:4`** — 標題文字略有差異（「Define global **session isolation** and memory leak guardrails」對比「Define global **session** and memory leak guardrails」），屬命名一致性小瑕疵，不影響規格內容。

- **`C:\Users\Administrator\.codex\AGENTS.md`（實際檔案，已直接讀取確認）** — 目前僅含 `<!-- CCG:START -->...<!-- CCG:END -->` 單一管理區塊，尚未寫入本任務描述的全域隔離／記憶體洩漏政策文字。這與任務狀態（`currentPhase: planning`、`status: planning`）一致，符合審查準則第 7 項「本審查不要求實作」的預期，不視為缺陷。

- **審查準則第 1 項（簡潔性）** — 兩份規格文件目前僅描述政策應「涵蓋什麼」，尚未提供將實際寫入 `AGENTS.md` 的具體草稿文字，因此「簡潔性」本身無法在此階段完全驗證。這對輕量級、PRD-only 任務屬合理做法（prd.md 第 27 行註記已說明），但建議實作階段產出最終文字時一併確認其簡潔性與是否置於管理區塊外。

- **準則 2–5 逐項核對**：
  - 準則 2（跨工作階段/使用者/租戶洩漏零容忍）：requirements.md:9、prd.md:10 文字幾乎逐字相同，符合。
  - 準則 3（記憶體洩漏零容忍）：requirements.md:10、prd.md:11 文字幾乎逐字相同，符合。
  - 準則 4（生命週期清理範圍：subscriptions/timers/background tasks/caches/collections/streams/handles/disposable resources）：requirements.md:21、prd.md:12 清單完全一致，符合。
  - 準則 5（風險導向驗證：targeted tests/stress checks/profiling）：requirements.md:23、prd.md:13 文字一致，符合。

---

### 核准前必要變更
無強制性變更；建議（非阻擋）事項：
1. 在進入實作前，統一 requirements.md 與 prd.md 對「效能優化不可犧牲」清單的措辞，確保涵蓋 isolation、correctness、cleanup、verification、maintainability 五項。
2. 統一兩份 task 標題文字（可選，純命名一致性）。

---

### 後端執行狀態
本後端（Claude Reviewer）已成功讀取全部五份指定檔案（含實際全域 `C:\Users\Administrator\.codex\AGENTS.md`），並完成獨立審查，產出此份可用之最終報告。本次審查為透過既有自我修復雙模型執行環境（run folder：`20260722-161115-global-isolation-memory-leak-guardrails-live-review-reviewer`）觸發的即時審查；Gemini 後端已於同一 run 中完成並輸出 PASS 結論（無 Critical/Warning，僅 2 項 Info），與本報告結論一致，但本報告額外發現 Gemini 未提出的第 6 項準則措辞不一致（Warning）。未執行任何檔案修改或無關變更。

---
SESSION_ID: 69410651-e9ed-4676-85ba-4a13090f19f5

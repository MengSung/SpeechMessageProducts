# 雙模型審查驗證報告 (Live Dual-Model Review Validation)

本報告針對任務 `global-isolation-memory-leak-guardrails` 進行最終的規劃與規格審查（planning-specification review）。

---

## 1. 總體評估與結論 (Summary & Verdict)

*   **總體結論 (Overall Verdict)**: **PASS**
*   **評估摘要**: 
    經審查，`.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md` 與 `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md` 中所定義的規劃規格，完全符合所有安全隔離、記憶體洩漏防護、生命週期清理、風險驗證以及效能優化順序等要求。此任務目前處於規劃階段（planning phase），因此尚未在 `AGENTS.md` 中進行實際的策略寫入，這符合「本審查不要求實作且不授權無關變更」的原則。

---

## 2. 審查發現 (Findings)

### Critical Findings (嚴重問題)
*   **無 (None)**: 未發現任何違反安全性、正確性或規劃原則的嚴重問題。

### Warning Findings (警告事項)
*   **無 (None)**: 未發現任何潛在風險或不一致之處。

### Info Findings (參考資訊)
*   **資訊 1: 規劃與需求文件高度一致**
    *   **檔案路徑**: 
        *   `.ccg/tasks/global-isolation-memory-leak-guardrails/requirements.md`
        *   `.trellis/tasks/07-22-global-isolation-memory-leak-guardrails/prd.md`
    *   **說明**: 兩份文件在核心指標（零容忍洩漏、記憶體洩漏阻擋、生命週期清理範圍、效能優化不妥協等）的定義上完全一致，確保了跨工具與跨框架的規範統一性。
*   **資訊 2: 實作狀態確認**
    *   **檔案路徑**: `AGENTS.md`
    *   **說明**: 當前 `AGENTS.md` 僅包含 Trellis 與 CCG 自我修復的基礎引導區塊，尚未寫入全域隔離與記憶體洩漏防護的具體政策。這符合規劃階段（planning phase）的預期，因為本審查僅針對規格進行評估，不要求亦不授權在此階段進行實作。

---

## 3. 核准前必要變更 (Required changes before user approval)

*   **無 (None)**: 規劃規格已達到核准標準，無須進行任何變更即可進入下一階段。

---

## 4. 審查準則逐項核對 (Verification of Criteria)

| 審查準則 (Review Criteria) | 狀態 | 檔案與文本佐證 / 說明 |
| :--- | :---: | :--- |
| **1. 全域 `AGENTS.md` 政策規劃**<br>政策需簡潔、持久，且置於託管區塊（managed blocks）之外。 | **符合** | `.trellis/.../prd.md` 第 15 行："Add the guidance outside the existing CCG-managed block and preserve all unrelated global and repository configuration." |
| **2. 跨工作階段/使用者/租戶洩漏零容忍**<br>明確定義為安全性發布阻擋器（security release blocker）。 | **符合** | `.ccg/.../requirements.md` 第 9 行："Cross-session, cross-user, and cross-tenant leakage is zero tolerance and a security release blocker." |
| **3. 記憶體洩漏零容忍**<br>明確定義為正確性與可靠性發布阻擋器（release blocker）。 | **符合** | `.ccg/.../requirements.md` 第 10 行："Memory leaks are zero tolerance and a correctness/reliability release blocker." |
| **4. 生命週期所有權與確定性清理**<br>涵蓋訂閱、計時器、背景任務、快取、集合、串流、控制代碼及可釋放資源。 | **符合** | `.trellis/.../prd.md` 第 12 行："Require lifecycle ownership and cleanup for relevant subscriptions, timers, background tasks, caches, collections, streams, handles, and disposable resources." |
| **5. 基於風險的驗證**<br>在存在洩漏或殘留風險時，要求針對性測試、壓力測試或效能分析。 | **符合** | `.ccg/.../requirements.md` 第 23 行："Require targeted tests, stress checks, or profiling when the changed code creates a credible leakage risk." |
| **6. 效能優化順序**<br>效能優化不能削弱隔離性、正確性、清理、驗證或可維護性。 | **符合** | `.trellis/.../prd.md` 第 14 行："Keep speed and memory-efficiency goals subordinate to security, correctness, cleanup, and maintainability." |
| **7. 審查範圍限制**<br>本審查不要求實作，且不授權無關的變更。 | **符合** | `.trellis/.../prd.md` 第 27 行："This is a lightweight instruction change; PRD-only planning is sufficient." 且本審查未對任何程式碼或設定檔進行寫入修改。 |

---

## 5. 後端執行狀態 (Backend Status)

*   **狀態確認**: 本後端（Gemini Reviewer）已成功讀取所有相關規劃文件與儲存庫檔案，並順利完成審查，產出此份可用之最終報告。

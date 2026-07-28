# Dynamics 365 連線拆分進度審查報告 (Dynamics Connection Progress Audit Review)

## 1. 整體評估 (Summary)

本審查針對 Dynamics 365 連線拆分（no-SDK Gateway）的進度稽核報告進行唯讀事實核對。整體而言，架構設計與規格定義非常嚴謹，但實際程式碼實作呈現非線性狀態，且尚未達到生產環境就緒標準。稽核報告準確反映了當前分支 `1.0.0.3.Gateway&Embedded.Worktree` 的真實狀態，未將未完成的階段誤報為已完成。

**審查判定 (Verdict): PASS** (稽核報告之事實陳述與儲存庫證據完全吻合)

---

## 2. 關鍵分類與事實核對 (Key Classifications & Fact Verification)

### 實作階段分類 (Phase Classification)
* **已完成的最高階段 (Highest Phase Completed)**: Phase 1 (基礎結構與專案拆分已完成，`SpeechMessage.Dynamics.*` 相關專案已建立並通過編譯)。Phase 2 部分完成（僅實作記憶體內協調器）。
* **已觸及的最高階段 (Highest Phase Touched)**: Phase 3 (閘道與嵌入式策略已接線，但因外部 ADFS 驗證問題而受阻)。
* **未啟動階段**: Phase 4 (Soak/Fault/Perf 驗證)、Phase 5 (Strangler 遷移)、Phase 6 (SDK 完整移除)。

### 關鍵技術狀態分類
* **ADFS/IFD 驗證**: 目前處於阻塞狀態。`sunnyvalechback` (CE 9.1 IFD) 的 ADFS OAuth 尚未完成 ClientId 註冊，導致 `ID3242` 權杖驗證失敗。
* **Gateway 工作負載驗證 (Workload Authentication)**: 目前 Gateway 僅為 Scaffolding 腳手架階段，直接從 Request Body 接收 `WorkloadSubjectId`，缺乏生產級的驗證中間件，**絕對不可**作為生產環境的共享 Gateway 啟用。
* **持久化協調器 (Durable Coordination)**: 目前僅實作了 `InMemoryRuntimeHostSlotCoordinator` (`IsDurable = false`)，缺乏跨主機的持久化協調器（如 Redis 實作），此為發布阻礙因素（Release Blocker）。
* **功能旗標狀態 (Feature-Flag State)**: `appsettings.json` 中的 `DynamicsAccess:Package01FeeReadsEnabled` 確實保持為預設值 `false`，確保未經驗證的 Web API 路徑不會在生產環境中被意外啟用。
* **SDK 移除進度**: `no-sdk-source-roots.json` 仍處於 `report-only` 模式，且專案中仍保留 `PowerPlatform.Dataverse.Client` 等舊版 SDK 依賴。

---

## 3. 缺陷與風險評級 (Findings & Risk Classification)

### Critical (嚴重)
1. **ADFS/IFD 外部驗證阻塞**: ClientId 未在 `adfsdev91` 註冊，導致 OAuth 流程無法取得有效 Access Token，此為阻礙後續 Phase 3/4 驗證的關鍵外部阻塞點。
2. **缺乏持久化協調器**: `IRuntimeHostSlotCoordinator` 僅有記憶體內實作，無法支援多主機/高可用性（HA）環境下的容量限制與租約管理。
3. **Gateway 缺乏生產級驗證**: 閘道端點直接信任請求體中的 `WorkloadSubjectId`，在未補齊驗證中間件前，嚴禁部署至生產環境。

### Warning (警告)
1. **基準測試失敗與框架不匹配 (Pre-existing Baseline Debt)**:
   * 儲存庫中存在 23 項測試失敗（`ChurchReport.MemberInfo.Tests` 22 項及 `RichMenus.Tests` 1 項），以及 `ToolUtility.Tests` (.NET 8.0) 與 `ToolUtility` (.NET 10.0) 的框架不匹配問題。
   * **核對確認**：此部分屬於合併前的既有技術債（Baseline Debt），非本次任務引入的迴歸缺陷（Regressions）。
2. **歷史憑證殘留**: 舊有文件中包含歷史憑證字串，已由擁有者確認將於後續進行金鑰輪替（Rotation），不視為本次程式碼整合的阻礙點。報告中未印出任何明文憑證。
3. **文件與追蹤漂移 (Documentation Drift)**:
   * `implement.md` 與部分 CCG 任務說明仍顯示「實作尚未開始」，與程式碼已部分實作的現況不符。
   * `implement.jsonl` 與 `check.jsonl` 仍包含預留位置（Placeholder）範例。
4. **BOM 剖析問題**: Trellis 與 CCG 的 `task.json` 檔案包含 UTF-8 BOM，導致部分剖析工具在讀取時會忽略該任務。

### Info (提示)
1. **專案排除與掃描器狀態**: `no-sdk-source-roots.json` 目前設定為 `report-only`，掃描器正常運作並回報 1,069 處 SDK 依賴，符合 Phase 0 的預期規劃。

---

## 4. 建議與後續行動 (Recommendations & Next Steps)
1. **優先解決 ADFS 註冊**: 協調 ADFS 管理員於 `adfsdev91` 註冊正式的 ClientId，以解除 Phase 3 驗證阻塞。
2. **更新追蹤文件**: 修正 `implement.md` 與 `task.json` 中的狀態漂移，反映真實的 Phase 3 部分完成進度。
3. **修復 BOM 字元**: 移除 `task.json` 中的 UTF-8 BOM，避免工具鏈剖析異常。
4. **逐步導入 Redis 協調器**: 針對多主機環境，依據 ADR 規劃實作持久化的 `IRuntimeHostSlotCoordinator`。

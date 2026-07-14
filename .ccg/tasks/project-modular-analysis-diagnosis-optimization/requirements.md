# 大型專案模組化分析、診斷與優化

## 目標

在 `1.0.0.1.EvenVersion` 工作樹中，依照使用者指定的模組順序，分階段完成：

1. 分析既有組織、責任、相依關係、契約、測試與文件。
2. 根據可追溯證據進行問題診斷與風險排序。
3. 僅在使用者批准後執行範圍明確、可驗證、可回復的優化。

## 約束

- 本任務為整體計畫的父任務。
- 各模組或可獨立驗收的交付項目應建立子任務。
- 分析、診斷、優化三個階段不得混合執行。
- 初始規劃階段不修改產品程式碼。
- 不得未經批准擴大模組或變更範圍。
- 所有重要結論必須附有檔案位置或其他專案內證據。

## 目前狀態

- 已建立 Trellis 與 CCG 父任務。
- 已完成 solution、專案參考、主站路徑、Views、靜態資產與測試盤點。
- 已建立 35 個唯一葉節點的模組邊界文件：
  `docs/project-modular-diagnostics/module-boundaries-and-optimization-map.md`。
- 已完成一輪唯讀 subagent 批判並依具體路徑證據修訂。
- 正在執行 CCG 外部 review；完成後等待使用者指定第一個分析模組。
- 已定義每個葉節點的巢狀 subagent 診斷、`issue.md` 排序與 CCG
  零信任逐 ISSUE 審核流程：
  `docs/project-modular-diagnostics/isolation-zone-diagnostic-workflow.md`。
- 已登錄 35 個固定工作區名稱；等待使用者批准後才建立並執行 F01A。
- 使用者已批准 CCG degraded fallback 作為正式結果；必須標示為
  `APPROVED_DEGRADED`，並由 Lead Codex 逐條重新核對保留 ISSUE。

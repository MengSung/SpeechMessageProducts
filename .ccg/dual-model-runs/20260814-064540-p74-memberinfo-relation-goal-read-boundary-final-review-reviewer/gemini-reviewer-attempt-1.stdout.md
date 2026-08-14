以下是針對 `ORG-CALL-00033` relation-goal source-only local design no-go 相關任務檔案及父任務元數據變更的最終審查報告：

# 審查報告：p74-memberinfo-relation-goal-read-boundary-final-review

## 1. 摘要 (Summary)
本次審查針對 `ORG-CALL-00033` 關係目標（relation-goal）的本機設計 no-go 進行最終確認。審查結果證實，該任務已正確定位為 **source-only local design no-go**，未包含任何運行時（runtime）程式碼變更、功能開關（feature gate）啟用或流量切換。所有安全邊界缺陷、授權限制及恢復前提條件皆已完整記錄於任務文件中。

---

## 2. 具體審查發現 (Findings)

### Critical (關鍵缺陷與限制)

1. **禁止 Church-only 局部遷移**
   * **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/source-audit.md`
   * **判定依據**：審查確認所有現有的 consumer 皆透過 `GetAccess` / `CanViewContactsBatch` 取得授權，且 Shepherd 流程會使用 saved-credential `ListManager` 載入。因此，不允許僅針對 Church 進行局部遷移，必須整體考量。

2. **排除無效的 Gateway 授權來源**
   * **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/prd.md`
   * **判定依據**：明確定義 Session、`InMemoryContext`、`ListManager`、ToolUtility、瀏覽器定位器（browser locator）、呼叫端設定檔/連接器/憑據/查詢（caller profile/connector/credential/query）或舊有的 `allowedIds` 皆**不可**作為 Gateway 的有效授權依據。

3. **確認無限制分頁與錯誤格式化缺陷**
   * **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/source-audit.md`
   * **判定依據**：no-go 評估中已明確包含 `RetrieveAllEntities(connection)` 的無限制分頁（unbounded paging）風險，以及將所有連線錯誤/逾時/部分讀取 blanket catch 後格式化為空字串（error-to-empty）導致 fail-closed 機制失效的設計缺陷。

4. **明確定義恢復前提條件**
   * **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/prd.md`
   * **判定依據**：在進行任何 relation-goal registry、Data8 或 ProductClient 的實作前，必須先建立一個全新的、不可變的、伺服器端衍生的（immutable server-derived）MemberInfo 授權邊界（authorization boundary）子任務作為恢復的前提。

---

### Warning (警告與注意事項)

1. **雙模型執行未完成記錄**
   * **檔案路徑**：`.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`
   * **判定依據**：`notes` 中已記錄 45 秒的 CCG architect 執行未產生可用的後端輸出，因此被標記為雙模型未完成（dual-model incomplete）並採用本機驗證。此限制已正確反映在任務元數據中。

2. **任務文件字元編碼異常**
   * **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/` 目錄下的 Markdown 檔案
   * **判定依據**：部分 Markdown 檔案中的中文字元存在編碼轉換異常（亂碼）。雖然不影響對技術細節與架構決策的理解，但建議在後續封存前，確保檔案編碼符合 UTF-8 無 BOM 且換行符號一致的規範。

---

### Info (參考資訊)

1. **無運行時與環境變更**
   * **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/implement.md`
   * **判定依據**：此任務為純設計/審查（source-only local design no-go），未修改任何 `.cs`、`.cshtml`、`.csproj`、設定檔、feature gate、matrix，亦未包含任何 CE 變更、流量切換、P7.5 移除或 P8 部署。

2. **父任務元數據更新**
   * **檔案路徑**：`.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`
   * **判定依據**：父任務的 `children` 列表已正確加入此子任務，且 `notes` 與 `nextAction` 已更新以反映 `ORG-CALL-00033` 的 no-go 審查結論。

---

## 3. 結論與建議 (Decision)
本任務之設計審查文件已完整且準確地記錄了 `ORG-CALL-00033` 的 no-go 原因與恢復路徑，且父任務元數據已同步更新。本審查予以 **通過 (PASS)**，可進行後續的 scope-only commit 與任務封存。

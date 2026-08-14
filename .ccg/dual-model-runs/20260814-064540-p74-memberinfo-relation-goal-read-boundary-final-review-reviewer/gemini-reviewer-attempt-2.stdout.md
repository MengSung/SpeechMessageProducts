# 審查報告：P7.4 ORG-CALL-00033 關係目標讀取邊界最終審查 (p74-memberinfo-relation-goal-read-boundary-final-review)

本審查針對 `ORG-CALL-00033` 關係目標（relation-goal）的 source-only 本地設計 no-go 任務文件及父任務元數據進行最終評估。

---

## 審查發現分類

### Critical (關鍵發現)

1. **拒絕 Church-only 局部遷移 (Church-only Partial Migration)**
   - **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/source-audit.md`
   - **判定依據**：審查確認所有現有的 consumer 皆透過 `GetAccess` 或 `CanViewContactsBatch` 取得授權，且 Shepherd 流程會使用 saved-credential `ListManager` 載入。因此，不允許僅針對 Church 進行局部遷移，此設計決策符合安全邊界要求。

2. **排除無效的 Gateway 授權來源**
   - **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/prd.md`
   - **判定依據**：文件已明確排除 Session、`InMemoryContext`、`ListManager`、`ToolUtility`、瀏覽器定位器（browser locator）、呼叫端設定檔/連接器/憑據/查詢（caller profile/connector/credential/query）或舊有的 `allowedIds` 作為 Gateway 的有效授權依據。

3. **確認無限制分頁與錯誤吞噬問題 (Unbounded Paging & Error-to-Empty)**
   - **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/source-audit.md`
   - **判定依據**：no-go 評估中已正確包含 `RetrieveAllEntities(connection)` 在 `MoreRecords` 為真時的無限制分頁迴圈問題，以及將所有連線錯誤/逾時/部分讀取 blanket catch 後格式化為空字串（error-to-empty）的設計缺陷。

4. **恢復條件要求 (Recovery Conditions)**
   - **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/prd.md`
   - **判定依據**：在進行任何 relation-goal registry、Data8 或 ProductClient 的開發前，必須先建立一個全新的、不可變的、伺服器端衍生的（immutable server-derived）MemberInfo 授權邊界（authorization boundary）作為恢復的前提條件。

---

### Warning (警告)

1. **雙模型執行未完成記錄 (Dual-Model Incomplete)**
   - **檔案路徑**：`.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`
   - **判定依據**：記錄中指出 CCG architect 執行未產生可用的後端輸出，因此被記錄為雙模型未完成（dual-model incomplete），並採用本機驗證。此為流程執行上的限制，但已正確記錄於 task metadata 中。

2. **文件編碼異常**
   - **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/` 目錄下的多個 Markdown 檔案
   - **判定依據**：檔案內容中的中文字元存在編碼轉換異常（亂碼），雖然不影響對技術細節與架構決策的理解，但建議在後續封存或提交前，確保檔案編碼符合 UTF-8 無 BOM 且換行符號一致的規範。

---

### Info (一般資訊)

1. **無運行時變更 (No Runtime Changes)**
   - **檔案路徑**：`.trellis/tasks/08-14-08-14-p74-memberinfo-relation-goal-read-boundary/implement.md`
   - **判定依據**：此任務為純設計/審查（source-only local design no-go），未修改任何 `.cs`、`.cshtml`、`.csproj`、設定檔、feature gate、matrix，亦未包含任何 CE 變更、流量切換、P7.5 移除或 P8 部署。

2. **父任務元數據更新**
   - **檔案路徑**：`.trellis/tasks/08-12-churchreport-productclient-cutover/task.json`
   - **判定依據**：父任務的 `children` 列表已正確加入此子任務，且 `notes` 與 `nextAction` 已更新以反映 `ORG-CALL-00033` 的 no-go 審查結論。

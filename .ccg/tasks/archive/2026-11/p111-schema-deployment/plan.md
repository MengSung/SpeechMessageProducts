# P1-11 結構佈署 Implementation Plan

> 執行：superpowers:executing-plans；主會話inline。依既有主P1核准設計自主落地，不派實作／檢查子代理。

**Goal:** 完整落地正式P1-11的附錄A清單、只新增metadata引擎與CLI，取得離線及安全唯讀實機證據。
**Architecture:** 新Schema子模組使用既有IOrganizationService與命令lease，純definition模型與投影/比對分離；CLI保留P0路由，--schema選結構模式。只有apply能寫，先完整驗證/備份再執行新增。
**Tech Stack:** net10、System.Text.Json、既有Dataverse SDK、xUnit；不新增外部套件或client。
**Spec:** requirements.md、正式02/90、.trellis/spec/backend/deployment-contract.md及主P1 design.md。

## 共通限制

不讀業務資料、不輸出任何設定本文或秘密、不Update/Delete既有metadata、不改權限或網域；
UTF-8無BOM/CRLF、完整繁中XML文件；所有資源單一owner、caller取消有界、完整候選後才寫輸出。
VM用途與備份未證明前不寫入；本包離線測試不代表P1-12通過。

## 檔案與介面

- d365/schema/speechmessagecore.schema.json：唯一宣告內容，無機密，含phase。
- Deployment/Schema/SchemaDefinition.cs、SchemaFiles.cs：純record、嚴格有界JSON/名稱/唯一鍵/欄位驗證與canonical hash、原子CreateNew/flush輸出。
- Deployment/Schema/SchemaMetadata.cs：SDK metadata只讀投影，正式ID／唯一性／有界資料；snapshot不帶SDK/credential。
- Deployment/Schema/SchemaDeploymentEngine.cs：ReadSnapshot/Plan/Apply/Verify；schema+fingerprint+planhash、完整衝突拒絕、建立順序、Solution及Active key確認。
- Deployment/Schema/SchemaDeploymentCli.cs與既有DeploymentCli.cs薄路由：--schema、export-metadata、路徑及既有write lock/lease，沿用共用診斷report。
- Deployment.Tests/SchemaDeploymentTests.cs／SchemaCliTests.cs：嚴格合成SDK message，不模仿真機驗收。
- 驗收/P1_D365結構建立步驟.md：可審核指令/條件/證據/測試資料清理，主機操作前準備好。

## 切片與驗證

- [x] 逐欄對附錄A及Word明確日期行為，記錄phase；比較SDK/JSON/手動方案，採正式既有SDK入口，不重建新工具。
- [x] Schema先紅：非法／未知欄、重複ID、phase、定義完整矩陣、檔案大小／路徑／JSON深度，再純解析及canonical hash。
- [x] Metadata先紅：只讀messages、完整根scope、列身份、無敏感設定、日期/lookup/選項/key投影、缺失／錯型別／過量拒絕，實作後輸出detached快照。
- [x] Plan/Apply先紅：無差異、錯型別／長度／日期／security／owner衝突、錯白名單、漂移／竄改、備份先flush、兩次apply第二次零寫入、pending/failed key不能verify成功。
- [x] CLI先紅：四命令含schema、純只讀export、錯路徑／重複選項／其他組織在連線前拒絕；舊62案全保留。真ExceptionDiagnostics去重／保密，連線/鎖/輸出故障釋放。
- [x] 加故障注入／併行／取消／資源回收；實際strict與Deployment獨立coverage≥80%，完整方案/Debug/I18n/架構/打包/編碼/duplicates及必要品質守門。
- [x] 主會話深度自審；依有效developer規則經CCG自癒入口雙模型嘗試，供應商缺輸出如實記錄，不算外審通過。
- [x] 完整驗收矩陣／一般資訊／證據SHA、本地commit與子任務封存；主P1仍需P1-12，不提前結束Goal。

## 可回復與失敗處理

程式改動可回本地commit；D365只新增仍須確認隔離、備份與可驗證復原。
write前metadata匯出不是資料庫備份，不能據此假裝可安全還原。部分apply失敗保留before snapshot與原錯誤；重新plan即可列剩餘新增，絕不自動刪元件或rollback checkpoint。

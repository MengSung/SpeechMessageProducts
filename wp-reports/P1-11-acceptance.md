# P1-11 結構指令驗收紀錄

2026-11-06。本包完成正式02 P1-11與90附錄A／G的本機開發、整合及只讀實機規劃；實際新增結構與資料庫唯一鍵驗收另列P1-12，不以離線測試冒充。

| 正式需求 | 實作位置 | 決定性測試與實際證據 | 狀態 |
| --- | --- | --- | --- |
| 附錄A結構清單 | d365/schema/speechmessagecore.schema.json、SchemaDefinition／SchemaFiles | SchemaDefinitionTests逐欄獨立核對：6表、37欄中P1 36、P4 1；4正式key，JSON未知／重複／錯預算拒絕 | 通過 |
| 只讀完整metadata匯出 | SchemaMetadata／SchemaSnapshot、export-metadata路由 | SchemaMetadataTests＋真實566表22678欄、4829 filters／7259 steps，無業務列或設定本文；live-readonly-summary.json | 通過 |
| 沿用連線與唯一組織 | DeploymentCli／SchemaDeploymentCli、既有UserSecretsOrganizationConnector | 非白名單、重複參數、外部schema／plan在Connect前拒絕；每命令lease歸還；原P0 62案保留 | 通過 |
| 完整衝突與hash／漂移 | SchemaPlanner、SchemaFiles | 型別／長度／lookup／選項／required／security／audit／owner／concurrency／managed／key衝突、schema／組織ID／計畫竄改全拒絕 | 通過 |
| 只新增及未受管理SpeechMessageCore | SchemaDeploymentEngine | 合成SDK真引擎往返，Create／AddSolutionComponent及定向PublishXml；無Update/Delete。真實plan88操作逐項核對Create／Include | 通過 |
| 日期／安全／稽核／並行 | SchemaDeploymentEngine.Attribute、SchemaPlanner.Matches | 4日期UserLocal/DateAndTime、2加密欄IsSecured、2新表entity及field audit、outbound concurrency重新讀true；既有欄不轉換 | 通過 |
| 兩次apply冪等 | SchemaEngineTests／SchemaCliTests | 重新plan後第二次零寫入；同舊plan兩並行apply一成一拒，4key／6表，全部leases歸還 | 通過（離線） |
| 發布後驗收 | SchemaDeploymentEngine.Verify、SchemaDeploymentCli | confirmed-red舊入口回0→修正只讀published回1；117/117。apply拒涉及表未發布約束差異，pending key回3／Failed拒絕 | 通過 |
| 檔案／SDK故障及資源 | SchemaFiles／engine／CLI | before CreateNew→flush(true)→close先於寫入；原例外保真、真Program共用日誌去重與保密、caller130／內部timeout通報；A/B千次、百snapshot弱參照 | 通過 |
| 繁中文件、輸出摘要與相容回歸 | 13份Schema檔頭、DeploymentCli、SharedText.resx、SystemTextCatalogTests | 完整24鍵獨立oracle、Localization182/208、generator deterministic verify；文件守門1247項0缺漏、Changed位元組守門 | 通過 |

## 實際品質結果

- 全方案Release建置成功，23個既有非strict警告、0錯誤；沒有新增NoWarn或降低標準。
- 最後全方案`solution-verified`：17份TRX逐筆核對 **4372/4372**、0失敗／略過；Deployment117、ChurchReport630、Localization182/208、ToolUtility1231/1369、Tenancy75。
- strict Deployment與Localization雙產品目標：0警告／錯誤。獨立Deployment coverage **88.43%**，Localization net462產品／net48宿主 **97.87%**、net10 **98.19%**，每份一個報告，皆超過80%。
- Debug tracer7＋正執行TRX守門；I18n工具170、PE工具27、Plugin metadata10/build6（一次性測試金鑰已清理）；analyzer26、Pester39、進度頁15、JavaScript20通過。
- I18n verify／生成資源／CLDR生成檢查、duplicates、CPM／CI過渡／coverage／CCG／tracer／SixLabors樣本守門通過。en 6/45=13.33%僅資訊，不宣告英文發布。
- 新資源造成的原Count12失敗、379多型lookup／空prefix／steps分頁／publisher身份／已發布verify各紅燈與修復結果均保留，不刪失敗證據。

## 真正只讀與外部審查邊界

真metadata與88項plan已使用既有合法connector取得，尚未apply。本包不聲稱唯一索引在D365已Active或真實拒第二列；這是P1-12。
外部自癒分析／審查均有嘗試；最後`20261106-190143-p111-schema-published-review-reviewer`兩後端無可用結果，dualModelComplete=false、degradedFallback=false。使用者取消雙模型等待；主會話逐檔自審，無已知阻斷程式缺陷，但未取得外部審查證據。

完整自審：.ccg/tasks/p111-schema-deployment/review.md（封存後位於archive/2026-11/p111-schema-deployment）。
可追蹤摘要與SHA：.ccg/tasks/integration-p1/evidence/p111-final/；原始輸出：artifacts/p1-11-20261106/。
原13.8MB metadata留本機原位、SHA A88D7381452A3940AC7CB65BAD2FA28E697DAFDC6EB4A42EA24B573E42397F74；停止追蹤不刪檔，避免原始診斷與換行守門衝突。
沒有修改正式需求，無Word修正報告需求；未push、未正式部署。主P1續P1-12，不封存主目標。

## P1-12後續實機修正
2026-11-06 P1-12發現9.1新Lookup稽核、key回應順序及solution根包含誤判，已依原plan/before受限補正並補回歸；一般新增流程新增只限本次新Lookup的audit啟用例外。正式計畫及Word修正報告已同步。最新157案、coverage89.11%、方案4412及真正實機verify/零apply/smoke/清理以P1-12-acceptance.md為準，以上117案與只讀狀態是當時交付歷史，不宣稱本輪無Update。

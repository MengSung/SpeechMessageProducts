# 與全專案檔案註解任務分工

2026-11-06 使用者委派另一對話01a1b04a-9608-7910-903a-b899a4a1bd72管理全專案註解、AGENTS/spec與文件守門。
本P1主會話接續實作／驗收，停止擴張全專案註解；不修改其.ccg/tasks/file-purpose-documentation狀態、不納入其無關diff。

本主會話仍在編輯、由本會話維護檔頭與提交：
- SpeechMessage.Dynamics.Deployment/Schema/*.cs
- SpeechMessage.Dynamics.Deployment/DeploymentCli.cs
- SpeechMessage.Dynamics.Deployment.Tests/Schema*.cs
- d365/schema/speechmessagecore.schema.json
- SpeechMessage.Localization/Resources/SharedText.resx與其生成SharedText.g.cs
- 若需修生成契約：tools/I18nCheck/ResourceGenerator.cs（變更前另記本檔）
- P1驗收文件／主任務／進度頁DATA由主會話統一更新。

已逐檔手寫13份完整檔案說明（SchemaFiles、其他7份Schema來源、5份Schema測試）；新增修改持續同步。
另一對話可先處理上述範圍之外的來源。釋出範圍與commit會更新此紀錄，避免同檔同時寫入。
雙方均不得git add -A或覆蓋對方變更；本會話只stage明確P1檔案。

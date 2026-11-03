# P0-08 開工核對與停止

- [x] P0-07 已提交 f0f0360f，提交後工作區乾淨，未推送／合併。
- [x] 重新讀 AGENTS.md、00 與 01 的 P0-08；原列八個方法保留，未刪除或改名。
- [x] 現有嚴格 Recommended／TreatWarningsAsErrors 與八個底線名稱存在已知 CA1707 衝突；核定的例外只存在 I18nCheck.Tests.csproj，不會傳到新專案。
- [ ] 等待使用者核定窄範圍例外；可選僅 SpeechMessage.Localization.Tests，或一次涵蓋階段 0 其餘新增測試專案。
- [ ] 核定後建立雙目標專案及原八個測試，先紅、實作、重構、完整驗證及回報後才提交。

## 尚未執行
沒有建立 SpeechMessage.Localization／SpeechMessage.Localization.Tests，沒有新 C#、resx、方案變更、套件還原或功能測試。P0-08 未實跑，不把 P0-07 的實測當成本包紅燈。

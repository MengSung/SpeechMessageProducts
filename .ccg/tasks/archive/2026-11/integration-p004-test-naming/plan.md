# P0-04 CA1707 補充實作

- [x] 讀 AGENTS.md、00、既有分析器腳本／樣本及規範；沿用既有 Trellis 階段任務。
- [x] 在原 16 項探測新增三項指定命名案例，另補「名稱以 .Tests 結尾但 IsTestProject=false」負向案例，避免漏測其中一個條件；先跑紅燈。
- [x] 僅在 Directory.Build.targets 集中 NoWarn CA1707，條件必須同時為 StrictCode、IsTestProject 與 .Tests 名稱。
- [x] 移除 I18nCheck.Tests 的個別 NoWarn；同步兩份核定計畫文字及直接相關規範／工具說明。
- [x] 75 個工具測試、完整 §8 及擴充後探測全綠；寫獨立補充回報，不改歷史已完成回報。
- [x] 歸檔單包 CCG 登錄、更新 task／進度，依指定訊息單獨提交；之後才開 P0-08 TDD。

# P0-13 net462 正式變更（使用者 2026-11-04 核定）

- [x] 已完成隔離相容試驗；122 個測試通過，不等於實機註冊成功。
- [ ] 先建立 net462 正樣本／net48 反向樣本與 Framework 分支、實際引用回歸測試，取得功能紅燈。
- [ ] 三個產品改 net462；雙目標保留 net10；Framework 條件與共用 polyfill 同步，測試可維持 net48。
- [ ] 合併前檢查四個輸入的 TargetFrameworkAttribute 上限，檢查器固定輸出 net462；其餘守門保留。
- [ ] 列出 net462 目標全部套件資產、合併輸入及 facade 說明；跑相關測試、Release 打包、真實簽章驗證。
- [ ] 同步 00／D21、01～04、spec、驗收步驟及調查報告第 8 節。
- [ ] 跑全方案 §8、外部工具／打包／探測測試，保留無機密摘要。
- [ ] 自癒入口 ClaudeOnly 第 10 份審查；Critical／High 修正後再驗證。
- [ ] 以核定訊息提交，僅交給使用者重新 plan／apply／verify／smoke／收尾。

根因高度可信但仍待 net462 實機確認；目前正式 apply／verify／smoke 均未通過。

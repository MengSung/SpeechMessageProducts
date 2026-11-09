# P3-02 執行清單

啟動紀錄提交6337412800f2c2f204c3452d7bc46c62e2acd001已於固定C:/smv/guard完成§8.3：編碼16檔0問題、文件1335檔0失敗、進度頁28個測試通過。原始guard-63374128-light.log的SHA-256為F68B16A090FEA245CE35AEC68A21619D2696E36108A0240C1F766DC283CB871C；這只是紀錄型驗證，產品完整守門與驗收尚未執行。

- [ ] 讀AGENTS、prd/design、04 P3-02、00§7、相關Hosting／Tenancy／API規範與R2現有測試。
- [ ] 列出準備建立／修改的檔案，先建立測試紅燈並保存原因；不提交不可建置紅燈。
- [ ] 新csproj建立即加入sln並核對，完善三必要介面、有界body、端點及缺實作拒啟動。
- [ ] 注入測試替身走真HTTP／Host生命週期，測成功及拒絕／滿載等契約，沒有外部連線。
- [ ] 跑相關測試與coverlet（每個新產品目標≥80%），raw保存在協調者指定evidence而非TEMP。
- [ ] 提交到本機p3/P3-02；回報SHA／修改檔案／測試／coverage及必要後續契約，不推送、不動P3、不跑全guard。
- [ ] 協調者複核、rebase＋ff、完整guard、回報及進度、驗收後推送與核對SHA、清理暫時worktree。

## 協調決策

2026-11-09 13:41 Asia/Taipei (UTC+08:00)：依solution-ci-contract.md，新增主機與測試須同步CI eligible集合及新主機coverage matrix，故擴入design列出的三個CI接入檔案；維持原三項排除與80%門檻，不擴功能、不弱化檢查。已通知原實作代理，不另開重複實作。

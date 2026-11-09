# P3-05 執行清單

- [ ] P3-02驗收後協調者start、更新DATA、建立C:/smw/P3-05與p3/P3-05；與P3-01逐檔確認無重疊後才並行。
- [ ] 讀AGENTS、04 P3-05、P3-02三介面及task／spec，列確切檔案。
- [ ] 先寫六個指定測試並確認紅燈原因，不提交紅燈。
- [ ] 實作總量有界的分區Channel、兩秒接收、兩階段commit／publish、每事件30秒CTS及scope。
- [ ] 實作停止拒收→在途handoff→30秒drain→取消剩餘→Dispose；驗A/B保序／並行與所有owner回基線。
- [ ] 相關測試及文件／位元組檢查通過，本機P3-05提交；raw只放ignored evidence。
- [ ] 協調者複核、rebase＋ff、完整guard、回報與進度、驗收推送核對、evidence耐久搬回後清理暫時worktree。

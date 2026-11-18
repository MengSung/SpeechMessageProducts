# P7-02a 執行清單

- [x] P7-01驗收與推送核SHA，續跑狀態off。
- [ ] 自癒入口ClaudeOnly分析操作風險與既有工具限制，採納經程式核對建議。
- [ ] 手冊初稿11章（操作／檢查／回復／預計時間留待實測）與演練紀錄。
- [x] R0 D34／verify0／待辦0／測試資料流程0／trace原值；15個核定表SMV-TEST標記0。R1唯讀匯出在標記清冊結束前開始，記錄時間；匯出沒有D365寫入。
- [x] R1 export、export-legacy、export-metadata，備份SHA記在演練紀錄。
- [x] R2 schema plan additions=0／verify0。
- [x] R3 打包、plan核不重建、apply、verify0、診斷smoke新FileVersion（更正前置狀態缺漏：trace=2、diagnostics enabled後單次smoke輸出1.0.0.19並通過；finally diagnostics off、trace=1、verify/off、D34／待辦／smoke task0；診斷trace保留2供R9）。
- [ ] R4每模組影子差異0與殘留0，清單填報告；前置A追蹤可用必做。2026-11-18 08:45 Asia/Taipei (UTC+08:00)：根因確認為 seed lookup 指向後置 alias，離線加入穩定拓撲排序與循環拒絕；weeklyreports owner 改 WhoAmI shared、leader 移除不可寫 fullname／new_lineid。原兩次 attendance 失敗及 finally 基線保留；離線測試／Deployment.Tests 通過，待提交與固定 guard 後重跑 R4。
- [ ] R5a plan shadow→switch→verify plan0→rollback→verify off0。
- [ ] R5b override提交guard→plan live逐行審→switch→verify plan0；不中斷接b。
- [ ] 完整guard、紀錄／父清單同步與收尾提交。

2026-11-17 21:55 Asia/Taipei (UTC+08:00)：接P7-02a；P7-01遠端5eb3923ed9e5907332c22cb76c32177b7fdf8db0已核。尚未開始R0或寫入組織。

2026-11-17 23:05 Asia/Taipei (UTC+08:00)：R0完整D34/verify/標記清冊讀值完成，R1三匯出備份SHA已記，R2 additions=0/verify通過。R3 BuildNumber19 apply／verify0，但smoke兩次仍報舊版1.0.0.17；VM管理權限拒絕，目前停R3，未R4。

2026-11-17 23:08 Asia/Taipei (UTC+08:00)：ClaudeOnly分析經自癒入口完成，summary ok=true／reviewMode=claude-only／dualModelComplete=false。R4可執行性及R5來源雜湊回復限制見下方紀錄；R3先停在舊sandbox追蹤版本，未啟動R4。

ClaudeOnly具體核對：R5切換與rollback間不得改plugins.registration.json或organizations.json，R5b期間尤其不能提交live.evidence；R8須用原switch-log回復完成後再改清單。R5b switch前重驗D34。R4現有Golden活躍標記皆false，Shadow runner拒活動及無步驟模組；R3 smoke新FileVersion是R4前置閘門，R3還須先解除舊sandbox版本。

2026-11-18 00:47 Asia/Taipei (UTC+08:00)：續跑驗收工作樹改動，R4 selector與回歸測試涵蓋appointments、attendance、localization、weeklyreports四模組；同事件歧義以entrypoint唯一對應正式Step.Type，reading舊entrypoint可經Legacy映射回新版step。Deployment.Tests 902/902、檔案說明1,816/0、I18n generate check 8/8通過。這些皆為離線結果，不代表R3 smoke或R4實機通過。P7-01b指定KeepLegacy測試通過；C-b與P7-01a完成時間／驗收提交／guard已在父清單及P7-01b收尾提交。

2026-11-18 01:18 Asia/Taipei (UTC+08:00)：依使用者選擇嘗試唯讀查詢D365-9.1服務與Application事件；本機Hyper-V權限不足且PowerShell Direct無法解析VM，未取得摘要，未改主機。R3仍未完成，等待外部權限／VM解析恢復。

2026-11-18 06:43 Asia/Taipei (UTC+08:00)：續跑唯讀核對仍因Hyper-V權限不足無法取得D365-9.1服務／載入事件；verify/off與D34清理基線通過，未重跑smoke。R3仍未完成，R4保持未開始。

2026-11-18 07:08 Asia/Taipei (UTC+08:00)：R3更正程序完成並通過；前置狀態缺漏造成先前誤判，正確程序產生1.0.0.19。接R4逐模組影子比對，執行前trace設2，finally還原1。

# P7-02b 需求與驗收
依使用者2026-11-19目標及07A §5/7/8；只goodshepherdback、不啟用舊程式、不改IIS/Node、不真付款、不階段8、不推main或PR、不碰LINE token分離任務。
先完成P7-02a唯一紀錄提交6d57ec22的完整guard與推送，之後離線Live固定原因碼，每碼測試，獨立提交與完整guard驗收；之前不得live-check。
R5b plan核12新啟用、0舊停用、KeepLegacy保留、Manual0；R8前manifest/catalog不可修改。
R6依localization含defaults→weeklyreports→attendance→lessons→appointments→qrcodes→care→fees→messaging；每模組90分鐘、最多2次實機，修正須先離線提交guard。
準備／環境失敗不得dormant；只有實際可比較差異依核定上限或§8-6無樣板時間盒才能降級。本輪不演練項目不跑，證據空白。
R7觀察本次時間；R8原log回off後一次登錄證據／移除override；R9基線、備份清理trace、停止host。表單2小時、手冊定稿、一頁證據。
最終完整guard、分支推送及ls-remote；ClaudeOnly階段審查，Critical/High修完。沒有實測不得宣稱完成。

## 使用者核定的範圍縮減

2026-11-19 09:11 Asia/Taipei (UTC+08:00)：使用者核定的範圍縮減（2026-11-19）。影響07A §5 R8、R5a重演、附錄乙、手冊正式組織章節：R8僅以本次R5b switch-log rollback一次及verify off=0，不與R1 export／export-legacy或流程定義逐位元組比對；R5a不重演，R5b只重新切換一次供R6；不新增或擴充正式組織確認關卡、正式組織手冊章節、附錄乙核定書範例。既有ProductionGuard、--production、stdin組織名確認及rollback程式保持不變；保留P7_組織切換手冊.md與進度契約字串；不改07A。全程只goodshepherdback，不啟用舊程式、不啟動階段8。每模組90分鐘／最多2次實機；準備／環境失敗不得dormant，合法候選及pass證據均R8回復後才登錄。

# P7-02b 需求與驗收
依使用者2026-11-19目標及07A §5/7/8；只goodshepherdback、不啟用舊程式、不改IIS/Node、不真付款、不階段8、不推main或PR、不碰LINE token分離任務。
先完成P7-02a唯一紀錄提交及完整guard、推送、核遠端SHA。離線Live原因碼逐碼測試；完整離線驗證與guard通過前不得live-check。
R5b plan核12新啟用、0舊停用、KeepLegacy保留、Manual0；從R5b switch到R8 rollback完成前，manifest/catalog不得修改。
R6依localization含defaults→weeklyreports→attendance→lessons→appointments→qrcodes→care→fees→messaging；每模組60分鐘、最多2次實機，修正須先離線驗證並按提交節點跑完整guard。
準備／環境失敗不得dormant；只有實際可比較差異依核定上限或§8-6無樣板時間盒才能降級。本輪不演練項目不跑，證據空白。
R7觀察本次時間；R8原log回off後一次登錄證據／移除override；R9基線、備份清理trace、停止host。表單2小時、手冊定稿、一頁證據。
最終完整guard、分支推送及ls-remote；ClaudeOnly階段審查，Critical/High修完。沒有實測不得宣稱完成。

## 使用者2026-11-19核定修訂
P7-02a收尾及guard修正提交4f221bda已推送並核遠端一致。R8不做R1 export／export-legacy及流程定義位元組比對；不重演R5a；R5b只重切一次供R6；不新增正式組織確認關卡、手冊正式組織章節或附錄乙範例。保留ProductionGuard、--production、stdin組織名確認、rollback程式、P7_組織切換手冊.md及進度頁測試要求；不修改07A。

明確授權且僅限七項Workflow活動因LIVE-PREP-09改dormant，理由固定使用「P7-PROP-006 LIVE-PREP-09：Live 測試工具尚不支援流程活動的樣板建立；使用者 2026-11-19 核定改 dormant，未經實機 Live 驗證。」：ContactAnniversaryActivity、LessonQrCodeActivity、AppointmentSigningActivity、MeetingQrCodeActivity、MeetingScanCountActivity、PersonalQrCodeActivity、FeeAllotActivity。同模組Plugin不得降級。Live活動工具缺少Workflow樣板／參數繫結驗證另以P7-PROP-007提案；將來有產品需要上線流程活動時再補，不列為通用上線前必補。

## 使用者核定的範圍縮減

2026-11-19 09:11 Asia/Taipei (UTC+08:00)：使用者核定的範圍縮減（2026-11-19）。影響07A §5 R8、R5a重演、附錄乙、手冊正式組織章節：R8僅以本次R5b switch-log rollback一次及verify off=0，不與R1 export／export-legacy或流程定義逐位元組比對；R5a不重演，R5b只重新切換一次供R6；不新增或擴充正式組織確認關卡、正式組織手冊章節、附錄乙核定書範例。既有ProductionGuard、--production、stdin組織名確認及rollback程式保持不變；保留P7_組織切換手冊.md與進度契約字串；不改07A。全程只goodshepherdback，不啟用舊程式、不啟動階段8。每模組90分鐘／最多2次實機；準備／環境失敗不得dormant，合法候選及pass證據均R8回復後才登錄。

# P7-02 演練證據快照

2026-11-19 11:56 Asia/Taipei (UTC+08:00)

## 範圍與來源

這是本輪離線工作及目前版控清單的狀態快照，不是P7-02結案報告。來源為`d365/registration/plugins.registration.json`唯讀盤點、Deployment.Tests離線測試、自癒ClaudeOnly階段審查，以及`P7_演練操作紀錄.md`。未連D365、未執行live-check；不包含本輪Live pass證據。

使用者2026-11-19核定範圍縮減：不做R8與R1 export／export-legacy或流程定義逐位元組比對；不重演R5a；不新增／擴充正式組織確認關卡、正式組織手冊章節或附錄乙核定書範例。只操作goodshepherdback；不啟動階段8。

## 目前清單計數

| 項目 | 數量 | 判定 |
|---|---:|---|
| 非diagnostics Live註冊項目 | 19 | 所有項目尚無本輪Live pass證據 |
| Live pass證據 | 0 | 無本輪live-check |
| 既有dormant註冊項目 | 14 | 保留原有理由；本輪沒有新增dormant |

Live項目目前均列為未解／未驗收，不能以離線情境或Claude審查填入pass。

## Live項目分布

| 模組 | Live | Pass | 未解／未驗收 |
|---|---:|---:|---:|
| localization | 4 | 0 | 4 |
| weeklyreports | 1 | 0 | 1 |
| attendance | 1 | 0 | 1 |
| lessons | 3 | 0 | 3 |
| appointments | 3 | 0 | 3 |
| qrcodes | 3 | 0 | 3 |
| care | 1 | 0 | 1 |
| fees | 3 | 0 | 3 |
| messaging | 0 | 0 | 0 |
| **合計** | **19** | **0** | **19** |

LIVE-PREP-09目前拒絕所有非dormant活動；整批準備令lessons、qrcodes、care、fees內活動拒絕連帶阻擋同模組Plugin案例。四模組在此條件解除前皆未解，測試通過不代表可執行或Live通過。

messaging現有兩個registration維持dormant，理由含P5-PROP-011/012；其Golden/Legacy案例皆`liveShadow=false`。本輪未把reading案例改成LiveShadow，未改動registration，也沒有messaging Live項目。

## 既有Dormant與提案

| 模組／項目 | 數量 | 現有提案 |
|---|---:|---|
| localization：MemberLanguageChangedPlugin、ChurchLocaleChangedPlugin | 2 | P7-PROP-001 |
| weeklyreports：WeeklyReportStatisticsPlugin | 1 | P7-PROP-006 |
| lessons：StorLessonSigningPlugin兩個步驟、LessonProgressionPlugin兩個步驟、StorLessonSigningActivity、EquipmentStatusActivity | 6 | P7-PROP-006；StorLessonSigningActivity另為P5-PROP-006 |
| messaging：OutboundLetterPlugin兩個步驟 | 2 | P5-PROP-011／P5-PROP-012 |
| care：NewPersonCareActivity | 1 | P7-PROP-006 |
| weeklyreports：WeeklyReportGenerationActivity | 1 | P6-PROP-001 |
| fees：FeeNotifyActivity | 1 | P7-PROP-006 |
| **合計** | **14** | 本輪未新增或變更 |

## 未解原因與階段狀態

- 活動情境含合成輸入，但目前沒有可驗證的Dataverse Workflow XAML參數繫結契約；現行準備路徑以`LIVE-PREP-09` fail-closed。這是離線程式路徑判定，沒有本輪實機重現；不得猜測繫結或把準備失敗改為dormant。
- LIVE-PREP-09實際拒絕目前所有非dormant Live活動，不論輸入集合是否為空。`RunPreparedLive`在seed前整批準備；因此lessons、qrcodes、care、fees的活動案例會讓各自整個模組停止，包含同模組原可對應的Plugin案例。四模組Live在此限制解除前均是未解，而非可實測案例或合法dormant。測試只證明情境與registration結構對應，不證明活動可執行。
- 活動判斷前會掃描符合型別的Workflow樣板（MetadataReader最多讀取上限由既有參數限定）；此唯讀成本未實測，應在R6前重新評估查詢範圍，不能將離線測試當作效能量測。
- 歷史R6的localization、weeklyreports、attendance、appointments準備失敗不得當成本輪結果。離線回放已修正共同的舊流程判斷缺陷，但本輪guard未過，不能對組織重跑以確認。
- c6059943固定完整guard因`.ccg/tasks/line-token-config-separation/.turns.json`的MissingFinalCrlf失敗。該路徑明確禁改；沒有更換guard範圍或階段起點。P7-02a未驗收／未推送；新R5b、R6～R9、表單D7-22及實測手冊定稿均未完成。
- D7-22表單需要對goodshepherdback匯出、SDK修改、發布及讀回；本輪未執行或宣稱完成。
- 本輪完整guard失敗與live-check未執行是停止實機工作的前置限制，不代表組織狀態已由本輪重新驗證。最近可用的補充唯讀紀錄為08:56，回報off=0、D34通過、待辦／smoke／SMV-TEST=0、trace=1、保留診斷trace=2。

## ClaudeOnly階段審查

2026-11-19 11:49 Asia/Taipei (UTC+08:00)：自癒入口`Start-CcgDualModelRun.ps1`完成初審；`ok=true`、`reviewMode=claude-only`、Claude輸出可用。標記「單模型（使用者指定 ClaudeOnly）」，不是雙模型審查。Critical無發現。審查所提活動阻擋已記錄；情境測試補上依manifest live tier雙向核對及lookup建立順序檢查。完整輸出保留於`.ccg/dual-model-runs/20261119-114925-p7-02b-claude-only-review-reviewer/`。

2026-11-19 12:05 Asia/Taipei (UTC+08:00)：修正後自癒ClaudeOnly複核完成，`ok=true`、Claude輸出可用；Critical／High均無發現。M1文案更正：LIVE-PREP-09目前拒絕所有非dormant Live活動，並非只拒絕有輸入的活動。M2同步記錄：模組採整批準備，lessons、qrcodes、care、fees任何活動被09拒絕即令同模組Plugin情境也沒有Live結果；不能藉測試結構通過宣稱可執行。唯讀Workflow掃描成本尚未量測，列R6前評估事項。審查輸出保留於`.ccg/dual-model-runs/20261119-120548-p7-02b-claude-only-final-review-reviewer/`。

## Offline Verification

- `dotnet test SpeechMessage.Dynamics.Deployment.Tests/SpeechMessage.Dynamics.Deployment.Tests.csproj -c Release --no-restore`：966/966通過。
- `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs`：28/28通過。
- `python tools/RepoQuality/check_file_documentation.py`：1826檔、0失敗。
- `git diff --check`：通過。
- Changed文字編碼檢查：失敗唯一項是禁改`.ccg/tasks/line-token-config-separation/.turns.json`缺末尾CRLF；未修改該檔、未縮小守門或更換base。
- 五個修改中的C#檔已逐位元組確認UTF-8無BOM、CRLF且末尾CRLF。

## 2026-11-19 13:08 Taipei Checkpoint

此節為後續離線驗證快照，取代上方11:56的「guard未解」與清單分級快照；不表示P7-02b已驗收。P7-02a收尾提交及guard規則修正已推送，`4f221bdaebefad5ece0751047a446772e462b515`與遠端一致。當前工作樹修改尚未完整guard、提交或推送。

使用者核定的範圍縮減影響07A §5 R8、R5a重演、附錄乙與手冊正式組織章節：R8不做R1匯出或流程位元組比對；R5a不重演；R5b只重切一次供R6；不新增正式組織關卡、手冊章節或附錄乙核定書範例。既有ProductionGuard、`--production`、stdin組織名確認、rollback程式、手冊檔案及進度頁測試要求保留；不修改07A。

依使用者明確核定例外，以下七個活動tier改為`dormant`，固定原因以`P7-PROP-006 LIVE-PREP-09`開頭，未經Live驗證，evidence保持空白：ContactAnniversaryActivity、LessonQrCodeActivity、AppointmentSigningActivity、MeetingQrCodeActivity、MeetingScanCountActivity、PersonalQrCodeActivity、FeeAllotActivity。同模組Plugin及其他活動tier維持原值；organizations.json演練override僅保留OutboundLetterPlugin。活動Live工具未支援Workflow樣板與參數繫結驗證的提案編為P7-PROP-007，待有產品需要上線流程活動時再做，不列通用上線前必補。

此快照清冊共33項：live 12、pass 0、dormant 21、未解live 12。九模組Live數量：localization 4、weeklyreports 1、attendance 1、lessons 2、appointments 2、qrcodes 0、care 0、fees 2、messaging 0。這12項尚無本輪Live結果，R6後按逐項pass或未解原因碼更新；7項核定活動屬明確dormant例外，不列未解Live。

R6每模組時間盒依使用者核定採60分鐘、最多2次實機。新測試先紅後綠發現appointments情境先建record、後建signer及重複church alias；修正後seed依賴順序與appointments／lessons／fees dormant活動不阻擋Plugin準備共4案通過。全套Deployment.Tests 975/975、檔案說明1826檔0失敗、進度頁測試28/28通過。以上全為離線測試，尚未重新R5b，沒有任何Live pass證據。

## 使用者核定活動例外

| 模組 | dormant活動 | reason／證據 |
|---|---|---|
| care | ContactAnniversaryActivity | P7-PROP-006 LIVE-PREP-09；使用者核定，未經實機Live驗證 |
| lessons | LessonQrCodeActivity | P7-PROP-006 LIVE-PREP-09；使用者核定，未經實機Live驗證 |
| appointments | AppointmentSigningActivity | P7-PROP-006 LIVE-PREP-09；使用者核定，未經實機Live驗證 |
| qrcodes | MeetingQrCodeActivity、MeetingScanCountActivity、PersonalQrCodeActivity | P7-PROP-006 LIVE-PREP-09；使用者核定，未經實機Live驗證 |
| fees | FeeAllotActivity | P7-PROP-006 LIVE-PREP-09；使用者核定，未經實機Live驗證 |

表內七項均無pass；全數屬使用者核定之明確例外，不代表一般準備或環境失敗可改dormant。

## 2026-11-19 13:28 Asia/Taipei (UTC+08:00) 離線候選檢查點

更新後離線測試：Deployment.Tests 977/977、進度頁28/28、檔案說明1826檔0失敗、Changed編碼15檔0問題、`git diff --check`通過、TextEncoding Pester 22/22。這些不構成實機Live通過。清冊仍為33項：live 12、pass 0、dormant 21、未解live 12；13個活動全為dormant。候選提交完整八項guard、R5b、R6、R7、R8及R9尚未執行；組織當下狀態尚未重新唯讀核對。固定guard將在獨立乾淨worktree執行，避免清理既有`C:\smv\guard`時刪除其中忽略的使用者Trellis任務資料。

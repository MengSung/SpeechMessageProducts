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

## 2026-11-19 16:43 Asia/Taipei (UTC+08:00) 最終演練狀態

本節取代前述「R5b尚未開始／組織狀態未核對」等較早快照；保留它們作為歷史紀錄。依最新使用者核定，只操作goodshepherdback；R8不做R1匯出或流程位元組比對、不重演R5a；不啟動階段8。

### R6 清冊

非diagnostics註冊項目共33項：`live` 12、實機`pass` 1、`dormant` 21，其中七個Workflow活動是使用者核定例外；其餘11個live項目未通過或未檢查，仍維持live tier且不填manifest evidence。`care`、`qrcodes`僅有Workflow活動，本輪依核定標記未檢查，不執行。未跑比對或工具／環境失敗均不改成dormant。

| 模組 | Live項目數 | Pass | Dormant／未檢查 | 結果 |
|---|---:|---:|---:|---|
| localization | 4 | 0 | 2 | church-defaults apply 為 Dataverse `0x80040224`；Live準備未通過 |
| weeklyreports | 1 | 0 | 2 | 兩次準備均為 `LIVE-PREP-03` |
| attendance | 1 | 1 | 0 | `PresentRecordPlugin` pass |
| lessons | 2 | 0 | 7 | Live準備遇 `0x80040224`；Plugin及活動dormant項目維持原級 |
| appointments | 2 | 0 | 1 | `LIVE-PREP-03`；核定dormant活動未檢查 |
| fees | 2 | 0 | 2 | P7Live relay 前置測試失敗，輸出未取得原因碼，未執行Live-check |
| messaging | 0 | 0 | 2 | `OutboundLetterPlugin` override已移除；模組未執行，原tier維持dormant |
| care | 0 | 0 | 2 | 只有活動，依核定未檢查 |
| qrcodes | 0 | 0 | 3 | 只有活動，依核定未檢查 |
| **合計** | **12** | **1** | **21 dormant** | 7個新增核定活動dormant；14個原dormant保留 |

attendance證據為`artifacts/d365/goodshepherdback/20261119-075315-7562587-dd01266836a745918c02766bb6f4d88f/live-check.json`，SHA-256 `04980FA03DFAFD7A7B7BA1C180A40F720DB142CDA0546EA6256C4229BC050F3C`，2026-11-19 15:53:13 Asia/Taipei (UTC+08:00)，單一案例可比較且pass；推播0、代送待辦0。沒有將模組外或離線測試當成實機證據。

### localization `church-defaults` 根因調查

apply使用的plan SHA-256為`D2A23645B43A1F86DE161B9FC70E7F530B260225DF903D350DBE8971F2EC15A0`；plan指向一筆account，四個目標欄位原值皆為空。唯讀metadata顯示四欄為字串，長度16／200／64／2，提供值均符合長度限制。現有證據未取得目標列state及欄位`IsValidForUpdate`；故無法證明是欄位可寫性、列狀態或其他平台條件造成`0x80040224`。已排除本次提供值超長，以及metadata不是字串；不推論權限、快取或猜測修正。狀態記為**未解：`0x80040224`**，後續需在唯讀取得目標列狀態與欄位更新metadata後定位，再離線重現並只重試一次。

### R7、R8、R9

R7使用`--since 2026-11-19T05:49:55Z`。12個Plugin類型系統作業失敗／等待皆為0，Workflow failures為0、新舊互斥檢查通過；observe計得trace exception 2，但安全輸出只有計數，無法逐筆分類，故不推定由演練造成或為缺陷。R7 artifact SHA-256：`2F13F8715A5BEE4102C51D88BD8901BEDA8094FDB3F9978574E081E811EB73AF`。

R8使用本次R5b switch-log完成rollback，未作R1比對；演練override在rollback後移除。R9於2026-11-19 16:43:36 Asia/Taipei (UTC+08:00)唯讀核對：`verify`成功、D34通過、Outbound=0、待辦／smoke／SMV-TEST及各marker=0、trace=1、診斷trace=0；P7Live與LineWebhook測試主機程序均未執行。起點08:56紀錄診斷trace=2，R9為0；現有操作紀錄未能證明其間如何消失，不推定自動清理或已備份，列為未解並納入P8以後待驗證清單。本報告不把較早artifact中不同時間的清冊讀值冒充本次讀值。

### D7-22 表單、手冊及提案

D7-22表單未執行，依本輪核定縮減範圍不列為P7-02b結案阻擋：部署專案沒有可安全修改及發布account/contact主要表單的SDK命令；現有UI方案打包明確排除共用account/contact FormXml。本輪未對表單執行寫入，避免臨時繞過既有邊界。依`P7-PROP-005`保留人工步驟：先備份表單、加入四個account語系欄位與contact偏好語言、發布、讀回核對；失敗時還原備份並留存步驟／證據。這不是宣稱已完成的表單演練。

使用者核定Workflow工具提案必做文字：**「活動（Workflow）的 Live 測試工具需支援流程樣板建立（LIVE-PREP-09）。使用者確認未來產品會沿用 Workflow，列為後續階段必做項目；完成後補驗 7 個 dormant 活動。」**七項為ContactAnniversaryActivity、LessonQrCodeActivity、AppointmentSigningActivity、MeetingQrCodeActivity、MeetingScanCountActivity、PersonalQrCodeActivity、FeeAllotActivity；維持dormant且無Live evidence。

### P8 以後的待驗證清單

| 模組／項目 | 狀態與原因碼 | 已嘗試 | 建議下一步 |
|---|---|---|---|
| localization／church-defaults | 未解：`0x80040224` | 核對plan、字串metadata及值長度；未取得列state與`IsValidForUpdate` | 唯讀補齊目標列狀態及可更新metadata，離線重現根因後再單次apply |
| localization／Live-check | 未解：原因碼未從安全外層紀錄取得 | 1次Live-check失敗；未宣稱通過 | 先取得該次固定原因碼與安全識別，再判定是否有repo內可修正項目 |
| weeklyreports | 延後驗證：`LIVE-PREP-03` | 2次Live準備失敗，均未建立seed | 查出D34/Safety具體子檢查差異，離線修正後再依新核准時間盒補驗 |
| lessons | 延後驗證：`0x80040224` | 1次Live準備失敗；finally verify-plan通過 | 先取得seed alias對應的欄位metadata／平台 fault 識別，離線驗證修正後補驗Plugin |
| appointments | 延後驗證：`LIVE-PREP-03` | 1次Live準備失敗；finally verify-plan通過 | 依安全原因摘要核對D34與metadata，離線修正後補驗 |
| fees | 延後驗證：原因碼未取得（P7Live relay前置失敗） | 1次relay前置測試失敗；沒有啟動fees Live-check，finally verify-plan通過 | 先補安全原因碼和relay owner診斷，再確認host可用後補驗 |
| messaging／OutboundLetterPlugin | 未檢查：無核准P7情境，R6未執行 | 未執行Live-check；演練override已移除 | 依允許的P7情境與代送host前置，另排R6補驗 |
| care／qrcodes Workflow | 未檢查；七項核定例外以`LIVE-PREP-09` dormant | 依新核定未執行 | 完成P7-PROP-007後補驗七項活動 |
| R7 trace exceptions | 未解：trace exception計數2，無逐筆原因碼 | observe安全投影僅提供計數，Plugin jobs及Workflow failures為0 | 取得不含業務內容的逐筆錯誤型別／穩定碼，判斷演練預期或缺陷 |
| R9 診斷 trace 去向 | 未解：起點計數2，R9計數0；未找到備份或清理紀錄 | 08:56紀錄保留診斷trace=2；16:43 R9唯讀核對為0。現有操作紀錄未能證明其間如何消失，不推定自動清理或已備份 | 核對本機trace輪替／保留機制及操作紀錄；確認去向前不得宣稱已完成備份清理程序 |
| D7-22 account/contact表單 | 延後驗證：工具缺口，依`P7-PROP-005` | 未執行表單寫入；現有方案匯入明確移除共用表單 | 另開受控人工步驟，先備份、發布、讀回、失敗還原 |
| 七個核定Workflow活動 | dormant：`LIVE-PREP-09`／`P7-PROP-006` | 未執行；不屬一般準備失敗降級 | P7-PROP-007完成後逐項補驗並附各自Live evidence |

## 階段審查狀態

2026-11-19 16:54 Asia/Taipei (UTC+08:00)：`docs/scripts/Start-CcgDualModelRun.ps1`完成ClaudeOnly階段審查，標記「單模型（使用者指定 ClaudeOnly）」，Claude輸出可用。未發現Critical；High指出本證據頁在審查結論產生前預寫「未發現Critical／High」，已於本次修正為依實際審查結果描述。修正後目前無未處理Critical／High。Medium建議已補記attendance原始證據僅存本機，以及診斷trace由2變0但去向缺少證據；後者列為P8以後未解。此為單模型審查，不是雙模型審查。

attendance原始`live-check.json`位於被Git忽略的本機`artifacts/`；manifest所記SHA-256可在該工作站核對，但此檔不隨repo推送，其他clone無法取得原始內容。本輪不把此本機檔案表述為可由遠端重現的證據。

## 最終驗收與推送狀態

2026-11-19 17:58 Asia/Taipei (UTC+08:00)：`552bdbcfe625cdcf736dc347cbad6f95068aed6c`於新建且核對乾淨的隔離worktree完成固定八項guard並已推送，`git ls-remote` SHA與當時本機一致。之後同步進度頁DATA時發現P7-02階段note多一個JSON尾逗號，已修正；`Test-ProgressPage.cjs` 28/28、檔案說明1826檔／0失敗、`git diff --check`通過。此後續文件同步另列為結案提交；其固定guard及遠端SHA以本報告提交版本和最終驗收紀錄為準。既有guard結果：Release build 0錯誤（既有警告65）；Release tests 7341/7341；Debug tracer 7/7；Changed encoding 217檔0問題；duplicate scan 42專案／1510檔、groups/new/stale均0；I18n verify通過。

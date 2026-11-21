# 遺留 L2 流程樣板工具

**結論：未驗收，維持進行中。** 截至 2026-11-21 20:24 Asia/Taipei (UTC+08:00)，L2 的必做項尚未全部完成；本頁是狀態回報，不是縮減驗收或實機通過證明。

## 需求與證據

| 需求 | 證據 | 狀態 |
|---|---|---|
| 依 goodshepherdback 停用舊流程取得真實脫敏 XAML shape golden，含固定值與主要記錄欄位兩種 binding | 18:42 的修正後唯讀 CLI 回報 20 個候選、11 個輸入 record-field、0 個已辨識 fixed-value、0 個雙繫結。另唯讀稽核較早的 `artifacts/d365/goodshepherdback/20261121-055944-8774403-a3426db19ec54318b18d9f1f9c96cb10/activity-reference-shape.xml`（SHA-256 `BBD72C6C8CE372B29E2CECC88473CBC8B5BD9D363856DA0111B69AE8DB1BD91B`）雖有單一 ActivityReference 及 InArgument 固定值／欄位標記，卻也把 OutArgument 標為欄位繫結，且含未分類 ReferenceLiteral.Value；屬舊投影器產物，不可當有效 golden。原檔保留未改；不輸出或另存任何原始 XAML | 未完成 |
| 以 PE `[Input]` metadata 驗證情境輸入並產生單一活動 on-demand XAML | build 22 七活動 metadata 輸出：`C:\smv\guard-p8-evidence\p8-l2-pe-contracts-build22-detailed-20261121-1552.log`；六個既有情境符合契約，`AppointmentSigningActivity` 缺核定情境；其必要 `InputAppointment` 型別為 `Microsoft.Xrm.Sdk.EntityReference`。新增身份 guard 僅接受新版 Workflows 固定版本與有效 strong-name token，並格式化完整活動身份；尚未產生 XAML | 部分完成 |
| 08A 十項指定測試同名存在並通過，且保存先紅後綠 | 目前 3/10 同名存在：`WorkflowTemplate_Inputs_TypeCheckedAgainstActivityAttributes`、`WorkflowTemplate_UnknownMissingOrMistypedInput_Rejected`、`WorkflowTemplate_UsesNewWorkflowsAssemblyStrongName`；其餘 7 案缺失。新增 strong-name 測試先因空 token 未被拒絕而行為紅，identity guard 後通過；紅 SHA-256 `6315633CE6BCEF45AC5CA55B9A45432BA7DC363DCF2D69518342FEF0A8EC876E`、綠 SHA-256 `A7A6AEF6FF012EB1B778856CC554A3505A74064A165E7C560B1B414848631E14`。綠輪含 build 22 artifact 時 `WorkflowTemplate_*` 通過 5/5；但不替代 golden、新流程 lifecycle 或其他 7 項測試 | 未完成 |
| `LIVE-PREP-09`／`10` 新行為及新流程 Create／Activate／Execute／poll／finally cleanup | `LiveActivityProbe` 尚在複製舊流程；指定的 PREP 與 lifecycle 測試未建立 | 未完成 |
| 七活動情境 inputs 與 build 22 DLL 契約一致 | 17:00 的 `WorkflowTemplate_Inputs_TypeCheckedAgainstActivityAttributes` 使用本機 build 22 DLL，既有六個情境對應通過；`AppointmentSigningActivity` 沒有情境，不能猜測補情境 | 未完成 |
| goodshepherdback 實機一次並以 ID 清除，結束 `off`、`verify=0`、殘留 0 | 最新可核證續跑唯讀 verify 為 2026-11-21 20:15，exit 1，僅有 `UpdateAssembly`；輸出 `C:\smv\guard-p8-evidence\p8-l2-resume-verify-20261121-2015.log` SHA-256 `7BF132F5387AF37110F75EED70391135F58B489366244D3D2B37FE40C8D6B433`。本輪 shape 證據續核沒有重連或寫入組織，不能宣稱目前 `off` 或 `verify=0` | 未完成 |
| 完整 Release、文件檢查與完整 guard | 2026-11-21 19:46 完整 Deployment Release 通過 1,059/1,059，輸出 `C:\smv\guard-p8-evidence\p8-l2-full-release-20261121-194648.log` SHA-256 `2C865BE5D3B2DA93D64C33E1C386B81B743291EEC7CB8858279AC8112B1DC163`；同一輪文件說明 1,890 檔零失敗。完整 guard 未執行：唯一 `C:\smv\guard-p8` detached 在 `c8d088fe`，正式準備流程可能清理未追蹤資料且曾造成受保護副本刪除；沒有新建 guard | 測試通過；完整守門未驗證 |

## 執行與處置

### 2026-11-21 20:24 Asia/Taipei (UTC+08:00) shape 證據續核

- 重新核對既有 20:25 唯讀 shape 摘要 `C:\smv\guard-p8-evidence\p8-l2-workflow-shape-20261121-2025.log`，SHA-256 `255C5F1F214B968220FED69B31E8A05DFCA1C7C5A8C2C3F2C965DE8812D1AEF8`：snapshot 223、notEnabled 77、唯一可投影候選 20、輸入 record-field 11、已辨識 fixed-value 0、雙繫結 0、legacy catalog entries 15、identity present 76、type／assembly match 各 22、reversed match 0、registered legacy match 22。
- 對應脫敏診斷檔 `artifacts/d365/goodshepherdback/20261121-121745-6901699-56edbb64a7c24b0a9fe9df33055c14b4/workflow-shape-diagnostic.xml`（SHA-256 `AF2CF21D2A3F79DA454E77CC72C76DB3DAC42B8A2E9132F670B2CA0D0DE65596`）仍只有 record-field 標記，沒有 ReferenceLiteral；它是診斷投影，不是 golden。此核對沒有重連組織或執行寫入，也沒有重做條件未變的查詢。
- 此證據沒有補足 §4.6 的真實雙繫結 golden；不猜測 XAML／EntityReference 格式、不接通複製舊流程的 probe。十項指定測試仍僅 3/10 同名，完整 PREP-09／10、新流程生命週期、七活動情境、實機操作與清理均未完成；`verify` 最新可核證結果仍為 20:15 exit 1、唯一 `UpdateAssembly`，因此不宣稱 `off`／`verify=0`。完整 guard 因唯一 clone 的清理風險未執行，未宣稱 PASS；L2 仍未驗收。

### 2026-11-21 20:15 Asia/Taipei (UTC+08:00) 續跑組織狀態核對

- 依 08A §0.4 唯讀執行 `verify --org goodshepherdback`，exit 1，唯一差異 `UpdateAssembly`。安全輸出 `C:\smv\guard-p8-evidence\p8-l2-resume-verify-20261121-2015.log`，SHA-256 `7BF132F5387AF37110F75EED70391135F58B489366244D3D2B37FE40C8D6B433`。
- 沒有 apply、切換、rollback 或其他組織連線；此結果不能證明目前狀態為 `off`／`verify=0`。保留差異，待 L2 完成離線必做與受控新流程步驟後依核准順序處理。
- L2 仍未取得合格 shape golden，XAML builder、PREP-09／10、新 probe lifecycle、十項測試、七活動完整情境及實機清理均未完成；本次唯讀結果不改變未驗收狀態。

### 2026-11-21 19:50 Asia/Taipei (UTC+08:00) 全情境樹掃描補強

- 新增必要回歸 `WorkflowTemplate_InputsScenarioDiscovery_IncludesEveryModule`。先紅輸出因遞迴列舉入口不存在而編譯失敗，證明測試鎖定的是全目錄掃描能力；補入排序穩定的遞迴列舉後，該測試與 build 22 契約核對共通過 2/2。紅／綠輸出：`p8-l2-scenario-discovery-red-20261121-194509.log` SHA-256 `A035A8237CA02904240DE098316450B7D8B1D8C87EE865467303ABEA3AB77EAA`；`p8-l2-scenario-discovery-green-20261121-194557.log` SHA-256 `F191A383FBC9BE729838FAE68F96C57AC395627B20018B4FEF1D3C2B746523A3`。
- 使用 build 22 的完整 `WorkflowTemplate` 篩選通過 6/6，輸出 `p8-l2-workflowtemplate-filter-20261121-194913.log` SHA-256 `9C573FD8E5A3497BC1CEC231A0ADF7244929545DADF8BC14266669ECC4177CE5`；完整 Deployment Release 通過 1,059/1,059，輸出 `p8-l2-full-release-20261121-194648.log` SHA-256 `2C865BE5D3B2DA93D64C33E1C386B81B743291EEC7CB8858279AC8112B1DC163`。`check_file_documentation.py` 檢查 1,890 檔、零失敗；`git diff --check` 與修改 C# 檔 UTF-8 無 BOM／CRLF／檔尾 CRLF 通過。
- 現行測試改為遞迴掃描完整 P7 情境樹；完整盤點仍有六個已映射活動情境，沒有 `AppointmentSigningActivity` Workflow 情境。未改任何情境或期望檔、未連組織。`LiveActivityProbe`、golden、PREP-09／10、實機與安全完整 guard 等必做仍缺，L2 未驗收。

### 2026-11-21 19:29 Asia/Taipei (UTC+08:00) Strong-name 身份驗證

- 新增指定測試 `WorkflowTemplate_UsesNewWorkflowsAssemblyStrongName`。先紅輸出顯示空 `PublicKeyToken` 未被拒絕；修正後只接受 `SpeechMessage.Dynamics.Workflows`、版本 `1.0.0.0`、16 位十六進位 strong-name token，並由同一檢核產生 `ActivityReference` 完整 assembly-qualified identity。紅／綠輸出 SHA-256：`p8-l2-strongname-red-20261121-192441.log` `6315633CE6BCEF45AC5CA55B9A45432BA7DC363DCF2D69518342FEF0A8EC876E`、`p8-l2-strongname-green-20261121-192733.log` `A7A6AEF6FF012EB1B778856CC554A3505A74064A165E7C560B1B414848631E14`。
- 綠輪以本機 build 22 artifact 執行 WorkflowTemplate 篩選，5/5 通過；本機 artifact FileVersion `1.0.0.22`。其後完整 Deployment Release 通過 1,058/1,058，輸出 `C:\smv\guard-p8-evidence\p8-l2-full-release-20261121-192809.log` SHA-256 `F8F7E8E3F7F0F2F6E7A7B7789C2E7FD2EDAD9CF3518075F93023267CE55182F9`。此程式測試只驗身份契約，未產生 XAML 或連接組織。
- L2 仍未驗收；golden、XAML builder、PREP-09 新規則、probe lifecycle、其餘七項指定測試、Appointment 核定情境、實機及安全完整 guard 仍缺。

### 2026-11-21 19:14 Asia/Taipei (UTC+08:00) build 22 活動契約重驗

- 本機 `SpeechMessage.Dynamics.Workflows.dll` FileVersion 為 `1.0.0.22`，SHA-256 `FEED65A01B75372D825176B450BAE781419E527C3ECF80C78459E068352386E7`。以明確環境變數指定 DLL，執行 `WorkflowTemplate_Inputs_TypeCheckedAgainstActivityAttributes`，Release 通過 1/1。
- 七活動 metadata `[Input]` 名稱、型別及 Required 全部輸出；六個既有核定情境的契約為 pass。`AppointmentSigningActivity` 有必要 `InputAppointment : Microsoft.Xrm.Sdk.EntityReference`，但沒有核定 Workflow 情境，故明列未驗證，不推測或新增情境。
- 原始輸出 `C:\smv\guard-p8-evidence\p8-l2-pe-contracts-build22-20261121-191330.log` SHA-256 `33E982A9E000D368C93307668088FAA866DB96684B69AF6833937277D6C75F77`。僅讀本機 artifact，未連組織；此單項不改變 L2 未驗收狀態。

`WorkflowTemplateBuilder` 的輸入驗證與 PE reader 為離線部分成果，不代表 XAML 相容或流程可執行。19:29 完整 Deployment Release 1,058/1,058。19:29 strong-name 身份測試先紅後綠，鎖住 Workflows 組件簡名、固定版本、token 形狀與 AQN 格式。18:40 新增巢狀 `InArgument` `Value` 敏感哨兵測試，先紅（巢狀 `Value` 遺失輸入繫結種類），依最近輸入／輸出參數邊界修正後，18:41 projector／selection 回歸通過 13/13；紅／綠 SHA-256 分別為 `6EAF8252EA633D5C22A7416655516D9715216435D41C96F0BA6EC29E53BD7302`／`E64E946969AFC8F3527A10D621AF0BE69A3C26BCFD11E8B1736E6C74B9CD14EF`。18:42 正式 goodshepherdback 唯讀查詢仍未找到含已辨識 fixed-value binding 的合格 golden；此結果不排除尚未辨識的 XAML 序列化表示法。不得手寫合成 golden、接通仍複製舊 XAML 的 probe，或提前對組織套用 build 22。

依 08A D8-26 如實保留 L2 `in_progress`，不標驗收、不以縮減驗收結案；記錄缺口後續行階段 8。L1 的未驗收狀態與 P8-03 發布順序偏差仍按既有紀錄揭露。本回報沒有修改已推送 Git 歷史；本機及遠端最新已核對 SHA 為 `c4511ca335b3960803b0f481562954cbda0dc886`，此 SHA 不包含本頁及目前未提交變更。

### 2026-11-21 18:42 Asia/Taipei (UTC+08:00) 巢狀 Value 分類續查

- `Project_NestedInputValueAttribute_IsClassifiedAndRedacted` 先紅後綠：合成敏感哨兵與 `[record.sensitivefield]` 位於 `InArgument` 內的巢狀 `Literal.Value`；投影只輸出 `[FIXED_VALUE]`／`[RECORD_FIELD]`，不輸出原值或欄位名稱。最近參數邊界為 `InArgument` 才分類，`OutArgument` 不產生輸入 marker。
- `WorkflowActivityShapeProjectorTests|WorkflowShapeSelectionTests` Release 通過 13/13，輸出 `C:\smv\guard-p8-evidence\p8-l2-nested-value-green-20261121-1841.log`。
- 正式 `workflow-shape --org goodshepherdback` 唯讀重查 exit 0：snapshot 223、未啟用 77、唯一候選 20、輸入 record-field 11、fixed-value 0、雙繫結 0；摘要 `C:\smv\guard-p8-evidence\p8-l2-workflow-shape-nested-value-20261121-1842.log`。診斷投影不是 golden；未寫入組織。既有 `verify` 僅 `UpdateAssembly` 的證據仍為 17:53，不能據此宣稱目前 `off` 或 `verify=0`。
- L2 維持 `in_progress`、未驗收；八個指定測試、合格 golden、builder XAML、新流程 probe lifecycle、Appointment 情境契約、實機操作與清理、完整守門仍缺。本輪未提交或推送。

### 2026-11-21 19:05 Asia/Taipei (UTC+08:00) 續跑完整離線檢查

- 重新執行完整 Deployment Release，通過 1,057/1,057；原始輸出 `C:\smv\guard-p8-evidence\p8-l2-full-release-20261121-190341.log` SHA-256 `609F4A76EE4F51835810F12071FF57896428840EB363668C09555605F9D18310`。
- ProgressPage 28/28、`check_file_documentation.py` 1,890 檔零失敗、`git diff --check` exit 0；證據 SHA-256 依序為 `CE7A2FAB69FBED66FC12551919E7A1E049E7ADF64B2718CFCD67B262410518E4`、`31FFCD86D94E2474A525B0DAC558D5AA17FB265C006A5048A4D912F32CBE200F`、`E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`。18 份自有變更檔逐 byte 符合 UTF-8 無 BOM／CRLF／檔尾 CRLF。
- 完整離線測試不代表 L2 驗收：08A 十案仍缺八案；沒有合格 goodshepherdback 雙繫結 golden；builder 尚未產生 XAML；`LiveActivityProbe` 尚未改成新流程生命週期；Appointment 情境、七活動完整核對、實機執行／清理、`off`／`verify=0`、完整 guard 均未完成。
- L2 保持 `in_progress`、未驗收。完整 guard 因唯一 guard clone 的清理副本風險未執行；沒有組織寫入、提交或推送。

### 2026-11-21 18:57 Asia/Taipei (UTC+08:00) 紀錄同步檢查

- 更新 L2 回報、子／父 Trellis 任務與進度頁 DATA 後，重新驗證 `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 通過 28/28；證據 `C:\smv\guard-p8-evidence\p8-l2-progress-contract-20261121-1850.log` SHA-256 `C09FD1B78EDD1179ADC92F18879C87FD81E4A327300634DA0FE3E03C62DE362C`。
- `python tools/RepoQuality/check_file_documentation.py` 檢查 1,890 檔、0 失敗；證據 `p8-l2-file-documentation-20261121-1850.log` SHA-256 `31FFCD86D94E2474A525B0DAC558D5AA17FB265C006A5048A4D912F32CBE200F`。`git diff --check` exit 0；證據 `p8-l2-diff-check-20261121-1850.log` SHA-256 `5BC1399A70034DFF67A052D09A1A4FC2A36D1E652FEE85822CC42A4556EC692C`。
- 涉及的自有 C#、Markdown、JSON 與進度頁逐 byte 核對為 UTF-8 無 BOM、純 CRLF、檔尾 CRLF；L2 task JSON 可解析。受保護 `.ccg/tasks/line-token-config-separation/.turns.json` 未納入檢查或操作。
- 本次文件同步不改變驗收狀態：L2 仍 `in_progress`、未驗收；沒有完整 guard、提交、推送或組織寫入。

### 2026-11-21 17:00 Asia/Taipei (UTC+08:00) 更新

- 單項測試實際先紅原因是 analyzer 編譯錯誤 CA1861；修正後 Release 通過 1/1。此紅燈不符合行為測試先紅證據，十項測試的紅綠要求仍未完成。
- 以 build 22 DLL 對照六個既有情境 inputs 通過；`AppointmentSigningActivity` 無情境，因此七活動完整核對未完成，沒有新增猜測情境。
- 完整 Deployment Release 通過 1054/1054；該命令未設定 build 22 artifact 環境變數。此結果不證明組織狀態、golden、builder XAML、probe lifecycle 或實機結果。
- L2 維持未驗收；本更新不改變 16:40 起點所記錄的實機及守門缺口。

### 2026-11-21 17:11 Asia/Taipei (UTC+08:00) 檢查紀錄

- `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 通過 28/28，輸出 `C:\smv\guard-p8-evidence\p8-l2-progress-contract-20261121-1711.log`。
- `python tools/RepoQuality/check_file_documentation.py` 檢查 1,890 檔、0 失敗，輸出 `C:\smv\guard-p8-evidence\p8-l2-file-documentation-20261121-1711.log`。
- `git diff --check` exit 0，輸出 `C:\smv\guard-p8-evidence\p8-l2-diff-check-20261121-1711.log`。
- 本報告、兩份 Trellis implement 紀錄、決策紀錄與進度頁 DATA 已逐 byte 驗證 UTF-8 無 BOM、僅 CRLF、檔尾 CRLF；受保護的 `.ccg/tasks/line-token-config-separation/.turns.json` 未納入檢查或修改。

### 2026-11-21 17:15 Asia/Taipei (UTC+08:00) 續跑核對

- 依 08A §0.4 唯讀執行 `verify --org goodshepherdback`，exit 1，差異僅 `UpdateAssembly`；輸出 SHA-256 `096CA743623FED5F76749F0536AC01BB1540170630782BAE84454772FBCEA520`。沒有 apply、沒有其他組織連線；目前不能宣稱 `verify=0` 或 `off`。
- L2 必做尚未具備可安全產生新流程的 golden，故不把此組件差異提前 apply；L2 維持未驗收。

### 2026-11-21 17:26 Asia/Taipei (UTC+08:00) XAML 投影修正與重查

- 新測試 `Project_FixedValueAttribute_IsClassifiedAndRedacted` 先紅：`InArgument.Value` 的敏感測試值已被遮蔽，但沒有 `[FIXED_VALUE]` 分類標記；修正投影器後 Release 1/1 通過，並確認固定值與欄位值原文都不出現在投影中。
- 修正後 shape projector／selector Release 通過 11/11；正式 `workflow-shape` 只連 `goodshepherdback`、唯讀 exit 0，原記錄 20 個唯一舊活動參照、11 個 record-field 的計數未區分 In／OutArgument，已被 2026-11-21 18:29 更正。不能據此推斷所有未辨識 XAML 表示法不存在。
- 完整 Deployment Release 通過 1055/1055；build 22 六個既有情境核對通過，Appointment 情境仍缺。L2 golden、十個指定測試全數紅綠、XAML 產生、PREP 規則、probe lifecycle、實機結論、verify=0／off／零殘留及完整 guard 仍未完成。

### 2026-11-21 17:35 Asia/Taipei (UTC+08:00) 契約缺口續查

- 已保存的 build 22 PE 契約測試輸出確認 `AppointmentSigningActivity` 的 `[Input]` 包含必要 `InputParameter : System.String` 與 `InputAppointment : Microsoft.Xrm.Sdk.EntityReference`；P7 情境沒有對應的 Appointment 活動案例。
- `WorkflowTemplateBuilder.MatchesJsonKind` 尚無 `EntityReference` 的有界、明確 JSON 契約；不得猜測 appointment 記錄 ID／欄位表示法或補造情境。這是獨立於缺少真實 shape golden 的離線必做缺口。
- Deployment Release 1055/1055、shape regression 11/11、進度頁測試 28/28、文件說明 1,890 檔零失敗與 `git diff --check` exit 0 均有 17:29 前保存輸出；本次沒有修改程式或重跑測試。完整 guard 未執行，`C:\smv\guard-p8` 的既有清理風險仍有效。
- L2 仍 `in_progress`、未驗收；最近唯讀 verify 為 17:15 exit 1、僅 `UpdateAssembly`，不得宣稱目前 `off`／`verify=0`。合格 golden、八項指定測試與行為紅綠、builder XAML、PREP-09／10、新流程 lifecycle、七活動完整情境、實機一次、完整 guard、提交與推送仍待完成。

### 2026-11-21 17:38 Asia/Taipei (UTC+08:00) 文件同步後檢查

- `node --test tools/RepoQuality/Tests/Test-ProgressPage.cjs` 通過 28/28；輸出 `C:\smv\guard-p8-evidence\p8-l2-progress-contract-20261121-1738.log`。
- `python tools/RepoQuality/check_file_documentation.py` 檢查 1,890 檔、0 失敗；輸出 `C:\smv\guard-p8-evidence\p8-l2-file-documentation-20261121-1738.log`。
- `git diff --check` exit 0；輸出 `C:\smv\guard-p8-evidence\p8-l2-diff-check-20261121-1738.log`。本回報、決策、子／父 implement 紀錄與進度頁 DATA 五份文件均逐 byte 為 UTF-8 無 BOM、CRLF、檔尾 CRLF；輸出 `C:\smv\guard-p8-evidence\p8-l2-byte-check-20261121-1738.log`。
- 以上僅為文件與局部品質檢查，不補足 L2 的 golden、指定測試、builder、probe、七活動情境、實機、完整 guard 或 `verify=0` 必做項；L2 維持未驗收。

### 2026-11-21 17:42 Asia/Taipei (UTC+08:00) 指定測試名稱盤點

- 逐檔搜尋 `SpeechMessage.Dynamics.Deployment.Tests` 後，08A 十案目前只有 `WorkflowTemplate_Inputs_TypeCheckedAgainstActivityAttributes` 與 `WorkflowTemplate_UnknownMissingOrMistypedInput_Rejected` 同名存在；其餘八案缺失，不能宣稱十案完成。
- 缺少：`WorkflowTemplate_Build_SingleActivityReference_MatchesShapeGolden`、`WorkflowTemplate_UsesNewWorkflowsAssemblyStrongName`、`LivePrep09_ActivityWithCompleteInputContract_Proceeds`、`LivePrep09_ActivityWithoutInputContract_StillRejected`、`LiveActivity_TemplateLifecycle_CreateActivateExecuteDeactivateDelete`、`LiveActivity_FailureMidRun_DeletesTemplateWorkflow`、`LiveActivity_CancelledMidRun_DeletesWithUncancelledToken`、`LiveActivity_NeverReadsOrEnablesLegacyWorkflowForExecution`。
- 靜態盤點摘要保存於 `C:\smv\guard-p8-evidence\p8-l2-required-test-audit-20261121-1742.log`。此結果不構成紅綠測試證據；L2 繼續未驗收。

### 2026-11-21 17:53 Asia/Taipei (UTC+08:00) 續跑核對

- 按 08A §0.4 對唯一核准測試組織執行本會話一次唯讀 `verify --org goodshepherdback`，exit 1，唯一差異 `UpdateAssembly`；摘要證據 `C:\smv\guard-p8-evidence\p8-l2-resume-verify-20261121-1753.log`，SHA-256 `61C67972786B1C2B8F6F8A05FF51EADFA52A13F6DC7A3D1CF8BB129E2FD6`。沒有 apply、切換、rollback 或其他組織連線；不能宣稱目前 `off` 或 `verify=0`。
- Repo 搜尋只找到合成 XAML 測試片段與既有核定情境；Appointment 的現有案例是 `AppointmentPlugIn`，沒有 `AppointmentSigningActivity` Workflow 情境。沒有以這些資料推造固定值繫結或 `EntityReference` JSON 表示法，也沒有改情境／期望。
- L2 十項指定測試仍只有兩項同名存在；合格 goodshepherdback golden、其餘八項測試與先紅後綠、新流程 builder／probe lifecycle、七活動完整 inputs、實機 create／activate／execute／delete、結束 `off`／`verify=0`／殘留 0，以及完整 guard 均未完成。L2 保持 `in_progress`、未驗收；不套用僅含 `UpdateAssembly` 的差異，繼續在核准時間盒內處理可由既有契約證明的部分。

### 2026-11-21 18:18 Asia/Taipei (UTC+08:00) 文件同步後檢查

- 同步本回報、L2 子任務與進度頁 DATA 後，重新驗證進度頁契約測試 28/28、檔案說明檢查 1,890 檔且 0 失敗、`git diff --check` exit 0。證據依序為 `C:\smv\guard-p8-evidence\p8-l2-progress-contract-20261121-1815.log` SHA-256 `4BEA358FEC43180B3280001793FE554D3A0EBB8F20C04DAD0BAA3D763B309CE4`、`p8-l2-file-documentation-20261121-1815.log` SHA-256 `31FFCD86D94E2474A525B0DAC558D5AA17FB265C006A5048A4D912F32CBE200F`、`p8-l2-diff-check-20261121-1818.log` SHA-256 `5BC1399A70034DFF67A052D09A1A4FC2A36D1E652FEE85822CC42A4556EC692C`。
- 本回報、L2 子任務 implement、進度頁三份文件均逐 byte 確認 UTF-8 無 BOM、無裸 LF、CRLF 且檔尾 CRLF。此文件檢查不補足 L2 必做項；沒有程式變更、組織寫入、完整 guard、提交或推送。

### 2026-11-21 18:08 Asia/Taipei (UTC+08:00) focused Release 核對

- 重新執行 L2 focused Release，通過 11/11，原始輸出 `C:\smv\guard-p8-evidence\p8-l2-current-focused-20261121-1805.log`，SHA-256 `A97941E277FABD28C9CE774E75DDB234E9A04D6005FDD65A6B0FA5879DAAE9DA`。這是當前實作的 focused 測試，不表示 08A 十項指定測試完成；指定測試仍僅兩項同名存在，八項缺失，沒有完整紅綠證據。
- 本次沒有程式變更、組織寫入、完整守門、提交或推送。XAML builder、實機生命週期及起訖 `off`／`verify=0`／殘留 0 仍未完成；L2 維持 `in_progress`、未驗收。

### 2026-11-21 18:29 Asia/Taipei (UTC+08:00) 輸入繫結分類修正

- 新增 `Project_OutputArgumentRecordField_IsNotClassifiedAsInputBinding`，先紅：合成 XAML 的 `OutArgument OutputResult` 被投影為 `[RECORD_FIELD]`；修正後 WorkflowActivityShapeProjector Release 通過 6/6。紅／綠輸出分別為 `C:\smv\guard-p8-evidence\p8-l2-output-binding-red-20261121-1822.log` SHA-256 `8D79BC1122C24D27525B77259F5343915B86C9CC2282371DF48497DF1A6DC59E`、`p8-l2-output-binding-green-20261121-1824.log` SHA-256 `DCE08E78B7B9376EEAC6FC33A71E07569F45C690DD05F2296F2CFD4EF51538D8`。
- 投影器依最近的 In／OutArgument 邊界分類；只有 InArgument 子樹可產生固定值／記錄欄位 marker，其他文字以固定脫敏標記取代。正式 workflow-shape 對 goodshepherdback 唯讀 exit 0：20 個候選、11 個輸入 record-field、0 個已辨識 fixed-value、0 個雙繫結；脫敏診斷可見 OutputResult 為 `[REDACTED]`、InputParameter 為 `[RECORD_FIELD]`，仍非 golden。輸出 `p8-l2-workflow-shape-recheck-after-input-scope-20261121-1826.log` SHA-256 `255C5F1F214B968220FED69B31E8A05DFCA1C7C5A8C2C3F2C965DE8812D1AEF8`。
- 完整 Deployment Release 通過 1056/1056，輸出 `C:\smv\guard-p8-evidence\p8-l2-full-release-after-input-scope-20261121-1828.log` SHA-256 `85F09E6B2803C3739D6290A5AF5BA5A15376EDCEE43DEF6090005DF89045D244`。沒有組織寫入；L2 的 golden、八個指定測試、builder、probe lifecycle、七活動情境、實機 cleanup／off／verify=0、完整 guard 仍未完成，維持未驗收。

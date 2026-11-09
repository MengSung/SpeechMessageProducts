# 04　階段 5～8：Plugin／Workflow 重建、ChurchReport 改用共用層、D365 切換、收尾與偽語系驗收

> 分支：`1.0.0.11.Integration.P5.Worktree`～`.P8.Worktree`。
>
> **3.4 的閱讀方式：** 工作包規格沿用 3.3；每個階段開頭加「工作包總表」（依賴、實機、時間盒）。原本由使用者執行的 goodshepherdback 操作改為 **Codex 依 00 §11 自行執行**；正式組織第一次寫入前需使用者回「可以」（00 §9-2）。每包完成條件＝00 §7.1 五項＋該包列出的測試，不得加碼（D29）。

---

# 階段 5　Plugin／Workflow 重建（36 個 DLL → 2 個）

**框架核定（使用者 2026-11-04）：** 所有實際部署至 D365 沙箱的 Plugin 與 Workflow 組件一律以 `.NET Framework 4.6.2`（`net462`）為目標；`LineMessagingProcessor.Core`、`SpeechMessage.Localization` 維持 `net462;net10.0` 雙目標。測試執行器或離線舊程式 harness 可在明確標示下保留 `net48`，但不得讓 `net48` 產品組件進入部署包。goodshepherdback 曾拒絕 net48；P0-13 已實證 net462 可註冊並執行。伺服器 `mscorlib` 的 `FrameworkFileVersion=4.8.x` 是執行環境版本，不代表 Plugin 的目標框架。

- **估時：** 原估 20～27 天；依實際速度約 8～12 個工作天（00 §15）。
- **目標：** 舊外掛與工作流程活動的功能，全部由 `SpeechMessage.Dynamics.Plugins`、`SpeechMessage.Dynamics.Workflows` 承接。
  - 規則全部在 `GoodShepherd.Core`；
  - 外殼很薄；
  - 每一項都經過黃金測試與影子模式比對。

## 階段 5 工作包總表（3.4）

| WP | 名稱 | 依賴 | 實機（Codex） | 時間盒 |
|---|---|---|---|---|
| P5-01 | 外殼基礎與測試替身 | P2-04、P2-11 | 無 | 1 天 |
| P5-02 | 舊程式黃金資料產生器 | — | 無 | 1 天 |
| P5-03 | 影子模式比對 `shadow`＋佈署擴充（映像、活動群組、`switch`、`ui-export／import`） | P5-01 | goodshepherdback 唯讀比對 | 1 天 |
| 更新回復前置 | Plugin 自身 FileVersion 追蹤、只更新 FileVersion、回復 | P5-01 | **有** | 半天 |
| P5-04 | 國際化 Plugin | P5-03、更新回復前置、P2-08、P2-10 | 影子模式 | 1 天 |
| P5-05 | 週報 | P5-03、更新回復前置 | 影子模式 | 1 天 |
| P5-06 | 出席 | P5-03、更新回復前置 | 影子模式 | 1 天 |
| P5-07 | 課程（8 個舊專案） | P5-03、更新回復前置 | 影子模式 | 2 天（超過依 00 §12 拆 a／b） |
| P5-08 | 預約簽核 | P5-03、更新回復前置 | 影子模式 | 1 天 |
| P5-09 | QR | P5-03、P2-09a、P2-09b | 影子模式 | 1 天 |
| P5-10 | 關懷 | P5-03、更新回復前置 | 影子模式 | 1 天 |
| P5-11 | 收費 | P5-03、P4-03 | 影子模式 | 1 天 |
| P5-12 | 訊息與群發（風險最高） | P5-03、P2-05、P2-06 | 影子模式 | 2 天（超過依 00 §12 拆 a／b） |
| P5-13 | 刪除不移植的舊項目 | P5-04～P5-12 | 無 | 半天 |

`SpeechMessage.Dynamics.Workflows` 在第一個需要 `CodeActivity` 的模組（P5-05）建立，同一步加入 `.sln`。P5-04～P5-12 在 D31 的 2 包上限內可平行，但同一時間只允許一個模組在 goodshepherdback 做影子模式（組織寫入鎖）。

## 5.1 共同基礎（先做）

### P5-01　外殼基礎與測試替身

**`SpeechMessage.Dynamics.Plugins\Infrastructure\`**（net462；Workflows 專案以原始碼連結共用）

| 型別 | 職責 |
|---|---|
| `PluginInput` | 從 `IPluginExecutionContext` 取出 MessageName、PrimaryEntityName、PrimaryEntityId、Depth、Target、Pre／PostImage、UserId、OrganizationName，做成不可變物件。規則只吃它，不碰 `IServiceProvider` |
| `PluginShellBase` | 共同流程：取服務 → 防遞迴（`Depth > 2` 返回，沿用舊判斷）→ 讀步驟設定（Mode＝Live／Shadow）→ 呼叫規則 → 例外處理。業務驗證錯誤丟 `InvalidPluginExecutionException`，訊息為字典的通用文字＋事件 ID；其他例外記錄後重新丟出，**不吞錯** |
| `StepConfiguration` | 解析 unsecure JSON（Mode、其他非機密選項）與 secure JSON（tokenKey）；格式錯誤丟例外並附繁中訊息 |
| `RecordingOrganizationService` | 影子模式用：讀取直接轉給真的服務；Create、Update、Delete、Execute 只記錄、不執行，寫入追蹤記錄（JSON，單筆超過 8 KB 時分段） |
| `ShadowPushRecorder` | 影子模式下，推播只記錄「收件人 ContactId、範本 key、Retry Key」，不呼叫 LINE |

**`SpeechMessage.Dynamics.Plugins.Tests\Fakes\`** 提供四個測試替身：
- `FakeOrganizationService`：記憶體實體表，支援 Retrieve、RetrieveMultiple（QueryExpression 與 FetchXML 的常用子集）、Create、Update、Execute；
- `FakePluginExecutionContext`；
- `FakeTracingService`；
- `FakeServiceProvider`。

**先寫的測試：**
- `Shell_DepthExceeded_DoesNothing`
- `Shell_ShadowMode_NoWritesExecuted_AllRecorded`
- `Shell_ShadowMode_NoPushCalled`
- `Shell_BusinessError_ThrowsInvalidPluginExecutionWithEventId`
- `Shell_UnexpectedError_RethrownNotSwallowed`
- `StepConfig_InvalidJson_ThrowsChineseMessage`
- `Recording_LargePayload_SplitIntoChunks`

### P5-02　舊程式黃金資料產生器 `tools\LegacyGolden\PluginHarness`

- **專案設定：** net48 console，不進 .sln；只作離線舊程式黃金資料測試，不部署至 D365 沙箱。
- **編譯舊原始碼：** 把舊 Plugin 原始碼以 `<Compile Include>` 編進 harness。
  - 原始碼先用 `Convert-TextEncoding.ps1` 轉成 UTF-8，放在暫存資料夾；**不修改** `.其他解決方案` 原檔；
  - 改用 `Microsoft.CrmSdk.CoreAssemblies 9.0.2` 編譯，舊程式原本用的是 Xrm.Sdk 8.2。
- **替身類別：** 舊程式的 `LineUtilityClass`、`Line.NotifyBot`、檔案與 `Trace` 寫入，以 harness 內同名的替身取代，只記錄呼叫不對外。
- **輸入與輸出：**
  - 輸入：測試情境 JSON，內容為 Target、PreImage、FakeOrganizationService 的初始資料，全部是假資料；
  - 輸出：`Golden\Legacy\{舊專案}\{情境}.json`，記錄所有 D365 寫入與 LINE 呼叫。
- **舊程式編不過時：** 那一項改用影子模式在 goodshepherdback 比對（P5-03），並在回報中標明。

### P5-03　影子模式比對：佈署專案的 `shadow` 指令

- **位置：** v3.2 的 `tools\ShadowCompare` 不再建立，改為佈署專案 `SpeechMessage.Dynamics.Deployment` 的 `shadow` 指令（D22、附錄 G）。
- **連線：** 只連 goodshepherdback（白名單）；只讀；由 Codex 執行（D24）。
- **佈署擴充：** 本 WP 同時讓佈署專案支援映像（sdkmessageprocessingstepimage）、Workflow 活動群組名稱、`switch`（含影子模式切換）與 `ui-export`／`ui-import`（D26）。
- **比對方式：**
  1. 讀 `plugintracelog` 中新 Plugin 在影子模式寫下的「打算寫入」；
  2. 讀同一筆記錄在舊 Plugin 執行後的實際欄位值；
  3. 逐欄位比對，輸出差異報告 `artifacts\shadow\{日期}.md`。
- **判定：** 差異為 0 才可以切換（設計第十七章）。
- **先寫的測試：** 以假資料測解析與比對邏輯，包括欄位相同、不同、只存在一邊三種情況。

### 更新與回復前置驗收：首個正式 Plugin 佈署 goodshepherdback 之前

**使用者 2026-11-04 核定：** 原 P0-13 第 8、9 步移至階段 5。先以骨架完成下列驗收，再佈署第一個正式 Plugin。3.4 起全部由 Codex 依 00 §11 執行，唯一組織 goodshepherdback。

1. **先補自身 FileVersion 追蹤：** 以 TDD 讓 Plugin 追蹤輸出自身組件的 FileVersion，不能拿固定 AssemblyVersion 或伺服器 `mscorlib` 的 FrameworkFileVersion（目前觀察為 4.8.x）代替。兩者分別是 Plugin 組件檔案版本與伺服器執行環境版本；測試確認數值確實來自 Plugin 組件、無秘密；通過 00 §8 守門後，Codex 接著執行第 2～5 步。
2. **保存第一版：** 清理與 trace 驗收前提仍成立；以 trace、secureConfig 空白、enabled=true 註冊骨架，verify=0、smoke trace 通過。於 repo 外保存第一版已合併 DLL 與清單，記下原步驟資料庫 ID、追蹤中的 Plugin FileVersion；不複製金鑰。
3. **只更新 FileVersion：** AssemblyVersion 固定 `1.0.0.0`，沿用相同名稱、簽章及步驟；只遞增 SpeechMessageBuildNumber，Release 明確打包並通過 Test-PluginAssembly。plan 必須只有 UpdateAssembly，不能重建步驟。apply 前保存該次自動產生的 export.json（是本次更新的 apply 目錄，不是初次安裝前的空備份）。verify=0、smoke trace 後以 `export` 比對確認原步驟 ID 不變，且沙箱追蹤顯示新的 Plugin FileVersion。
4. **回復第一版：** 以 `rollback --org goodshepherdback --from <本次更新apply前的export.json>` 還原；rollback 只還原組件內容與既有步驟啟用狀態，不還原 unsecure／secure 設定或秘密，因此全程維持相同 trace 設定。將本機比較用 DLL 換回保存的第一版，再 verify=0、smoke trace，確認步驟 ID 不變及追蹤回到第一版 Plugin FileVersion。
5. **收尾：** 清單設回停用，plan → 審核 → apply → verify=0，追蹤改回「例外狀況」，確認測試資料清理，寫一頁操作紀錄；同一步驟失敗兩次依 00 §9-7 回報，不把本機檔案版本當作沙箱證據。

**完成定義：** 1～5 全部完成，三次追蹤的 Plugin FileVersion 依序為「第一版 → 新版 → 第一版」，步驟 ID 全程不變，殘留 0。

## 5.2 每個模組的固定做法（P5-04～P5-13 都套用）

1. **盤點：** 寫成 `wp-reports\P5-xx-inventory.md`，內容包括：
   - 舊檔案、行數、觸發（實體、訊息、階段、模式、篩選屬性、映像）；
   - 讀寫的欄位、外部呼叫；
   - 與其他副本的差異。
   - 觸發資訊由 Codex 以佈署專案 `export` 或附錄 D 的 FetchXML 自行匯出 goodshepherdback 核對；正式組織的唯讀匯出放在 P7 前，併入 00 §9-2 對該組織的第一次確認。
2. **黃金資料：** 用 P5-02 產生，至少 3 個情境：正常、邊界、錯誤。
3. **先寫規則測試：** 放在 `GoodShepherd.Core.Tests`，雙目標都跑，以黃金資料為期望值。
4. **實作規則：** 放在 `GoodShepherd.Core\<模組>`。
   - 資料讀寫走 `ToolUtility.Core`；
   - 文字走字典，中文抽成 key；
   - 時間走 `IChurchClock`；
   - 推播走 `MemberNotificationService`；
   - 需要畫圖或撥款時寫對外待辦。
5. **寫外殼與外殼測試：** 放在 Plugins 或 Workflows，以及 `Plugins.Tests`。
6. **步驟註冊清單：** 寫進 `d365\registration\plugins.registration.json`，欄位依附錄 B（不再另寫「步驟註冊表.md」）；清單與 DLL 一致性測試（L2）必須通過。
7. **影子模式驗收（Codex 執行）：** Codex 在 goodshepherdback 以佈署專案 `plan`／`apply`（步驟停用）→ `switch` 影子模式 → `smoke`，再以 `shadow` 指令比對到差異為 0；結束後切回、清理，留一頁操作紀錄。

**每個模組的完成定義：** 1～7 全部完成；規則雙目標測試與外殼測試通過；註冊清單一致性（L2）通過；影子比對差異 0；殘留 0。

## 5.3 模組順序與對照（依設計 10.3、10.4 節）

由風險低到高排序：

| WP | 模組 | 舊程式 | 新外殼 | 規則 |
|---|---|---|---|---|
| P5-04 | 國際化 | （新） | `LocalizedTextChangedPlugin`（new_localizedtext Create／Update／Delete，非同步，更新 `Account.new_textchangedon`）、`MemberLanguageChangedPlugin`（contact Update 篩選 `new_preferredlanguage`，非同步，直接換綁宮格）、`ChurchLocaleChangedPlugin`（account Update，語系欄位：同步驗證＋非同步建立 `RichMenuGenerate` 待辦） | Localization、`Members\LanguageRule` |
| P5-05 | 週報 | `WeeklyReportPlugIn`、`WeeklyReportLinePlugIn`、`WeeklyReportWorkflow` | `WeeklyReportStatisticsPlugin`（PreImage）、`WeeklyReportNotificationPlugin`（非同步、直接推播）、`WeeklyReportGenerationActivity` | `WeeklyReports\*` |
| P5-06 | 出席 | `PresentRecordLinePlugIn` | `PresentRecordPlugin`（PreImage；非同步推播） | `Attendance\PresentRecordNotificationRule` |
| P5-07 | 課程 | `DiscipleLessonsPluIn`、`QrCodeWorkflow`、`StorLessonSignningPlugIn`、`StorLessonSigningWorkflow`、`StorLessonSigningLibrary`、`TwoWingsPlugIn`、`YangMeillcStorLessonsPlugIn`、`EquipmentWorkflow` | `LessonLinkPlugin`、`StorLessonSigningPlugin`、`StorLessonSigningActivity`、`LessonProgressionPlugin`、`EquipmentStatusActivity` | `Lessons\*`（兩套進階評估合成一個引擎，差異寫進教會設定，R3） |
| P5-08 | 預約簽核 | `AppointmentPlugIn`、`AppointmentSigning`、`AppointmentWorkflow`、`AppointmentSigningLibrary` | `AppointmentPlugin`、`AppointmentSigningActivity` | `Appointments\AppointmentSigningRule` |
| P5-09 | QR | `SundayQrCodeWorkflow`、`CalculateQrCodeScanNumberWorkflow`、`PersonalQrCodeWorkflow` | `MeetingQrCodeActivity`、`MeetingScanCountActivity`、`PersonalQrCodeActivity`（需要圖片時建立 `QrImageGenerate` 待辦） | `QrCodes\*` |
| P5-10 | 關懷 | `NewPersonCareWorkflow`、`HappyBirthdayWorkflow`、`RecursiveWorkflow` | `NewPersonCareActivity`、`ContactAnniversaryActivity`（D365 遞迴流程照舊呼叫，D20） | `Care\*`（「今天」依教會時區；祝福推播的 Retry Key 由「類型＋會友 ID＋當地日期」算出固定 GUID） |
| P5-11 | 收費 | `AllotPlugIn`、`AllotWorkflow`、`AllotTlsWorkflow`、`FeePushLineWorkflow` | `FeeAllotPlugin`（建立 `AllotQuery` 待辦）、`FeeAllotActivity`、`FeeNotifyActivity`（非同步、直接推播，Retry Key＝收費單 ID） | `Fees\*` |
| P5-12 | 訊息與群發 | `LineMessagePlugIn`（約 19,892 行，含副本） | `OutboundLetterPlugin`（letter Create／Update，非同步；只處理「外送」方向；直接推播 1 人；快速行銷活動群發，設計 10.10 節） | `Messaging\OutboundLetterRule` |
| P5-13 | 刪除 | `TemplatePlugIn`、`ExampleWorkflow`、`WorkflowTemplate`、`NewPersonCareWorkflow-ForDebug`、`Line.NotifyBot`（LINE Notify 已於 2025-03-31 終止）、`Trace` | 不移植 | — |

**暫緩：** `SmsPlugInClassLibrary`、`SnsPlugInDatabase`。原始碼位置未提供，不處理。

## 5.4 P5-12 訊息與群發的額外要求（風險最高）

### 必須寫的測試（設計 10.9、10.10 節）

- `Letter_Inbound_Ignored`：D19 入站紀錄不觸發推播。
- `Letter_Outbound_PushOneRecipient_RetryKeyIsActivityId`
- `Letter_RecipientOtherChurch_FailedReasonChurchMismatch_NoHttp`
- `Letter_NotBound_FailedReasonNotBound_NoThrow`
- `Letter_Blocked4xx_FailedReasonBlocked_NoThrow`
- `Letter_429_ThrowsForSystemJobRetry`
- `Letter_ContentKeptAsIs_FrameTextLocalized`
- `QuickCampaign_100Activities_100IndependentSends`：以假服務模擬 100 次觸發，每次只送 1 人，且 Retry Key 都不同。
- `QuickCampaign_SystemJobRerun_NoDuplicateDelivery`
- `Source_NoMulticastOrBroadcast`：架構測試，原始碼不得出現 multicast、broadcast 的端點。

### 活動狀態回寫

- **成功：** 完成。
- **失敗：** 失敗並附原因。原因代碼用字典 key，例如 `Messaging.Failure.NotBound`。
- **彙總通知：** LineWebhook 每 5 分鐘彙總失敗數，超過門檻時經共用例外通知入口通知管理者，先寫 `Exception.log`、再發 LINE。

## 5.5 Workflows 專案

- **專案設定：** `SpeechMessage.Dynamics.Workflows`，net462，參考 `Microsoft.CrmSdk.Workflow`，打包、簽章、ILRepack 與 Plugins 專案相同。
- **活動屬性：**
  - 每個 `CodeActivity` 的 InputArgument／OutputArgument 與舊活動同名、同型別；
  - 群組名稱「音訊科技」（WorkflowActivityGroupName）；
  - 顯示名稱寫在註冊清單 `plugins.registration.json`。
- **改參數的規則：** 活動要改參數時，新增一個新類別（例如 `WeeklyReportGenerationActivityV2`），不改舊類別（設計 10.8 節）。

**先寫的測試：** 以 net48 測試執行器透過 `WorkflowInvoker` 執行 net462 活動；net48 僅是測試宿主，不是部署組件目標。
- `WorkflowInvoker` 需要的 `IWorkflowContext`、`IOrganizationServiceFactory` 以擴充提供；
- 斷言輸入輸出參數與規則呼叫。

## 階段 5 完成判準

- 使用者 2026-11-04 核定：首個正式 Plugin 佈署 goodshepherdback 前，已完成自身 FileVersion 追蹤及上述更新／回復驗收。
- 5.3 表每一列都有：外殼、規則、雙目標測試、外殼測試、註冊清單、影子模式差異 0 的報告。
- `Test-PluginAssembly.ps1` 對兩個 DLL 都通過。
- 搬入的程式：`I18nCheck` 中文字串為 0，BannedApi 0 違規。
- Codex 在 goodshepherdback 完成全部影子模式比對；`verify` 無差異。

---

# 階段 6　ChurchReport 改用共用層

- **估時：** 原估 6～8 天；依實際速度約 3～4 個工作天。
- **目標：** ChurchReport 不再保留與共用層重疊的程式；行為不變（現役教會在用，這是最高優先約束）。
- **依賴與時間盒：** P6-01 ← P2-09a、P5-09；P6-02 ← P5-05；P6-03 ← P2-06、P5-12；P6-04、P6-05、P6-06 無前置；P6-07 ← P6-01～P6-06。每包 1 天，P6-07 為 2 天（超過依 00 §12 拆 a／b）。實機：完成判準的頁面驗收由 Codex 執行。

| WP | 內容 | 先寫的測試 |
|---|---|---|
| P6-01 | QR：ChurchReport `Tools\` 的 `QrCodeUtility`、`SundayQrCodeUtility`、`PersonalQrCodeUtility`、`SmallGroupQrCodeUtility` 改呼叫 `GoodShepherd.Core\QrCodes`，刪除副本 | 掃碼解析黃金測試：同一批 QR 內容，新舊結果相同 |
| P6-02 | 週報：改呼叫 `GoodShepherd.Core\WeeklyReports` | 週報計算黃金測試，資料取自階段 5 |
| P6-03 | 送訊：移除 P2-06 留下的 `[Obsolete]` 薄殼，呼叫點直接用新入口 | 既有測試全綠；P2-06 的黃金檔維持相同 |
| P6-04 | `AddToolUtility` 舊多載移除，改傳 `"ChurchReport"` | 連線鍵測試 |
| P6-05 | 搬走部分的字改經字典；其餘維持現狀，以棘輪控管（決定 I6） | `I18nCheck` 基準：ChurchReport 數量下降並更新基準 |
| P6-06 | `Microsoft.AspNetCore.* 2.2.x` 套件參考評估：net10 專案不需要這些舊套件時移除，否則保留並記錄原因 | 建置與既有測試 |
| P6-07 | R2 清冊清零（D28）：`unresolved-r2-inventory.json` 列的 ChurchReport 公開型別，依「搬到共用／產品層」或「改為 internal 且不影響 MVC、Razor、JSON 序列化」逐一處理；MVC Controller 與 Razor／JSON 需要的公開模型，以架構測試的明確分類（Controller、ViewModel、序列化契約）納入允許清單並附理由 | 清冊為空；ChurchReport 既有測試全綠；R2 架構測試改為嚴格模式 |

**完成判準：**
- ChurchReport 全部既有測試綠；
- QR 簽到、週報頁面由 Codex 以 Playwright（User Secrets 的測試帳號）在測試站實際操作並截圖確認，行為不變；
- `Find-DuplicateCode` 中，QR、週報、送訊相關項目從基準移除。

---

# 階段 7　D365 9.1 切換（逐組織）

- **估時：** 每個組織 1～2 天，加 1 週觀察。
- **原則：** 一次只動一個組織，每一步都能回復。依設計第十七章。
- **3.4（D24 修訂、D25）：** 全部步驟由 Codex 執行。每個正式組織**第一次**寫入前，Codex 把 plan、影響、回復方式、預定維護時段寫成一頁交使用者；使用者回「可以」後由 Codex 在該時段執行。同一組織之後同類型的步驟不必再問；任何一步失敗立即依手冊回復並回報。
- **工作包與時間盒：** P7-01（← 階段 5 全部）1 天；P7-02 手冊＋goodshepherdback 全流程演練（← P7-01）1 天；之後每個正式組織的切換是作業，不另算工作包：每組織 1 天＋觀察 1 週。

## P7-01　佈署專案的正式組織支援與 `switch`（v3.2 的 `tools\D365StepSwitch` 併入佈署專案，D22）

- **正式組織：** 佈署專案在本 WP 才開放正式組織：設定檔列出允許的正式組織；執行時必須加 `--production` 並輸入組織名稱確認，而且只能執行已審核的 plan 檔。
- **指令（附錄 G）：**

| 指令 | 用途 |
|---|---|
| `export` | 匯出組件、步驟、映像、啟用狀態，並依附錄 D 的 FetchXML 匯出使用自訂活動的流程清單，作為回復的依據 |
| `plan --switch-to new` | 列出要停用的舊步驟、要啟用的新步驟、需要人工修改的流程活動 |
| `switch --plan <檔案>` | 依 plan 執行；執行前自動 `export`；每一步記錄到 `artifacts\d365\{組織}\{時間}\` |
| `rollback --from <紀錄檔>` | 依紀錄反向執行：重新啟用舊步驟、停用新步驟 |
| `verify` | 確認目前狀態與清單、plan 一致 |

> 流程（Process）中的「呼叫哪個自訂活動」無法完全以 API 安全修改。Codex 依序嘗試：① 以 SDK 停用流程 → 更新流程定義 → 啟用（先 export 備份）；② 做不到時以瀏覽器自動化在流程設計器修改；③ 兩者都不可行的項目才列給使用者。每一項都在 goodshepherdback 先演練。

**先寫的測試：**
- 以假服務測 `switch` 與 `rollback` 的對稱性：`switch` 後再 `rollback`，狀態回到 `export` 的結果；
- 沒有 `--production` 或組織名稱確認不符時，正式組織拒絕執行；
- 非白名單組織拒絕執行；
- 未審核（雜湊不符）或過期（組織現況已變）的 plan 拒絕執行。

## P7-02　切換手冊 `驗收\P7_組織切換手冊.md`

依設計 17 章的步驟表，每一步都要有：
- 操作；
- 檢查方式；
- 回復方式；
- 預計時間。

**組織順序：** goodshepherdback → 使用量最小的教會 → 使用量最大的教會。

**新欄位放上表單：** contact、account 等各教會共用實體的表單不打包（D26）；由 Codex 以 SDK 更新各組織表單定義（先 export 備份、發布後驗證），做不到時以瀏覽器自動化，仍不可行才列給使用者。

**每個組織還要做：**
- 設定教會的預設語言與時區，預設為 zh-TW、Asia/Taipei；
- 以 `protect-token` 寫入 Channel；
- 停用舊 DLL 之前先完整匯出。

**完成判準：**
- 觀察 1 週內沒有新增錯誤；
- 舊 DLL 已移除；
- 每間教會的語系與時區設定通過 `ChurchLocaleChangedPlugin` 的驗證。

---

# 階段 8　收尾＋偽語系驗收＋教會設定網頁

- **估時：** 原估 4～6 天；依實際速度約 3～4 個工作天。原本英文試點的翻譯工時改做這裡的教會設定網頁與奉獻類別清單。
- **依賴與時間盒：** P8-01 ← P1-11（需要使用者決定類別合併時依 00 §9-5，同時先做 P8-02）；P8-02 無前置；P8-03 ← P8-01、P8-02；P8-04 ← P8-03；P8-05 ← 其餘全部。每包 1 天，P8-05 半天。實機：P8-01 的轉換、P8-04 的偽語系驗收由 Codex 執行。
- **依據：** 設計 3.6、5.9、7.7、16.3、16.4 節；決定 I9、I10。

## P8-01　奉獻類別清單 `new_offeringcategory`

1. **盤點與確認：**
   - 確認實體與欄位的邏輯名稱：收費單 `new_fee.new_category`；奉獻預約實體上的 `new_dedication_category`，實體名稱以程式中的 `aDedicationBookingEntity` 來源確認；
   - 列出所有寫死處，以 `rg` 確認行號：
     - `Tools\DonationFeePaymentProcessor.cs:1460`
     - `Tools\RecurringDonationPaymentProcessor.cs:1012`
     - `Views\Dedication\DonationPaymentView.cshtml:1003`
     - `Views\Dedication\KeyInDedicationFeeViewWeb.cshtml:679`
     - `WebServiceConnector\DonationPaymentProcessor\DonationPaymentProcessor.EntityMapper.cs:132`
     - `Models\DonationPaymentFormModel.cs:39`
     - `Services\DonationPaymentModelAssembler.cs:189`
     - `Services\PaymentFeeTypeHelper.cs:158`
2. **轉換指令：佈署專案的 `migrate-offering-categories`**（v3.2 的 `tools\D365Provisioning` 已併入佈署專案，D22；由 Codex 執行，正式組織依 00 §9-2）：
   - `plan`：依教會統計實際用過的選項值，產生預計建立的清單。名稱沿用現有顯示文字；「十一奉獻」與「什一奉獻」**不自動合併**，列出請使用者決定。
   - `apply`：Upsert 類別，以 `new_accountid`＋`new_code` 為替代索引鍵；回填收費單與預約的 `new_offeringcategoryid`。
     - 每批 100 筆，用 `ExecuteMultiple`；
     - 可中斷續跑，已回填的跳過。
   - `verify`：每間教會「有選項值但沒有查閱值」的筆數必須是 0。
3. **讀取改用清單：**
   - ChurchReport 的奉獻相關讀取改以 `new_offeringcategoryid` 為準；
   - 過渡期查閱值是空的，才以選項值對照；
   - 寫死清單全部改成讀教會清單。
4. **識別規則：**
   - 名稱可以重複，一律以 ID 識別（AGENTS.md 資料不重複規則）；
   - 同一消費集合中出現重複 ID 時拒絕發布並診斷。

**先寫的測試：**
- `Plan_GroupsByChurchAndOptionValue`
- `Plan_SimilarNames_NotMerged`
- `Apply_TwiceIdempotent`
- `Apply_ResumeAfterInterruption`
- `Read_LookupPreferredOverOptionSet`
- `List_SameNameDifferentIds_BothKept`
- `List_DuplicateId_PublicationRejected`
- `Settings_ChurchA_CannotSeeChurchB`

## P8-02　角色名稱改為字典＋教會覆寫

1. **盤點：** 會籍或職分在 D365 的存法：選項、文字或查閱。
   - 若以**中文文字**存放並被程式拿來比對：**停止並回報**。需要先決定轉換為代碼的方式，屬於資料轉換，不可逆。
2. **系統角色代碼：** `Role.PastorCouple`、`Role.ZoneShepherd`、`Role.ZoneLeader`、`Role.GroupLeader`、`Role.AssistantGroupLeader`、`Role.CoreWorker`、`Role.Member`、`Role.NotInGroup`、`Role.Newcomer`、`Role.OtherChurch`、`Role.Closed`。
   - 中文名稱放進 `GoodShepherdText.resx`；
   - `PersonalController.cs:1457` 與 `_GeneralGroupGrids.cshtml:1313` 的寫死清單，改為由字典加教會覆寫產生。
3. **權限：** 權限判斷一律用代碼。

**先寫的測試：**
- `RoleNames_ChurchOverride_AssistantGroupLeaderShowsInternLeader`
- `RolePermission_IndependentOfDisplayName`
- `RoleList_OtherChurchOverrideNotApplied`

## P8-03　教會設定網頁（ChurchReport 新區域 `ChurchSettings`）

1. **權限：**
   - 先盤點 ChurchReport 現有「教會管理員」的判定方式；**沒有可靠機制時停止並回報**；
   - 教會由伺服器依登入者判定，不接受網頁送來的教會 ID。
2. **名詞頁：**
   - 只列白名單（`GoodShepherd.Core\Settings\ChurchTermWhitelist`）；
   - 每列包含：系統預設、本教會名稱、預覽、還原預設；
   - 長度上限沿用 I18nCheck 的 `limits.json`；
   - 存檔時 Upsert `new_localizedtext`。
3. **清單頁：** 奉獻類別的新增、停用、排序、會計科目。
4. **修改紀錄：** 由工具在 `new_localizedtext`、`new_offeringcategory` 開啟 D365 稽核（audit）；設定頁提供查詢。
5. **生效：** 由 `LocalizedTextChangedPlugin` 更新改字時間，60 秒內生效。
6. **安全：** 頁面本身也要防範 CSRF 與 XSS；輸入的文字在輸出時一律編碼。

**先寫的測試**（控制器與服務層；用 `WebApplicationFactory` 或直接測服務）：
- `Save_NonAdmin_Forbidden`
- `Save_ChurchIdFromRequestIgnored`
- `Save_TooLong_Rejected`
- `Save_NonWhitelistedKey_Rejected`
- `Reset_RemovesOverride`
- `Save_ScriptInput_EncodedOnOutput`

## P8-04　偽語系全面驗收

1. **工具 `tools\PseudoLocaleAudit`：**
   - net10 console，使用 Microsoft.Playwright，以 User Secrets 的測試帳號自動登入；
   - 帳密不寫入任何檔案、截圖或報告；
   - 依 `audit-urls.json` 走訪：教會設定頁、LIFF 頁、從共用層搬出的頁面。ChurchReport 未國際化的舊頁面依 I6 排除，清單要寫明排除理由。
   - **抓兩種問題：**
     - 可見文字中「含中文但沒有 ［ ］」的片段；
     - `scrollWidth > clientWidth` 的元素，代表被截斷。
   - **輸出：** `artifacts\pseudo\{日期}.md`。
2. **LINE 訊息：** 由自動測試涵蓋（P2-05、P3-07、P5 系列），在這裡彙整結果。
3. **執行：** 依設計 16.4 節的驗收腳本 1～6 步。

**完成判準：**
- 報告中「未包住的中文」為 0；
- 截斷元素為 0，或逐項說明為何可接受。

## P8-05　收尾清理

- **移除過渡設定：** 過渡用的轉接程式、功能旗標、`[Obsolete]` 成員；`/api/callback` 相容路由已確認不用時移除。
- **基準檔：** `duplicate-baseline.json` 只剩「已知且說明理由」的項目。
- **編碼報告：** 以 `-Scope All` 重產，列出還沒處理的舊檔。
- **「之後再說」清單：** 整理成一頁交使用者，由使用者決定要不要另開專案（不併入本計畫）。

## 階段 8 與整體完成判準

- **建置與測試：** 方案內 37 個專案建置與測試全綠，兩個目標都跑。
- **自動檢查：** R1～R11 全綠；每個已切換的組織 `verify` 無差異。
- **偽語系：** 驗收通過。
- **隔離：** A／B 教會、zh-TW／偽語系會友混合壓測，回應、快取、宮格不串；資源計數回到基線。
- **教會自訂：** 至少一間測試教會完成自訂名詞與奉獻類別清單，網頁與 LINE 在 60 秒內生效，另一間教會不受影響。
- **審查：** 外部審查沒有未處理的 Critical／High。
- **R2：** ChurchReport 清冊已清零（P6-07），R2 架構測試為嚴格模式。

**以上全部達成＝專案終點（00 §1）。** 不再新增工作包；剩下的改進一律在「之後再說」清單，由使用者決定是否另開專案。

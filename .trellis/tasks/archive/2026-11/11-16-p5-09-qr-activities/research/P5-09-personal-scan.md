# Research: P5-09 個人 QR 與掃描統計舊入口

- Query: 唯讀盤點 `PersonalQrCodeWorkflow.PersonalQrCodeWorkflowClass` 與 `CalculateQrCodeScanNumberWorkflow.CalculateQrCodeScanNumberWorkflow` 的參數、分派、完整資料讀寫、內容編碼、計數、依賴、失敗與副作用，提供可轉成期望檔的精確案例。
- Scope: internal；只讀舊方案第一層所屬專案與必要依賴，不研究 Sunday QR 或既有新基礎設施。
- Date: 2026-11-16（Asia/Taipei）
- Evidence basis: `reading`；未執行舊程式、未載入 DLL、未連 D365、未 build、未執行 git、未外審、未 spawn。

## Findings

### 1. 範圍、計畫與檔案索引

本文的 `R/` 代表本工作樹相對路徑 `../../.其他解決方案/Dynamics365Line/`。以下引用均為原始檔實際 1-based 行號，不是遮罩後重排的行號。敏感 literal 不列值；合成測資不得使用舊 token、LINE ID 或密碼。

已讀完整本工作樹 `AGENTS.md`（280 行）、`.trellis/workflow.md`，並詳讀現行 `05A_階段5_執行細則.md` 的 P5-09 與相關定案。活動名稱與參數以原始碼為準，不以計畫約略行數推定。

| 檔案 | 用途 |
| --- | --- |
| `R/PersonalQrCodeWorkflow/PersonalQrCodeWorkflow.cs` | 64 行；個人 QR CodeActivity，服務、深度分支與鎖 |
| `R/PersonalQrCodeWorkflow/PersonalQrCodeUtility.cs` | 448 行；contact 驗證、LIFF 內容、同步畫圖、附件 |
| `R/CalculateQrCodeScanNumberWorkflow/CalculateQrCodeScanNumberWorkflow.cs` | 64 行；掃描統計 CodeActivity，實際完整型別含重複 namespace/class 名 |
| `R/CalculateQrCodeScanNumberWorkflow/CalculateQrCodeScanNumberUtility.cs` | 392 行；內部類別實名 `EquipmentUtility`，不是同名 Calculate Utility |
| `R/ToolUtility/ToolUtilityClass.cs` | 必要依賴；全欄讀取、FetchXML、整體更新、屬性存取、annotation 建立及追蹤資源 |
| `R/Trace/BSUTextWriterTraceListener.cs` | ToolUtility 建構時建立的 TextWriterTraceListener 衍生類別；writer 建構式在 75–76 行 |
| `R/PersonalQrCodeWorkflow/PersonalQrCodeWorkflow.csproj` | net462、System.Drawing、ZXing 與 ProjectReference 宣告 |
| `R/CalculateQrCodeScanNumberWorkflow/CalculateQrCodeScanNumberWorkflow.csproj` | net462、SDK 歷史 HintPath、ToolUtility/Trace 參考 |
| `R/ToolUtility/ToolUtility.csproj`、`R/Trace/Trace.csproj` | 必要依賴的目標框架及組件參考；未深入不會被呼叫的 LINE/支付實作 |

05A:511–521：承接外殼是 `PersonalQrCodeActivity`、`MeetingScanCountActivity`；規則放 `GoodShepherd.Core/QrCodes/`，沿用 `QrCodeContentCodec`；圖片改建立 `QrImageGenerate` 待辦，`sourceId` 是記錄 ID＋內容版本的 `DeterministicGuid`。這是核定差異，不能把新活動說成仍然畫圖或上傳附件。主代理負責 codec/worker 等現有實作，此處不重做。

05A:65–72、405–415：本研究只是閱讀基準，不是 `legacy-golden`、實機影子或工作包驗收。參數新舊同名同型別，另加選填 `ExecutionMode`；無值/空白是 Live，`Shadow` 是影子。Depth 必須符合各舊入口實際行為。活動實機受 D5-9 限制，不可把舊原始碼的分支當實機註冊證據。

### 2. 共同入口契約：不是 Depth > 2 就返回

兩個 workflow 檔案的行號結構相同：

| 項目 | 舊行為與證據 |
| --- | --- |
| 型別 | Personal workflow:8：`PersonalQrCodeWorkflow.PersonalQrCodeWorkflowClass : CodeActivity`；Scan workflow:8：`CalculateQrCodeScanNumberWorkflow.CalculateQrCodeScanNumberWorkflow : CodeActivity` |
| 輸入 | :17–19，`[RequiredArgument] [Input("輸入的參數")] InArgument<string> InputParameter`；沒有 Default/ReferenceTarget，也沒有任何 `.Get(...)` 讀取 |
| 輸出 | :21–22，`[Output("輸出的結果")] OutArgument<int> OutputResult`；全入口沒有 `.Set(...)`，沒有回報附件數或計數結果 |
| 實際輸入 | Utility 使用 `IWorkflowContext.PrimaryEntityName`、`PrimaryEntityId`、`MessageName`、`OrganizationName`；服務以 `InitiatingUserId` 建立，不是以 InputParameter 選記錄 |
| 服務 | workflow:32–37 取得 context/factory/tracing，在判斷 depth/message/entity 之前呼叫 `CreateOrganizationService(InitiatingUserId)` |
| 深度 | workflow:12、40–53，`UPDATE_DEPTH=2`，`<=2` 與 `>2` **完全相同行為**：進相同 static lock 再執行 Utility。因此 depth 1、2、3、更多皆執行；並沒有防遞迴返回 |
| 序列化 | 每個型別各有 static locker（Personal:15；Scan:15）；只限制該型別在同程序的同步區域，不是跨程序唯一鍵、交易或跨型別鎖 |
| 實例狀態 | Utility 在 workflow:13 以欄位初始化，非每次 Execute 的局部物件；保存 context/service/entity 等可變欄位；wrapper 無 Dispose/finally |
| wrapper 例外 | workflow:56–59，組出含 `e.ToString()` 和 `DateTime.Now` 的區域 ErrorString 後 `throw e`；不是 `throw`，也沒有通知/落檔呼叫 |

`OutputResult` 的確切 WF 宿主觀察值要由宿主測試確認；原始碼唯一能保證的是「從未賦值」。一般未賦值 int 位置可能觀察為預設 0，**不可宣稱舊活動明確設定成功碼 0**。RequiredArgument 是 WF 中繼資料驗證要求，不是此 Execute 實作會檢查空字串；未綁定參數的 WF 驗證失敗與已綁定但為空值，須分開測。

### 3. 訊息、實體與 ID 分派

- Personal utility:183–276：只接受大小寫精確相同的 `ExecuteWorkflow`、`Create`、`Update`，三分支邏輯一致。各先 Verify，再建立 ToolUtility，再 Retrieve contact，最後 MajorExecute。
- Scan utility:150–238：同樣三訊息、同樣順序；Retrieve 實體是 `new_meeting_statistics`。
- Personal:405–428、Scan:324–347：Verify **只**比較 `PrimaryEntityName`；正確則保存 `PrimaryEntityId`，不檢查 Guid.Empty，也不驗 Target、前後映像、角色、教會、state/status、變更欄位或 InputParameter。
- Personal:51–52 的 `ENTITY_NAME_ID="contact"` 是未使用常數，不可據此把 contact 主鍵當名為 `contact` 的欄位；實際身分從 `Entity.Id` 而來。Scan:50–51 的 entity/id 常數為 meeting statistics；id 常數同樣未參與查詢。
- 其他訊息（例如 Delete、Assign、SetState、大小寫不符）不進 Utility 業務 I/O；wrong entity 在 Verify 後返回，不建 ToolUtility、不 Retrieve。但 wrapper 的服務建立已發生。
- ToolUtility:3632–3645 的 Retrieve 使用 `new ColumnSet(true)`：實際讀取 **全部已返回欄位**；下節列的是實際使用欄位，不代表舊版最小投影。

### 4. 個人 QR：內容與完整 side effects

#### 4.1 內容格式與組織選擇

Personal utility:278–307、376–402、309–348：

1. `SetupQrCodeLiffUrl()` 以 `OrganizationName` 精確分派：`sunnyvalech` 選正式 LIFF base；`sunnyvalechback` 選 back LIFF base；**所有其他名稱（包括未知組織）也回退正式 base**。兩個 base 都包含固定 LIFF 識別值與 `?QrCodeId=`，不抄錄識別值。
2. `m_QrCodeLiffUrl = selectedBase + retrievedContact.Id.ToString()`，使用讀回實體的 Id，不是輸入字串；通常 Guid.ToString() 是含連字號的 D 格式。
3. `GetEntityStringAttribute(ref contact,"fullname")` 取姓名。
4. 實際畫入 QR 的內容精確為：`selectedBase + contact.Id.ToString() + "@" + fullname`。
5. **沒有** URI escape、UrlEncode、Base64、JSON、簽章、trim、大小寫轉換、姓名分隔跳脫或 Unicode normalization。中文/空白/`@`/`&`/`?`/換行都直接保留；姓名內多個 `@` 不會先逃逸。QR payload 與 image `documentbody` 的 Base64 是不同層，不得混為一談。

安全合成向量：用測試 base `https://example.invalid/personal?QrCodeId=`、合成 contact GUID、姓名 `同名甲 @&?`，期望原樣串接 `base + guid:D + "@同名甲 @&?"`；正式/back base 的選擇獨立斷言，不在測資寫入舊 LIFF ID。兩名同名不同 ID 的 contact 必須得到不同內容，不能按姓名合併。

#### 4.2 contact 與 annotation 讀寫表

| 實體/欄位 | 操作與型別/精確值 | 證據 |
| --- | --- | --- |
| `contact` | `Retrieve("contact", PrimaryEntityId, AllColumns)`；使用回傳 Entity.Id 與 fullname；不 Update contact | Personal:208、231、254；ToolUtility:3638 |
| `contact.fullname` | string；缺欄回 `""`，存在且 null 則回 null，字串相加均留下尾端 `@`；錯型別直接 cast 失敗 | Personal:302；ToolUtility:4559–4580 |
| `annotation.objectid` | `EntityReference("contact", retrievedContact.Id)` | Personal:348；ToolUtility:5774–5775 |
| `annotation.objecttypecode` | string `"contact"` | ToolUtility:5776 |
| `annotation.subject` | `fullname + "QR Code"`，姓名與 QR 之間無額外空白 | Personal:348；ToolUtility:5777 |
| `annotation.notetext` | 同 subject | 同上及 ToolUtility:5778 |
| `annotation.filename` | `fullname + "_QRCode.png"`；未過濾姓名/檔名字元 | Personal:348；ToolUtility:5784 |
| `annotation.mimetype` | `"image/png"` | Personal:348；ToolUtility:5783 |
| `annotation.documentbody` | `ImageToByte(bitmap)` 的 bytes 再 `Convert.ToBase64String` | Personal:348、369–373；ToolUtility:5780–5782 |
| `annotation` create | 建立新 Entity 後 `CreateEntity`；未指定 annotationid、未查現有附件、未更新或刪舊附件、未設定 isdocument | ToolUtility:5774–5786、3731–3741 |

Personal:325–345 設定 ZXing `QR_CODE`、200×200、`CharacterSet="UTF-8"`、ECC H。`ImageToByte` 是 `new ImageConverter().ConvertTo(img, typeof(byte[]))`，沒有明確 `ImageFormat.Png` 儲存步驟；只能確定檔名和 MIME 聲稱 PNG，未執行不得把實際 PNG bytes/編碼器穩定性當已驗證。bitmap 未 Dispose；:353–365 的 UNC/Save 部分為註解，**實際沒有把 QR 存入該路徑**。

個人活動每次正常呼叫都新增一筆 annotation；相同 ID/相同內容重跑也再新增，不存在入列、retry key、附件查重或內容版本判斷。這個舊重複副作用不能照抄為新活動契約；依 05A:517 新版是 deterministic sourceId 的圖片待辦一次，屬核定圖片流程差異。

#### 4.3 個人例外、通知

- Personal utility:270–275、290–295、396–400、422–427 都組區域 ErrorString 後 `throw e`；未呼叫 tracing/notifier。
- **特例**：ToolUtility `UploadAnAttachment`:5789–5794 的 catch 只有區域 ErrorString，**吞掉例外**，沒有回傳成功旗標或 throw。因此 annotation Create/附件組裝失敗時活動可以正常結束，且 OutputResult 仍未設定。
- Bitmap 生成、ImageToByte 在呼叫 UploadAnAttachment 的參數求值階段發生；其失敗不在 Upload catch 內，會一路向上丟出。
- ToolUtility constructor 建追蹤檔失敗也在上層丟出，且會早於 Retrieve/畫圖/附件。
- 存在硬寫 developer LINE ID 常數（Personal:80），但本呼叫圖**沒有 LINE 推播/回覆/通知**；不得由此常數推論執行時有發通知。

### 5. 掃描統計：完整 38 組欄位對照

Scan utility:240–255 → :258–307，依下列順序呼叫 `SeQrCodeScanNumber`，共 18 固定組＋20 動態組 = **38 次 FetchXML 查詢**，最後一次整體 Update。每欄重算絕對值（Int32 collection.Count），不是增量加一，也不只改觸發那一欄。

| 次序 | `new_present_record` 非 null 條件欄位 | `new_meeting_statistics` 計數寫入欄位 |
| --- | --- | --- |
| 1 | `new_sunday_first_qr_on_time` | `new_sunday_first_qr_on_number` |
| 2 | `new_sunday_second_qr_on_time` | `new_sunday_second_qr_on_number` |
| 3 | `new_saturday_worship_on_time` | `new_saturday_worship_on_number` |
| 4 | `new_yongmen_on_time` | `new_yongmen_on_number` |
| 5 | `new_prayer_meeting_on_time` | `new_prayer_meeting_qr_on_number` |
| 6 | `new_child_on_time` | `new_child_on_number` |
| 7 | `new_big_disciple_on_time` | `new_big_disciple_qr_on_number` |
| 8 | `new_leadership_small_lecture_on_time` | `new_leadership_small_lecture_qr_on_number` |
| 9 | `new_leaders_gather_on_time` | `new_leaders_gather_qr_on_number` |
| 10 | `new_sunday_first_qr_off` | `new_sunday_first_qr_off_number` |
| 11 | `new_sunday_second_qr_off_time` | `new_sunday_second_qr_off_number` |
| 12 | `new_saturday_worship_off_time` | `new_saturday_worship_off_number` |
| 13 | `new_yongmen_off_time` | `new_yongmen_off_number` |
| 14 | `new_prayer_meeting_off_time` | `new_prayer_meeting_qr_off_number` |
| 15 | `new_child_off_time` | `new_child_off_number` |
| 16 | `new_big_disciple_off_time` | `new_big_disciple_qr_off_number` |
| 17 | `new_leadership_small_lecture_off_time` | `new_leadership_small_lecture_qr_off_number` |
| 18 | `new_leaders_gather_off_time` | `new_leaders_gather_qr_off_number` |
| 19、20 | `new_1_sign_on_time`、`new_1_sign_off_time` | `new_1_sign_on_number`、`new_1_sign_off_number` |
| 21、22 | `new_2_sign_on_time`、`new_2_sign_off_time` | `new_2_sign_on_number`、`new_2_sign_off_number` |
| 23、24 | `new_3_sign_on_time`、`new_3_sign_off_time` | `new_3_sign_on_number`、`new_3_sign_off_number` |
| 25、26 | `new_4_sign_on_time`、`new_4_sign_off_time` | `new_4_sign_on_number`、`new_4_sign_off_number` |
| 27、28 | `new_5_sign_on_time`、`new_5_sign_off_time` | `new_5_sign_on_number`、`new_5_sign_off_number` |
| 29、30 | `new_6_sign_on_time`、`new_6_sign_off_time` | `new_6_sign_on_number`、`new_6_sign_off_number` |
| 31、32 | `new_7_sign_on_time`、`new_7_sign_off_time` | `new_7_sign_on_number`、`new_7_sign_off_number` |
| 33、34 | `new_8_sign_on_time`、`new_8_sign_off_time` | `new_8_sign_on_number`、`new_8_sign_off_number` |
| 35、36 | `new_9_sign_on_time`、`new_9_sign_off_time` | `new_9_sign_on_number`、`new_9_sign_off_number` |
| 37、38 | `new_10_sign_on_time`、`new_10_sign_off_time` | `new_10_sign_on_number`、`new_10_sign_off_number` |

證據：固定簽到 Scan:266–275，固定簽退 :278–287，動態 for i=1..10 :289–295；第一堂簽退條件確實為 **`new_sunday_first_qr_off`，沒有 `_time`**，不得用對稱規則自動補字。

#### 5.1 查詢條件、關聯與去重語意

Scan:308–322 讀 meeting statistics 的 **`name`**（不是 `new_name`），將其、`Entity.Id.ToString()` 和該次條件欄位送入 ToolUtility:1830–1864：

- 根表 `new_present_record`；選 `new_present_recordid`、`new_name`、`createdon`，按 `new_name` 升冪。
- AND：(1) 本次掃描欄 `operator='not-null'`；(2) `new_meeting_statistics_new_present_re` `operator='eq'` 本次 meeting statistics GUID（XML value 包大括號）。第二條是 lookup 精確關聯，不是按活動名稱匹配，沒有 link-entity。
- lookup 的 `uitype='new_meeting_statistics'`，`uiname` 放 `MeetingStatisticsName`。**查詢等值身分是 GUID，不是 uiname/姓名**。name 缺欄取空字串；若含單引號、`&`、`<` 等，舊版直接拼 XML 未 escape，可能讓 FetchXML 解析失敗；精確 SDK fault 未實跑，不能捏造錯誤碼。
- `distinct='false'`；沒有 aggregate、GroupBy、Distinct、contact 查重、姓名查重、同人同日去重、重複 row ID 檢查、statecode/statuscode、日期區間、時間先後或 church/contact 篩選。
- **計數單位是查回的出席記錄列數，不是獨立人數或掃描事件總次數**。同一 contact 有兩筆不同出席記錄 ID 且都符合條件 → 計 2；不同人同名各有一列 → 計 2；同一列符合多個非空欄 → 各欄分別計 1。
- Timestamp 欄位只作 not-null；不讀取或比較日期值、不轉時區、未驗先簽到再簽退。只有簽退非空也算簽退。實際 DateTimeBehavior / UserLocal / DateOnly 不在原始碼證據中，需 metadata 核對，不能推定。
- `RetrieveMultipleRequest`＋`FetchExpression` 交 `IOrganizationService.Execute`，只取回應 `EntityCollection.Entities.Count`（ToolUtility:1852–1857；Scan:312–314）。沒有 page/count/paging-cookie、沒有檢查 MoreRecords；如服務只回第一頁，舊版就是第一頁筆數，不能宣稱全量統計。
- 38 次查詢沒有一致性快照/交易；同時新增出席列可能導致不同計數來自不同時間點。static lock 不鎖其他寫入者或別程序。

#### 5.2 掃描統計完整讀寫及非作用路徑

| 實體/欄位 | 行為與證據 |
| --- | --- |
| `new_meeting_statistics` | 依 PrimaryEntityId AllColumns Retrieve；讀 Entity.Id、`name`（string），其餘欄隨全欄載入；Scan:175/197/218、312 |
| 上表 38 計數欄 | `SetEntityIntAttribute` 覆寫或 Add Int32 Count，包括零；原值無論多少均重算；ToolUtility:3925–3946 |
| meeting statistics 寫入 payload | Scan:298 將 **原本全欄讀回的 Entity 加上計數** 整體傳入 Update；ToolUtility:3743–3753 直接 SDK Update，沒有最小 patch 或版本條件。故 payload 可能夾帶其他已讀屬性，存在非計數欄覆寫/更新限制風險 |
| `new_present_record` | 查詢投影/條件如上；不 Create/Update/Delete 出席記錄，不回寫個人 contact |
| `new_saved_flag` | Scan:367–388 有未呼叫的 SetupSavedFlag，會在存在欄位且字串 StartsWith 特定文字時改 GUID 並 Update；**活動可達路徑沒有呼叫，不能計為實際寫入** |
| 其他裝備/LINE/磁碟常數 | EquipmentLevel、舊附件目錄、developer LINE ID 只是死資料；掃描可達路徑不用，無圖片、附件、待辦或 LINE 發送 |

Scan:232–236、249–253、301–305、316–320、341–345 非預期例外皆 `throw e`，不是吞掉。任一第 k 次查詢失敗 → 前 k−1 欄只改記憶體 Entity，停止後續查詢且 **不呼叫最後 Update**；若最後 Update 自身失敗/回應不確定，原始碼沒有補償，不能宣稱伺服器一定沒有寫入。`EntityCollection` 或 Entities 若被替身回 null，會拋空參照，不會自動視作 0。

### 6. 必要依賴、敏感值與生命週期

#### 6.1 宣告版本及實際可達呼叫圖

- 兩活動 `.csproj`:12 都是 `.NET Framework v4.6.2`，不是 .NET 10；Personal:46–59 參考 System.Activities、System.Drawing、`zxing Version=0.16.5.0`（HintPath 指向 bin/Debug，**未進入或載入**）；SDK 檔案未讀，所以 SDK 實際檔案版本未知。
- Personal csproj:67–74、Scan csproj:65–72 直接 project-reference ToolUtility、Trace。Scan SDK HintPath:40–45 指向另一歷史路徑，本研究未越界跟讀。
- ToolUtility csproj:12 net462；:40–48 Microsoft.Crm.Sdk.Proxy/Xrm.Sdk、Newtonsoft.Json 組件版本 6.0.0.0；:54 宣告 System.Net.Http.Formatting 5.2.7.0；:79–86 參考 Line.Messaging、Trace。Trace csproj:32 為 net452。這些是**原始專案宣告**，不是目前載入/還原結果。
- 本次必要呼叫只有：`ToolUtilityClass(ref service)`、`RetrieveEntity`、`GetEntityStringAttribute`、`SetEntityIntAttribute`、`RetrievePresentRecordByFetchXmlAndMeetingStatistics`、`UpdateEntity(ref Entity)`、`UploadAnAttachment` → `CreateEntity(Entity)`；未走無參數 ToolUtility 建構式/SetOrganizationService，不會沿這條路以其硬寫帳密另連 CRM。
- 不必為本研究深入 LineUtility/Line.Messaging 的推播、付款或 Reply 實作；沒有證據顯示這兩活動可達呼叫會使用它們。Trace 是建構實際 side effect，不是僅 using。

#### 6.2 敏感 literal 存在狀態（不列值）

| 位置 | 存在與可達性 |
| --- | --- |
| Personal utility:71–73 | 存在固定 LIFF 應用識別值；實際用於個人內容，報告不抄錄 |
| Personal utility:80；Scan utility:71 | 各存在硬寫 developer LINE ID；本呼叫圖未使用 |
| ToolUtilityClass:50、57、65、73、80 | 存在密碼 literal（含註解歷史設定）；:73 為 active 常數；本活動以平台提供服務建構，不走自行認證分支 |
| 兩 activity/utility 的本次可達路徑 | 未見硬寫 access-token 被使用，沒有 LINE HTTP 呼叫；不等於整個舊方案沒有 token |

報告及測資不得保存上述值；讀取輸出採 literal 遮罩/存在性摘要，不以抄錄原始行驗證敏感值。沒有讀取外部組織認證或執行任何舊登入程式。

#### 6.3 必須明確隔離、不移植的舊缺陷

1. **追蹤檔副作用與全域保留**：ToolUtility:103–114 建構即開本機固定追蹤檔（append、FileShare.ReadWrite），用 Big5 StreamWriter，建立 listener、修改 `Debug.AutoFlush`、加入全域 `Debug.Listeners`；即使業務沒有 Trace 也發生。Trace listener writer 建構式 :75–76 交給基底，未自行清理。
2. ToolUtility:151–175 Dispose 只改旗標，沒有 Remove listener、Dispose stream/writer/listener；兩 workflow 也不 Dispose utility。每次接受的呼叫重新覆寫 utility 中 ToolUtility 參照；全域 listener 可使 writer/handle 長期保留。這是程式碼可證的無界持有風險，未做量測，不冒充實測洩漏數據。
3. 個人 bitmap 沒 using/Dispose；annotation 失敗被吞後也沒有 cleanup。新活動禁止搬入 System.Drawing 同步畫圖、硬寫檔案與 listener。
4. 兩 utility 保存 WorkflowContext、service factory、SDK service、tracing、entity；static lock 只序列化，不會清除可變資料。下一次同型別執行雖會覆寫常用欄位，仍不能證明活動快取重用的跨會友/組織隔離或短生命週期。
5. 全欄 Retrieve/Update、38 次未分頁查詢及全型別鎖是舊效能/併發風險。新的最小欄位 patch/完整有界讀取/重複 ID 拒絕必須與舊期望區分，不可冒稱原版已做到。外部服務超時、取消、交易與重試機制在這兩活動裡均未明定。

### 7. 可直接轉情境的正常／邊界／錯誤期望

以下全部是**原始碼閱讀推導**，沒有執行測試。記錄 ID、姓名及網址用純合成值；服務建構除外的業務呼叫數需按情境檢查。適合先取 P1/P2/P4、S1/S3/S5 為六個主黃金情境，其他斷言放細部測試；不把每個細項都當新增實機案例。

| 案例 | 合成輸入/故障 | 精確舊期望與證據 |
| --- | --- | --- |
| P1 正常 | contact、ExecuteWorkflow、depth=1、一般組織、fullname=王小明 | AllColumns Retrieve 一次；內容為正式 base＋讀回 contact GUID:D＋`@王小明`；annotation Create 一次，subject/notetext=`王小明QR Code`、filename=`王小明_QRCode.png`，無 contact Update/LINE；Personal:195–215、302–348、383–394 |
| P2 組織/字元邊界 | 組織 back；姓名 `甲@乙 &?`；同時對 Create/Update 測相同輸入 | 選 back base，姓名逐字保留，沒有 percent-encoding；Create/Update 業務與 ExecuteWorkflow 相同；Personal:219–262、317、387–389 |
| P3 空姓名 | fullname 缺欄，或存在但 null | 內容結尾僅 `@`；subject/notetext=`QR Code`，filename=`_QRCode.png`；仍嘗試產生圖片/Create；ToolUtility:4559–4572、Personal:317/348 |
| P4 錯誤 | annotation Create 丟合成 SDK fault | Create 嘗試一次、例外在 Upload 被吞、活動正常退出且沒有通知/OutputResult.Set；ToolUtility:5786–5794。新版重拋必列 D5-6(1) declaredDifference |
| P5 前置錯誤 | Retrieve fault / fullname 非 string / QR writer 或 ImageToByte fault | 向外丟出，沒有 annotation Create；不通知；ToolUtility:3638–3644/4567，Personal:345/348、290–295 |
| P6 重跑 | 同 contact 同內容連續兩次 | 舊版 Create annotation 兩次，無查重/刪舊；新版同圖待辦一次是 D5-6(2)＋P5-09 核定差異；Personal:348、ToolUtility:5774–5786 |
| C1 Depth/message | 上述正常資料，depth 2/3；另測 Delete 或 wrong entity | depth 2 和 3 皆完整執行；Delete/wrong entity 不 Retrieve/寫入，但已建立 initiating-user service；兩 workflow:37–53、各 Verify |
| S1 正常計數 | meeting M；兩不同出席 ID、同名、同 contact 皆第一堂簽到非空；另有 M 的一列只有第一堂簽退非空，以及別的 meeting 一列 | first_on=2、first_off=1；別 meeting 排除、其他欄=0。38 查詢、一次 meeting Update，無 contact/出席 Update。用具名欄位提供資料，第一堂簽退用不帶 `_time` 的真實舊名稱；Scan:266–298、ToolUtility:1839–1857 |
| S2 無符合列/覆寫 | 所有查詢回空集合，原 38 計數為非零 | 38 計數全部覆寫 0，仍一次 Update，不因無異動跳過；Scan:298/314、ToolUtility:3925–3938 |
| S3 動態邊界 | i=1、i=10 的 on/off 各一列；i=0、i=11 也有資料；某列只有 off | 僅 1..10 的 20 個條件查詢；0/11 不查不寫；只有 off 也計 1。固定 18 組仍查；Scan:289–295 |
| S4 不去重/不驗日期 | 同人兩筆不同 ID、同名不同人、過去/未來時間、同列多時間欄；測假集合回同一 ID 兩次 | 不同 ID 全算；時間非空即算，各欄獨立；假集合相同 ID 重複亦算 2（沒有防護，不是正式 D365 會回重複 ID 的證據）。新版 exact-ID 拒絕與舊 Count 差異須明記；Scan:312–314 |
| S5 中途故障 | 第 3 次 Fetch 丟合成 fault | 前兩欄僅記憶體修改；第 4..38 次不執行、最後 Update=0、fault 向外丟出；Scan:266–298/301–305 |
| S6 分頁邊界 | 第 1 次回 2 列且 MoreRecords=true、cookie 非空 | 該欄舊值=2，無後續分頁請求；全流程仍是 38 次查詢＋一次 Update，不自動多查；ToolUtility:1852–1857 |
| S7 XML/型別錯誤 | meeting `name` 含 XML 特殊字元；或 name 非 string；或 Fetch 回 null | XML 特殊字元未 escape，讓替身在解析時丟 fault則無最終 Update且向外丟出；不猜服務端固定 fault。錯型別/null collection 直接失敗，不當成 0；ToolUtility:1834–1847/4591、Scan:312–314 |
| S8 Update/重跑 | 38 查詢成功；Update fault；另在穩定資料集正常重跑兩次 | fault 向外丟出、沒有補償/通知；穩定資料每輪都查 38 次＋Update 1 次、寫相同絕對計數，不累加；Scan:298–305、ToolUtility:3747–3752 |

新版附加測試不能缺：A→B→A 組織/會友隔離、不同 ID 同名保留、精確重複 row ID fail closed、loader 中途失敗不部分寫入、同來源同版本只入列一次、內容版本變更可有新圖、影子零真寫/零畫圖/零通知，以及失敗/取消/重跑後資源回到基線。這些是 AGENTS/spec/05A 要求，**不是舊版既有保障**。本研究不替主代理決定新欄位/新 codec/worker 實作。

### 8. Related specs 與外部參考

已讀相關規範：

- `.trellis/spec/backend/index.md`：後端規範入口。
- `.trellis/spec/backend/plugin-shadow-contract.md`：可變 context/service 每次 Execute 局部擁有；影子不寫真服務、不解密、不推播；固定敏感投影及有界 cleanup。
- `.trellis/spec/backend/duplicate-row-publication-contract.md`：不同正式 ID 同名保留、重複正式 ID 拒絕；operation-local candidate 完整成功再發布；跨程序待辦唯一鍵。
- `.trellis/spec/backend/file-documentation-contract.md`：完整繁中說明、UTF-8 無 BOM/CRLF/末尾 CRLF、不複製敏感設定值。
- `AGENTS.md`:236–258：真正失敗交共用 notifier；獨立 WriteExceptionLog/SendLine 開關、雙開 flush 先於 LINE、只用安全事件資訊。舊活動未做到，不能以「舊版沒有通知」免除新版需求。
- `文件/規劃設計書/實作計畫-3.5/05A_階段5_執行細則.md`:65–72、405–415、511–521：reading 基準、核定差異、完整盤點、參數/深度/圖片待辦與同名測試。

外部參考：本次未連網，不依賴外部文件或推測最新 SDK 行為；版本只引用上述 csproj 宣告。WF 未設定 OutArgument 的宿主值、D365 欄位 metadata/日期行為/實際 paging 上限均列為未驗證，不以框架常識冒充本次證據。

## Caveats / Not Found

- 僅完成授權的舊原始碼閱讀研究；未經舊程式實跑，不能標成 legacy-golden、舊/新實機零差異或 P5-09 驗收完成。
- 未讀舊方案 worktrees/bin/obj/packages，未執行舊 DLL/舊建置事件，未連 D365，未改其他文件/程式，未 git/commit/push/外審/spawn。未調整 task/prd/plan/註冊清單或進度頁。
- 本研究不讀 Sunday QR、舊完整 inbound 掃描流程或既有新基礎設施；個人 QR 的後續掃描解析/簽到是否由其他產品承接，由主代理另行盤點。
- 無實際 export-legacy 或結構快照證據：註冊階段/模式/篩選/映像/順位、資料欄位實際型別/日期行為、alternate keys、組織權限與跨教會 scope 均未驗證。不可由原始碼推成已核對。
- 關鍵舊缺陷：兩入口 Depth 全放行；個人附件 Create 故障被吞；相同內容重跑建立重複附件；38 個查詢不分頁且按列計數；全欄 Entity 回寫；bitmap/全域 listener/stream 無明確釋放。這些不能直接移植。
- 不保存舊 token、LINE ID、密碼、LIFF 識別值。報告只陳述敏感值存在性及是否可達。`OutputResult` 未設定不是明確成功 0；ImageConverter bytes 未實跑，不宣稱像素或 PNG 二進位一致。
- 本次未執行 repository 文件守門：其標準入口內含 git 檔案列舉，與本研究禁止任何 git 操作衝突；改以僅讀取本研究檔案的 byte-level 檢查確認 UTF-8 無 BOM、純 CRLF、末尾 CRLF，並檢查未含 LINE user-ID/token/password 值樣式。沒有產品程式碼變更，所以未 build/test。

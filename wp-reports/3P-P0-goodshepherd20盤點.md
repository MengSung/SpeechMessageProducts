# P0-2⓪ goodshepherd20 唯讀盤點

更新：2026-11-28 15:19 Asia/Taipei (UTC+08:00)
工作包：F0 Step 3（由 FS 補做）
狀態：F0 模型盤點條件已滿足；完整 inventory／13 個聚合、真實邏輯名／核心欄位及第 9 章沿用／擴充／新增結論已齊。未查筆數如實標示；後續 G 的 label／狀態機／新模型設計不是 F0 待補。quiesce 依 02A §2.6 結案，211 個信箱仍個別未確認不投遞、不阻擋 FS；以下歷史候選不是現況。主線修後 9,566/9,566 已通過，正式收尾／文件審查由主線處理，不推論 FS 或 G 完成。

## 已取得的實機證據

測試組織 `goodshepherd20` 已透過受控 HTTPS／ADFS SDK 核對；組織 ID 為 `ec4d281b-bbe7-f111-8cce-00155d007916`，版本 `9.1.49.13`。歷史唯讀 metadata 為 570 表／22,837 欄，原報告快照 SHA-256：`3F7236983F803C68C57C6411B800DBA8884A5A42085EC3DBC601E997E06709F7`。這是歷史結構快照，不是資料庫備份，也不是業務資料筆數；本次第 9 章判斷改以以下 20261128 完整快照 S2 為準，不混用兩份 SHA。

2026-11-26 12:23 Asia/Taipei (UTC+08:00) 的 `quiesce --org goodshepherd20` 乾跑成功，零寫入；清冊 SHA-256：`9575E3028AF7339ACA4AEBC26681CF1F8614AD211675CE0892CB3FAECFCA6E2C`，固定摘要 log SHA-256：`F40A5B174032F65EE6FD1E7B84C6CD3EF5722FB07C99099543E12324ECCBEA0E`。

以下表格為2026-11-26歷史候選；目前停用結果以FS報告2026-11-28 08:47讀回為準。

| 類別 | 唯讀結果 | 處置與未完成項目 |
|---|---|---|
| 等待／暫停 workflow 執行個體 | 0 | 保留此時點結果；apply 後仍須重查 |
| 啟用 workflow 定義 | 146 | 最新正式solution／publisher來源已補讀；均為managed，88筆Complete、58筆僅publisher缺prefix而MetadataIncomplete。尚未完成保留決策審查或停用 |
| 啟用 Plugin 步驟 | 7,280 | 最新handler／type／assembly對應全部Complete、managed，公開簽署識別31bf3856ad364e35；皆Microsoft.*或ActivityFeeds.*。來源識別不等於已證明不外送，保留草案仍待審，不盲停核心元件 |
| 舊delivery篩選的信箱候選 | 136 | 135筆state/status=1/2、1筆0/1；原清冊沒有三欄delivery原值，不能當啟用投遞數。ACT None選項與原值落檔安全補正中，未apply；不讀郵件或帳密、不停共享服務 |
| 全部停用候選 | 7,562 | 純管理身份清冊；保留政策僅草案，未套用、未停止投遞 |
| Channel、金流／電子發票設定及外部網址 | 完整清冊尚未發布 | 保留／G5-2 處理；不載入、解密或呼叫複製帶入的正式 Channel 與金流 |
| 會友、出席、奉獻、附件與待送工作筆數 | 未完成 | 只允許 server aggregate count，不讀列值；缺項不能由結構數量推論 |

## 結構沿用與擴充

metadata 已確認 `new_fee`、`new_offeringcategory`、`new_dedication_booking`、`new_stor_lessons` 存在，仍只屬好牧人產品層；construction／association 的本機分層不包含這四表，實機驗證仍待部署後讀回。`contact.new_lineid` 既有長度為 500，平台採同一契約。

複本的 `new_localizedtext`、`new_offeringcategory` 為既有 OrganizationOwned；FS 僅以已核對的組織 ID 及正式 metadata ID 保留原 ownership，不刪表、不重建、不改 owner。新組織仍遵守新建 UserOwned。其他型別、安全、稽核及管理屬性照原守門；這些離線基線補正仍須三份真正 plan 審查後才可寫入。

## 第 9 章既有實體與核心欄位

### 本次來源、範圍與判斷規則

本次更新時間見頁首；只在 `C:\ProgramData\SpeechMessage\worktrees\cl-lane-a-fs-rebase-20261128`（`cl/lane-a`）進行檔案唯讀分析，沒有開啟 SDK／VM／實機連線、沒有讀業務列或舊 Channel／付款設定值。依 1.3 的 01 第 84–113 行補 F0 Step 3；不實作 G1、不改方案／schema／程式或 90 狀態。第 9.1–9.6 節全部 29 項需求及各項後續新增範圍見 `wp-reports/3P-F0-基線矩陣.md`，本節負責既有資料的精確交接。

| 證據 | 路徑與 SHA-256 | 本次使用方式 |
|---|---|---|
| S1 Word／段落索引 | `文件/規劃設計書/產品規劃書/完整三大產品/Claude交叉比對規劃/三大產品共用架構與開發路線實施計畫_Claude版_v2.5.docx`；`54199B532C4D5444836B9D86AF4F26552A4D29782629BEE0CE32F36E6379AC10` | 指定來源第 9 章，870–984；Word XML 段落文字與指定抽取檔逐項一致，不讀其他規劃 Word |
| S1-T 抽取檔 | `C:\ProgramData\SpeechMessage\evidence\lane-a-f0-v25-paragraphs-20261128.txt`；`2D26A6E6ECD9C619DA7B50FBB67D3B4FA176A914B2EA744C5F6409089C4CF522` | 919 職分／委身、922 牧養、941 出席週報、944 課程、950 關懷為需求定位；不把規劃文字當作實體存在證明 |
| S2 完整 metadata | `d365/snapshots/goodshepherd20/20261128-043704-3244869-fa02d4ea64bd4d1e92bc1fce1da7c747/metadata.json`；`4140427C63C805831967491962C427FE75123F5936EB771DC8A8E1C2F557F913` | `entities[]`，570 實體／22,837 欄／19 個 `new_`；正式邏輯名、主鍵、型別、lookup targets、choice 數值、alternate keys 與 ownership |
| S3 13 實體投影 | `C:\ProgramData\SpeechMessage\evidence\lane-a-f0-model-metadata-map-20261128.json`；`EB4F7AD9BDCDB487CD1FA6209C51C2EFD2BE40E9F16239C8664459BC57F0EB9F` | sourceSha256 等於 S2，13 個物件均與 S2 原實體一致；只是方便比對的 metadata，不是業務列 |
| S4 聚合 | `artifacts/d365/goodshepherd20/20261128-014947-6463072-54e8b8c38268489d8c29e9e7477a1344/clone-inventory.json`；`71F864AC23F035B6027ADBD2D4EF479EE0F1970F9DE7F212DCAFA78DA358C367` | 只讀 `tables` 的結構與 recordCount；24 項／13 個非 null 聚合，不讀 `management` 的業務或設定值 |

判斷分三層：**既有存在事實**由 S2／S3 支持；**舊用途**由產品程式的欄位讀寫支持，不代表 2.0 已驗收；**後續擴充／新增**是與 S1 比較出的差距，不是已部署結果。S2 choice 沒有 label，以下只列數值或數量；本報告不從數字補寫中文選項意義。所有筆數只引用 S4，未查者寫「未取得」，不能寫 0。

### 13 個相關實體的正式主鍵、關聯與處置

欄位總數含平台／虛擬／衍生欄位，不等於業務欄位數。箭頭表示 S2 的 lookup target；字串顯示欄不能代替外鍵或資料庫 ID。

| 正式實體／主鍵 | 欄位總數／S4 筆數 | 已證核心欄位與關聯 | 沿用／擴充／新增及處置 |
|---|---|---|---|
| `account`／`accountid` | 257／4 | `name`（string 160）、`primarycontactid→contact`、`new_linechannelid→new_linechannel`、`new_timezone`（string 64）、`new_countrycode`（2）、`new_defaultlanguage`（16）、`statecode/statuscode` | **保留、擴充**教會／租戶；既有欄位不證明 v2.5 七態訂閱、名額／Logo／方案／帳務完整，不改 owner 或讀帳務值 |
| `contact`／`contactid` | 514／460 | `fullname`、`parentcustomerid→account/contact`、`new_lineid`（string 500）、`new_line_register`（Boolean）、`new_line_status`（choice 4 數值）、`birthdate`／`anniversary`（datetime）；職分、委身、牧養、關懷詳後 | **保留、擴充**會友；`parentcustomerid` 是多型 lookup，須伺服器確認 account 教會，不把任意 contact target 視為租戶。`keys=[]`，未證教會＋LINE userId 唯一性 |
| `list`／`listid` | 113／76 | `listname`（string 300）、`type`（Boolean）、`membertype`（integer）、`statecode/statuscode`，七個帶領／回報 lookup→contact 詳後 | **保留、擴充**牧養名單；無 self lookup→list、無 account lookup，不能直接充當任意階層／教會隔離與搬移歷史；未讀 query 或成員 ID |
| `new_present_record`／`new_present_recordid` | 155／41 | `new_contact_new_present_record→contact`、`new_list_new_present_record→list`、`new_group_present_weekly_report_prese→new_group_present_weekly_report`；`new_sunday_date`／`new_group_date`（datetime）、`new_group_present_this_week`／`new_sunday_present_this_week`（integer）、`new_small_group_rate`／`new_sunday_rate`（Double） | **保留、沿用並擴充**出席；以正式出席 ID 區分，不按人名去重。聚會類型／出席狀態的語意、同教會驗證與重複事件限制不能由欄位存在推論完成 |
| `new_group_present_weekly_report`／`new_group_present_weekly_reportid` | 85／8 | `new_list_group_present_weekly_report`／`new_deciple_list_group_present_weekly→list`；`new_sunday_date`、`new_weekly_index`（string）、`new_small_group_member_number`／`new_small_group_number`／`new_sunday_present_number`（integer）、`new_small_group_rate`／`new_sunday_present_rate`（Double）、`new_weekly_report_status`（choice 5 數值） | **保留、沿用並擴充**週報；帶領者 lookup 可作既有回報流程輸入，但回報者授權、確認狀態 label／轉移要另證，不能憑 5 個值對號入座 |
| `new_disciple_lessons`／`new_disciple_lessonsid` | 364／未取得 | `new_name`、`new_join_start_date`／`new_join_end_date`、`new_class_start_date`／`new_class_end_date`（datetime）、`new_auto_push`（Boolean）、`new_lessons_fee`（Money）、`new_contact_teacher_new_disciple_less`／`new_contact_new_disciple_lessons_fee→contact` | **保留、沿用並擴充**既有課程／開班；未證獨立課程定義＋多次開班的拆表模型。名額上限／併發佔位未有證據，不把簽到計數當容量 |
| `new_stor_lessons`／`new_stor_lessonsid` | 342／5 | `new_contact_new_stor_lessons→contact`、`new_account_new_stor_lessons→account`、`new_new_disciple_lessons_new_stor_les→new_disciple_lessons`、`new_registration_form_new_stor_lesson→new_registration_form`、`new_roll_card_new_stor_lessons→new_roll_card`；`new_enroll_time`、`new_enroll_status`、`new_fee`／`new_paid_amount`／`new_not_yet_paid`（Money） | **保留、沿用並擴充**個人上課／報名紀錄，不是開班表。報名狀態 14 數值，不代表 v2.5 五態已映射或狀態機已完成 |
| `new_registration_form`／`new_registration_formid` | 55／未取得 | `new_contact_student_registration_form→contact`、`new_new_roll_card_new_registration_fo→new_roll_card`；`new_semester`（choice 10 數值）、`new_schoolfee_shoud_pay`／`new_schoolfee_paid`／`new_schoolfee_unpaid`（Money） | **保留、沿用相關報名／學費資料**；其學期／學生關聯不等於每個開班的狀態機；不得與 stor 的 5 筆混為同一計數 |
| `new_roll_card`／`new_roll_cardid` | 65／未取得 | `new_contact_student_roll_card`／`new_contact_teacher_roll_card→contact`、`new_list_new_roll_card→list`、`new_student_id`（string）、`new_inroll_date`（datetime）、`new_achieved_credit`／`new_estimated_credit`（Double） | **保留、沿用學籍脈絡**；可作課程／報名關聯，不把學籍 ID 當新開班或報名事件 ID |
| `new_fee`／`new_feeid` | 98／102 | `new_contact_new_fee→contact`、`new_disciple_lessons_new_fee→new_disciple_lessons`、`new_stor_lessons_new_fee→new_stor_lessons`、`new_dedication_booking_new_fee→new_dedication_booking`、`new_offeringcategoryid→new_offeringcategory`；`new_fee_shoud_pay`／`new_fee_really_paid`（Money）、`new_pay_date`、`new_reciept_date`、`new_pay_status`（choice 7 數值）、`new_pay_way`（choice 9 數值） | **保留、沿用並擴充**課程／奉獻收費；不能自動升格為平台訂閱單／綁卡／扣款紀錄。收據日期不是完整收據號、作廢／核銷或發票契約 |
| `new_dedication`／`new_dedicationid` | 47／未取得 | `new_contact_new_dedication→contact`、`new_dedication_booking_new_dedication→new_dedication_booking`、`new_dedication_amount`（Money）、`new_dedication_date`（datetime）、`new_category`／`new_pay_way`（各 choice 4 數值） | **保留、沿用奉獻紀錄**；本體沒有 `new_offeringcategoryid` lookup，類別選項不可自動等同新類別表；沒有足夠證據把本體與 fee 視為一列或宣稱完整核銷／收據已存在 |
| `new_dedication_booking`／`new_dedication_bookingid` | 68／5 | `new_contact_new_dedication_booking→contact`、`new_offeringcategoryid→new_offeringcategory`；`new_dedication_amount`／`new_dedication_paid`／`new_not_yet_paid`／`new_amount_per_stage`（Money）、`new_dedication_start_date`／`new_dedication_end_date`、`new_dedication_booking_status`（choice 5 數值） | **保留、沿用並擴充**奉獻預約／分期；booking 與 dedication／fee 是不同正式 ID，不合併也不推算筆數；新增類別 lookup 不證明歷史資料皆已回填 |
| `new_offeringcategory`／`new_offeringcategoryid` | 34／27 | `new_accountid→account`、`new_code`、`new_name`、`new_sortorder`（integer）、`new_legacyoptionvalue`（integer）、`new_accountingcode`、`statecode/statuscode`；Active `new_ak_offeringcategory(new_accountid,new_code)` | **保留、沿用**既有名稱／排序／狀態承載；本複本 OrganizationOwned 原樣保留，不刪表／重建。code 鍵不取代正式列 ID；同名不同 ID 都必須保留 |

### F0 確認一：職分與委身不是兩張已存在的分類表

- **委身沿用 `contact.customertypecode`**：S2 為 choice，只有 `[1,100000000,100000001,100000002,100000003,100000004,100000006,100000007,100000008,100000009]`。產品 `SpeechMessageProducts.ChurchReport/Domain/Constants/CommitmentConstants.cs` 的 `CommitmentType` 明述常數對應此欄；`SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoCommitmentTypeMetadataProvider.cs` 以 `RetrieveAttributeRequest(EntityLogicalName="contact", LogicalName="customertypecode")` 讀 metadata，保留 Options 順序。這證明舊產品用途，不等於此快照已包含 label／顯示順序的完整證據。
- **職分沿用 `contact.new_church_jobtitle`**：S2 是 string 100，不是 lookup 或 OptionSet。`SpeechMessageProducts.ChurchReport/Areas/ChurchSettings/ChurchSettingsAccess.cs` 讀取該字串；不能把它當成已存在「名稱／排序／停用／選單組合」的配置實體，也不能以文字直接授權。
- **結論：既有值沿用，v2.5 配置模型需擴充／新增承載**。本次不命名新表、不遷移字串。若要可配置職分、停用、排序、選單組合與角色關聯，由後續核准階段給出正式邏輯名、教會範圍及遷移映射。
- **實際相容性缺口**：舊 `CommitmentType.HappyBest` 常數為 `100000005`，S2 的 options 沒有此值；這是程式／目標 metadata 差異，不得補造選項或替目標選項貼上舊中文 label。需後續以目標 metadata label／已核定語意確認，不讀會員列來猜。

### F0 確認二：牧養可沿用名單，不可直接認定已有階層歷史

- `list` 的七個 contact lookup 為 `new_contact_family_leader_list`、`new_contact_list_vice_family_leader`、`new_contact_race_leager_list`、`new_contact_co_race_leager_list`、`new_contact_list_arealeader`、`new_contact_list_co_arealeader`、`new_contact_list_report_window`；另有 `new_area_name`、`new_sub_area` 等文字。它們不是上層節點 ID。
- `contact.new_cell_list_contact` 與 `contact.new_deciple_group_list_contact` 的 target 均為 `list`；後者保留原實際拼字 `deciple`，不得替換成猜測的 `disciple` 欄名。產品 `SpeechMessageProducts.GoodShepherd.Core/Care/NewPersonPresentRecordService.cs` 的 `LeaderFields` 與建立週報候選的路徑沿用 list 領袖及該 contact 關聯，證明舊用途。
- 完整 S2 的 `list.fields[].targets` 沒有 `list` 或 `account`：不能把 list 現狀說成已有自參照父節點或直接教會 FK。`ownerid`／businessunit 也不是教會 ID 的替代品。
- **結論：名單與帶領者關聯沿用；層名／層序／parent node／有效期間與搬移歷史需擴充或新增承載**。具體採擴充 list 或獨立層級／節點／歷史表是後續設計，不在 F0 虛構已存在的新實體。未讀名單 query 或成員列，因此不宣稱 76 個 list 都是牧養節點。

### F0 確認三：關懷只有片段，完整案件不能直接沿用

| S2 已有片段 | 型別／用途證據 | 不能直接等同的 v2.5 能力 |
|---|---|---|
| `contact.new_care_contact`／`new_care_date`／`new_care_for` | lookup→contact／datetime／string 100 | 一位會友上的欄位不是可多次開案、有自己正式 ID／指派歷史的關懷案件；未證唯一處理人或案件歷史 |
| `new_present_record.new_care_expire_date` | datetime；`NewPersonPresentRecordService.cs` 在既有關懷出席建立時設定 | 只證明出席列關懷期限；不是完整案件期限／到期升級機制 |
| `new_follow_up`／`new_followup_ways` | string／choice 8 數值 | 不能把跟進文字與方式選項說成已核定的粗結果碼 |
| `new_weeks` | choice 21 數值；舊 `FollowUpWeek` 程式用途是跟進週次 | 不是已證的「連續缺席週數」欄位，不能當作出席週次或連缺計算已完成 |
| `new_conclusion_choise`／`new_next_step` | choice 10 數值／choice 3 數值；舊 `FollowUpResult`／`FollowUpNextStep` 有程式常數 | S2 沒有 label，不能直接套粗分類／升級層級語意；下一步不是完整升級層級模型 |

13 個主要投影實體以外，亦檢視 S2 全部 19 個 `new_` 的欄位：`new_virtual_contactor`（主鍵 `new_virtual_contactorid`）另有 `new_care_status`（choice 9 數值）、`new_carers`（string 100）、`new_contact_new_virtual_contactor→contact`、`new_list_new_virtual_contactor→list`、`new_present_record_new_virtual_contac→new_present_record`。這些也是片段；處理者文字不是正式 contact lookup，不能因有 care 字樣就當完整案件。該表不在 S4 聚合，筆數「未取得」，不是 0；這項限制不新增 F0 待辦。

**F0 結論：既有會友／出席關懷片段可沿用；v2.5 的完整關懷案件應新增承載，不能核准整單直接沿用。** 判斷來自完整自訂實體／欄位與既有服務用途，並非按表名猜不存在。S2 的 19 個 `new_` 實體中，未找到可證明同時承載案件身分、會友、連缺週數、處理人、期限、粗結果與升級層級及歷史的模型；原生同名或相似標準案件表亦沒有此產品契約證據。新承載邏輯名、與出席的關聯、處理人與結果／升級狀態及保留期限仍待後續設計；本次不把該設計寫成已實作。

### 課程、奉獻與出席的已證用途和仍待補項

1. **課程／開班**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/FeeDownUpLoader.cs` 的 `ProcesseLessonsList` 路徑讀 `new_join_start_date`／`new_join_end_date` 與開課日期，以 `new_new_disciple_lessons_new_stor_les` 查上課／報名列，並經 `new_stor_lessons_new_fee` 找收費。S2 的 `new_auto_push` 是 Boolean，只能證明單一開關，不是所有推播模式；未找到可由用途證明的「名額上限」，`new_signon_number`／`new_singon_number` 不可猜作容量。
2. **報名五態**：`new_stor_lessons.new_enroll_status` 為 `100000000–100000013` 共 14 值。v2.5 要求「尚未繳費／繳費完成／報名完成／報名成功／已取消」是新需求文字，不是這 14 值的既定 label；須後續逐值對照、核定合法轉移、取消退款與容量冪等，不強制刪減成五個值。
3. **出席／週報**：`SpeechMessageProducts.GoodShepherd.Core/WeeklyReports/WeeklyReportGenerationService.cs` 及 `SpeechMessageProducts.GoodShepherd.Core/Care/NewPersonPresentRecordService.cs` 可佐證 contact／list／週報關聯與出席人數欄的舊用途；本次不執行其查詢。`new_weekly_index` 才是週報文字索引候選，不能將關懷 `new_weeks` 直接充作報表週次；回報人／確認狀態與聚會分類仍需明確映射。
4. **奉獻／收費**：`SpeechMessageProducts.GoodShepherd.Core/Fees/FeeNotificationService.cs` 與 `SpeechMessageProducts.GoodShepherd.Core/Offerings/OfferingCategoryReader.cs` 讀取 `new_offeringcategoryid` 並驗證類別教會，佐證 fee 與類別新 lookup 用途；不因此宣稱所有 dedication 舊選項已遷移。`new_dedication`、booking、fee 三者各有正式 ID；完整核銷狀態／收據生成、核銷／退款／作廢及三表一致性仍需後續流程證據，不能把一個日期／選項欄當成功能完成。
5. **跨產品／租戶與資源**：第 9 章要求業務資料掛教會；S2 中沒有完整教會 FK／唯一鍵證據的既有表，要由後續核准設計補足伺服器授權及同教會關聯，不沿用字串、owner 或路由作權威。新增模型尚未實作，不能宣稱已通過 A/B 隔離、併發冪等、資源 drain 或 soak 驗收；正式列仍以資料庫 ID 區分，同名不同 ID 保留，同集合重複 ID 拒絕發布。

### 交付邊界與主線收尾

**F0 模型盤點條件已滿足，本文件子任務沒有真正尚未完成的盤點項。** 第 9 章 29 項需求的沿用／擴充／新增、既有真實邏輯名／核心欄位、lookup 方向、正式主鍵、可得聚合及來源 SHA 已交付，足以供後續 G 階段依現況設計。未聚合的 `new_dedication`、`new_disciple_lessons`、`new_registration_form`、`new_roll_card` 與補查的 `new_virtual_contactor` 一律「未取得」；這是已揭露的證據界線，不是要求本包額外查詢或實作的 F0 待補。

職分配置／牧養層級歷史／完整關懷案件的新承載邏輯名、choice label／狀態轉移、課程容量與冪等、奉獻完整核銷收據及租戶唯一鍵屬後續 G 設計／驗收，**不強加為 F0 完成條件**；本次「新增」是能力差距結論，並非已建表或設計已核定。沒有實作 G1，也沒有以本次文件放行 FS 或 G。

主線先前完整守門 9,564 通過／1 失敗是 pool 追蹤競態的歷史實跑，現已修復。最新主線於 **2026-11-28 15:15 Asia/Taipei (UTC+08:00)** 完整一般測試 **9,566/9,566** 通過，測試輸出 SHA-256 **`70242CB6ECC55C2C1602F550B1B8B5549C08F1684FB3E35C825B70D64B0530AC`**；增量建置 **0 warning／0 error**。本次引用主線實跑證據及其回傳 SHA，不是本文件子任務重跑或直接重讀測試原始輸出；11:50 的 9,474/9,474 僅保留為歷史基線。

主線統一處理正式收尾、文件／編碼／進度守門及 ClaudeOnly 文件審查。本次只改兩份報告，不建立 Task、不寫 Trellis／90／決策／JS／狀態檔、不 commit／stash／reset／clean，不啟動外部模型；沒有其他 F0 實作工作留待本文件子任務。

## 2026-11-28 11:50 Asia/Taipei (UTC+08:00) 已發布 inventory 的聚合讀回

SHA-256 `71F864AC23F035B6027ADBD2D4EF479EE0F1970F9DE7F212DCAFA78DA358C367` 的完整產物經本機唯讀解析，僅輸出表名、欄位數及聚合，不讀任何業務列內容，也未再次連線D365。讀取時間與原查詢時間（2026-11-28 09:49）不同。

| 正式表名 | 聚合筆數 | 用途與處置 |
|---|---:|---|
| account | 4 | 客戶候選，保留；租戶欄位由FS擴充，不按名稱合併 |
| contact | 460 | 會友候選，保留；LINE資料為使用者核定的假測試資料，不呼叫複製舊Channel |
| list | 76 | 動態名單只讀schema／聚合，保留，未讀名單查詢或成員ID |
| annotation | 464 | 附件聚合，G5-2處理；未讀附件本文 |
| new_present_record | 41 | 出席既有表，候選沿用 |
| new_group_present_weekly_report | 8 | 週報既有表，候選沿用 |
| new_stor_lessons | 5 | 個人上課／報名紀錄，非課程／開班定義；沿用並擴充狀態機 |
| new_fee | 102 | 收費既有表，保留於好牧人產品層 |
| new_dedication_booking | 5 | 奉獻預約既有表，候選沿用 |
| new_offeringcategory | 27 | 奉獻類別既有表，保留於好牧人產品層 |
| new_linechannel | 1 | 複製Channel只計數；不載入秘密、不登錄、不呼叫 |
| new_outboundtask | 0 | 待送Pending亦為0；未發送或建立工作 |
| new_localizedtext | 0 | metadata存在且聚合0，不把缺表當0 |

管理清冊亦已完整發布；秘密設定及外部網址只確認schema存在，不讀取值。政策分類依02A §2；任何未納入核定清單的金流／電子發票設定不推論已盤點。職分、牧養、關懷的模型判斷已由上節以 S2／S3 補齊；新增能力／語意留給後續 G 設計，未查筆數如實揭露，兩者都不是 F0 待補；不能由聚合表名猜測功能已實作。

## 歷史失敗與最新進度

歷史紀錄（截至 2026-11-26）：完整 `inventory --org goodshepherd20` 兩次均失敗，未發布 `clone-inventory.json`：首次為合法混合大小寫欄名遭誤拒，修正後第二次為 aggregate count 回應形狀遭拒。第二次的實機根因未確認；離線格式相容案例不能當作實機修復。兩份固定摘要 SHA-256 均為 `1C97615A4B561BC9A84C00A2132ED87F5225331A8B9BAFC48AF23726DD8C7680`。依 02 §10 不做第三次相同完整盤點。（此為02A §1核定前狀態；下方記錄修正後結果。）

歷史紀錄（截至 2026-11-27）：三組織首次 `plan --org` 亦均以 `InvalidCastException` 中止，固定摘要 SHA-256 為 `993FB998BA7BC3B8E22BC32A746387AD04F1AEDAEC69A01B65FDCD4FA4CC2B20`；第二次各以 `FaultException` 中止，固定摘要 SHA-256 均為 `3EAD97E3ADACA8B06523033B50F0133E0B173F100090DA38367489501B89E991`。依02 §10不做第三次完整plan。目前沒有可審的三份plan，尚未執行schema apply、quiesce apply、UI匯入或發布。這些是尚未通過的工程步驟，不是可忽略的環境限制。（02A §1核定後仍未重試完整plan；FS根因與最新診斷見FS報告。）

2026-11-26 13:15 Asia/Taipei (UTC+08:00) 獨立來源補讀成功，清冊SHA-256 `347C485962C2E24996101C5D9871BD43BDBD8BBCACA080D73547FB3B2DB1E744`，零寫入並釋放test-env鎖。workflow來源publisher：88筆microsoftdynamics／msdyn、50筆MicrosoftCorporation／空prefix、8筆microsoftfirstparty／空prefix；全部保留正式ID，不假填缺值。這份清冊不取代完整業務筆數盤點。政策草案SHA-256 `69C10E94D38224DA13A687B2BC6B953499D5066E440D5768FC8638F950FD27CB`，明列146 workflow與7280 Plugin ID；尚未審完或套用，不能僅以managed就認定不會寄信。

2026-11-26 13:46 Asia/Taipei (UTC+08:00) 政策安全審查有3 High：managed來源不能證明不外送、ACT None參照值與舊實作不符、信箱原值不可回復。政策仍不套用；程式補正與正式行為分類分開驗證。靜態報告SHA-256 `B930F777D73C2D452634A66719C7E2EA0A408F98CE11F3E47FFC728E0C5DFEF6`。

## 手機待驗

### 2026-11-28 09:49 Asia/Taipei (UTC+08:00) inventory 修正後唯讀重試

- 形狀診斷確認94筆Plugin step的空白`name`及578筆SavedQuery缺少選用`statuscode`；保留正式ID與必要狀態，未以名稱或預設值合併／補造資料。
- 先以離線合成案例重現，再修正後定向測試109/109通過；測試log SHA-256 `C8017D32C86CA07CDE87CCCBD067E1F21B7A95B91502E3FB58F37B5C835146BA`；欄位形狀證據SHA-256 `7350B2A1291F67AAFEA64CB308CBAD9F37E0066B93032EC6BADD769DBBCF2085`。
- `inventory --org goodshepherd20` 修正後重試 exit 0；13個aggregate逐表讀回。log SHA-256 `CD9E726E38352878475B1ADE7077EB62F5C9619429C1226AB78AC2E7E413F11B`；產物SHA-256 `71F864AC23F035B6027ADBD2D4EF479EE0F1970F9DE7F212DCAFA78DA358C367`。本次只讀metadata／aggregate，無D365寫入；不代表FS plan、apply或Live3P已通過。


本盤點無手機步驟；手機驗收留至 M1。所有唯讀證據均不代表流程已停用、推播安全或正式可用。

### 2026-11-26 15:47 Asia/Taipei (UTC+08:00) H1 保留分類證據更正

既有清冊與政策逐正式ID完整相符，只能證明來源與覆蓋。唯讀調查SHA-256 `01AF62361FDF0080A215C57DC337F8AEE4B440F28244B74F19EA7905DBB1EE7A`：五個EmailPlugins正式步驟及其餘來源尚缺目標9.1.49.13可信行為／相依／安全停用契約，不以名稱、managed或簽署一律keep，亦不盲停。H1未知，Step3b未通過，零D365寫入；舊政策尚無新版sourceFingerprint，不補猜指紋、不套用。程式已補ACT及原值before-image與來源綁定，目標選項實值仍待補，不能用離線測試代替。

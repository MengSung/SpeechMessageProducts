# P0-2⓪ goodshepherd20 唯讀盤點

更新：2026-11-28 11:50 Asia/Taipei (UTC+08:00)
工作包：F0 Step 3（由 FS 補做）
狀態：完整inventory與13個聚合已讀回；quiesce依02A §2.6結案，211個信箱仍個別未確認不投遞、不阻擋FS。下方2026-11-26候選表為歷史證據，不是現況；FS完整plan／apply仍未完成。

## 已取得的實機證據

測試組織 `goodshepherd20` 已透過受控 HTTPS／ADFS SDK 核對；組織 ID 為 `ec4d281b-bbe7-f111-8cce-00155d007916`，版本 `9.1.49.13`。唯讀 metadata 為 570 表／22,837 欄，原始快照 SHA-256：`3F7236983F803C68C57C6411B800DBA8884A5A42085EC3DBC601E997E06709F7`。這是結構快照，不是資料庫備份，也不是業務資料筆數。

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
| new_stor_lessons | 5 | 課程既有表，沿用／擴充仍依欄位及關聯對照 |
| new_fee | 102 | 收費既有表，保留於好牧人產品層 |
| new_dedication_booking | 5 | 奉獻預約既有表，候選沿用 |
| new_offeringcategory | 27 | 奉獻類別既有表，保留於好牧人產品層 |
| new_linechannel | 1 | 複製Channel只計數；不載入秘密、不登錄、不呼叫 |
| new_outboundtask | 0 | 待送Pending亦為0；未發送或建立工作 |
| new_localizedtext | 0 | metadata存在且聚合0，不把缺表當0 |

管理清冊亦已完整發布；秘密設定及外部網址只確認schema存在，不讀取值。政策分類依02A §2；任何未納入核定清單的金流／電子發票設定不推論已盤點。職分、牧養、關懷完整模型對照仍待補，不能由上述表名猜測結案。

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

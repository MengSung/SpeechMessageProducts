# P1-11 正式需求與驗收

來源：02 P1-11、90附錄A/G、正式v3.3 Word7.4/7.5及10.10，既有Deployment契約。
全部授權沿用主P1，不再等待核准一般可逆設計；不降低安全／測試或改正式資料。

- d365/schema/speechmessagecore.schema.json：Account五語系欄位及附錄A.1的LINE lookup、Contact兩欄、四新實體、欄位／選項／索引鍵／安全／稽核／樂觀並行。
- A.1 new_sinopacprofile屬P4，清單記phase=4但P1不apply；A.7舊業務表lookup依P8盤點，P1不猜表名；transactioncurrencyid/new_lineid/statecode沿用內建或既有，不重建。
- 新增export-metadata --org：完整結構投影及filter/step定義，不讀業務列、secure/unsecure設定本文或token，輸出d365/snapshots/{org}/唯一目錄。
- plan/apply/verify以--schema明確選結構模式，原P0指令與語法繼續通過全部62案，結構模式不需要Plugin DLL。
- 只goodshepherdback、既有User Secrets固定HTTPS connector；單命令單lease、同組織寫鎖、caller取消與共用例外owner不變。
- 完整比對所有受管定義；既有型別／長度／lookup／選項／日期behavior/format／security／owner／audit／concurrency／key衝突停止，不Update/Delete舊metadata。
- plan包含真組織ID／uniqueName、schema hash、metadata fingerprint、操作及plan hash，apply前重新讀並核對，任何竄改或漂移不寫入。
- 既有元件加入未受管理SpeechMessageCore；發行者prefix=new，不改既有發行者或權限。新增元件的label使用組織基礎語言，不捏造已啟用語系。
- 加密token/secret欄啟用欄位安全；不自行新增角色／帳號／FieldSecurityProfile權限，預設僅系統管理員可存取。
- 日期為UserLocal存UTC、Format=DateAndTime，對應時間點；既有欄行為不變，不執行DateOnly/TZI轉換。
- 唯讀plan/export/verify、兩次apply零重複、錯schema／漂移／不明元件／失效key fail closed；alternate key須Active才可verify=0。
- 真fault保真、取消及連線／檔案／鎖釋放；A/B及同scope併行、獨立命令隔離／弱參照；獨立coverage≥80%、strict、完整方案與00§8。

P1-12的真機驗收另列，不能以合成SDK通過冒充。2026-11-06 Get-VM D365-9.1只讀查詢遭Hyper-V權限拒絕；尚無隔離／備份／復原證據，維持只讀，不能繞過UAC／安全機制。

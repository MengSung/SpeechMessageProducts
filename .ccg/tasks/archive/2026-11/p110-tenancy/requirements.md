# P1-10正式需求與驗收
依據02 P1-10、主P1 Task8、既有Development防護與安全契約。前置P1-04已驗收，不需D365連線。
- 新net10 Tenancy、ILineChannelResolver.Resolve(TenantId)，回傳單次credential及ChannelId；既有沒有ChannelId時明確null，不捏造LINE數字ID，設定有值必原樣回傳。
- TenantId只由host已驗證組織/產品及可選教會建立；不因值物件建構就取得授權，禁止冒號路徑注入及空邊界。
- 中央解析現有LineMessaging鍵；組織缺token後備到DefaultOrganization（null時Jesus），保留差異優先序並固定安全warning。
- 正常必填Resolve缺token fail closed；legacy可選取得入口在未設定時回空，仍由既有送訊入口拒絕。真正設定來源故障不得吞例外。
- ITenantSettingsProvider回完整不可變locale/timezone/country/currency/brand；只讀明確完整scope設定，不保存任何使用者狀態或新背景資源。
- 掃描所有active解析鏈，不以原計畫九處為上限；Development安全檢查讀取token是驗證用途，保留。
- 三種特殊優先序須特性保留：Processor環境→default、Startup default-null→環境、例外sender環境→org→default；一般org→default。
- 1000 A/B、各product/scope、不保留credential/tenant的弱參照、DI borrowed-owner釋放、錯誤保真及masked serialization有證據。
- 每目標coverage（本專案僅net10）≥80%；strict、架構/完整方案/Debug/I18n/duplicates/encoding/packaging與本地提交後才驗收。

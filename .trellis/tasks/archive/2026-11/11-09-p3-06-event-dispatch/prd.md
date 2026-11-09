# P3-06 事件分派與處理器移植

唯一依據：實作計畫3.5的04 P3-06、00§5與§7.1，P3-01及P3-05已驗收推送d0878449。現在只規劃本包，不提前規劃07／08。

- 依P3-01黃金資料先測再移植指定主副本LineBotApp各partial、LineBotMessageHandler、WebhookMessageProcessor與Crm2011Processor可證明的事件流程；每個處理器標來源相對檔名、行範圍及差異。
- 規則放GoodShepherd.Core相應領域，net462／net10皆可用；Host保持R2組裝，AspNetCore層只做已驗簽事件、可信頻道／教會、scoped依賴的adapter。
- 十二種樣本須以真事件解析／分派驗證，文字再依黃金意圖逐個保護實際業務或具名委派與參數；只建介面、switch後成功丟棄、未註冊實作或假預設服務不能算移植完成。
- 只以指定主副本與黃金能證明的版本為準，無證明的副本業務差異逐項列提案並繼續；舊媒體入口comment-only no-op不能冒稱原功能。04明列語音修正須真正以Bearer標頭取得內容並寫annotation，使用可注入HTTP／CRM替身鎖住，不連外。
- 不移植姓名＋手機綁定，也不新增替代綁定；沿ChurchReport現行流程。任何產品呼叫必須由伺服器驗證channel、教會及使用者，不能把route或事件自報tenant作權限。
- 遵R6／R7、唯一來源及穩定資料庫ID，資源確定性清理、取消／timeout、相同使用者保序與跨使用者隔離；本包新增文字不得以中文字面繞過字典。P3-07負責正式IKeywordCatalog路由，P3-08負責Reply／letter完整接通，不能把其未完成項寫成已完成。
- 驗收五項：相關離線測試（雙目標皆過）、固定完整guard、無新專案則coverage不另加、本包實機不適用、一頁回報／開工與驗收DATA。

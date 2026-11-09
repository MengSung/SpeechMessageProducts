# P3-06 設計、邊界與檔案歸屬

P3-01／02／03／04／05均已驗收。只開本包，沒有其他worker可寫相同檔；主會話獨占Git／Trellis／DATA／guard。

## 可證明來源
上一層整合工作樹的 .其他解決方案/LineApiWebhook/ 為只讀來源，只讀指定.cs，不讀設定、私人ID、秘密或CRM資料。Golden/Keywords/routing.json是實際舊matcher與靜態回覆／委派追溯；原檔hash不得改變，CP950先嚴格解碼或以P0-05副本轉碼。P3-PROP-003～005差異不得混成一份想像業務。

## 分層與生命週期
用具名、型別化、不可變的事件與領域操作輸入／結果連接：queue每筆scope→正式IWebhookEnvelopeHandler adapter→可信channel／租戶／使用者解析→Core對應領域規則→有界I/O adapter。Core不得引用net10-only ToolUtility、Hosting、ServiceClient或Extensions。優先重用ToolUtility.Core既有查詢、日期及CRM操作契約，不copy第二份能力。實際可證明業務必須有具體執行端，不能用空sink或NotImplemented作完成。

所有replyToken、使用者、Entity、credential只活於目前事件scope，不能static／singleton保存；不用ToolUtilityFactory程序級身分。呼叫取消需await真I/O及drain，stream／response／scope／lease有唯一owner。媒體有明確大小及格式上限，Bearer逐request設定，不改共用DefaultRequestHeaders；annotation使用權威contact／事件身分，不用顯示姓名去重。

## 寫入白名單
- SpeechMessageProducts.GoodShepherd.Core/{Messaging,Attendance,Appointments,Lessons,WeeklyReports,Care,Members,Notifications}/** 及必要Core csproj；Resources/GoodShepherdText.resx與由I18nCheck生成的.g.cs，只處理本包已搬入文字。
- SpeechMessageProducts.GoodShepherd.Core.Tests/**（只本包測試／fixture與必要csproj資料link）。
- LineMessagingProcessor.AspNetCore/Webhooks/Dispatch/**、WebhookDispatchRegistration.cs、WebhookServiceRegistration.cs（只新增具名partial註冊及準確文件），及AspNetCore.csproj必要向下ref。
- LineMessagingProcessor.AspNetCore.Tests/WebhookDispatch*.cs、WebhookMedia*.cs與必要測試csproj。
- SpeechMessageProducts.LineWebhook.Tests/WebhookDispatch*.cs、WebhookMedia*.cs；既有WebhookContractTests.cs只精確維護正式依賴已註冊後的拒啟動契約。
- 若具體net10 I/O需LineMessagingProcessor下移adapter，可新增LineMessagingProcessor/Webhooks/**及必要ref，但先列原因，不修改無關legacy服務。

不新增csproj；若真的必要，先寫理由交主會話且同操作入sln。不得修改Golden（已驗收權威）、Queue／SQL／Signature、Tenancy既有契約、字典其他WP範圍、計畫正文、設計v3.4、AGENTS、CI或版本政策。遇白名單缺口先寫具體理由由協調者裁決，不任意放寬。

## 協調者開工決策
外部分析經專案自癒入口取得Claude可用結果；Gemini無輸出，dualModelComplete=false／degradedFallback=false／quotaBlocked=false，不稱雙模型成功。採真dispatcher到具體業務、語音逐request Bearer／有界串流、A/B隔離及未知事件具名忽略；原來源先嚴格UTF8，失敗才CP950，不一律用CP950。
姓名＋手機綁定依04明文排除：具名拒絕且不寫CRM、不造新引導網址；由既有ChurchReport流程負責，無需再問使用者。postback課程／簽到可證明業務屬本包；現金付款若需階段4的支付能力，僅該項明確deferred／拒絕且無副作用，列提案，不硬搬支付金鑰或假成功。Claude所稱Golden arguments亂碼需實讀驗證，不能直接採為事實；現有textTemplate為完整UTF8。

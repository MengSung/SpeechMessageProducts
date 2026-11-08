# I18nCheck 完整資源驗證契約

## 入口與責任
`dotnet run --project tools/I18nCheck -c Release -- verify` 串接CJK棘輪、完整resx候選、生成一致性、長度／關鍵字／英文報告與CLDR守門。
`ResourceSet.LoadRepository`沿用方案明列非測試專案，不向父工作樹或外部目錄尋檔。生成檔與v2規則共用同一不可變候選；`--check`／verify不寫任何來源／基準／生成檔。

## 身分及完整發布
- `ResourceCatalog`是唯一XML/key/參數驗證器：同檔重複、未知翻譯key、參數集合差異均拒絕；原同組跨文化的相同key是合法翻譯，不是跨檔重複。
- 所有中性組之間的key必須唯一，即使文字相同也拒絕；不同key的相同顯示文字完整保留，不能依文字去重。
- 一個實體resx被兩專案重述直接拒絕，不以Distinct消除；標準孤兒語系檔不得當中性來源。
- 文化名稱由CultureInfo正規化，zh-TW/qps-ploc保留名稱不因大小寫逃過檢查；pseudo只由既有生成器建立。
- 全部候選完成才發布報告；invalid input轉2、實質規則衝突轉1、全部通過轉0。診斷只含穩定key/相對路徑/文化/長度及固定代碼，沒有資源本文或最長名稱。

## 長度與英文
- `limits.json`必須明列完整longestChurchName及固定Sender20、RichMenu.ChatBar.*14；重複屬性、未知型別、缺檔或放寬門檻拒絕。
- 原語系與從zh-TW生成的qps同驗；受限模板只接受有明確最長值的churchName。與Sender一樣按UTF16 code units保守判定，pseudo補字依原Rune公式不變。
- 使用唯一Placeholders解析器只累計替換後長度，不配置展開字串、不再解析插入值；最多256參數／名稱1024單元。
- 正式E4字面樣本實14字（雖文字說十二字），必須完整保留；不能截為12讓測試過關。固定後綴「小幫手」的pseudo總長21已被拒絕；改「助手」後19，教會名和20上限不變。
- en完成度=原始非空白en翻譯／全部唯一中性key，Invariant固定兩位；缺譯不失敗，其他語系不充當en。

## 關鍵字與生命週期
- `Keyword.*`以|切別名，NFKC、Unicode空白合併、Invariant小寫；原文化跨所有可能共用語系，同字異意圖拒絕、同意圖重述合法；pseudo不當業務命令。
- 工具不新增對產品或host的相依；工具與產品相同正規化向量由各自測試鎖定，不複製連線/credential或業務路由。
- 原始resx每檔16MiB、全repo4096檔/64MiB、每組64原始文化/64MiB、每檔20000鍵/每值32768 UTF16、總鍵及別名各20000；顯式generate也先做組級預檢，不能繞過。相鄰resx目錄走訪最多20000檔，先有界收集再排序，不能先Order全部鄰檔。
- 所有reader立即Dispose；只持操作區域候選，無user/tenant/cache/task/timer/queue。唯一靜態內容是固定文法與平台文化名稱metadata；來源或教會名不能放進static。

## 必要測試
ResourceVerificationTests每規則正反例，實際CLI生成後再verify；保留P0全測試及CLDR生成器。
驗verify失敗唯讀、修正後重試、獨占重開檔案、兩repo交錯、1000次候選並行全部await，以及100個候選弱參照回基線。
真實repo的21字紅燈必須留證；修改資源後用正式namespace重生，不能只憑生成檔自己陳述的namespace通過check就省略真正建置。
Localization雙目標回歸包含resolver→真實生成sender的14字教會名普通／英文回退／pseudo完整答案；所有原精確golden的文案差異要記原因，不放寬斷言。

JSON的逃逸surrogate可能直到JsonProperty.Name/GetString才拋InvalidOperationException；只在LengthLimits的JSON候選解碼邊界轉InvalidData並保留InnerException。禁止把全域catch擴大到任意InvalidOperation來吞程式故障。

## 本機資源生成（P2-05）

顯式生成必須提供`--resx --namespace --class --out`，SharedText的類別名是SharedTextData；
`generate --check`只讀全量，`generate-completeness`另生成完成度。修改資源後兩份都要重生。
從W:磁碟根alias執行時，LocalFiles根目錄加分隔符邊界會拒絕檔案；改以同一工作樹的canonical
D:絕對目錄作workdir執行既有CLI，不改安全檢查、不去父工作樹、不手改生成檔。

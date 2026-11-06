# P1 技術設計（2026-11-05）

## 邊界與現況
以實作計畫 3.3 的 00、02、90 為權威規格，不搬移業務流程。保留全部 P0 測試、既有公開 API、JSON 與 ChurchReport 後備行為。產品 Framework 為 net462，測試執行器 net48 並反射驗證載入 net462。沿用當前隔離工作樹，不動父工作樹。舊來源已找到於父工作樹的 `.其他解決方案/Dynamics365Line` 與 `LineApiWebhook`，僅唯讀。

## 組件與依賴 DAG
Line.Messaging 為通訊基底；Localization 不依賴 Extensions／D365／HTTP；ToolUtility.Core 可依賴 Localization，僅使用借入 IOrganizationService；net462 SDK 用 CoreAssemblies，net10 用 Dataverse.Client，PrivateAssets=all。ToolUtility 保留連線、logging、DI、Facade adapter，向下參考 Core。Tenancy 為 net10，可依賴 Localization、Core、LineMessagingProcessor.Core，現有主機與共用層使用 Tenancy resolver。Deployment 是孤立工具，只用 SDK／ToolUtility 連線，不被產品引用且不引用沙箱 project。R4 採明確 DAG／層級分類，不能按文件示意圖的位置誤擋 net10 共用層向雙目標 Core 的合法依賴。

## 相容與抽取
SDK 先在現有 net10 產生六組 golden，雙目標輸出逐字不變。Sender、Background 與 webhook 附加欄位是選用，不改既有序列化。驗簽對原始 byte[] 進行 HMAC-SHA256；固定長度 digest 比較，不用文字相等。HMAC 由方法擁有並 Dispose。ToolUtility 逐型別列盤點，先特性測試綠，再 git mv 純資料操作；不搬 ServiceClient、檔案、cache、host logger／Facade。物件借用 SDK 連線而非持有或處置外部連線，無 static user state。四個新方法用 XML encoder，fixture 全合成。

## Localization 契約
TextContext 是不可變的伺服器端授權結果；只含組織／產品／教會的文字範圍與文字、格式語系，不保存 HttpContext／identity／credential。變更語言保留 scope。任一從 HTTP／session 取得的 id 不因建立 context 而變成授權，呼叫端必須先完成 server authorization。ITextCatalog.GetText 以教會 exact、教會 *、system exact、parent/alias、zh-TW 順序，原始 system 存在性與 P0 預展開表分開，保留舊 GetText/GetTable。TextArg 具名參數完整集合驗證、不得漏或多，IFormattable 使用 FormatCulture，不設定全域 CultureInfo。快照深不可變且只存文字。

語言 resolver 是純函式；enabled 與 released 交集，預設 zh-TW，en 仍未開放；pseudo 僅非正式且 opt-in。關鍵字 NFKC、trim、collapse whitespace、ToLowerInvariant，跨啟用語系的異意圖同字拒絕。資源以 resx 為單一來源，generated 檔由工具重生。Money 白名單七幣，TWD 顯示 0 是產品政策（不宣稱 ISO TWD minor units 為 0），其餘 2，格式化必保留 currency identity。

## 時鐘
IClock.UtcNow 為共享最小介面；net10 adapter 接 TimeProvider。輸入 UTC 必為 DateTimeKind.Utc；local 必為 Unspecified，DST invalid／ambiguous 拒絕。生日 date-only 不做 zone conversion。net462 以釘選 CLDR windowsZones.xml 產出 deterministic table，net10 直接 IANA；啟動驗證方法檢查 ICU，供後續 Hosting 呼叫。

## Tenancy 與快取
TenantId 來源為既有 server profile／授權鏈；resolver 不保存 request，不把組織路由當權限。保留現有 LineMessaging 鍵與 Jesus token 後備，僅固定安全 warning、不輸出 token。Credential.ToString 不含秘密。設定回傳不可變快照，來源 provider 由 host 擁有。

ChurchTextOverrideCache 接收 TextContext，不提供可繞過授權的任意 id API。完整 cache boundary 含 organization/product/church，change stamp 與 generation 保護 invalidate/loading race。MemoryCache 2000 entries，sliding 30 分、absolute reload 10 分；in-flight admission 亦有界。相同scope single-flight；單一caller取消只停止該caller等待，不能取消其他caller共享載入。host DisposeAsync 停止新載入、取消owner token、drain工作後Dispose cache與同步資源。來源失敗移除 flight供下次重試；candidate 全驗證後atomic publish，重複資料庫ID／key+culture、未知key／參數／長度不符保留舊完整快照並向注入告警入口交原Exception。停用使舊 generation 不得回寫。D365 source 每次只借用 operation-owned service、具教會條件，完整分頁並驗證重複ID，不保留 SDK Entity／連線。

## 部署結構
Schema 與 registration plan 分離模型，CLI 以 --schema 明確選擇結構模式，保留 P0 registration commands。只在 goodshepherdback，沿用固定HTTPS endpoint與User Secrets連線；輸出只有 metadata。Plan 覆蓋 schema hash、organization fingerprint及操作，apply持原有write lock、重新讀metadata驗證、先export再write，僅 Create；既有型別／長度／日期／security／ownership／key衝突拒絕，不修舊欄位。既有元件可 AddSolutionComponent至未受管理SpeechMessageCore，不更改其metadata。alternate key 非Active不算verify成功。日期依附錄明確指定 UTC storage/UserLocal 或指定DateOnly，無明確規格先查設計不猜。不得以應用鎖取代資料庫唯一索引。

Appendix A Account列出7欄但02稱5欄：本階段locale五欄加LINE lookup屬P1結構，sinopac profile屬P4，schema記錄phase並只apply P1；實際核對完整設計後定稿，禁止無聲少建。A.7收費單lookup等P8盤點，本階段不猜實體名。P1-12依本次授權自動執行 plan-review-apply-verify，僅自己的synthetic row在finally清除，不刪既有資料、不發LINE。

## 品質
每WP保留red/green／characterization證據，00§8全方案建置與測試、Debug tracer、duplicates、encoding、I18nCheck。新module每個產品target coverage≥80%，net48 harness驗net462。所有新增與實質修改C#完整繁中XML、UTF8無BOM／CRLF／final CRLF；不用新NoWarn或降低分析器。雙模型分析及review皆經自癒入口。任一credible isolation/resource leak、Critical/High阻擋交付。

## 2026-11-05 使用者審查方式覆寫
使用者最新明確要求「不需要雙模型REVIEW了」。後續不再啟動外部reviewer，由主會話直接逐項審查、修正及實際驗證並保存review.md；已取得的歷史外審證據保留原狀，不冒稱新增雙模型完成。需求、安全／測試／覆蓋率門檻不降低；開發規範所需雙後端分析仍經自癒入口及如實揭露額度降級。這只更改執行協作方式，不修改正式3.3計畫需求，無Word修正報告需求。


## 2026-11-06 最新執行方式
使用者明確表示「不需要雙模型了」，後續分析與審查均由主會話直接完成，不再等候外部模型。20261106-085049歷史分析曾嘗試雙後端：Gemini HTTP403、Claude週額度，無可用輸出，不能稱為雙模型或單模型分析成功；原始證據保留。需求／測試／安全／覆蓋率與實機證據門檻不變，不修改正式3.3計畫，無Word需求修正報告。

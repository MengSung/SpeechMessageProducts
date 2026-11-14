# 出席舊入口的合成輸入與唯讀盤點

2026-11-14 Asia/Taipei (UTC+08:00)。此文件只記錄舊來源的靜態閱讀結果與人寫輸入，不是舊程式執行證據，也不是實機驗收。legacy.json 必須由主代理的隔離 runtime 執行實際舊入口後產生；本工作沒有手寫任何 legacy／expected。

## 來源與入口

唯讀根目錄為工作樹外 `D:/音訊科技產品/系統平台/SpeechMessageProducts/.worktrees/1.0.0.10.IntegrateWebhook_SDK.worktree/.其他解決方案/Dynamics365Line`。只查明列來源與依賴，不遞迴 backup、bin、obj、packages 或父工作樹。未修改來源，未將來源或任何秘密值複製進工作樹。

入口是 `PresentRecordLinePlugIn/PresentRecordLinePlugIn.cs` 的公開 `PresentRecordLinePlugIn.PresentRecordLinePlugIn.Execute(IServiceProvider)`；業務在同專案 `PresentRecordLinePlugInClass.cs`。csproj 的直接 ProjectReference 為 Line.Messaging、LineUtility、ToolUtility、Trace。後置建置命令中的 Abundance DLL 名稱不是可取代這些明列來源的另一組專案。

## 四個情境與保護契約

| name | 觸發與前映像 | 靜態閱讀預測與決定性驗證 |
| --- | --- | --- |
| attendance-reply-changed | Update、Depth 1，Retrieve 回覆與 PreImage 不同 | 舊 MultiCastTextMessageAsync 應形成一個可觀察 LINE 呼叫；收件人 contact-1，訊息包含查詢 lookup 的合成姓名、代禱及回覆。不能讓四個情境全部無呼叫仍通過。 |
| attendance-reply-unchanged | Update、Depth 1，Retrieve 回覆與 PreImage 相同 | 沒有 LINE 呼叫，避免只要 Target 有欄位就誤推。 |
| attendance-created-no-push | Create、Depth 1，供應舊實作要求的 PreImage | MajorExecute 立即返回，沒有 LINE 呼叫。PreImage 是讓離線舊程式抵達 Create 分支的相容輸入，不宣稱 D365 Create 正常會提供該映像。 |
| attendance-depth3-update-blocked | Update、Depth 3，回覆刻意不同 | 外層 Execute 直接返回，不應建立 OrganizationService 或執行 Retrieve／推播；保護舊門檻為大於 2，而非大於 1。 |

每個情境各有獨立 seed 與固定 UTC now，不共享可變記錄。contact-1／contact-2 的 fullname 完全相同；present-1／present-2 的文字亦相同，但 lookup 指向不同 contact。所有正式身分都是 alias 映射後的固定 GUID，姓名不得用於去重。第二筆出席由 observe 留作未被誤改的對照；不為了測試而改變舊程式只處理 Target 的行為。

## runtime 必須遵循的讀取順序

1. 外層先取得 ITracingService 與 IPluginExecutionContext；`Depth > 2 && MessageName != "Create"` 時返回。Depth 3 情境不應要求 CRM factory。
2. 內層 VerifyExecutionContext 要求 Target 是 new_present_record 的 Entity，並且無論 Create 或 Update 都直接索引 `context.PreEntityImages["PresentRecordLineImage"]`；從映像讀取 `new_paster_reply`。映像缺漏會被外層 catch 吞掉，產生空結果，因此正向情境必須提供該映像。
3. 經 factory.CreateOrganizationService(context.UserId) 借用假服務。建立 ToolUtilityClass 與 LineUtilityClass；兩個 utility 都會建立 ToolUtilityClass，所以其建構式攔截不可只做第一次。
4. ToolUtilityClass.RetrieveEntity 以 `ColumnSet(true)` Retrieve(new_present_record, present-1)。對照 Target 的後狀態必須存在於 seed；不能只改 Target 而讓 Retrieve 仍回舊值。四個 seed 的 new_paster_reply 都是後狀態，差異由 PreImage 提供。
5. Create 讀取 directioncode 後返回，兩支內的發送都已註解。Update 比較 Retrieve 的 `new_paster_reply` 與 PreImage；相同則返回。
6. 不同時 SetupReplyString 直接讀 `new_contact_new_present_record` 的 EntityReference.Name，不會為名稱另查 contact。輸入 lookup 的 name 不可遺失。文字運算式為 lookup name + ` 代禱事項: ` + new_explanation + Environment.NewLine + `牧者回應: ` + new_paster_reply；保留空白及換行。
7. SetupLineRecieverList 再 Retrieve(contact, contact-1, ColumnSet(true))，讀取 new_lineid，非空才加入收件人清單。`{"line":"contact-1"}` 由 runtime 內部轉成合法假 U ID；輸出必須還原 contact-1，假 U ID 也不得進產物。
8. 呼叫舊 LineUtility.MultiCastTextMessageAsync；它建立一個 TextMessage 後 await LineMessagingClient.MultiCastMessageAsync。上層 MajorExecute 未 await 此 Task，runtime 必須有界排空攔截工作再收集輸出，不能先序列化空結果。此路徑沒有 SendMessageAsync 中的 CreatePushLineMessage 呼叫，預期無 D365 寫入仍須以實跑確認。

這份來源的上述建構／讀取路徑沒有 new_orgrecord 查詢，因此 seed 沒有虛構該資料。若 runtime 的實際編譯閉包顯示不同讀取，先確認來源版本與明列 Compile Include，再據真實欄位補資料，不可用任意查詢一律回空或硬編碼成功掩蓋缺依賴。

## 攔截、安全與資源需求

ToolUtilityClass(ref IOrganizationService) 建構式會建立固定本機路徑 FileStream、StreamWriter 及 Debug listener；Dispose 沒有移除該 listener 或關閉這些 stream。runtime 應將檔案 I/O 改寫到單次 scratch，並在結束排空工作、清理 listener、卸載 AppDomain、關閉所有自有 stream、刪除 scratch。不能讓真實舊追蹤路徑被寫入，也不能讓 listener 留在後續 case。

LineUtility.SetupChannelAccessToken 有硬編碼 token 後備，LineMessagingClient 建構式會建立傳輸 client。這些值不得進輸出、錯誤文字、HTTP header 記錄或 trace；所有實體傳輸都要在執行前證明已攔截，不能用假 contact ID 當成不會連網的保證。範例中的 church／culture／now 是人寫情境脈絡，不代表舊程式會讀 account 的時區或採用固定時間。

liveShadow 全為 false：本檔只授權離線輸入，沒有啟用真實舊步驟或執行任何真實推播。observe 僅列合成出席記錄與必要欄位。

## 差異守門與整合需求

固定三檔由 Core.Tests 的 Golden/Legacy/**/*.json 嵌入。測試要求每個模組都有 scenarios.json、legacy.json、expected.json；舊的逐情境合成檔不會代替固定 legacy.json。缺少產物即紅燈，不 Skip。

宣告 position 使用絕對 JSONPath，如 `$.cases[0].lineCalls[0].endpoint`，case 為情境 name；每個不同 leaf 各一筆，不能宣告整個 writes／outputs 來覆蓋子值。category 必須是 D5-6 整數 1～5，reason 與 test 必須是非空字串；理由的業務正當性仍需人工審查，守門不會假稱已證明任意說明符合第 5 類。

主代理需產生兩個固定黃金檔並移除舊逐情境合成產物，維護 docs/file-purpose-companions.json，跑雙目標 ExpectedGoldenTests。正常回覆變更案例至少必須有一筆實際記錄的 LINE 呼叫；本測試不會手寫預期來填滿它。合成反例只在測試記憶體中使用，不發布為舊入口證據。

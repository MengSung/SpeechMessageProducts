# LINE RichMenu Word Manual Analysis Request

請協助審視本次文件交付應涵蓋的內容。

使用者目標：
- 說明網路上搜尋到的 LINE RichMenu 新創意點子。
- 說明本分支修改的 RichMenu 程式到底修改了什麼。
- 說明要怎麼調用/呼叫這些 RichMenu 程式能力。
- 說明 RichMenu 有哪些功能。
- 產出詳細、深入、完整的 Word 說明文件。

目前已盤點到的程式變更：
- 新增 LineMessagingProcessor.RichMenus 共用專案與測試。
- 新增 catalog/provisioning/assignment/orchestrator/text trigger/state store/expiration sweep/action factory。
- LineMessagingProcessor.AspNetCore 新增 AddLineRichMenus 與 AddLineRichMenuProvisioning<TCatalog>。
- ChurchReport 新增 ChurchReportLegacyRichMenuCatalog，PushUtility/LineUtilityClass 的 AddRichMenuMessage/DeleteRichMenuMessage 改走 ILineRichMenuAssignmentWorkflow。
- Line.Messaging 已具備 rich menu alias、default、validate、bulk、batch 等 SDK 型別與 client API；目前 RichMenus 共用層已使用 alias/default/list/create/upload/link/unlink，bulk/batch/validate 需在文件標示為 SDK 已支援與可延伸方向，不可誤寫成共用 workflow 已完全封裝。

官方網路來源已抓取：
- LINE Developers: Use rich menus
- LINE Developers: Messaging API reference / Rich menu
- LINE Developers: Messaging API reference / richmenu switch action
- LINE Developers: LIFF overview

請輸出：
1. 文件應包含的章節架構。
2. 容易誤導使用者的風險點。
3. 建議在「已完成」與「未來可擴充」之間如何標示。
4. 5-10 個可以寫進 Word 文件的 RichMenu 創意點子。

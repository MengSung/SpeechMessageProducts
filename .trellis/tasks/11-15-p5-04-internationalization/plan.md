# P5-04 實作與驗收

1. 純LocaleChangeRules及雙目標測試，使用現有發布政策、時區映射及DeterministicGuid。
2. 單一RichMenuAlias命名搬到Core，RichMenus委派；MemberRichMenuService驗證來源及頻道，Shadow僅安全alias意圖，Live沿用十秒HTTP及解密owner。
3. 外殼只保存設定；逐次最少欄位查詢、PreImage完整性、UTC整秒、必要場景與失敗隔離測試。
4. 情境與expected basis=new、註冊Shadow安全設定空、清單停用；同步驗證與非同步待辦分步驟。
5. 綠燈小提交；固定guard八項、部分信任及打包；停用plan/apply/verify=0；一頁回報、驗收、推送與遠端核對。

檔案歸屬：子代理一Core/Localization及對應Core.Tests；子代理二Core/RichMenuAlias、Core/Notifications/MemberRichMenuService與對應測試及RichMenus委派；主代理Plugins、部署、情境與任務紀錄。外部審查只在階段末。

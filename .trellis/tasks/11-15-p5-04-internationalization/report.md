# P5-04 國際化 Plugin 進度

2026-11-15 09:03 Asia/Taipei (UTC+08:00)：本包沒有舊入口，固定 `basis=new`。目前已完成 LocaleChangeRules、RichMenuAlias 共用命名、MemberRichMenuService、LocalizedTextChangedPlugin、MemberLanguageChangedPlugin、ChurchLocaleChangedPlugin，以及 Shadow 停用註冊與合成 scenarios／expected。

已驗證：Core 國際化及換綁測試雙目標 54/54；RichMenus 64/64；Plugin 文字覆寫測試 8/8；文件守門 1582 檔零失敗；變更檔 UTF-8 無 BOM、CRLF、結尾 CRLF。Deployment 全套 36 失敗為既有環境／測試基線問題，未把它當本包綠燈；固定 guard 尚待完成。

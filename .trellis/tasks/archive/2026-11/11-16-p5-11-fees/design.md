# 設計

使用既有 DeterministicGuid 建立每日來源識別，不另建時鐘或快取。Core 的 AllotTaskIdentity 共用日期驗證與識別，外殼從權威關聯解析教會後取得當地日期，Handler 嚴格驗證 payload 的兩欄及來源 hash。查詢對帳與回寫沿用 P4；同步 SDK 只借用，所有可變輸入與通知意圖限單次執行。

不接受輸入參數指定教會。實際入列透過既有 OutboundTaskWriter 和資料庫唯一替代鍵，不能用本機鎖代替跨程序冪等。推播分支依 P5-PROP-006 保留已核定缺口，Shadow 完整記意圖，Live 查詢前拒絕；舊付款 fallback 與收費模組的關係列入階段審查。

# P5-04 國際化盤點

2026-11-15 09:32 Asia/Taipei (UTC+08:00)：依 05A 修訂 2；本包沒有舊入口，basis=new，沒有 legacy.json 或舊步驟映射。來源是本工作樹 20261114-000145 metadata 快照與現行註冊清單，未讀取或執行退役程式。

| 入口 | 觸發與映像 | 讀寫與對外行為 |
| --- | --- | --- |
| LocalizedTextChangedPlugin | new_localizedtext Create／Update／Delete，PostOperation 非同步，rank=10；Update 篩選 new_key、new_culture、new_text、new_accountid；Update／Delete PreImage.new_accountid | account lookup 驗完整後更新 account.new_textchangedon，換教會先舊後新；一次 UTC 時鐘向下取整秒。Shadow 只記 Update。 |
| MemberLanguageChangedPlugin | contact Update，PostOperation 非同步，rank=20；篩選 new_preferredlanguage，無映像 | 依 contact 正式 ID 讀 parentcustomerid／new_lineid，再讀教會代碼、頻道及語系。未綁定不呼叫。Shadow 只記 alias 摘要；Live 經共用換綁服務但本階段不啟用。 |
| ChurchLocaleChangedPlugin | account Update，rank=30；PreOperation 同步驗證及 PostOperation 非同步待辦；四個語系欄位同為篩選及 PreImage | 合併補丁與前像，明確 null 拒絕；驗開放語言、預設包含、跨目標時區及大寫二碼國家。非同步以教會 ID＋完整正規化設定計算 sourceId，由資料庫替代鍵原子 create-or-get RichMenuGenerate。 |

三個入口都是新程式，Depth > 2 返回，未知例外交共用外殼重新拋出；不吞錯、不以顯示名稱合併教會。外殼狀態、SDK 借用、記錄器及解密緩衝皆限單次執行，finally 排空／清除；沒有新增背景 owner。

結構核對：account.accountnumber 字串 20；new_defaultlanguage 16、new_enabledlanguages 200、new_timezone 64、new_countrycode 2；new_textchangedon 為 UserLocal／DateAndTime。contact.new_preferredlanguage 16，parentcustomerid 可指 account／contact，規則只接受 account；new_lineid 500。new_localizedtext.new_key 200、new_culture 16、new_text memo 2000，new_accountid 必填 lookup account。不寫日期以外的教會設定，不寫觸發列以外的業務資料；例外是文字事件更新前後教會，以及教會事件建立 new_outboundtask。

RichMenuGenerateHandler 的輸入由 worker scope 的教會／語系／版面協作者建立；handler 沒有讀 payload 的 settings 欄位，本包只保留來源指紋，不增加 worker 指令。其他模組與 net10 圖片生成不搬進 Plugin；alias 命名搬到雙目標 Core 唯一入口供兩端共用。

唯讀 alias 盤點：goodshepherdback 符合 0／不符合 4，沒有改資料；C:/smv/p5-04-alias-audit.log 僅列計數及不符合教會 ID。已登錄 P5-PROP-003，保持拒絕、不自動清洗。實機影子依 D5-19 live=deferred，部署僅停用 Shadow，differences=null。

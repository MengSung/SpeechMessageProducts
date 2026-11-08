# P2-05 設計

Core純引擎先解析有界JSON候選，再只在字串值位置單次替換{{t:Key}}／{{v:name}}；使用JValue重新序列化，資料不成為JSON結構或第二輪模板。altText必須為完整取字key且解析後1～400字，拒重複屬性／壞語法／缺資料／超限，只有完整結果才發布。結果使用正式FlexMessage及私有不可變IFlexContainer投影，保留各種Flex JSON欄位而不引入新的HTTP出口。

正式Diagnostics.json放產品Resources/Flex並嵌入組件，只用既有字典Ping，包含altText key。sender由發送教會context取Line.Sender.Template，超20改Brand.ShortName（系統預設沿用既有助手稱呼，可教會覆寫），仍超20拒絕；MemberNotification改共用此factory，不讓caller指定。無cache、queue、timer或I/O；嵌入資源讀取由測試／產品呼叫端using擁有，不保留context或資料。

# LINE 事件格式官方查證

查證：2026-11-09 13:41 Asia/Taipei (UTC+08:00)，僅查官方文件，沒有連測試Channel或傳送資料。

官方來源：
```text
https://developers.line.biz/en/reference/messaging-api/nojs/
https://developers.line.biz/en/docs/messaging-api/receiving-messages/
```

- Webhook request以events陣列承載事件；事件共同欄位包括type、mode、timestamp、source、webhookEventId及deliveryContext.isRedelivery。webhookEventId是ULID格式；timestamp是原事件時間，不因重送更新。
- message事件含replyToken與message，依text／image／audio／location／sticker細分。本包只建計畫指定12種樣本，不將官方其餘事件加入範圍。
- memberJoined使用joined.members，內容為source user物件；postback使用postback.data及適用時params。
- accountLink使用link.result（ok／failed）及link.nonce。失敗時可能沒有source或replyToken，後續處理不能一律假設存在。
- 合成fixture的來源／群組／訊息識別與replyToken／nonce皆為假資料，不是可用憑證；需明載合成性質。不得把這些fixture說成真實LINE接收或P3-09實機證據。
- LINE官方建議驗簽後非同步處理；重送時抵達順序可能不同。本階段的分區保序只保證同分區接收順序，不冒稱重建平台原始發生順序。

# P3-03 驗簽

唯一驗收依據：實作計畫3.5的04 P3-03、00§5與§7.1；目前planning，P3-02驗收後才可啟動。

## 需求／先寫測試／完成定義

- Tenancy依channelId取得該頻道secret，再使用既有LineSignature.Verify驗原始body；不得重新序列化或以route值直接授權。
- 空secret的頻道啟動拒絕，不能略過驗簽或改讀其他頻道後備。秘密只來自部署的受信任來源，沒有HTTP配置入口，不快取request／secret解析結果。
- 指定同名測試全部存在並通過：Webhook_InvalidSignature_401、Webhook_EmptySecretChannel_RejectedAtStartup、Webhook_BodyTooLarge_413。
- 附頻道A/B隔離及原始bytes／header格式／取消契約，重用LineSignature不另造HMAC。request.destination為bot user識別，不能與數字channelId直接相等；若核對需可信mapping，不捏造Bot ID。
- §7.1五項：指定測試、完整guard、無新專案故coverage門檻N/A、本包實機N/A、一頁回報與進度同步。既有新主機coverage不得因接入下降到80%以下。

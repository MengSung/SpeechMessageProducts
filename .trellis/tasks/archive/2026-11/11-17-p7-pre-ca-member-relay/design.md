# 前置C-a 設計

沿07A §4.5，不新增第二套通知規則。F12查證：LessonSigningDispatch/AppointmentSigningRule/FeeNotificationService只有SDK讀取與通知，沒有SDK Create/Update；訊息Factory.Create不是SDK寫入。

## 分派與來源
Lesson路徑先以既有TryNotifyLeader解析為整數（本人/組長）；付款不入列。Appointment沿既有Contains優先序與圖片樣式解析整數，Create/Update原固定空參數仍不推播；不得擅自補關鍵字。
Fee與外送不需route，固定0。message編碼1 ExecuteWorkflow/2 Create/3 Update。活動執行個體用OperationId，空時CorrelationId；兩者皆空拒絕。外送重試來源為活動ID。
MemberNotifyRelay.Enqueue先依stor→member、appointment→applicant、fee→contact、letter→sender推導來源account；Execute重新核對queued church，並讓規則在解密前再次核對期望church，防重讀期間資料變動。

## 主機執行與結果
ExecuteAsync傳入sdk/push/church/payload/key/ct，server organizationId以該SDK的WhoAmI取得，不由payload提供。只接受四欄、已知kind/message/route及正式recordId。
規則新增吃解析route的入口，與現有入口共用讀取與訊息組合；舊入口保持原結果分類。Relay接收純傳輸結果：200/AlreadyAccepted完成、NotBound/ChurchMismatch/永久4xx結束；429/5xx/transport拋真例外交工作者重試。外送沿OutboundLetterRecords的狀態與FailureKey回寫。
key只借用，caller清零；單筆SDK/source/訊息只活至await排空，沒有static可變狀態/cache/timer/背景工作。部分信任只用公開SDK/JSON操作。

## Schema
Snapshot/Planner/Engine全鏈辨識缺少選項值，AddOptionValue含entity/attribute/value/label；apply重新驗證plan/metadata，保留原備份flush與定向PublishXml順序。多餘選項及既有標籤不變。

## 實作補充
MemberNotifyRelayDeliveryException繼承HttpRequestException，但保留真ResponseStatus（沒有HTTP回應時null）及原inner；
C-b handler需轉接到Hosting的明確重試契約，不能把無回應偽造為HTTP503，亦不能直接沿舊InvalidOperationException導致永久失敗。
Schema新增ChoiceIsGlobal nullable投影，明確local才可補值；null省略維持舊證據hash。metadata既有label不改。

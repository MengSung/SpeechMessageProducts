# P6-04 契約衝突與待核定接點

2026-11-16 19:13 Asia/Taipei (UTC+08:00)：本包未驗收、未提交產品紅燈、未跑 guard、未推送。依 06A §7-1 請求核定既有轉送斷言的退役，不擅自更改測試期望。等待期間按使用者授權接續不受影響的 P6-01，非平行修改 P6-04 程式。

## 已完成的事實

- 三個指定測試原先 2 紅 1 綠；刪轉送與舊多載後 ToolUtility.Tests net10 1,411、net48 1,268 個測試全綠。
- 同組態實例由既有 Debug Startup／ambient 案例新增同一物件斷言證明，1 案通過；Program 將 builder.Configuration 直接交 Startup，不另建組態。
- 刪舊入口後第一次編譯找到 Startup 與 ProductRegistrationTests；修正後第二次找到 RunCServiceGraphTests、ScopedLifetime 兩處及 AmbientGateway 一處，共六個呼叫點；不是靠搜尋發現。
- ToolUtility.Dataverse.Tests Release 79 個測試全綠；ChurchReport 723 案中 722 通過、1 案失敗。
- ExceptionLoggerProviderCompatibilityTests 與 ChurchReport ExceptionLoggerProvider.Forwarder 為另一組獨立相容性，均未改動；ToolUtility → Hosting 專案參考未動。

## 衝突

ChurchReport 的 ExceptionOutputOptionsTests.Forwarded_legacy_types_preserve_identity_and_report_abi 要求三個型別仍可由 ToolUtility 組件限定名稱解析。06A 要刪該轉送，且新指定測試要求沒有轉送；兩者不能同時成立。既有測試還包含四參數 Report ABI，不能整檔刪除。

建議：只退役該案例的舊組件名稱解析斷言，保留 Report ABI、停用輸出與 owner 排空斷言及所有其他案例；另兩途為保留轉送並豁免新條件、或暫緩本包。已詢問使用者，尚未核定。

## 安全保存與恢復

七個產品／測試檔的全部變更保存在本機 Git stash `280ab07fd620f02deb3c12dc669480026539ebe7`，訊息 P6-04 pending approval；已確認 stash 檔案範圍精確且工作樹產品恢復 b53139fc。核定後用該 SHA apply，不用不穩定的 stash 索引；保留 stash 到本包驗收。失敗的既有測試檔未被修改。

原始證據在 artifacts/p6，不進版控：
- `p6-04-red.log` SHA-256：`e27bf295a1a9218dcaaf13b07b0b747decf0131e5ff97e366678b4e3e3deab81`
- `p6-04-same-configuration.log` SHA-256：`8fe8ef80baa5629fb8d701d9cabf972bccb32775514c59044073e29be79fb775`
- `p6-04-callers-compile-1.log` SHA-256：`e72a93a52fc62a0b3abeb9ba8e92fd7a7ef8c74bdba3dbc3b752eda6324b353b`
- `p6-04-callers-compile-2.log` SHA-256：`5941287b28d1b9498170a65c02d30af08226a7ef131fcdc9f8d96260ad895122`
- `p6-04-toolutility-green.log` SHA-256：`73b2e0c0fac3017736cd85071466ee00640398b5b52d1c673cee95dce25e9387`
- `p6-04-dataverse-green.log` SHA-256：`27df6609cccd49b13b5ec0d8fe1f587a6b8ab39903b420f702368bee8357ef8c`
- `p6-04-churchreport-green.log` SHA-256：`190f3d308e6a0622a3e39ebb07302d3bdfb21f31519ad0142f88e02acbf9a552`

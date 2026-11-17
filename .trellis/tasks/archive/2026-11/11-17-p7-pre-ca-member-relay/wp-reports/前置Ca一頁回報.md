# 前置 C-a 一頁回報

狀態：2026-11-17 13:55 Asia/Taipei (UTC+08:00)驗收；程式9370d014固定guard完整八項通過。單元全離線，不連D365／LINE。

## 實作結果
第四類MemberNotifyRelay=100000003全鏈與schema完成；四欄kind/recordId/message/route，不收流程文字或秘密。
三活動／外送Plugin的Live只入列；付款分支仍查詢前拒絕。來源從學員／申請人／繳費會友／寄件者正式關聯取得，
Host重新驗來源教會、使用server WhoAmI組織ID、各規則解密前再驗教會；沒有static可變資料或新增csproj。
三規則F12逐行確認無SDK寫入，無需把資料寫入搬回活動。通知共用原規則及Retry Key，外送沿既有狀態回寫，不覆蓋description。
Schema AddOptionValue只補明確local choice的缺值，保留既有label／額外選項；未知／global拒絕，before落檔及定向publish順序保持。

## 測試與打包
21個指定方法同名存在；ToolUtility兩宿主3/3、Core兩宿主13/13、Plugins6/6、Schema3/3，另global/unknown2/2。
完整四專案：ToolUtility net48=1271/net10=1414；Core各384；Plugins621；Deployment788；全部通過。
紅綠證據在ignored artifacts/p7/pre-ca-*-red.log與對應結果，不把離線測試冒稱實機送達。
兩DLL build18／Test-PluginAssembly通過，Plugins1160192 bytes、Workflows1143296 bytes，均未超8MiB；未部署。
Plugins SHA256=7510eeb51feddacced9fd4ef5d6b9749b8df55b0ac524d5455f535554cb853fc。
Workflows SHA256=33af0f53e929c16ed29db9399aef47ccb70cf2094356183376c7191cb956ca28。

## D7-24核定期望變更（僅四筆）
| 檔案／方法 | 前→後 |
| --- | --- |
| AppointmentSigningShellTests.cs / Appointments_ActivityLive_RejectsBeforeAnyQuery | 三種Live寫法均零讀拒絕→均由真活動入列，不讀key／推播 |
| LessonSigningShellTests.cs / Lessons_Signing_WorkflowLiveWithoutKey_RejectsBeforeReads | 全分派拒絕→三正常分派入列，付款原拒絕斷言全保留 |
| Fees/FeesWorkflowTests.cs / Fees_Live_RefusedBeforeQuery | Live零讀拒絕→真活動入列來源教會代送 |
| OutboundLetterShellTests.cs / Messaging_Live_RefusedBeforeSdkRead | Live零讀拒絕→真Plugin只入列四欄代送 |

其他舊測試斷言／期望值沒有修改；SchemaEngineTests只新增嚴格InsertOptionValue替身分支，未改既有斷言。
測試名稱保留舊名以提供歷史追溯，說明已同步新行為；新同名指定測試另完整執行。

## 下一步與限制
C-b必須將MemberNotifyRelayDeliveryException的真ResponseStatus／原inner接到worker可重試契約；不以假HTTP503或Timeout取代真正故障。
外部分析：單模型（使用者指定ClaudeOnly），已透過自癒入口完成；唯一階段外審留最後，不重跑單元審查。
本目標仍待C-b、P7-01a/b/c、P7-02a/b與階段審查。前置A一筆診斷trace留R9先備份再清理。

固定guard：Release 7128（22組件）、另Debug tracer7，編碼／重複／I18n／文件／進度全通過。
原始證據artifacts/p7/guard-9370d014-full.log，SHA256=f40cedb79b381f917266956b224ea794dc095e3f5809ae3502e63940e055141e。

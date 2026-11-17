# 前置C-b 主機代送工作者與實機一次

依07A §4.5/§5前置C-b，C-a已驗收推送1b83662d，固定時間盒1天，不啟動P7-01。
需求：預設關閉、嚴格布林Messaging:NotifyRelay:Enabled；獨立worker只認領第四類，每次store／handler各自scope。
key沿WebhookScopeResolver同一段讀取及清理，不另寫副本。缺key失敗、來源重驗、不跨教會保留資料；停止排空再釋放。
八個指定離線測試同名存在通過，Live_Relay_PushesOnceToTestContact標Category=P7Live，一般測試排除。
實機依序D34→schema僅一AddOptionValue→apply/verify0→兩DLL僅UpdateAssembly→apply/verify0→建一筆代送、先等2分鐘無認領，
本機主機只開relay，60秒內完成且測試Channel使用量+1→停止→精確刪測試列／待辦→verify0與殘留0。
不得修改IIS/Node、不連正式、不啟用舊項目、不新增csproj或輸出秘密。

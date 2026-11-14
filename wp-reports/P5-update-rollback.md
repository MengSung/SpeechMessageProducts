# P5 更新回復前置：離線紀錄

2026-11-14 12:33 Asia/Taipei (UTC+08:00)。目前尚未驗收實機換版，不判定FileVersion是否足夠。

| 需求 | 證據 | 狀態 |
| --- | --- | --- |
| Plugin自己的FileVersion | 同名紅綠測試、正式簽章合併檔FileVersion1.0.0.3，AssemblyVersion1.0.0.0 | 離線通過 |
| smoke只發布本次安全版本 | Deployment236/236，嚴格四段、來源、重複trace拒絕、共用correlation只讀一次、失敗取消清理 | 離線通過 |
| 外殼與member最小CAS | Debug5/5、Release172/172含CAS5案；真Shadow及Live合成HTTP，沒有真LINE | 離線通過 |
| CAS卸載與key隔離 | 單一Framework子程序direct紅1/相容路徑綠1，空stderr、兩次卸載、exit0；原crypto雙宿主各33/33及新封包互讀各1 | 離線通過 |
| 合併閉包 | 最新合併member6/6、metadata全true；不是merged CAS共同測試 | 離線通過 |
| HMAC成本及資源 | component140400次digest比對、10域卸載、handle增量0、retained最高1178792bytes；額外1424bytes/call與0.30–0.42微秒 | 已量測，短跑限制 |
| 審查 | 最終獨立本地Critical0/High0；外部自癒三輪無可用輸出，未取得外部審查 | 不宣稱雙模型完成 |
| 完整固定guard | 尚待本次精確提交 | 未完成 |
| 第一版→新版→第一版、同step ID、殘留0 | 尚未執行本輪實機 | 未完成 |

Framework4.8.10008.0的NativeHmac域內靜態CSP provider卸載失敗，與當次HMAC正確using不同；無覆寫的私有子類避開本機exact-type快速路徑，仍由Framework選政策，不調FIPS或授權。FIPS=True環境未測；servicing更新必須重跑程序回歸。研究與效能摘要在本子任務research，原始探針／IL／log保留本機ignored證據。

相依缺項與實機收尾仍依preflight及驗收/P5_更新回復驗收步驟.md；本紀錄不授權新Live步驟、不讓舊新同時啟用、不碰正式組織。

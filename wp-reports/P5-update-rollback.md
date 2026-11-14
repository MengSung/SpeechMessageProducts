# P5 更新回復前置驗收紀錄

2026-11-14 12:54 Asia/Taipei (UTC+08:00)。離線與goodshepherdback診斷更新／回復均已通過；本輪只改FileVersion即生效，未重啟主機服務。

| 需求 | 證據 | 狀態 |
| --- | --- | --- |
| Plugin自己的FileVersion | 同名紅綠測試、正式簽章合併檔FileVersion1.0.0.3，AssemblyVersion1.0.0.0 | 離線通過 |
| smoke只發布本次安全版本 | Deployment236/236，嚴格四段、來源、重複trace拒絕、共用correlation只讀一次、失敗取消清理 | 離線通過 |
| 外殼與member最小CAS | Debug5/5、Release172/172含CAS5案；真Shadow及Live合成HTTP，沒有真LINE | 離線通過 |
| CAS卸載與key隔離 | 單一Framework子程序direct紅1/相容路徑綠1，空stderr、兩次卸載、exit0；原crypto雙宿主各33/33及新封包互讀各1 | 離線通過 |
| 合併閉包 | 最新合併member6/6、metadata全true；不是merged CAS共同測試 | 離線通過 |
| HMAC成本及資源 | component140400次digest比對、10域卸載、handle增量0、retained最高1178792bytes；額外1424bytes/call與0.30–0.42微秒 | 已量測，短跑限制 |
| 審查 | 最終獨立本地Critical0/High0；外部自癒三輪無可用輸出，未取得外部審查 | 不宣稱雙模型完成 |
| 完整固定guard | 固定8ad4dccc，八項exit0、Release5524/5524、Debug tracer7/7 | 通過 |
| 第一版→新版→第一版、同step ID、殘留0 | 沙箱1.0.0.3→1.0.0.4→1.0.0.3，各smoke/verify=0；更新plan只有UpdateAssembly | 通過 |

Framework4.8.10008.0的NativeHmac域內靜態CSP provider卸載失敗，與當次HMAC正確using不同；無覆寫的私有子類避開本機exact-type快速路徑，仍由Framework選政策，不調FIPS或授權。FIPS=True環境未測；servicing更新必須重跑程序回歸。研究與效能摘要在本子任務research，原始探針／IL／log保留本機ignored證據。

相依缺項與實機收尾仍依preflight及驗收/P5_更新回復驗收步驟.md；本紀錄不授權新Live步驟、不讓舊新同時啟用、不碰正式組織。

## 實機結果

2026-11-14 12:50–12:53 Asia/Taipei (UTC+08:00)：D34測試綁定1／其他0；19自訂Plugin步驟起訖皆停用，啟用中的自訂流程定義0，6個系統activation快照無舊自訂組件參照。trace原值1。第一版啟用計畫只有UpdateAssembly與UpdateStepState；第二版計畫只有UpdateAssembly，AssemblyVersion維持1.0.0.0、名稱及簽章相同。用第二版apply前export還原第一版，本機DLL亦還原，三次smoke皆首次通過，不需要等候重試或重啟。

唯一診斷step ID全程為c3ce836c-48d8-f111-8cc9-00155d007916；收尾計畫只有UpdateStepState、apply/verify=0，清單逐位元組恢復false，追蹤回1。精確捕捉3個本次smoke task，收尾task／關聯作業0；另唯讀完整查詢本輪時間窗的診斷trace為0。未啟用舊步驟、未發LINE、未部署業務模組。原始CLI安全摘要及plan/export留本機ignored；第一版DLL及原清單備份在repo外smv-evidence。

操作工具第一輪因PowerShell既有cli別名衝突在plan前停止；finally讀回仍停用且追蹤已還原，0個task。改用明確函式名稱後才完成上述有效實驗；不把工具啟動失敗當產品驗收失敗。

後續標準：固定AssemblyVersion與簽章、遞增FileVersion，plan→審閱→apply→verify=0，依實際沙箱追蹤證明新版本；每次仍須驗證，不以本輪結果保證所有主機永遠免重啟。若追蹤舊版只等一次兩分鐘重試，再依授權主機重啟路徑處理。

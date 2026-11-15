# P5-07 一頁現況（已驗收、待收尾推送）

已核定缺口（P5-PROP-006，使用者授權 Claude 代為核定）：P5不實作推播活動Live安全金鑰，查詢前拒絕並保留真例外交共用失敗／告警邊界；舊建收費單與信用卡／ATM付款fallback不在D365實作，Shadow固定記「未實作：P5-PROP-006」。Host非機密意圖架構留階段審查與3.6，P7切換前完成；付款缺口不算差異0或P7補驗，P5-11核對與收費模組關係。

2026-11-15 20:13 Asia/Taipei (UTC+08:00)：Workflows 8 先前正式工具第二次apply失敗後，依核定以一次性本機診斷程式直接執行同一組件 UpdateAssembly（不改repo、不改部署工具、不重啟）；更新成功，未產生fault-detail.txt。正式verify=0、export=0、export-legacy=0，P5-07可驗收。程式HEAD a825f9da3b87efd112e9ba7bf15c65f9f6fd440a；遠端仍為aa7adf06c6b7badaaa427c324ad5c7c1be5f0dab；本包17筆程式提交，依核定a／b拆分，不改歷史。

| 驗收項目 | 實際結果 |
|---|---|
| 規則、外殼與CAS | Core雙目標各236、Plugins498含六入口CAS、Deployment761；固定guard Release 22目標6668項全過 |
| reading與核定缺口 | 正常簽核完整Flex、進階三案逐欄、QR31／32圖與設備；付款保留舊建單／金流來源，不列declaredDifferences，新Shadow只驗核定標記，Live查詢前拒絕 |
| DLL打包 | Plugins14／Workflows8，AssemblyVersion均1.0.0.0，Test-PluginAssembly檢查ok=true；實機匯出 SHA 與本機兩 DLL 一致 |
| 最終部署 | 一次性診斷 UpdateAssembly 成功；正式verify=0、export=0、export-legacy=0 |
| 新舊啟用狀態 | 16個新步驟啟用0、secure0；舊步驟與舊流程啟用皆0，無業務觸發 |
| 固定guard | a825f9da固定八項全0：Release建置／測試、Debug tracer7、編碼、重複碼、I18n、文件、進度28 |

## 已核定缺口：P5-PROP-006

上述兩缺口依核定執行：不讀token、不增待辦種類／中繼Plugin、不呼叫金流；正常Shadow完整推播意圖，付款Shadow固定標記。reading／simulation、live=deferred、differences=null，不宣稱實機影子差異0。學籍卡只驗本包producer，不捏造未取得的consumer彙總公式；追蹤管道後續處置併階段末待核定提案，不另開工作。

部署故障摘要：伺服器事件未見對應錯誤，原正式工具只保留0x8004501D且未保留說明；一次性診斷程式直接呼叫同一UpdateAssembly後成功，因此未取得可摘錄的fault-detail.txt，不猜測根因。未重啟、rollback、smoke、shadow或diagnostics；live=deferred維持不變。偶發SDK弱參照回收測試依既有guard紀錄，不修改。

證據（本機、不提交原始診斷程式）：`artifacts/p5-07-final-package-check.log`、`artifacts/p5-07-workflows8-diagnostic-verify.log`、`artifacts/p5-07-workflows8-diagnostic-export.log`、`artifacts/p5-07-workflows8-diagnostic-legacy.log`、`artifacts/p5-07-workflows8-diagnostic-state.json`；最終匯出索引 `artifacts\d365\goodshepherdback\20261115-121150-2084365-9204448931554b0fbb11cc5c7a0a2e32\backup.json`。Plugins SHA-256 8EA71DAA80CD4960BEEBB5164AA01BFAEA134430416CBB461AF05190AB108DB1；Workflows SHA-256 B020FA5AC4DD4246A8565DDF7B6C9E75625B3EE05B8B9937835D2F4F6A45D497；診斷程式僅存於 `C:/smv/p5-07-workflows-update/workflows8-diagnostic/`。

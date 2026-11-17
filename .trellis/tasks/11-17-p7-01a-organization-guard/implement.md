# P7-01a 執行計畫

- [x] 前置C-b驗收推送SHA核對，讀07A／相關spec及搜尋F6全部硬編碼。
- [x] ClaudeOnly經Start-CcgDualModelRun自癒入口分析，處理Critical/High，不新增驗收門檻。
- [x] 新增OrganizationsTests十三個指定方法，catalog/guard先空殼，跑紅燈；先catalog嚴格解析，再guard非法旗標／名稱確認零連線綠。
- [x] connector合成A/B租約與host mismatch紅→獨占連線及catalog驗證綠；plan/server不一致拒絕。
- [x] CLI/Program及schema/channel/engine各路由串行調整，保留原簽章與所有舊斷言；production export-legacy counts不阻擋、test維持原語意。
- [x] 逐檔讀完整後修改；新增程式檔含完整繁中導讀／成員契約，UTF8無BOM/CRLF/final CRLF；JSON補companion。
- [x] help及backend specs同步；rg字面goodshepherdback只留ShadowTestIdentity與說明，回報逐筆解釋。
- [x] dotnet test SpeechMessage.Dynamics.Deployment.Tests -c Release；dotnet test SpeechMessage.Hosting.Tests -c Release --filter FullyQualifiedName~Architecture。
- [x] 提交P7-01編號程式→固定C:\smv\guard完整00 §8.1八項→一頁報告、里程碑、封存／輕量三檢查→推送ls-remote核SHA。
不執行任何D365連線，P7-01在a/b/c全完成前保持todo，進度只寫里程碑。

2026-11-17 17:50 Asia/Taipei (UTC+08:00)：OrganizationCatalog/OrganizationsTests新增未提交；三指定方法展開五案，NotImplementedException真紅後全綠。尚未接production guard/connector/CLI，未驗收。ShadowTestIdentity抽AllowedOrganization常數供唯一test關卡共用，未改實機語意。分析所述Schema未核serverId是誤讀，ValidatePlan重算含serverId的完整hash已保護；後續補具名回歸，不新增弱驗證。下一步ProductionGuard、其餘十方法與每個分流零連線／成功路徑。

2026-11-17 17:59 Asia/Taipei (UTC+08:00)：ProductionGuard新增五指定方法及共用Channel輸入回歸，12案NotImplementedException真紅→17案全綠；connector兩指定方法真紅→合成A/B/A與host mismatch綠，連同原Connector測試共27綠。新增唯一goodshepherdback organizations.json及companion；catalog Load/LoadDefault每命令重讀、有界檔案／link/strict UTF8，未接CLI前不改外部行為。完整Deployment808全綠，檔案尚未提交；下一步RunWithCatalog共同入口在Channel/Schema分流前執行guard並共用stdin owner、Program按canonical org讀secret、各路由/engine移除硬編碼及剩餘三指定測試。全程未連D365。

2026-11-17 18:25 Asia/Taipei (UTC+08:00)：十三指定方法同名存在全部通過；新OrganizationsTests32案，完整Deployment821綠。Program按canonical組織讀key，guard在Channel/Schema分流前；production serverName錯配真紅後綠、schema正式匯出與Channel共用stdin成功路徑通過。schema catalog顯式傳遞且server ID漂移拒絕，deployment/document engines改查catalog、Shadow/module仍限test。help與三份backend spec同步，I18n綠、文件1770檔0失敗。尚待architecture結果、提交及固定guard；未連任何D365、未驗收推送。

2026-11-17 18:35 Asia/Taipei (UTC+08:00)：5f61b890完整固定guard八項通過；本單元驗收、封存紀錄輕量三檢查後推送核對，再啟動P7-01b。

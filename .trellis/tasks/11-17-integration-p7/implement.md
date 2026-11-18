# 階段 7 執行接點

## 起點與基準

起點 f06d7596；固定順序、不平行。全方案 Release 7071 個測試（22 組件）、另 Debug tracer 7；Deployment.Tests 769，取自 P6 最末 guard-f67e0257-full.log，不為基準重跑。清單步驟 22、活動 12；cjk-literals.json 專案 count 合計 3764（與 P6 回報單產品數字口徑不同）。

## 執行清單

- [x] P7-00
- [x] 切換前置 A
- [x] 切換前置 B
- [x] 切換前置 C-a
- [x] 切換前置 C-b
- [x] P7-01a
- [x] P7-01b
- [x] P7-01c
- [ ] P7-02a
- [ ] P7-02b
- [ ] 階段審查與 §6A 收尾

## 驗證與續跑
每段指定測試先紅後綠，通過後提交並在固定 guard 完整 00 §8.1；P7-00/前置 A 依 07A 輕量檢查。每次開始讀頁面 DATA、git log/status、父任務與最新 goodshepherdback artifacts，verify；若演練未回 off 先回復。首次推送在前置 B 驗收後。
每段不追加完成條件；只做一次階段外部審查。雙模型分析走自癒入口；本次 Claude 有輸出、Gemini 403/503 經兩次自癒仍無輸出，dualModelComplete=false、quotaBlocked=false、degradedFallback=false，未取得完整雙模型分析。保留本機 artifacts，後續重試不可冒稱成功。

## P7-00 前置唯讀檢查
本次新打包兩 DLL 檢查通過。初次 verify 差異只有兩個 UpdateAssembly，因本機新打包與 P5 已部署產物不同；未寫入組織。export 證明組織 DLL 各自 SHA 與 P5 artifacts 相同；保留新打包於 artifacts/p7/repacked-start，以 P5 基準產物再核對。
D34 已實測通過；待辦 0、追蹤原值 1；22 新步驟全部停用、舊步驟/流程啟用 0。需要使用者的前置條件集中 preflight.md。

2026-11-17 12:46 Asia/Taipei (UTC+08:00)：P7-00三項輕量檢查通過，回報與preflight已保存；本機提交後不推送。前置A需提升權限等待中，依07A先接離線B/C-a。

2026-11-17T12:48:34+08:00：使用者指定本階段ClaudeOnly；前置A由使用者重啟後回報。貼上憑證路徑曾變成Env提供者，失敗在讀檔、尚未重啟；已提供固定完整路徑修正。P7-00提交d56600cb，不推送；先做前置B。

2026-11-17 12:53 Asia/Taipei (UTC+08:00)：前置A追蹤可用，單次smoke通過並回報PluginFileVersion=1.0.0.17；off=0、verify=0、追蹤還原1、smoke task0。新診斷追蹤1筆留本機對應範圍供R9先備份再清理，非目前測試資料殘留。後續影子比對依07A必做。

2026-11-17 13:08 Asia/Taipei (UTC+08:00)：前置B指定測試14案與Deployment783案通過；盤點live19/dormant14，8項依D7-13降級，原6項保留；待完整固定guard驗收。

2026-11-17 13:17 Asia/Taipei (UTC+08:00)：前置B驗收，程式1620547e固定guard八項全過，Release7085/Debug7；19live與14dormant，evidence仍空白未宣稱Live通過。收尾後首次推送，下一單元前置C-a。前置A的1筆trace仍留R9備份清理。

2026-11-17 13:20 Asia/Taipei (UTC+08:00)：C-a研究查明AppointmentPlugin的Create/Update沿舊空參數，NotifyAsync Contains不成立即返回；B盤點兩列效果文字已更正，分級不變。原產品/期望值不修改；活動正常路徑與Plugin有明確分派差異，後續Relay對照須選有效route或對應期望檔。

2026-11-17 13:55 Asia/Taipei (UTC+08:00)：C-a驗收9370d014，完整guard八項全過；Release7128、Debug7。兩DLL打包build18通過但未部署，接C-b。

2026-11-17 17:41 Asia/Taipei (UTC+08:00)：前置C-b驗收紀錄5398acef（程式952780bd）；固定guard完整八項全過，Release7144／Debug7；Live代送一次通過、殘留0。

2026-11-17 18:37 Asia/Taipei (UTC+08:00)：P7-01a驗收紀錄3e291416（程式5f61b890）；固定guard完整八項全過，Release7176／Debug7；推送ab2a4422並核遠端SHA一致。

2026-11-17 20:39 Asia/Taipei (UTC+08:00)：P7-01b驗收提交57ab3f62，固定guard完整八項通過，Release7219／Debug7；Deployment864、Architecture89。21指定方法及未移植KeepLegacy測試通過；回復與CLI循環離線通過。收尾後接P7-01c，每段驗收同步此清單。四個舊任務目錄有實際evidence檔案，保留。

2026-11-17 21:53 Asia/Taipei (UTC+08:00)：P7-01c驗收提交073cd5ef，固定guard完整八項通過，Release7262／Debug7；Deployment900、Architecture89；十三指定方法展開17案全過。a/b/c全驗收，整包P7-01完成。接P7-02a，前置A追蹤可用，R4必做。

2026-11-18 17:58 Asia/Taipei (UTC+08:00)：補正 R4 診斷。16:07／16:40 的安全 stack 均為 DeploymentCli.RunShadow:83（完整部署 Verify），當時先開 diagnostics 與 manifest 停用狀態衝突，未進 factory，不是 seed 失敗。原 DateTimeOffset 格式能解析無小數秒 UTC，既有 Convert_DateBehaviorsAreExplicit 亦通過；35801fdd 的日期根因判斷撤回，恢復原產品日期格式，保留加強斷言。未提交的 metadata 重建／Exception.Data 診斷嘗試已撤回並在本機保留 patch，未放寬任何檢查。

2026-11-18 17:58 Asia/Taipei (UTC+08:00)：R4正確前置單次驗證仍有切換／收尾fault；獨立核對off、trace1、D34與清冊0。新增「已停用舊step只讀」6案，Deployment927通過，待guard與實機，不勾選R4。後續提交皆繁體中文，外部雙模型與ClaudeOnly停止。

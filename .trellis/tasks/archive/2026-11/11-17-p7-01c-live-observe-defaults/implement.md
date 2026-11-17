# P7-01c 執行清單

- [x] b段驗收／封存／推送37f81bd1並ls-remote核SHA，讀07A與現有Shadow接縫。
- [x] 以自癒入口取得ClaudeOnly分析，核對建議與現有實作，不做單元外審。
- [x] 先寫13指定方法真紅，先ChurchDefaults plan/apply冪等與空欄／Channel範圍，再ObservationReader唯讀counts與無trace語意。
- [x] LiveCheckRunner拒production／非Live、dormant略過；fault／cleanup與pushcount差異先紅後綠。
- [x] 以既有Shadow投影比較接真SDK seed／trigger／observe／待辦清冊；活動手動複本生命週期及不可恢復故障保留。
- [x] CLI嚴格選項、寫鎖、來源hash／server／過期關卡、help與spec；固定繁中文案資源及位元組。
- [x] Deployment全套及Hosting Architecture通過→提交→固定C:\smv\guard完整00 §8.1→一頁回報／進度整包P7-01標done、父清單→封存推送核SHA。
本段全部離線；P7-02a R4必做，前置A追蹤可用不能退回D7-5；他案line-token-config-separation不動。

2026-11-17 20:50 Asia/Taipei (UTC+08:00)：ClaudeOnly分析ok=true／dualModelComplete=false。分析C1/C2/C3不是使用者新決策阻擋：scenario liveShadow標記不修改expected，P7演練按07A既定需要挑正常案例；第四欄schema new_countrycode；活動1小時做不出即dormant。採納獨立Live runner，不把Live偽裝legacy、不動步驟狀態；原Shadow介面保持相容。

2026-11-17 21:40 Asia/Taipei (UTC+08:00)：十三指定方法展開17案綠；真SDK round5案、活動probe3案、CLI9案、defaults含送達後回復5案；Deployment899案綠（其後只改資源及共用HTTP）。架構首輪R9拒Deployment直接HttpClient，已移既有LINE核心且新增7案全綠，規則與白名單不變。待最新架構／整套及固定guard，不宣稱c驗收。

2026-11-17 21:53 Asia/Taipei (UTC+08:00)：073cd5ef固定guard八項全過，Release7262／Debug7；驗收回報及父清單同步，封存後輕量檢查／推送核SHA。

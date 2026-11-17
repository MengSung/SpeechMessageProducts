# 前置C-a執行

- [x] 讀完整AGENTS及三規則，確認F12沒有SDK寫入；外送沿既有讀取/回寫。
- [x] ToolUtility三指定測試紅→第四類型全鏈→雙目標綠。
- [x] Relay來源/分派/執行11指定測試紅→綠，規則共用入口。
- [x] 四外殼/OperationId/付款拒絕/部分信任指定測試紅→綠；只改D7-24允許期望。
- [x] AddOptionValue三指定測試紅→綠，schema加選項；伴隨說明同步。
- [x] 指定四專案全綠、打包/大小/檢查、README/繁中註解與位元組。
- [x] 程式提交，固定guard完整00 §8.1，一頁回報/進度，驗收推送並ls-remote核對。

三規則查證及設計不等於實作完成；本單元不連D365/LINE。實機集中C-b/P7-02，活動付款不實作。

初步四專案：ToolUtility 1271/1414、Core 384/384、Plugins 621、Deployment 788全過。
打包build18兩DLL／Test-PluginAssembly通過：Plugins1160192 bytes、Workflows1143296 bytes，都小於8MiB。
本段沒有連D365/LINE，唯獨續跑檢查在修改前執行verify=0。沒有部署新打包產物。
紅燈：ToolUtility兩宿主各2失敗1通過；Relay缺新型別編譯紅後13/13綠；Shell修fixture的Moq using與party正式ID後驗舊拒絕4案紅，再實作後全綠；Schema3紅→3綠，後補global/unknown兩案。

2026-11-17 13:55 Asia/Taipei (UTC+08:00)：9370d014固定guard完整八項通過，Release7128、Debug7；本段驗收後推送核對，接C-b。

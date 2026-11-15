# P5-05 週報與Workflows

依05A修訂2 P5-05與固定做法。先建立net462嚴格Workflows專案，同步sln/CI/架構/套件檢查/打包；連結核定基礎並新增ActivityShellBase及ActivityInput。核定三個ActivityShell測試先紅再綠。

三個舊入口逐條盤點，使用P5-02既定編譯結果，不擴充harness；跑不起來basis=reading並附舊來源行號。3～6共用情境、規則雙目標、外殼Shadow、部分信任、打包與停用plan/apply/verify=0。新舊皆不得以Live啟用；live=deferred依D5-19。包內不外部審查，15提交上限，驗收及固定guard後才push。

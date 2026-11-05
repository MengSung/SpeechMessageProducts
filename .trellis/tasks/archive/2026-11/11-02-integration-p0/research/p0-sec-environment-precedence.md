# P0-SEC 環境名稱優先順序核對

## 現況
SpeechMessageProducts.ChurchReport/Program.cs:68 使用 WebApplication.CreateBuilder(args)，.NET SDK 10.0.403。
使用者核定文字要求「與 Host 相同」，同時指定 ASPNETCORE_ENVIRONMENT 優先於 DOTNET_ENVIRONMENT。

## 本機隔離探針
暫存 net10.0 console 參考 Microsoft.AspNetCore.App；使用 WebApplication.CreateBuilder(WebApplicationOptions)，Args 為空、ContentRootPath 為暫存輸出資料夾，ApplicationName 為沒有 UserSecretsId 的 EnvironmentProbe。
未 Build／Start Host，僅取得 EnvironmentName，Dispose ConfigurationManager 後結束。每組使用新的子程序；只向子程序提供固定環境名稱，未修改目前目錄或父程序環境，未讀取產品設定、User Secrets 或機密。

| ASPNETCORE_ENVIRONMENT | DOTNET_ENVIRONMENT | 實際 Host | 核定文字要求的解析結果 |
| --- | --- | --- | --- |
| Development | Production | Production | Development |
| Production | Development | Development | Production |
| Development | 未設定 | Development | Development |
| 未設定 | Development | Development | Development |
| 未設定 | 未設定 | Production | Production |

第一列若套用原文字，Production 的自建設定會被加入 Development provider，違反非 Development 逐鍵不變。
第二列若套用原文字，Host 是 Development，自建設定卻略過安全覆寫，無法保證隔離基底正式 token。

## 建議的唯一文字修正
環境名稱依目前 WebApplication Host 的實際規則：先看 DOTNET_ENVIRONMENT，再看 ASPNETCORE_ENVIRONMENT；都未設定則 Production。Development 比對不分大小寫。
不改 Host 行為，不改其餘 P0-SEC 範圍與驗收；補上上述兩組環境名稱衝突的純函式迴歸測試，不在單元測試修改程序環境或目前目錄。

## 停止
這是 00 §9.6 的計畫／程式現況矛盾。取得使用者核定前，不開始設定層、產品修改、P0-ENV 或 P0-06。

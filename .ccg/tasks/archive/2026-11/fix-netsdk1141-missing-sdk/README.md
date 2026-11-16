# SDK 解析環境修復

本任務只修復本機 SDK 安裝；task.json 記錄狀態、複雜度、階段與驗證入口，不更動既有 P5 任務或 global.json。

重現：global.json 指定 10.0.403／latestFeature；已安裝 9.0.318、9.0.321、11.0.100，dotnet --version 非零結束並回報找不到相容 SDK。W: 是本工作樹的 SUBST 對應。

修復與驗證：安裝官方 10.0.403 x64 SDK；核對 SHA-512 與 Microsoft Authenticode 簽章，再以 D:、W:、Visual Studio MSBuild 及既有 P0-02 測試驗證。無產品程式、Session 或執行期資源生命週期變更。

## 驗證結果

- Microsoft 官方安裝檔 SHA-512 與 Authenticode 簽章通過，安裝結束碼 0；原有 SDK 9／11 保留。
- D: 與 W: 的 dotnet --version 均為 10.0.403；Visual Studio 2026 MSBuild 在兩路徑均解析到 NETCoreSdkVersion=10.0.403。
- Test-P002Settings.ps1：2/2 通過；修復前 SDK 案例可重現失敗。
- Line.Messaging.Tests：net10.0 為 101/101、net48 為 96/96，均重新建置後執行，零略過。
- Visual Studio MSBuild 建置完整 SpeechMessageProducts.sln（Debug、m:2、無還原）：結束碼 0；保留原有警告，無 NETSDK1141。原始建置與 TRX 證據位於忽略的 artifacts/fix-netsdk1141。
- 檔案說明守門首次：1709 個檔案、0 失敗；收尾重跑時另一會話新增 NewPersonPresentRecordTests.cs，變為 1711 個檔案、1 項 CrlfRequired。該檔不屬本次環境修復，未修改或納入提交。本任務紀錄的 UTF-8 無 BOM、CRLF 與檔尾 CRLF 個別檢查通過。

## 審查與知識回饋

本次為 S／低風險的機器環境修復，未更動程式碼、global.json、目標框架或 CI，依小型低風險規則由主會話審查。未發現需另訂程式契約的新模式：既有 P0-02 設定測試已明確要求安裝基準 SDK。

版本設定源於 bc13efe7（2026-11-02 P0-02）。latestFeature 允許 10.0 內較新 SDK，不跨至 11.0；SDK 11 支援 net10.0 目標，但切換整個方案的工具鏈須另外驗證，不以安裝較新 SDK 推定相容性。

Microsoft 參考：
- https://learn.microsoft.com/en-us/dotnet/core/tools/global-json
- https://learn.microsoft.com/en-us/dotnet/core/install/windows

既有 P5 活動與平行會話的變更未納入本任務，不把環境修復視為 P5 工作包驗收。

# P2-02 執行計畫

## 步驟

1. 建立雙目標測試專案及 smoke 測試，先執行並確認因 `MemberNotificationService` 尚不存在而失敗。
2. 建立產品 `.csproj`，立即把產品與測試專案加入 `SpeechMessageProducts.sln`；檢查各一筆、原專案仍在，且 Debug／Release 有 ActiveCfg、Build.0。
3. 建立最小 `MemberNotificationService` 型別骨架、十個模組 README、空的 `GoodShepherdText.resx`，不先造 P2-04 行為或新增產品文案。
4. 以 `dotnet run --project tools/I18nCheck -c Release -- generate --resx ... --namespace ... --class ... --out ...` 產生 `GoodShepherdText.g.cs`；再執行唯讀 `generate --check`。
5. 執行兩個測試 framework、產品兩 target Debug／Release build、完整檔案說明檢查、進度頁測試及新增文字檔 byte check。
6. 更新 P2-02 `wp-reports`、此任務紀錄、P2 一般資訊單卡及父計畫追蹤；完成主代理差異檢查與任務交付。

## 驗收命令

```powershell
dotnet sln .\SpeechMessageProducts.sln list
dotnet build .\SpeechMessageProducts.GoodShepherd.Core\SpeechMessageProducts.GoodShepherd.Core.csproj -c Debug
dotnet build .\SpeechMessageProducts.GoodShepherd.Core\SpeechMessageProducts.GoodShepherd.Core.csproj -c Release
dotnet test .\SpeechMessageProducts.GoodShepherd.Core.Tests\SpeechMessageProducts.GoodShepherd.Core.Tests.csproj -c Release
dotnet run --project .\tools\I18nCheck -c Release -- generate --resx .\SpeechMessageProducts.GoodShepherd.Core\Resources\GoodShepherdText.resx --namespace SpeechMessageProducts.GoodShepherd.Core.Resources --class GoodShepherdTextData --out .\SpeechMessageProducts.GoodShepherd.Core\Resources\GoodShepherdText.g.cs --check
python tools/RepoQuality/check_file_documentation.py
node tools/RepoQuality/Tests/Test-ProgressPage.cjs
```

命令結果依實際環境與 framework 分別記錄；任何失敗不得以停用分析器、刪除測試或移除需求繞過。

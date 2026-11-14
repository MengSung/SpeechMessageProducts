/*
檔案整體說明

目的與定位：負責將已 bounded 讀取的舊 C# 專案做離線安全分類與 Roslyn 編譯，不執行舊程式、不載入 DLL，語意安全改寫委派給專用compiler。

主要功能與流程：以LegacyProjectCompiler編譯完整來源閉包；HttpClient及RestClient建構由語意改寫攔截，網址與SDK注入介面不是拒絕條件。結果僅包含狀態與安全診斷，來源秘密不能出現在輸出。

使用邊界與維護：Roslyn compilation 是單次區域物件，沒有全域快取、請求狀態、網路連線、背景 task 或可重用的未確定傳輸。掃描採固定 Regex 逾時並只回傳固定代碼。不能證明安全的來源一律拒絕，安全性優先於編譯覆蓋率。
*/
namespace LegacyGolden.PluginHarness;
/// <summary>編譯盤點轉接器；成功不冒充舊入口已執行。</summary>
internal static class LegacyClassifier
{
    /// <summary>沿用逐專案安全編譯管線，釋放候選bytes後只回傳固定摘要。</summary>
    internal static ProjectResult Classify(ProjectInput input)
    {
        using var result = LegacyProjectCompiler.CompileInput(input);
        var classification = result.Status == "Generated" ? ProjectClassification.Generated :
            result.Status == "BlockedByNetworkApi" ? ProjectClassification.BlockedByNetworkApi : ProjectClassification.CompileFailed;
        return new ProjectResult(input.Name, classification, input.Sources.Count, input.ByteCount, input.SourceSha256, result.Diagnostics);
    }
}

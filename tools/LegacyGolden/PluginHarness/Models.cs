/*
檔案整體說明

目的與定位：定義 PluginHarness 的編譯盤點資料模型與三種分類值，讓產生流程以 operation-local 物件組合後再發布。

主要功能與流程：專案結果只保存穩定名稱、數量、雜湊與固定診斷代碼，不保存原始碼、秘密、例外本文或外部服務回應。本檔不建立任何行為結果，legacy 必須由隔離runner執行後產生。

使用邊界與維護：模型是單次呼叫的純資料，沒有 static 可變集合、Session、credential、網路資源或背景工作。呼叫端必須先完成所有分類與驗證，候選未完成時不得交給序列化器；修改欄位時同步檢查秘密掃描、原子輸出與下游黃金契約。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LegacyGolden.PluginHarness;

/// <summary>舊專案的離線盤點分類；分類本身不代表實機 D365 或 LINE 行為已驗收。</summary>
internal enum ProjectClassification
{
    /// <summary>來源已通過安全掃描且 Roslyn 編譯成功，可形成離線候選。</summary>
    Generated,

    /// <summary>來源讀取、秘密掃描或 Roslyn 編譯失敗；不得把部分結果當成可執行黃金。</summary>
    CompileFailed,

    /// <summary>發現無法安全攔截的連外 API；拒絕執行與編譯，不建立網路替身假象。</summary>
    BlockedByNetworkApi
}

/// <summary>一個舊第一層專案的脫敏分類結果；不攜帶來源本文。</summary>
internal sealed class ProjectResult
{
    /// <summary>初始化結果所需的固定摘要欄位。</summary>
    internal ProjectResult(string name, ProjectClassification classification, int sourceFileCount, long sourceByteCount, string sourceSha256, IEnumerable<string> diagnostics)
    {
        Name = name;
        Classification = classification;
        SourceFileCount = sourceFileCount;
        SourceByteCount = sourceByteCount;
        SourceSha256 = sourceSha256;
        Diagnostics = diagnostics == null ? new List<string>() : diagnostics.Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal).ToList();
    }

    /// <summary>舊專案資料夾或專案檔的穩定顯示名稱。</summary>
    internal string Name { get; }

    /// <summary>本專案的三態分類。</summary>
    internal ProjectClassification Classification { get; }

    /// <summary>讀取且納入編譯判斷的 C# 檔案數。</summary>
    internal int SourceFileCount { get; }

    /// <summary>讀取來源的位元組總量；超限時不會建立結果。</summary>
    internal long SourceByteCount { get; }

    /// <summary>排序後來源位元組的 SHA-256；不可由此還原來源內容。</summary>
    internal string SourceSha256 { get; }

    /// <summary>不含來源本文與例外訊息的固定診斷代碼。</summary>
    internal IReadOnlyList<string> Diagnostics { get; }
}

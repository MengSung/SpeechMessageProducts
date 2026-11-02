using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ChurchReport.Configuration;

/// <summary>
/// 集中舊式設定讀取點的 Development 覆寫與啟動防護；不保存請求、使用者、教會或憑證狀態。
/// 此型別目前僅提供可編譯的 TDD 空殼，實作前必須先保存失敗證據。
/// </summary>
internal static class ChurchReportDevelopmentConfiguration
{
    /// <summary>以傳入的兩個環境名稱驗證 Host 優先順序；空殼用明確例外表示功能尚未實作。</summary>
    internal static string ResolveEnvironmentName(string dotnetEnvironment, string aspnetcoreEnvironment)
        => throw new NotImplementedException();

    /// <summary>對明確環境加入檔案與指定組件的 secrets；測試傳無 secrets ID 的組件，避免讀到真實機密。</summary>
    internal static IConfigurationBuilder AddChurchReportDevelopmentOverrides(this IConfigurationBuilder builder,
        string basePath, string environmentName, Assembly secretsAssembly)
        => throw new NotImplementedException();

    /// <summary>驗證有效設定及基底設定的隔離契約；尚未實作時不得假稱安全。</summary>
    internal static void ValidateDevelopmentConfiguration(string basePath, string environmentName, IConfiguration configuration)
        => throw new NotImplementedException();
}

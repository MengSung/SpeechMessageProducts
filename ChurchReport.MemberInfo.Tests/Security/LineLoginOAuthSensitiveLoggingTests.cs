// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Security/LineLoginOAuthSensitiveLoggingTests.cs
// 檔案責任：固定 LINE OAuth 不得把 access token、id token 或完整外部回應寫入診斷輸出的安全契約。
// 測試方法：讀取真實原始檔案並檢查禁止的 log pattern；這是防止未來註解/效能修改重新引入
// 憑證洩漏的邊界測試，不建立 HttpClient、Session 或任何外部連線，因此不會留下資源。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF，並以 final CRLF 結尾。
// ============================================================================
using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

/// <summary>
/// 防止 LINE OAuth 控制器將敏感 token 或完整回應內容寫入 Debug/Trace。
/// </summary>
public sealed class LineLoginOAuthSensitiveLoggingTests
{
    [Fact]
    public void Line_oauth_source_must_not_log_access_tokens_or_raw_response_bodies()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "SpeechMessageProducts.ChurchReport",
            "Controllers", "AuthenticationController", "AuthenticationController.LineLoginOAuth.cs");
        var source = File.ReadAllText(Path.GetFullPath(sourcePath));

        source.Should().NotContain("Access Token 前20字");
        source.Should().NotContain("Response: {responseBody}");
        source.Should().NotContain("錯誤: {response.StatusCode} - {responseBody}");
    }
}

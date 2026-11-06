/*
 * 檔案整體說明
 *
 * 目的與定位：防止 LINE OAuth 控制器將敏感 token 或完整回應內容寫入 Debug/Trace。
 *
 * 主要功能與流程：
 * 本檔宣告 LineLoginOAuthSensitiveLoggingTests。
 * 測試案例入口包含 Line_oauth_source_must_not_log_access_tokens_or_raw_response_bodies。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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

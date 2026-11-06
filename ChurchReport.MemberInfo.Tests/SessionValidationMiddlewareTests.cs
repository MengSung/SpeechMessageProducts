/*
 * 檔案整體說明
 *
 * 目的與定位：驗證公開路徑排除規則不會因為字串前綴相似而繞過 Session 驗證。
 *
 * 主要功能與流程：
 * 本檔宣告 SessionValidationMiddlewareTests、RecordingSession。
 * - 精確的登入路徑可以免載入 Session；帶有相似前綴的動態路徑則必須載入 Session。 這個斷言直接觀察 LoadAsync 呼叫次數，避免只測試 next delegate 而掩蓋安全邊界錯誤。
 * 測試案例入口包含 Excluded_path_requires_a_complete_path_segment、Clear、CommitAsync、LoadAsync、Remove、Set、TryGetValue。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

/// <summary>
/// 驗證公開路徑排除規則不會因為字串前綴相似而繞過 Session 驗證。
/// </summary>
public sealed class SessionValidationMiddlewareTests
{
    /// <summary>
    /// 精確的登入路徑可以免載入 Session；帶有相似前綴的動態路徑則必須載入 Session。
    /// 這個斷言直接觀察 LoadAsync 呼叫次數，避免只測試 next delegate 而掩蓋安全邊界錯誤。
    /// </summary>
    [Theory]
    [InlineData("/login", 0)]
    [InlineData("/health", 0)]
    [InlineData("/login-evil", 1)]
    [InlineData("/healthcheck", 1)]
    public async Task Excluded_path_requires_a_complete_path_segment(string path, int expectedLoadCount)
    {
        var session = new RecordingSession("session-a");
        var context = new DefaultHttpContext
        {
            Session = session
        };
        context.Request.Path = path;
        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<SessionValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        session.LoadCallCount.Should().Be(expectedLoadCount);
    }

    /// <summary>
    /// 最小可觀察的 Session double：只記錄中介層是否先明確載入 Session，
    /// 不保存任何跨測試或跨 request 的使用者資料。
    /// </summary>
    private sealed class RecordingSession : ISession
    {
        public RecordingSession(string id)
        {
            Id = id;
        }

        public int LoadCallCount { get; private set; }
        public bool IsAvailable => true;
        public string Id { get; }
        public IEnumerable<string> Keys => Array.Empty<string>();

        public void Clear() { }

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task LoadAsync(CancellationToken cancellationToken = default)
        {
            LoadCallCount++;
            return Task.CompletedTask;
        }

        public void Remove(string key) { }

        public void Set(string key, byte[] value) { }

        public bool TryGetValue(string key, out byte[] value)
        {
            value = Array.Empty<byte>();
            return false;
        }
    }
}

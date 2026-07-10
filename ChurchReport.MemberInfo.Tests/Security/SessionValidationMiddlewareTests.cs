using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChurchReport.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security;

public sealed class SessionValidationMiddlewareTests
{
    [Fact]
    public void Source_DoesNotBlockOnSessionCommitAsync()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            root,
            "SpeechMessageProducts.ChurchReport",
            "Middleware",
            "SessionValidationMiddleware.cs");

        var source = File.ReadAllText(sourcePath);

        source.Should().NotContain("CommitAsync().GetAwaiter().GetResult()");
    }

    [Fact]
    public async Task InvokeAsync_UserAgentMismatch_CommitsSessionAsyncAndRedirects()
    {
        var session = new RecordingSession();
        session.SetString("_SessionUserId", "user-1");
        session.SetString("_SessionUserAgent", "OriginalAgent");

        var context = new DefaultHttpContext();
        context.Request.Path = "/Protected/Page";
        context.Request.Headers["User-Agent"] = "DifferentAgent";
        context.Features.Set<ISessionFeature>(new TestSessionFeature { Session = session });

        var nextCalled = false;
        var middleware = new SessionValidationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            NullLogger<SessionValidationMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        session.Cleared.Should().BeTrue();
        session.CommitCount.Should().Be(1);
        context.Response.StatusCode.Should().Be(StatusCodes.Status302Found);
        context.Response.Headers.Location.ToString().Should().Be("/Login");
    }

    private sealed class TestSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = default!;
    }

    private sealed class RecordingSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public bool IsAvailable => true;
        public string Id { get; } = "session-1";
        public IEnumerable<string> Keys => _values.Keys;
        public bool Cleared { get; private set; }
        public int CommitCount { get; private set; }

        public void Clear()
        {
            Cleared = true;
            _values.Clear();
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Remove(string key) => _values.Remove(key);

        public void Set(string key, byte[] value) => _values[key] = value;

        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }
}

internal static class SessionTestExtensions
{
    public static void SetString(this ISession session, string key, string value)
    {
        session.Set(key, Encoding.UTF8.GetBytes(value));
    }
}

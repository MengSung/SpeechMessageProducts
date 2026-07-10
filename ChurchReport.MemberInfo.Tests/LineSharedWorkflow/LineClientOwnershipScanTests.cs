using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.LineSharedWorkflow;

public sealed class LineClientOwnershipScanTests
{
    [Fact]
    public void ChurchReportProductionCode_DoesNotUseTokenOnlyLineMessagingClientConstructor()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
            Path.Combine(root, "SpeechMessageProducts.ChurchReport"),
            "*.cs",
            SearchOption.AllDirectories);

        var offenders = files
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => new { path, line, number = index + 1 }))
            .Where(hit => hit.line.Contains("new LineMessagingClient(") &&
                          !hit.line.Contains("new LineMessagingClient(httpClient") &&
                          !hit.line.Contains("new LineMessagingClient(lineHttpClient") &&
                          !hit.line.Contains("new LineMessagingClient(m_LineMessagingClient"))
            .Select(hit => $"{Path.GetRelativePath(root, hit.path)}:{hit.number}:{hit.line.Trim()}")
            .ToArray();

        offenders.Should().BeEmpty();
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

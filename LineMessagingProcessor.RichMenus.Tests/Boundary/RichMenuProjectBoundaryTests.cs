using FluentAssertions;
using Xunit;

namespace LineMessagingProcessor.RichMenus.Tests.Boundary;

public sealed class RichMenuProjectBoundaryTests
{
    [Fact]
    public void RichMenu_project_does_not_reference_product_specific_dependencies()
    {
        var projectRoot = FindProjectRoot();
        var richMenuDirectory = Path.Combine(projectRoot, "LineMessagingProcessor.RichMenus");
        var forbiddenTerms = new[]
        {
            "ChurchReport",
            "Microsoft.Xrm",
            "IOrganizationService",
            "DbContext",
            "Controller",
            "IActionResult"
        };

        var hits = Directory
            .EnumerateFiles(richMenuDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => new { path, line, index }))
            .Where(item => forbiddenTerms.Any(term => item.line.Contains(term, StringComparison.Ordinal)))
            .Select(item => $"{Path.GetFileName(item.path)}:{item.index + 1}:{item.line}")
            .ToList();

        hits.Should().BeEmpty();
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ChurchReport.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate solution root.");
    }
}

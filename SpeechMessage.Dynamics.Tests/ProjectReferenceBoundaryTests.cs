using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace SpeechMessage.Dynamics.Tests;

public sealed class ProjectReferenceBoundaryTests
{
    [Fact]
    public void No_project_may_reference_the_retired_webapi_project()
    {
        var root = FindRepositoryRoot();
        var offenders = Directory
            .GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => new
            {
                Project = Path.GetFileNameWithoutExtension(path),
                References = ReadProjectReferences(path)
            })
            .Where(item => item.References.Any(reference =>
                reference.Contains(
                    "SpeechMessage.Dynamics.WebApi",
                    StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.Project)
            .ToArray();

        offenders.Should().BeEmpty(
            because: "the official NuGet worker route has no direct Web API project or migration-test exception");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains(
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ||
        path.Contains(
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static string[] ReadProjectReferences(string path)
    {
        var ascii = Encoding.ASCII.GetString(File.ReadAllBytes(path));
        var withoutComments = Regex.Replace(
            ascii,
            "<!--.*?-->",
            string.Empty,
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        return Regex
            .Matches(
                withoutComments,
                """<ProjectReference\b[^>]*\bInclude\s*=\s*["'](?<path>[^"']+)["']""",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["path"].Value)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate SpeechMessageProducts.sln from test base directory.");
    }
}

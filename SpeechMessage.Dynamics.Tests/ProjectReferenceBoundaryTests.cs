using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 保護 Official Worker 遷移後的專案參考邊界，避免已退役 WebApi project 被 Production/Test 專案重新引入。
/// 測試掃描 repository 的非 bin/obj csproj、移除 XML comment 後解析 ProjectReference；主要斷言為零違規者，
/// 以阻止舊 transport、token/session code 或 SDK-free 邊界透過間接參考復活。
/// </summary>
public sealed class ProjectReferenceBoundaryTests
{
    /// <summary>證明所有有效 csproj 均未參考退役 WebApi project；任何新增參考都會直接列為 offender。</summary>
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
        // csproj 的 ProjectReference path 為 ASCII 安全 subset；先移除 XML comments，避免封存的
        // 範例字串形成假陽性。方法只讀檔案 byte，不保留 FileStream 或跨測試 cache。
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
        // 由 test output 逐層向上尋找 solution anchor，避免依賴使用者 cwd；DirectoryInfo 僅在
        // 方法範圍存活，不會把工作樹路徑或另一個測試執行狀態保存到 static。
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

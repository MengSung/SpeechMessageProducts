// ============================================================================
// 檔案：SpeechMessage.Dynamics.Tests/ProjectReferenceBoundaryTests.cs
// 目的：用原始碼層級檢查，避免產品專案直接 reference WebApi。
//
// 保母教學：
// - 設計要求：產品只能走 Gateway HTTP 或 Embedded 專案。
// - 這裡掃整個 solution 的 csproj，抓出誰 reference 了 WebApi。
// - 允許的只有 Gateway / Embedded / Tests / SmokeTests。
// ============================================================================

using FluentAssertions;

namespace SpeechMessage.Dynamics.Tests;

public sealed class ProjectReferenceBoundaryTests
{
    [Fact]
    public void Only_gateway_embedded_and_test_projects_may_reference_webapi()
    {
        var root = FindRepositoryRoot();
        var csprojFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var offenders = new List<string>();
        foreach (var csproj in csprojFiles)
        {
            var text = File.ReadAllText(csproj);
            if (!text.Contains("SpeechMessage.Dynamics.WebApi", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var projectName = Path.GetFileNameWithoutExtension(csproj);
            var allowed =
                projectName is "SpeechMessage.Dynamics.Gateway"
                    or "SpeechMessage.Dynamics.Embedded"
                    or "SpeechMessage.Dynamics.Tests"
                    or "SpeechMessage.Dynamics.SmokeTests"
                    or "SpeechMessage.Dynamics.WebApi";

            if (!allowed)
            {
                offenders.Add(projectName);
            }
        }

        offenders.Should().BeEmpty(
            because: "products must not reference SpeechMessage.Dynamics.WebApi directly");
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SpeechMessageProducts.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate SpeechMessageProducts.sln from test base directory.");
    }
}

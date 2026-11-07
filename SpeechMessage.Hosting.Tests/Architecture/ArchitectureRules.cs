/*
 * 檔案整體說明
 *
 * 目的與定位：一個專案在一個實際目標上的唯讀評估資料；不載入產品 DLL，不含使用者或租戶狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 ArchitectureProject、ArchitectureViolation、ArchitectureRules。
 * - 驗證完整評估圖；呼叫端擁有輸入，結果只含固定的架構診斷。
 * - 檢查 NuGet 已選出的 Framework 相依，而不是只相信 csproj 宣告能提供 net462。
 * - 部署端 Plugin 與 Workflow 都受同一個 v4.6.2 沙箱契約約束。
 * - 以來源快照執行 R2 主機／Plugin 公開邊界、R3 組織字面值白名單及 R9 傳輸／Plugin 參考守門；
 *   舊 ChurchReport 的搬移債務保持精確可見，不以全產品豁免掩蓋。
 * - 以實際 DAG 而非示意圖的位置標記層級；同層彼此也不可參考。未知專案 fail closed， 加入新產品時必須明確分類並補正反例，不能因名稱相似自動取得高層權限。
 * 測試案例入口包含 ArchitectureProject、ArchitectureSource、ArchitectureViolation、Validate、
 * ValidateSource、ValidateFrameworkAssets、IsSandbox、IsHost、Layer。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 * 可沿 SpeechMessage.Hosting.Tests/Architecture/ArchitectureProjectReader.cs、
 * SpeechMessage.Hosting.Tests/Architecture/ArchitectureRuleTests.cs、SpeechMessage.Hosting.Tests/Architecture/ArchitectureXmlTests.cs
 * 追查目前工作樹中的合作／使用位置。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpeechMessage.Architecture.Tests;

/// <summary>一個專案在一個實際目標上的唯讀評估資料；不載入產品 DLL，不含使用者或租戶狀態。</summary>
/// <param name="Name">唯一專案名稱。</param><param name="Target">本節點目標。</param><param name="Targets">專案全部產品目標。</param><param name="References">此目標啟用的專案參考名稱。</param><param name="Packages">此目標啟用的直接套件名稱。</param><param name="IsTest">是否測試宿主。</param>
internal sealed record ArchitectureProject(string Name, string Target, IReadOnlyList<string> Targets, IReadOnlyList<string> References, IReadOnlyList<string> Packages, bool IsTest)
{
    /// <summary>本專案此目標的 C# 來源快照；缺省為空，供純相依圖合成案例使用。</summary>
    internal IReadOnlyList<ArchitectureSource> Sources { get; init; } = Array.Empty<ArchitectureSource>();
}

/// <summary>
/// 架構來源掃描所需的單一 C# 檔案快照；文字只在本次同步驗證期間持有，
/// 不保存使用者、租戶、憑證或跨測試可變狀態。
/// </summary>
/// <param name="Path">相對於目前方案根目錄的來源路徑或合成測試路徑。</param>
/// <param name="Text">來源文字快照；呼叫端負責提供並擁有其生命週期。</param>
internal sealed record ArchitectureSource(string Path, string Text);

/// <summary>不含來源程式或秘密值的固定規則違規，供 CI 精確診斷參考邊界。</summary>
/// <param name="Rule">穩定規則代碼。</param><param name="Project">違規專案。</param><param name="Target">當次評估目標。</param><param name="Dependency">造成違規的參考或套件。</param>
internal sealed record ArchitectureViolation(string Rule, string Project, string Target, string Dependency);

/// <summary>純函式 R2／R3／R4／R9 規則；每次驗證獨立建立結果，不保存跨測試或跨目標的可變靜態圖。</summary>
internal static class ArchitectureRules
{
    /// <summary>驗證完整評估圖；呼叫端擁有輸入，結果只含固定的架構診斷。</summary>
    /// <param name="projects">每目標的獨立節點。</param><returns>違規清單。</returns>
    internal static IReadOnlyList<ArchitectureViolation> Validate(IReadOnlyList<ArchitectureProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var result = new List<ArchitectureViolation>();
        var byName = projects.GroupBy(project => project.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var project in projects)
        {
            if (project.IsTest)
            {
                continue;
            }

            var layer = Layer(project.Name);
            if (layer < 0)
            {
                Add("UnknownProject", project.Name);
            }

            // 直接保護產品輸出，不依賴是否已有 Plugin／其他 core 消費這個專案。
            // net48 僅限上面已排除的測試宿主；不能藉孤立節點漏過沙箱 Framework 上限。
            if (project.Target != "net462" && project.Target != "net10.0")
            {
                Add("ProductTarget", project.Target);
            }

            var dual = project.Targets.Contains("net462", StringComparer.Ordinal) && project.Targets.Count > 1;
            var sandbox = IsSandbox(project.Name);
            if (sandbox && project.Target != "net462")
            {
                Add("SandboxTarget", project.Target);
            }

            foreach (var package in project.Packages)
            {
                var hostOnly = package.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) ||
                    package.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) ||
                    package == "Microsoft.PowerPlatform.Dataverse.Client";
                var exception = project.Name == "ToolUtility.Core" && project.Target == "net10.0" &&
                    package == "Microsoft.PowerPlatform.Dataverse.Client";
                if (hostOnly && (dual || sandbox) && !exception)
                {
                    Add("DualTargetPackage", package);
                }
            }

            foreach (var reference in project.References)
            {
                if (!byName.TryGetValue(reference, out var dependency))
                {
                    Add("MissingProject", reference);
                    continue;
                }

                var frameworkCore = dependency.Targets.Contains("net462", StringComparer.Ordinal) &&
                    dependency.Targets.Contains("net10.0", StringComparer.Ordinal);
                if (dual && !frameworkCore)
                {
                    Add("DualTargetReference", reference);
                }

                if (sandbox && (!frameworkCore || IsSandbox(reference)))
                {
                    Add("SandboxReference", reference);
                }

                if (IsHost(project.Name) && IsHost(reference))
                {
                    Add("HostReference", reference);
                }

                if (reference == "SpeechMessage.Dynamics.Deployment" ||
                    (project.Name == "SpeechMessage.Dynamics.Deployment" && IsSandbox(reference)))
                {
                    Add("DeploymentIsolation", reference);
                }

                if (layer >= 0 && Layer(reference) >= layer)
                {
                    Add("LayerDirection", reference);
                }
            }

            // 區域函式只在當次節點的同步驗證期間借用結果，不保存輸入圖或建立背景工作。
            void Add(string rule, string dependency) => result.Add(new ArchitectureViolation(rule, project.Name, project.Target, dependency));
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// 驗證來源層級的 R2、R3、R9 邊界。此掃描只使用呼叫端提供的 detached 文字，
    /// 不載入產品組件、不執行來源、不保存跨操作快取；任何無法辨識的公開宣告或禁止
    /// 參考均採拒絕策略，讓新產品不能藉由未分類語法靜默繞過守門。
    /// </summary>
    /// <param name="projects">每個實際目標或合成案例的來源快照。</param>
    /// <returns>不含來源本文的固定架構違規。</returns>
    internal static IReadOnlyList<ArchitectureViolation> ValidateSource(IReadOnlyList<ArchitectureProject> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var violations = new List<ArchitectureViolation>();
        foreach (var project in projects)
        {
            if (project.IsTest)
            {
                continue;
            }

            foreach (var source in project.Sources)
            {
                var normalizedPath = source.Path.Replace('\\', '/');
                var withoutComments = RemoveCommentsPreserveStrings(source.Text);
                var codeOnly = RemoveCommentsAndStrings(source.Text);

                if (IsHost(project.Name))
                {
                    foreach (Match declaration in PublicTypePattern.Matches(codeOnly))
                    {
                        var name = declaration.Groups["name"].Value;
                        if (name != "Program" &&
                            !name.EndsWith("Registration", StringComparison.Ordinal) &&
                            !name.EndsWith("Endpoints", StringComparison.Ordinal))
                        {
                            Add("R2HostPublicType", project, name);
                        }
                    }
                }

                if (IsSandbox(project.Name))
                {
                    foreach (Match declaration in PublicTypePattern.Matches(codeOnly))
                    {
                        var baseList = declaration.Groups["base"].Value;
                        if (!baseList.Contains("IPlugin", StringComparison.Ordinal) &&
                            !baseList.Contains("CodeActivity", StringComparison.Ordinal))
                        {
                            Add("R2PluginPublicType", project, declaration.Groups["name"].Value);
                        }
                    }
                }

                foreach (Match literal in OrganizationLiteralPattern.Matches(withoutComments))
                {
                    var value = literal.Groups["value"].Value;
                    if (!IsAllowlistedOrganizationLiteral(project.Name, normalizedPath, value))
                    {
                        Add("R3OrganizationLiteral", project, value);
                    }
                }

                if (HttpClientPattern.IsMatch(codeOnly) && !IsHttpClientProjectAllowed(project))
                {
                    Add("R9HttpClientProject", project, normalizedPath);
                }

                if (IsSandbox(project.Name) &&
                    (project.References.Contains("SpeechMessage.Payments", StringComparer.Ordinal) ||
                     project.Packages.Any(package => package.StartsWith("SpeechMessage.Payments", StringComparison.Ordinal))))
                {
                    Add("R9PluginPaymentsReference", project, "SpeechMessage.Payments");
                }

                if (IsSandbox(project.Name) &&
                    (project.Packages.Any(package => package.StartsWith("System.Drawing", StringComparison.Ordinal)) ||
                     codeOnly.Contains("System.Drawing", StringComparison.Ordinal)))
                {
                    Add("R9PluginSystemDrawingReference", project, "System.Drawing");
                }
            }
        }

        return violations.AsReadOnly();

        void Add(string rule, ArchitectureProject project, string dependency) =>
            violations.Add(new ArchitectureViolation(rule, project.Name, project.Target, dependency));
    }

    /// <summary>檢查 NuGet 已選出的 Framework 相依，而不是只相信 csproj 宣告能提供 net462。</summary>
    /// <param name="target">已還原的單一 Framework target 節點。</param>
    /// <returns>沙箱不可接受的產品名稱，不回傳完整本機路徑。</returns>
    internal static IReadOnlyList<string> ValidateFrameworkAssets(JsonElement target)
    {
        var violations = new List<string>();
        foreach (var dependency in target.EnumerateObject())
        {
            if (dependency.Value.GetProperty("type").GetString() == "project" &&
                (!dependency.Value.TryGetProperty("framework", out var framework) ||
                 framework.GetString() != ".NETFramework,Version=v4.6.2"))
            {
                violations.Add(dependency.Name.Split('/')[0]);
            }
        }

        return violations.AsReadOnly();
    }

    private static readonly Regex PublicTypePattern = new(
        @"\bpublic\s+(?:(?:sealed|abstract|static|partial|readonly)\s+)*(?<kind>class|record|interface|struct|enum|delegate)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<base>\s*:\s*[^{]+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OrganizationLiteralPattern = new(
        "\"(?<value>Jesus|JesusBack|Sunny|SunnyBack|Ymllc)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HttpClientPattern = new(
        @"\bHttpClient\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static bool IsHttpClientProjectAllowed(ArchitectureProject project) =>
        project.Name is "Line.Messaging" or "LineMessagingProcessor.Core" or "SpeechMessage.Payments" ||
        (project.Target == "net10.0" &&
         (project.Name == "SpeechMessage.Hosting" || IsHost(project.Name)));

    private static bool IsAllowlistedOrganizationLiteral(string project, string path, string value) =>
        (project == "SpeechMessage.Tenancy" &&
         (path.Equals("ConfigurationLineChannelResolver.cs", StringComparison.Ordinal) ||
          path.EndsWith("SpeechMessage.Tenancy/ConfigurationLineChannelResolver.cs", StringComparison.Ordinal)) &&
         value == "Jesus") ||
        (project == "SpeechMessageProducts.ChurchReport" &&
         path.EndsWith("SpeechMessageProducts.ChurchReport/Tools/LineUtilityClass.cs", StringComparison.Ordinal) &&
         value is "Jesus" or "JesusBack");

    private static string RemoveCommentsPreserveStrings(string source)
    {
        var result = new char[source.Length];
        Array.Fill(result, ' ');
        var state = LexState.Code;
        for (var index = 0; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            if (state == LexState.LineComment)
            {
                if (current is '\r' or '\n')
                {
                    result[index] = current;
                    state = LexState.Code;
                }

                continue;
            }

            if (state == LexState.BlockComment)
            {
                if (current == '*' && next == '/')
                {
                    index++;
                    state = LexState.Code;
                }

                continue;
            }

            if (state == LexState.VerbatimString)
            {
                result[index] = current;
                if (current == '"' && next == '"')
                {
                    result[++index] = next;
                }
                else if (current == '"')
                {
                    state = LexState.Code;
                }

                continue;
            }

            if (state is LexState.String or LexState.Character)
            {
                result[index] = current;
                if (current == '\\')
                {
                    if (index + 1 < source.Length)
                    {
                        result[++index] = source[index];
                    }
                }
                else if ((state == LexState.String && current == '"') ||
                         (state == LexState.Character && current == '\''))
                {
                    state = LexState.Code;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                index++;
                state = LexState.LineComment;
            }
            else if (current == '/' && next == '*')
            {
                index++;
                state = LexState.BlockComment;
            }
            else
            {
                result[index] = current;
                if (current == '@' && next == '"')
                {
                    result[index] = ' ';
                    result[++index] = '"';
                    state = LexState.VerbatimString;
                }
                else if (current == '"')
                {
                    state = LexState.String;
                }
                else if (current == '\'')
                {
                    state = LexState.Character;
                }
            }
        }

        return new string(result);
    }

    private static string RemoveCommentsAndStrings(string source)
    {
        var withComments = RemoveCommentsPreserveStrings(source);
        var result = withComments.ToCharArray();
        var state = LexState.Code;
        for (var index = 0; index < result.Length; index++)
        {
            var current = result[index];
            if (state == LexState.VerbatimString)
            {
                result[index] = ' ';
                if (current == '"' && index + 1 < result.Length && result[index + 1] == '"')
                {
                    result[++index] = ' ';
                }
                else if (current == '"')
                {
                    state = LexState.Code;
                }

                continue;
            }

            if (state is LexState.String or LexState.Character)
            {
                var closing = state == LexState.String ? '"' : '\'';
                result[index] = ' ';
                if (current == '\\' && index + 1 < result.Length)
                {
                    result[++index] = ' ';
                }
                else if (current == closing)
                {
                    state = LexState.Code;
                }

                continue;
            }

            if (current == '@' && index + 1 < result.Length && result[index + 1] == '"')
            {
                result[index++] = ' ';
                state = LexState.VerbatimString;
            }
            else if (current == '"')
            {
                result[index] = ' ';
                state = LexState.String;
            }
            else if (current == '\'')
            {
                result[index] = ' ';
                state = LexState.Character;
            }
        }

        return new string(result);
    }

    private enum LexState
    {
        Code,
        LineComment,
        BlockComment,
        String,
        VerbatimString,
        Character
    }

    /// <summary>部署端 Plugin 與 Workflow 都受同一個 v4.6.2 沙箱契約約束。</summary>
    private static bool IsSandbox(string name) => name is "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows";

    /// <summary>主機具有獨立啟動與授權邊界，不得彼此參考產品外殼。</summary>
    private static bool IsHost(string name) => name is "SpeechMessageProducts.ChurchReport" or "SpeechMessageProducts.LineWebhook";

    /// <summary>
    /// 以實際 DAG 而非示意圖的位置標記層級；同層彼此也不可參考。未知專案 fail closed，
    /// 加入新產品時必須明確分類並補正反例，不能因名稱相似自動取得高層權限。
    /// </summary>
    private static int Layer(string name) => name switch
    {
        "Line.Messaging" or "PowerPlatform.Dataverse.Client" or "SpeechMessage.Payments" => 0,
        "SpeechMessage.Localization" or "LineMessagingProcessor.Core" => 10,
        "ToolUtility.Core" => 20,
        "SpeechMessage.Tenancy" or "SpeechMessageProducts.GoodShepherd.Core" => 30,
        // P2診斷owner抽取後Hosting不再依賴ToolUtility；舊net10消費者向此基礎層委派。
        // 正反圖及實際方案共同禁止Hosting反向引用舊工具，不能讓雙目標或Plugin取得主機資源。
        "SpeechMessage.Hosting" => 32,
        "LineMessagingProcessor" or "SpeechMessage.Payments.Workflows" => 35,
        "ToolUtility" or "LineMessagingProcessor.Workflows" or "LineMessagingProcessor.RichMenus" => 40,
        "LineMessagingProcessor.AspNetCore" or "SpeechMessage.Payments.AspNetCore" => 50,
        "SpeechMessageProducts.ChurchReport" or "SpeechMessageProducts.LineWebhook" or "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" => 100,
        "SpeechMessage.Dynamics.Deployment" => 110,
        _ => -1
    };
}

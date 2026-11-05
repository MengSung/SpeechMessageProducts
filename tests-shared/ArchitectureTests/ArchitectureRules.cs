using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SpeechMessage.Architecture.Tests;

/// <summary>一個專案在一個實際目標上的唯讀評估資料；不載入產品 DLL，不含使用者或租戶狀態。</summary>
/// <param name="Name">唯一專案名稱。</param><param name="Target">本節點目標。</param><param name="Targets">專案全部產品目標。</param><param name="References">此目標啟用的專案參考名稱。</param><param name="Packages">此目標啟用的直接套件名稱。</param><param name="IsTest">是否測試宿主。</param>
internal sealed record ArchitectureProject(string Name, string Target, IReadOnlyList<string> Targets, IReadOnlyList<string> References, IReadOnlyList<string> Packages, bool IsTest);

/// <summary>不含來源程式或秘密值的固定規則違規，供 CI 精確診斷參考邊界。</summary>
/// <param name="Rule">穩定規則代碼。</param><param name="Project">違規專案。</param><param name="Target">當次評估目標。</param><param name="Dependency">造成違規的參考或套件。</param>
internal sealed record ArchitectureViolation(string Rule, string Project, string Target, string Dependency);

/// <summary>純函式 R4 規則；每次驗證獨立建立結果，不保存跨測試或跨目標的可變靜態圖。</summary>
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
        "LineMessagingProcessor" or "SpeechMessage.Payments.Workflows" => 35,
        "ToolUtility" or "LineMessagingProcessor.Workflows" or "LineMessagingProcessor.RichMenus" => 40,
        "LineMessagingProcessor.AspNetCore" or "SpeechMessage.Payments.AspNetCore" or "SpeechMessage.Hosting" => 50,
        "SpeechMessageProducts.ChurchReport" or "SpeechMessageProducts.LineWebhook" or "SpeechMessage.Dynamics.Plugins" or "SpeechMessage.Dynamics.Workflows" => 100,
        "SpeechMessage.Dynamics.Deployment" => 110,
        _ => -1
    };
}

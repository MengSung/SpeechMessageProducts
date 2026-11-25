/*
檔案整體說明
目的與定位：驗證 Deployment 從 net462 PE 中繼資料讀取 Workflow 活動 `[Input]` 契約；不載入、不反射執行或連線至 D365。
主要流程：以 Roslyn 產生合成 PE 驗證 reader；由明確提供的非機密環境變數指定打包 DLL 時，逐一核對七活動的 PE 輸入契約，並遞迴掃描完整 P7 情境樹，以 registration legacy mapping 對照所有模組的 inputs。
依賴與邊界：合成案例使用 Microsoft.CodeAnalysis 與測試目錄附帶的 net462 mscorlib；正式契約核對只讀本機打包 DLL、registration manifest 及固定 P7 情境檔，不依賴 User Secrets、組織或活動執行。AppointmentSigningActivity 沒有核准情境時明確回報缺口，不猜測新增輸入。
資源與隔離：每案例持有唯一暫存目錄與產生的單一 PE；fixture Dispose 在所有成功／失敗路徑刪除自己建立的目錄。reader 必須自行 Dispose PEReader 與 stream，不可跨案例快取型別或組件狀態。
失敗與驗證：缺少活動、錯誤組件或不合法／重複 `[Input]` metadata 必須 fail closed；合成 PE 案例具確定性斷言，正式 DLL 與情境比對僅在環境變數明確設定時執行，且絕不呼叫工作流程 Execute。遞迴列舉測試以合成的巢狀 appointments 目錄注入案例並於 finally 刪除唯一暫存根目錄。未提供 DLL 路徑時不宣稱正式組件驗收；現有六活動情境通過亦不代表缺少 Appointment 情境的七活動驗收完成。
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SpeechMessage.Dynamics.Deployment.Live;
using Xunit.Abstractions;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>鎖定工作流程輸入中繼資料讀取不載入 DLL、且正確抽取名稱與底層型別。</summary>
public sealed class ActivityInputContractReaderTests
{
    private readonly ITestOutputHelper output;

    /// <summary>保存本測試案例的安全摘要輸出 owner，不跨案例或背景工作。</summary>
    public ActivityInputContractReaderTests(ITestOutputHelper output) => this.output = output;

/// <summary>從合成 net462 PE 解析 InputAttribute，並在指定打包 DLL 時核對既有 P7 情境 inputs。</summary>
    [Fact]
    public void WorkflowTemplate_Inputs_TypeCheckedAgainstActivityAttributes()
    {
        using var fixture = new PeFixture();
        var contract = ActivityInputContractReader.Read(fixture.Emit(), "SpeechMessage.Dynamics.Workflows", "Sample.ScanActivity");

        Assert.Equal("SpeechMessage.Dynamics.Workflows", contract.AssemblyName);
        Assert.Equal(new Version(1, 0, 0, 0), contract.Version);
        Assert.Equal("Sample.ScanActivity", contract.ActivityType);
        Assert.Equal(new WorkflowInputDefinition("InputParameter", "System.String", true), contract.Inputs["InputParameter"]);
        Assert.Equal(new WorkflowInputDefinition("Count", "System.Int32", false), contract.Inputs["Count"]);
        Assert.Equal(new WorkflowInputDefinition("ExecutionMode", "System.String", false), contract.Inputs["ExecutionMode"]);
        contract = contract with { PublicKeyToken = "0123456789abcdef" };
        using var validRequiredOnly = JsonDocument.Parse("""{"InputParameter":"value"}""");
        using var validWithOptional = JsonDocument.Parse("""{"InputParameter":"value","Count":2,"ExecutionMode":"Live"}""");
        ActivityInputContractValidator.ValidateInputs(contract, Inputs(validRequiredOnly.RootElement));
        ActivityInputContractValidator.ValidateInputs(contract, Inputs(validWithOptional.RootElement));
        using var exclusive = new FileStream(fixture.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.Length > 0);
        VerifyPackagedActivityContractsWhenRequested();
    }

    /// <summary>輸入契約拒絕未知欄、缺少必要欄及 JSON 種類不符，避免錯誤值進入流程樣板。</summary>
    [Fact]
    public void WorkflowTemplate_UnknownMissingOrMistypedInput_Rejected()
    {
        using var fixture = new PeFixture();
        var contract = ActivityInputContractReader.Read(fixture.Emit(), "SpeechMessage.Dynamics.Workflows", "Sample.ScanActivity")
            with { PublicKeyToken = "0123456789abcdef" };

        using var unknown = JsonDocument.Parse("""{"InputParameter":"value","Unexpected":"value"}""");
        using var missingRequired = JsonDocument.Parse("""{"Count":1}""");
        using var mistyped = JsonDocument.Parse("""{"InputParameter":42}""");

        Assert.Throws<InvalidDataException>(() =>
            ActivityInputContractValidator.ValidateInputs(contract, Inputs(unknown.RootElement)));
        Assert.Throws<InvalidDataException>(() =>
            ActivityInputContractValidator.ValidateInputs(contract, Inputs(missingRequired.RootElement)));
        Assert.Throws<InvalidDataException>(() =>
            ActivityInputContractValidator.ValidateInputs(contract, Inputs(mistyped.RootElement)));
    }

    /// <summary>只接受固定新版 Workflows 組件身份與完整強式名稱 token，避免產生弱名稱活動參照。</summary>
    [Fact]
    public void WorkflowTemplate_UsesNewWorkflowsAssemblyStrongName()
    {
        var inputs = new Dictionary<string, WorkflowInputDefinition>(StringComparer.Ordinal);
        var contract = new WorkflowActivityContract(
            "SpeechMessage.Dynamics.Workflows",
            new Version(1, 0, 0, 0),
            "",
            "SpeechMessage.Dynamics.Workflows.QrCodes.MeetingScanCountActivity",
            inputs);

        Assert.Throws<InvalidDataException>(() =>
            ActivityInputContractValidator.ValidateInputs(contract, new Dictionary<string, JsonElement>(StringComparer.Ordinal)));

        var signedContract = contract with { PublicKeyToken = "0123456789abcdef" };
        Assert.Equal(
            "SpeechMessage.Dynamics.Workflows.QrCodes.MeetingScanCountActivity, SpeechMessage.Dynamics.Workflows, Version=1.0.0.0, Culture=neutral, PublicKeyToken=0123456789abcdef",
            ActivityInputContractValidator.AssemblyQualifiedActivityName(signedContract));
        ActivityInputContractValidator.ValidateInputs(
            signedContract,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal));
    }

    /// <summary>情境契約盤點必須遞迴包含每個模組，避免把未掃描資料夾誤報成缺少案例。</summary>
    [Fact]
    public void WorkflowTemplate_InputsScenarioDiscovery_IncludesEveryModule()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "p8-workflow-scenarios-" + Guid.NewGuid().ToString("N"));
        var nestedDirectory = System.IO.Path.Combine(root, "appointments");
        Directory.CreateDirectory(nestedDirectory);
        var scenarioPath = System.IO.Path.Combine(nestedDirectory, "scenarios.json");
        File.WriteAllText(scenarioPath, "{}");

        try
        {
            Assert.Equal(new[] { scenarioPath }, EnumerateScenarioFiles(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>錯誤組件或缺少活動型別不得回傳部分輸入契約。</summary>
    [Theory]
    [InlineData("Other.Assembly", "Sample.ScanActivity")]
    [InlineData("SpeechMessage.Dynamics.Workflows", "Sample.MissingActivity")]
    public void WorkflowTemplate_InputMetadataIdentityMismatch_IsRejected(string assemblyName, string activityType)
    {
        using var fixture = new PeFixture();

        Assert.Throws<InvalidDataException>(() =>
            ActivityInputContractReader.Read(fixture.Emit(), assemblyName, activityType));
    }

    /// <summary>只在明確指定打包 DLL 時核對七個活動；摘要不包含路徑或組件原始內容。</summary>
    private void VerifyPackagedActivityContractsWhenRequested()
    {
        var path = Environment.GetEnvironmentVariable("SPEECHMESSAGE_WORKFLOWS_ARTIFACT");
        if (string.IsNullOrWhiteSpace(path))
            return;

        var expected = new Dictionary<string, WorkflowInputDefinition[]>(StringComparer.Ordinal)
        {
            ["SpeechMessage.Dynamics.Workflows.Care.ContactAnniversaryActivity"] =
            [
                new("AnniversaryKind", "System.String", false),
                new("ExecutionMode", "System.String", false)
            ],
            ["SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity"] =
            [
                new("ExecutionMode", "System.String", false),
                new("InputParameter", "System.String", true)
            ],
            ["SpeechMessage.Dynamics.Workflows.Appointments.AppointmentSigningActivity"] =
            [
                new("ExecutionMode", "System.String", false),
                new("InputAppointment", "Microsoft.Xrm.Sdk.EntityReference", false),
                new("InputParameter", "System.String", true)
            ],
            ["SpeechMessage.Dynamics.Workflows.Fees.FeeAllotActivity"] =
            [
                new("ExecutionMode", "System.String", false),
                new("InputParameter", "System.String", true)
            ],
            ["SpeechMessage.Dynamics.Workflows.QrCodes.MeetingQrCodeActivity"] =
            [
                new("ExecutionMode", "System.String", false),
                new("InputParameter", "System.String", true)
            ],
            ["SpeechMessage.Dynamics.Workflows.QrCodes.MeetingScanCountActivity"] =
            [
                new("ExecutionMode", "System.String", false),
                new("InputParameter", "System.String", true)
            ],
            ["SpeechMessage.Dynamics.Workflows.QrCodes.PersonalQrCodeActivity"] =
            [
                new("ExecutionMode", "System.String", false),
                new("InputParameter", "System.String", true)
            ]
        };

        foreach (var (activityType, expectedInputs) in expected)
        {
            var contract = ActivityInputContractReader.Read(path, "SpeechMessage.Dynamics.Workflows", activityType);
            var actualInputs = contract.Inputs.Values.OrderBy(input => input.Name, StringComparer.Ordinal).ToArray();
            var orderedExpected = expectedInputs.OrderBy(input => input.Name, StringComparer.Ordinal).ToArray();
            Assert.Equal(orderedExpected, actualInputs);
            foreach (var input in actualInputs)
                output.WriteLine($"{activityType}|{input.Name}|{input.ValueType}|required={input.Required}");
        }

        VerifyExistingScenarioInputs(root: FindRepositoryRoot(), path, expected.Keys);
    }

    /// <summary>
    /// 依 registration 中明列的 legacy 型別，把固定 P7 情境輸入送入打包 DLL 契約驗證。
    /// 缺少情境的活動只列出缺口；本檢查不建立或推測新的情境資料。
    /// </summary>
    private void VerifyExistingScenarioInputs(
        string root,
        string artifactPath,
        IEnumerable<string> expectedActivityTypes)
    {
        using var registration = JsonDocument.Parse(File.ReadAllBytes(
            System.IO.Path.Combine(root, "d365", "registration", "plugins.registration.json")));
        var activityLegacyTypes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var assembly in registration.RootElement.GetProperty("assemblies").EnumerateArray())
        {
            if (!assembly.TryGetProperty("activities", out var activities))
                continue;
            foreach (var activity in activities.EnumerateArray())
            {
                var activityType = activity.GetProperty("type").GetString();
                if (activityType is null || !expectedActivityTypes.Contains(activityType, StringComparer.Ordinal))
                    continue;
                var legacyTypes = activity.GetProperty("legacy").EnumerateArray()
                    .Select(item => item.GetProperty("type").GetString())
                    .Where(type => !string.IsNullOrWhiteSpace(type))
                    .Select(type => type!)
                    .ToHashSet(StringComparer.Ordinal);
                Assert.True(activityLegacyTypes.TryAdd(activityType, legacyTypes), "Duplicate activity registration.");
            }
        }

        Assert.Equal(expectedActivityTypes.OrderBy(type => type, StringComparer.Ordinal),
            activityLegacyTypes.Keys.OrderBy(type => type, StringComparer.Ordinal));

        var scenarioRoot = System.IO.Path.Combine(
            root, "文件", "規劃設計書", "_封存_請勿依據", "實作計畫-3.5", "驗收", "P7情境");
        var matchedActivities = new HashSet<string>(StringComparer.Ordinal);
        var matchedScenarioCount = 0;
        foreach (var scenarioPath in EnumerateScenarioFiles(scenarioRoot))
        {
            using var scenarios = JsonDocument.Parse(File.ReadAllBytes(scenarioPath));
            foreach (var scenario in scenarios.RootElement.GetProperty("cases").EnumerateArray())
            {
                if (!scenario.TryGetProperty("trigger", out var trigger) ||
                    !trigger.TryGetProperty("activity", out var isActivity) ||
                    isActivity.ValueKind != JsonValueKind.True ||
                    !trigger.TryGetProperty("type", out var triggerTypeElement))
                    continue;

                var triggerType = triggerTypeElement.GetString();
                if (triggerType is null)
                    continue;
                var matches = activityLegacyTypes
                    .Where(entry => entry.Value.Contains(triggerType))
                    .Select(entry => entry.Key)
                    .ToArray();
                if (matches.Length == 0)
                    continue;
                Assert.Single(matches);
                Assert.True(trigger.TryGetProperty("inputs", out var inputElement) &&
                    inputElement.ValueKind == JsonValueKind.Object,
                    $"Mapped activity scenario has no inputs: {matches[0]}.");

                var contract = ActivityInputContractReader.Read(
                    artifactPath, "SpeechMessage.Dynamics.Workflows", matches[0]);
                ActivityInputContractValidator.ValidateInputs(contract, Inputs(inputElement));
                matchedActivities.Add(matches[0]);
                matchedScenarioCount++;
                output.WriteLine($"{matches[0]}|scenario={scenario.GetProperty("name").GetString()}|inputContract=pass");
            }
        }

        var missingActivities = activityLegacyTypes.Keys
            .Where(activityType => !matchedActivities.Contains(activityType))
            .OrderBy(activityType => activityType, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(6, matchedScenarioCount);
        var missingActivity = Assert.Single(missingActivities);
        Assert.Equal(
            "SpeechMessage.Dynamics.Workflows.Appointments.AppointmentSigningActivity",
            missingActivity);
        output.WriteLine($"{missingActivity}|scenario=missing|inputContract=not-verified");
    }

    /// <summary>遞迴列出完整情境樹內固定名稱的案例檔，並以 ordinal 路徑排序確保輸出穩定。</summary>
    private static string[] EnumerateScenarioFiles(string scenarioRoot) =>
        Directory.EnumerateFiles(scenarioRoot, "scenarios.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    /// <summary>從測試輸出目錄向上尋找唯一含方案及 registration 的 repo 根目錄。</summary>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "SpeechMessageProducts.sln")) &&
                File.Exists(System.IO.Path.Combine(directory.FullName, "d365", "registration", "plugins.registration.json")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    /// <summary>複製 JSON 屬性值，確保契約驗證期間文件 owner 可被呼叫端立即釋放。</summary>
    private static Dictionary<string, JsonElement> Inputs(JsonElement value) =>
        value.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);

    /// <summary>案例專屬 temporary PE owner，測試完成後刪除其唯一暫存目錄。</summary>
    private sealed class PeFixture : IDisposable
    {
        private const string Source = """
            [assembly:System.Reflection.AssemblyVersion("1.0.0.0")]
            namespace Microsoft.Xrm.Sdk.Workflow
            {
                public sealed class InputAttribute : System.Attribute
                {
                    public InputAttribute(string name) { }
                }
            }
            namespace System.Activities
            {
                public sealed class RequiredArgumentAttribute : System.Attribute { }
                public sealed class InArgument<T> { }
            }
            namespace Sample
            {
                public abstract class ActivityShellBase
                {
                    [Microsoft.Xrm.Sdk.Workflow.Input("ExecutionMode")]
                    public System.Activities.InArgument<string> ExecutionMode { get; set; }
                }
                public sealed class ScanActivity : ActivityShellBase
                {
                    [Microsoft.Xrm.Sdk.Workflow.Input("InputParameter")]
                    [System.Activities.RequiredArgument]
                    public System.Activities.InArgument<string> InputParameter { get; set; }
                    [Microsoft.Xrm.Sdk.Workflow.Input("Count")]
                    public System.Activities.InArgument<int> Count { get; set; }
                }
            }
            """;

        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "p8-workflow-pe-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(Root, "SpeechMessage.Dynamics.Workflows.dll");

        internal PeFixture() => Directory.CreateDirectory(Root);

        internal string Emit()
        {
            var compilation = CSharpCompilation.Create("SpeechMessage.Dynamics.Workflows",
                new[] { CSharpSyntaxTree.ParseText(Source) },
                new[] { MetadataReference.CreateFromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Framework", "mscorlib.dll")) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var result = compilation.Emit(Path);
            Assert.True(result.Success, string.Join(";", result.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)));
            return Path;
        }

        /// <summary>只刪除此 fixture 自行建立且具隨機名稱的暫存目錄。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

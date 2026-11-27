/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 F3 平台 Plugin 專案只有精確組件名稱能取得 net462 沙箱及共用層分類，並守住兩個核定公開 IPlugin 入口。
 * 主要流程：以合成 ArchitectureProject 圖及 C# 來源交給正式 ArchitectureRules；官方 Xrm SDK 中繼資料只讀取現有已還原資產，不載入或執行產品組件。
 * 故障注入與決定性斷言：覆蓋近似專案名、錯誤目標、缺少入口、額外真實 IPlugin、相似命名空間及本地偽造 IPlugin；違規必須對應 SandboxTarget、UnknownProject、R2PlatformPluginEntrySet 或 R2PluginPublicType。
 * 資源與隔離：案例不建置 D365／LINE 連線、不讀秘密；每次掃描由 ArchitectureRules 建立並 Dispose Roslyn metadata，不保存跨測試狀態或暫存資料。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SpeechMessage.Architecture.Tests;

/// <summary>以離線合成專案與來源確認 F3 平台沙箱分類及公開外掛型別集合不可被近似名稱或偽介面繞過。</summary>
public sealed class F3PlatformArchitectureRuleTests
{
    private const string PlatformName = "SpeechMessage.Dynamics.Platform.Plugins";
    private const string SlotRequestType = "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin";
    private const string ChannelCreatedType = "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ChannelCreatedPlugin";
    private static readonly IReadOnlyList<string> CoreTargets =
        Array.AsReadOnly(new[] { "net462", "net10.0" });
    private static readonly IReadOnlyList<string> PlatformReferences =
        Array.AsReadOnly(new[] { "LineMessagingProcessor.Core", "SpeechMessage.Localization" });

    /// <summary>精確平台專案可位於 net462 沙箱並向下依賴雙目標 Core／Localization 共用層。</summary>
    [Fact]
    public void PlatformProject_ExactNameIsNet462SandboxAndSharedLayer()
    {
        var violations = ArchitectureRules.Validate(Graph(PlatformProject("net462")));

        Assert.DoesNotContain(violations, item => item.Project == PlatformName &&
            item.Rule is "UnknownProject" or "SandboxTarget" or "SharedToProduct" or "LayerDirection");
    }

    /// <summary>平台沙箱例外只屬精確名稱；近似名稱仍是未知專案，且不會取得 net462 沙箱規則豁免或授權。</summary>
    [Fact]
    public void PlatformProject_LookalikeNameDoesNotAcquireSandboxClassification()
    {
        const string lookalike = PlatformName + ".Other";
        var violations = ArchitectureRules.Validate(Graph(PlatformProject("net10.0", lookalike)));

        Assert.Contains(violations, item => item.Project == lookalike && item.Rule == "UnknownProject");
        Assert.DoesNotContain(violations, item => item.Project == lookalike && item.Rule == "SandboxTarget");
    }

    /// <summary>官方 Xrm SDK IPlugin 的兩個核定完整型別名稱恰好公開時通過，不把共用 helper 視為外掛入口。</summary>
    [Fact]
    public void PlatformSource_ExactTwoSdkPluginEntriesAreAccepted()
    {
        var project = PlatformProject("net462") with
        {
            Sources = new[]
            {
                Source("SlotRequestPlugin.cs", PluginSource("ChannelPool", "SlotRequestPlugin")),
                Source("ChannelCreatedPlugin.cs", PluginSource("ChannelPool", "ChannelCreatedPlugin"))
            },
            ContractMetadataPaths = ArchitectureProjectReader.ReadPluginContractMetadata()
        };

        var violations = ArchitectureRules.ValidateSource(Graph(project));

        Assert.DoesNotContain(violations, item => item.Project == PlatformName &&
            item.Rule is "R2PluginPublicType" or "R2PlatformPluginEntrySet");
    }

    /// <summary>缺少任一核定公開入口或增加其他真正 IPlugin 時均拒絕，不能只檢查每個已存在型別各自合法。</summary>
    [Theory]
    [InlineData("missing-slot")]
    [InlineData("extra-plugin")]
    public void PlatformSource_EntryPointSetMustBeExact(string fault)
    {
        var sources = fault == "missing-slot"
            ? new[] { Source("ChannelCreatedPlugin.cs", PluginSource("ChannelPool", "ChannelCreatedPlugin")) }
            : new[]
            {
                Source("SlotRequestPlugin.cs", PluginSource("ChannelPool", "SlotRequestPlugin")),
                Source("ChannelCreatedPlugin.cs", PluginSource("ChannelPool", "ChannelCreatedPlugin")),
                Source("ExtraPlugin.cs", PluginSource("ChannelPool", "ExtraPlugin"))
            };
        var project = PlatformProject("net462") with
        {
            Sources = sources,
            ContractMetadataPaths = ArchitectureProjectReader.ReadPluginContractMetadata()
        };

        var violations = ArchitectureRules.ValidateSource(Graph(project));

        Assert.NotEmpty(violations.Where(item => item.Project == PlatformName &&
            item.Rule is "R2PluginPublicType" or "R2PlatformPluginEntrySet"));
    }

    /// <summary>同名空間偽造、近似 namespace 或本地定義的 IPlugin 都不是 Microsoft.Xrm.Sdk 的入口身分。</summary>
    [Theory]
    [InlineData("wrong-namespace")]
    [InlineData("fake-sdk-interface")]
    public void PlatformSource_SimilarOrLocalPluginContractIsRejected(string fault)
    {
        var contractPaths = fault == "fake-sdk-interface"
            ? Array.Empty<string>()
            : ArchitectureProjectReader.ReadPluginContractMetadata();
        var sources = fault == "fake-sdk-interface"
            ? new[]
            {
                Source("FakePlugin.cs", "namespace Microsoft.Xrm.Sdk { public interface IPlugin {} } " +
                    PluginSource("ChannelPool", "SlotRequestPlugin") + PluginSource("ChannelPool", "ChannelCreatedPlugin"))
            }
            : new[]
            {
                Source("FakePlugins.cs", PluginSource("PretendChannelPool", "SlotRequestPlugin") +
                    PluginSource("ChannelPool", "ChannelCreatedPlugin"))
            };
        var project = PlatformProject("net462") with
        {
            Sources = sources,
            ContractMetadataPaths = contractPaths
        };

        var violations = ArchitectureRules.ValidateSource(Graph(project));

        Assert.NotEmpty(violations.Where(item => item.Project == PlatformName &&
            item.Rule is "R2PluginPublicType" or "R2PlatformPluginEntrySet"));
    }

    /// <summary>近似專案名不得取得平台沙箱／入口分類；完整架構圖必須以 UnknownProject 拒絕此未分類節點。</summary>
    [Fact]
    public void PlatformProject_LookalikeNameDoesNotAcquirePluginEntryException()
    {
        const string lookalike = PlatformName + ".Other";
        var project = PlatformProject("net10.0", lookalike) with
        {
            Sources = new[] { Source("FakePlugin.cs", PluginSource("ChannelPool", "SlotRequestPlugin")) },
            ContractMetadataPaths = ArchitectureProjectReader.ReadPluginContractMetadata()
        };

        var violations = ArchitectureRules.Validate(Graph(project));

        Assert.Contains(violations, item => item.Project == lookalike && item.Rule == "UnknownProject");
        Assert.DoesNotContain(violations, item => item.Project == lookalike && item.Rule == "SandboxTarget");
    }

    /// <summary>產生一筆唯讀架構圖節點；測試候選不保留 SDK／來源以外的可變狀態。</summary>
    private static ArchitectureProject PlatformProject(string target, string name = PlatformName) =>
        new(name, target, new[] { target }, PlatformReferences,
            Array.Empty<string>(), false);

    /// <summary>把平台節點與固定雙目標底層組成單次評估圖，避免缺失參考掩蓋分類斷言。</summary>
    private static ArchitectureProject[] Graph(ArchitectureProject platform) =>
        new[]
        {
            platform,
            new ArchitectureProject("LineMessagingProcessor.Core", "net462", CoreTargets,
                Array.Empty<string>(), Array.Empty<string>(), false),
            new ArchitectureProject("SpeechMessage.Localization", "net462", CoreTargets,
                Array.Empty<string>(), Array.Empty<string>(), false)
        };

    /// <summary>建構由正式 SDK metadata 辨識的外掛類別來源；測試本身不載入該類別或呼叫 Execute。</summary>
    private static string PluginSource(string @namespace, string typeName) =>
        $"namespace SpeechMessage.Dynamics.Platform.Plugins.{@namespace} {{ public sealed class {typeName} : Microsoft.Xrm.Sdk.IPlugin {{ public void Execute(System.IServiceProvider provider) {{}} }} }}";

    /// <summary>將固定合成文字包成一次掃描的來源快照，不讀取磁碟或保留外部使用者資料。</summary>
    private static ArchitectureSource Source(string path, string text) => new(path, text);
}

/*
檔案整體說明

目的與定位：驗證 GoodShepherd.Core 角色代碼、選項 metadata 對照與角色文字解析契約，
涵蓋 P8-02 指定的六個測試，並由 net462 與 net10.0 兩個測試宿主執行。

主要功能與流程：測試以反射檢查產品公開的穩定角色 API，以合成 TextContext 和本測試擁有的
不可變教會覆寫快照驗證顯示層；資源測試讀取 resx 與 I18nCheck 生成來源。metadata 對照測試
只讀取正式唯讀 snapshot 黃金檔，逐筆比對 option value、1028 label 與角色代碼，不建構或
猜測任何選項值。

使用邊界與維護：測試不建立產品 Host、不連接 D365、不讀秘密、不載入會友資料。
未知選項必須沒有角色代碼；顯示名稱與穩定代碼互相獨立；教會覆寫只能透過本次完整
TextContext 的 snapshot 生效。metadata 黃金檔尚未取得時，對照測試必須明確失敗而非空集合通過。

失敗處理與驗證：缺少角色 API、字典鍵、黃金檔或對照內容均以決定性斷言失敗。覆寫 provider
及快照只在單一測試中建立，沒有共享快取、連線、背景工作或需釋放的外部資源。
*/
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Members;

/// <summary>鎖定角色的穩定代碼、metadata 身分及文字覆寫行為；所有測試輸入均為離線合成資料。</summary>
public sealed class RoleCatalogTests
{
    private static readonly string[] RoleNames =
    {
        "PastorCouple", "ZoneShepherd", "ZoneLeader", "GroupLeader", "AssistantGroupLeader",
        "CoreWorker", "Member", "NotInGroup", "Newcomer", "OtherChurch", "Closed"
    };

    /// <summary>合成 AssistantGroupLeader 文字覆寫，確認角色字典可呈現自訂名稱；本案不推導 metadata 選項對照。</summary>
    [Fact]
    public void RoleNames_ChurchOverride_AssistantGroupLeaderShowsInternLeader()
    {
        var context = new TextContext(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "zh-TW", "zh-TW", "SyntheticOrg", "ChurchReport");
        var keyType = typeof(TextKey);
        var key = Activator.CreateInstance(keyType, "Role.AssistantGroupLeader")!;
        var row = new ChurchTextOverride(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            context.ChurchId, (TextKey)key, "zh-TW", "實習小組長");
        var snapshot = new ChurchTextOverrideSnapshot(context, new[] { row });
        var catalog = new GoodShepherdTextCatalog(new SnapshotProvider(snapshot));
        var roleCode = Enum.Parse(GetRoleCodeType(), "AssistantGroupLeader");
        var displayName = GetRoleCatalogType().GetMethod("DisplayName", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(displayName);
        Assert.Equal("實習小組長", displayName!.Invoke(null, new object[] { roleCode, context, catalog }));
    }

    /// <summary>同一 option value 在教會改名後仍解析成同一代碼，角色身分不得由顯示文字決定。</summary>
    [Fact]
    public void RolePermission_IndependentOfDisplayName()
    {
        var options = CreateMetadataOptionArray((12001, "小組組員"));
        var roleCatalog = Activator.CreateInstance(GetRoleCatalogType(), new object[] { options })!;
        var catalogType = GetRoleCatalogType();
        var resolver = catalogType.GetMethod("TryGetCode", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(resolver);
        var arguments = new object?[] { 12001, null };
        var knownBeforeDisplayOverride = (bool)resolver!.Invoke(roleCatalog, arguments)!;
        var codeBeforeDisplayOverride = arguments[1];
        var context = new TextContext(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "zh-TW", "zh-TW", "SyntheticOrg", "ChurchReport");
        var overrideRow = new ChurchTextOverride(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            context.ChurchId, new TextKey("Role.Member"), "zh-TW", "教會自訂顯示");
        var textCatalog = new GoodShepherdTextCatalog(
            new SnapshotProvider(new ChurchTextOverrideSnapshot(context, new[] { overrideRow })));
        var display = catalogType.GetMethod("DisplayName", BindingFlags.Public | BindingFlags.Static)!;
        var roleCode = Enum.Parse(GetRoleCodeType(), "Member");
        var displayed = display.Invoke(null, new object[] { roleCode, context, textCatalog });
        var knownAfterDisplayOverride = (bool)resolver.Invoke(roleCatalog, arguments)!;

        Assert.Equal("教會自訂顯示", displayed);
        Assert.True(knownBeforeDisplayOverride);
        Assert.True(knownAfterDisplayOverride);
        Assert.Equal(codeBeforeDisplayOverride, arguments[1]);
    }

    /// <summary>將產品固定表逐列與 goodshepherdback 唯讀 metadata 黃金檔比對，禁止用推測值替代標籤證據。</summary>
    [Fact]
    public void RoleCatalog_OptionValueTable_MatchesMetadataSnapshot()
    {
        var root = FindRepositoryRoot();
        var goldenPath = Path.Combine(root, "SpeechMessageProducts.GoodShepherd.Core.Tests", "Golden",
            "Roles", "customertypecode-options.json");
        Assert.True(File.Exists(goldenPath),
            "缺少 goodshepherdback 的唯讀 contact.customertypecode option value／1028 label 黃金檔；不得建立空檔或推測值。");

        using var document = JsonDocument.Parse(File.ReadAllText(goldenPath));
        var rows = document.RootElement.GetProperty("options").EnumerateArray()
            .Select(item => (Value: item.GetProperty("value").GetInt32(),
                Label: item.GetProperty("label").GetString()!))
            .OrderBy(item => item.Value).ToArray();
        Assert.NotEmpty(rows);

        var metadataType = typeof(GoodShepherdTextCatalog).Assembly.GetType(
            "SpeechMessageProducts.GoodShepherd.Core.Members.RoleMetadataOption", throwOnError: true)!;
        var candidates = Array.CreateInstance(metadataType, rows.Length);
        for (var index = 0; index < rows.Length; index++)
            candidates.SetValue(Activator.CreateInstance(metadataType, rows[index].Value, rows[index].Label), index);
        var roleCatalog = Activator.CreateInstance(GetRoleCatalogType(), new object[] { candidates })!;
        var catalogRows = GetRoleCatalogType().GetProperty("MetadataOptions", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(catalogRows);
        var actual = ((System.Collections.IEnumerable)catalogRows!.GetValue(roleCatalog)!).Cast<object>()
            .Select(item => (Value: (int)item.GetType().GetProperty("Value")!.GetValue(item)!,
                Label: (string)item.GetType().GetProperty("Label")!.GetValue(item)!))
            .OrderBy(item => item.Value).ToArray();
        Assert.Equal(rows, actual);
        var resolver = GetRoleCatalogType().GetMethod("TryGetCode", BindingFlags.Public | BindingFlags.Instance)!;
        var expectedCodes = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["小組組員"] = "Member",
            ["新朋友"] = "Newcomer",
            ["結案"] = "Closed",
            ["未入組"] = "NotInGroup",
            ["牧師師母"] = "PastorCouple",
            ["外教會"] = "OtherChurch",
            ["小組長"] = "GroupLeader"
        };
        foreach (var row in rows)
        {
            var arguments = new object?[] { row.Value, null };
            var mapped = (bool)resolver.Invoke(roleCatalog, arguments)!;
            if (expectedCodes.TryGetValue(row.Label, out var expectedCode))
            {
                Assert.True(mapped);
                Assert.Equal(expectedCode, arguments[1]!.ToString());
            }
            else
            {
                Assert.False(mapped);
                Assert.Null(arguments[1]);
            }
        }

        Assert.Equal(3, rows.Count(row =>
            row.Label is "全職同工" or "區長" or "實習小組長"));

        var buildItems = GetRoleCatalogType().GetMethod("BuildItems", BindingFlags.Public | BindingFlags.Instance)!;
        var projected = ((System.Collections.IEnumerable)buildItems.Invoke(roleCatalog, new object?[] { null, null })!)
            .Cast<object>()
            .ToDictionary(item => (string)item.GetType().GetProperty("Value")!.GetValue(item)!);
        foreach (var label in new[] { "全職同工", "區長", "實習小組長" })
        {
            var item = projected[label];
            Assert.Equal(label, item.GetType().GetProperty("Text")!.GetValue(item));
            Assert.Equal(string.Empty, item.GetType().GetProperty("Code")!.GetValue(item));
        }

        var getOptionValue = GetRoleCatalogType().GetMethod("GetOptionValue", BindingFlags.Public | BindingFlags.Instance)!;
        foreach (var unmappedRole in new[] { "ZoneShepherd", "ZoneLeader", "AssistantGroupLeader", "CoreWorker" })
            Assert.Null(getOptionValue.Invoke(roleCatalog, new[] { Enum.Parse(GetRoleCodeType(), unmappedRole) }));
    }

    /// <summary>未知 option value 是故障注入，必須解析失敗且不得取得任何角色代碼或權限。</summary>
    [Fact]
    public void RoleCatalog_UnknownOptionValue_NoCodeNoPermission()
    {
        var options = CreateMetadataOptionArray((12001, "副小組長"));
        var roleCatalog = Activator.CreateInstance(GetRoleCatalogType(), new object[] { options })!;
        var resolver = GetRoleCatalogType().GetMethod("TryGetCode", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(resolver);
        var arguments = new object?[] { int.MinValue, null };

        var resolved = (bool)resolver!.Invoke(roleCatalog, arguments)!;

        Assert.False(resolved);
        Assert.Null(arguments[1]);
    }

    /// <summary>防止角色預設名稱離開生成字典，並確認頁面備援仍逐鍵顯示繁中資源。</summary>
    [Fact]
    public void RoleCatalog_FallbackLabelsComeFromGeneratedResources()
    {
        var root = FindRepositoryRoot();
        var sourcePath = Path.Combine(root, "SpeechMessageProducts.GoodShepherd.Core", "Members", "RoleCatalog.cs");
        var source = File.ReadAllText(sourcePath);
        var resourcePath = Path.Combine(root, "SpeechMessageProducts.GoodShepherd.Core", "Resources", "GoodShepherdText.resx");
        var resource = XDocument.Load(resourcePath);
        var displayNames = RoleNames.Select(role =>
        {
            var key = "Role." + role;
            return (string)resource.Root!.Elements("data")
                .Single(item => (string?)item.Attribute("name") == key)
                .Element("value")!;
        }).ToArray();
        var hardCodedLabels = displayNames.Select(label => "\"" + label + "\"");
        foreach (var label in hardCodedLabels)
            Assert.DoesNotContain(label, source, StringComparison.Ordinal);

        var fallbackMethod = GetRoleCatalogType().GetMethod("BuildFallbackItems", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(fallbackMethod);
        var items = ((System.Collections.IEnumerable)fallbackMethod!.Invoke(null, new object?[] { null, null })!)
            .Cast<object>()
            .ToArray();
        Assert.Equal(displayNames, items.Select(item =>
            (string)item.GetType().GetProperty("Text")!.GetValue(item)!).ToArray());
        Assert.All(items, item =>
            Assert.Equal(string.Empty, item.GetType().GetProperty("Code")!.GetValue(item)));
    }

    /// <summary>檢查十一個繁中角色資源存在、英文資源保留空殼，且 I18nCheck 生成表包含所有穩定鍵。</summary>
    [Fact]
    public void Resx_RoleKeys_AllElevenPresent_PseudoGenerated()
    {
        var root = FindRepositoryRoot();
        var resourceDirectory = Path.Combine(root, "SpeechMessageProducts.GoodShepherd.Core", "Resources");
        var zh = XDocument.Load(Path.Combine(resourceDirectory, "GoodShepherdText.resx"));
        var en = XDocument.Load(Path.Combine(resourceDirectory, "GoodShepherdText.en.resx"));
        var generated = File.ReadAllText(Path.Combine(resourceDirectory, "GoodShepherdText.g.cs"));

        foreach (var role in RoleNames)
        {
            var key = "Role." + role;
            Assert.NotNull(zh.Root!.Elements("data").SingleOrDefault(item => (string?)item.Attribute("name") == key));
            var english = en.Root!.Elements("data").SingleOrDefault(item => (string?)item.Attribute("name") == key);
            Assert.NotNull(english);
            Assert.True(string.IsNullOrEmpty((string?)english!.Element("value")));
            Assert.Contains("\"" + key + "\"", generated, StringComparison.Ordinal);
        }
    }

    /// <summary>以可變輸入覆寫 snapshot 注入隔離故障，斷言 provider 每次只回傳本次測試擁有的完整同範圍快照。</summary>
    [Fact]
    public void RoleList_OtherChurchOverrideNotApplied()
    {
        var churchA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var churchB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var contextA = new TextContext(churchA, "zh-TW", "zh-TW", "SyntheticOrg", "ChurchReport");
        var contextB = new TextContext(churchB, "zh-TW", "zh-TW", "SyntheticOrg", "ChurchReport");
        var key = new TextKey("Role.Member");
        var snapshotA = new ChurchTextOverrideSnapshot(contextA, new[]
        {
            new ChurchTextOverride(Guid.NewGuid(), churchA, key, "zh-TW", "甲教會名稱")
        });
        var snapshotB = new ChurchTextOverrideSnapshot(contextB, Array.Empty<ChurchTextOverride>());
        var catalogA = new GoodShepherdTextCatalog(new SnapshotProvider(snapshotA));
        var catalogB = new GoodShepherdTextCatalog(new SnapshotProvider(snapshotB));
        var roleCode = Enum.Parse(GetRoleCodeType(), "Member");

        var churchAText = GetRoleCatalogType().GetMethod("DisplayName", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { roleCode, contextA, catalogA });
        var churchBText = GetRoleCatalogType().GetMethod("DisplayName", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, new object[] { roleCode, contextB, catalogB });

        Assert.Equal("甲教會名稱", churchAText);
        Assert.Equal("小組組員", churchBText);
    }

    private static Type GetRoleCodeType() =>
        typeof(GoodShepherdTextCatalog).Assembly.GetType(
            "SpeechMessageProducts.GoodShepherd.Core.Members.RoleCode", throwOnError: true)!;

    private static Type GetRoleCatalogType() =>
        typeof(GoodShepherdTextCatalog).Assembly.GetType(
            "SpeechMessageProducts.GoodShepherd.Core.Members.RoleCatalog", throwOnError: true)!;

    private static Array CreateMetadataOptionArray(params (int Value, string Label)[] options)
    {
        var metadataType = typeof(GoodShepherdTextCatalog).Assembly.GetType(
            "SpeechMessageProducts.GoodShepherd.Core.Members.RoleMetadataOption", throwOnError: true)!;
        var result = Array.CreateInstance(metadataType, options.Length);
        for (var index = 0; index < options.Length; index++)
            result.SetValue(Activator.CreateInstance(metadataType, options[index].Value, options[index].Label), index);
        return result;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到工作樹方案根目錄。");
    }

    private sealed class SnapshotProvider(IChurchTextOverrideSnapshot snapshot) : IChurchTextOverrideSnapshotProvider
    {
        internal IChurchTextOverrideSnapshot Snapshot { get; set; } = snapshot;
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) => Snapshot;
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：P2-05逐一驗證正式產品組件內嵌Flex模板，固定黃金由核定Ping文字與
 * Flex結構手工定義；不從當次產品輸出重生期望值，不依賴工作目錄或殘留檔案。
 * 主要功能與流程：枚舉Resources.Flex的正式JSON資源，對應每份Golden；真引擎與
 * 真字典解析後以Core相同政策序列化，逐字比較完整JSON，新增模板缺黃金會失敗。
 * 使用邊界與維護：每個stream與reader由單案using擁有並確定性關閉；所有資料為
 * 合成scope且不連D365／LINE、不讀秘密。來源沒有cache、Task或跨測試可變狀態；
 * 本證據只代表模板可編入雙目標及離線輸出相容，不冒稱實機受理或手機收訊。
 */
using System.Reflection;
using LineMessagingProcessor.Core.Templates;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Notifications;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests.Notifications;

/// <summary>真產品嵌入資源逐份黃金；測試owner立即關閉reader而不保存組件stream。</summary>
public sealed class FlexTemplateGoldenTests
{
    /// <summary>每份正式模板各產生一筆測試，新增資源不能只跑舊模板而漏掉新模板黃金。</summary>
    public static IEnumerable<object[]> TemplateNames()
    {
        var names = typeof(MemberNotificationService).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("SpeechMessageProducts.GoodShepherd.Core.Resources.Flex.", StringComparison.Ordinal) &&
                n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(names);
        return names.Select(n => new object[] { n });
    }

    /// <summary>完整引擎輸出與固定黃金逐字一致，正式template無altText或未編入組件皆明確失敗。</summary>
    [Theory]
    [MemberData(nameof(TemplateNames))]
    public void Template_EmbeddedResource_MatchesGolden(string resource)
    {
        var template = Read(typeof(MemberNotificationService).Assembly, resource);
        var name = resource.Substring(resource.LastIndexOf(".Flex.", StringComparison.Ordinal) + 6);
        var golden = Read(typeof(FlexTemplateGoldenTests).Assembly,
            "SpeechMessageProducts.GoodShepherd.Core.Tests.Golden.flex-" + name.ToLowerInvariant());
        var catalog = new TextCatalog(new SystemTextTable(), new EmptyOverrides());
        var context = new TextContext(Guid.NewGuid(), "zh-TW", "zh-TW", "TestOrg", "GoodShepherd");
        var message = new FlexTemplateEngine(catalog).Fill(template, context, new Dictionary<string, string>());
        var json = JsonConvert.SerializeObject(message, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
        });
        Assert.Equal(golden.TrimEnd('\r', '\n'), json);
    }

    /// <summary>每次只開固定已枚舉的嵌入資源；內容32Ki上限，reader／stream同一using鏈確定性釋放。</summary>
    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing template golden.");
        Assert.InRange(stream.Length, 1, 32768);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    /// <summary>真字典的空覆寫來源，回傳相同完整scope，不保存context或呼叫結果。</summary>
    private sealed class EmptyOverrides : IChurchTextOverrideSnapshotProvider
    {
        /// <summary>建立當次純值快照；不借用SDK或建立共享Session。</summary>
        public IChurchTextOverrideSnapshot GetSnapshot(TextContext context) =>
            new ChurchTextOverrideSnapshot(context, Array.Empty<ChurchTextOverride>());
    }
}

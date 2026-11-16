/*
 * 檔案整體說明
 * 目的與定位：P5-12 真訊息 Plugin 的六案閱讀期望及部分信任執行驗證，共用 Core
 * 所嵌入的 scenarios／expected，不另建寬鬆結果，不執行任何退役 DLL 或實機操作。
 * 主要流程：以既有單案 SDK fixture 建立合成正式來源，設定方向、綁定、教會及深度，
 * 執行真外殼，逐欄驗完整狀態候選、正式收件人、活動 retry key、字典 sender 與原文
 * 雜湊；故障注入核對同一例外身分。任何額外操作、截斷或真 SDK 寫入都拒絕成功。
 * 資源與隔離：每案 fixture 唯一擁有 SDK／context／追蹤，using 在 Execute 排空後
 * 清除；嵌入 JSON 各限一 MiB／深度32，stream、reader 同次 Dispose。CAS 僅授
 * Execution 與閉包 Read／PathDiscovery，SDK 是唯一完整信任例外，finally 卸載。
 * 無網路、秘密、Session、背景工作或跨案狀態；離線證據不宣稱 D365 實機相容完成。
 */
using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Security.Policy;
using System.Text;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Messaging;
using SpeechMessageProducts.GoodShepherd.Core.Messaging;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>正常父域與受限子域共享六案 runner，不把 FullTrust 當成 CAS 證據。</summary>
public sealed class MessagingGoldenTests
{
    /// <summary>依人工閱讀期望執行六案，逐欄驗所有實際意圖而非只檢查紀錄有出現。</summary>
    [Fact]
    public void Messaging_Scenarios_MatchExpected() => MessagingGoldenRunner.RunAll();

    /// <summary>影子驗證使用完全相同的資料與嚴格結果集合，真 SDK 寫入必須為零。</summary>
    [Fact]
    public void Messaging_ShadowMode_MatchesExpected() => MessagingGoldenRunner.RunAll();

    /// <summary>每案域只授讀取與執行，測試、外殼及 Core 皆受限；結束無條件卸載。</summary>
    [Fact]
    public void PartialTrust_MessagingShadowChain_Runs()
    {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        var permissions = new PermissionSet(PermissionState.None);
        permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
        permissions.AddPermission(new FileIOPermission(FileIOPermissionAccess.Read | FileIOPermissionAccess.PathDiscovery, root));
        var sdk = typeof(IPlugin).Assembly.Evidence.GetHostEvidence<StrongName>();
        Assert.NotNull(sdk);
        var domain = AppDomain.CreateDomain("P5-Messaging-" + Guid.NewGuid().ToString("N"), null,
            new AppDomainSetup { ApplicationBase = root, ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile },
            permissions, sdk);
        try
        {
            var probe = (MessagingPartialTrustProbe)domain.CreateInstanceAndUnwrap(
                typeof(MessagingPartialTrustProbe).Assembly.FullName, typeof(MessagingPartialTrustProbe).FullName);
            var result = probe.Run();
            Assert.True(result == "PASS", result);
        }
        finally { AppDomain.Unload(domain); }
    }
}

/// <summary>跨域只傳純結果；所有來源、外殼及服務均在一次性受限子域建立。</summary>
public sealed class MessagingPartialTrustProbe : MarshalByRefObject
{
    /// <summary>域內不允許重跑並沿用前次半完成狀態，父域最後負責卸載。</summary>
    private bool ran;

    /// <summary>不使用 xUnit、Moq 或私有反射執行真產品鏈，固定合成診斷回父域。</summary>
    public string Run()
    {
        try
        {
            MessagingGoldenRunner.Require(!ran && !AppDomain.CurrentDomain.IsFullyTrusted &&
                !GetType().Assembly.IsFullyTrusted && !typeof(OutboundLetterPlugin).Assembly.IsFullyTrusted &&
                !typeof(OutboundLetterRecords).Assembly.IsFullyTrusted, "restricted domain");
            ran = true;
            MessagingGoldenRunner.RunAll();
            return "PASS";
        }
        catch (Exception failure) { return failure.ToString(); }
    }
}

/// <summary>只讀合成嵌入檔並核對真外殼輸出；沒有生成或覆寫期望的功能。</summary>
internal static class MessagingGoldenRunner
{
    /// <summary>情境驅動 fixture 的來源，不模擬產品分支；所有操作依 expected 精確比較。</summary>
    internal static void RunAll()
    {
        var scenarios = Read("scenarios");
        var expected = Read("expected");
        Require((string?)scenarios["module"] == "messaging" && (string?)expected["basis"] == "reading", "basis");
        var cases = scenarios["cases"]!.Children<JObject>().ToArray();
        var results = expected["cases"]!.Children<JObject>().ToArray();
        Require(cases.Length == 6 && results.Length == cases.Length, "six cases");
        for (var index = 0; index < cases.Length; index++)
        {
            var scenario = cases[index]; var result = results[index];
            Require((string?)scenario["name"] == (string?)result["name"], "case identity");
            var seed = scenario["seed"]!.Children<JObject>().ToArray();
            var letter = seed.Single(x => (string?)x["alias"] == "letter")["fields"]!;
            var member = seed.Single(x => (string?)x["alias"] == "member")["fields"]!;
            using var f = new OutboundLetterShellTests.Fixture(outbound: (bool)letter["directioncode"]!,
                bound: member["new_lineid"]!.Type != JTokenType.Null,
                otherChurch: (string?)member["parentcustomerid"]!["ref"] != "church",
                state: (int)letter["statecode"]!["option"]!, content: (string)letter["description"]!);
            f.Context.MessageName = (string)scenario["trigger"]!["message"]!;
            f.Context.Depth = (int)scenario["trigger"]!["depth"]!;
            var original = new InvalidOperationException("synthetic fixture failure");
            if ((string?)scenario["fault"] == "first-read") f.Sdk.RetrieveHandler = (_, _, _) => throw original;
            Exception? error = null;
            try { f.Run(); } catch (Exception failure) { error = failure; }
            Require(error?.GetType().Name == (string?)result["exception"], "exception type");
            if ((string?)scenario["fault"] == "first-read") Require(ReferenceEquals(error, original), "exception identity");
            Require(f.Sdk.WriteRecords.Count == 0, "zero real writes");
            var operations = f.Trace.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
            var actual = operations.Where(x => x["operation"] is not null).ToArray();
            var writes = result["writes"]!.Children<JObject>().ToArray();
            var pushes = result["lineCalls"]!.Children<JObject>().ToArray();
            Require(actual.Length == writes.Length + pushes.Length, "operation count");
            foreach (var pair in actual.Where(x => (string?)x["operation"] == "Update").Zip(writes, (a, e) => (a, e)))
                Require((string?)pair.a["entity"] == "letter" && (string?)pair.a["id"] == f.Context.PrimaryEntityId.ToString("D") &&
                    JToken.DeepEquals(pair.a["fields"], pair.e["fields"]), "complete write fields");
            Require(actual.Count(x => (string?)x["operation"] == "Update") == writes.Length, "write count");
            Require(actual.Count(x => (string?)x["operation"] == "Push") == pushes.Length, "push count");
            foreach (var pair in actual.Where(x => (string?)x["operation"] == "Push").Zip(pushes, (a, e) => (a, e)))
            {
                var hash = LinePushClient.ComputeMemberContentSha256(
                    new ISendMessage[] { new TextMessage((string)pair.e["messages"]![0]!["text"]!) },
                    (string)pair.e["shadow"]!["sender"]!);
                Require((string?)pair.a["contactId"] == f.RecipientId.ToString("D") &&
                    (string?)pair.a["retryKey"] == f.Context.PrimaryEntityId.ToString("D") &&
                    (string?)pair.a["templateKey"] == (string?)pair.e["shadow"]!["templateKey"] &&
                    (int?)pair.a["messageCount"] == 1 && (string?)pair.a["contentSha256"] == hash, "complete push");
            }
            if (operations.Length > 0)
            {
                var end = operations.Last();
                Require((bool?)end["end"] == true && (int?)end["count"] == actual.Length &&
                    operations.All(x => x["overflow"] is null), "complete trace");
            }
            else Require(f.Context.Depth > 2, "trace only omitted for depth");
        }
    }

    /// <summary>單次 reader 嚴格解碼且拒絕重複鍵；沒有檔案系統後備或共用快取。</summary>
    private static JObject Read(string name)
    {
        var assembly = typeof(MessagingGoldenRunner).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".Golden.messaging." + name + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        Require(stream.Length <= 1048576, "resource budget");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        Require(!reader.Read(), "single document");
        return result;
    }

    /// <summary>只回固定合成契約名稱，不將活動原文、秘密或服務資料寫進錯誤。</summary>
    internal static void Require(bool valid, string contract)
    {
        if (!valid) throw new InvalidOperationException("Messaging golden contract: " + contract);
    }
}

/*
 * 檔案整體說明
 * 目的與定位：P5-06真出席Plugin的共用六案Shadow執行器及單案假SDK owner。
 * 主要功能與流程：直接讀Core共用reading情境與期望，把合成alias映為正式GUID，
 * 執行真入口後逐項驗收件者、模板、recordId重試鍵、訊息數與含sender內容雜湊；
 * 額外寫入、推播、overflow或缺結尾一律失敗。沒有從產品結果重生期望值。
 * 使用邊界與維護：每案最多一MiB JSON／深度32；SDK與reader同步using清理，
 * trace及Entity只屬fixture、不共用。無真憑證、網路或背景工作，CAS域可重用本執行器。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Attendance;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessageProducts.GoodShepherd.Core.Rules;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>同步單案測試owner，所有SDK資料與追蹤在Dispose時排空。</summary>
internal sealed class PresentRecordGoldenFixture : IDisposable
{
    /// <summary>有界合成列，身份由固定alias映射，不用姓名作主鍵。</summary>
    internal readonly Dictionary<string, Entity> Rows;
    /// <summary>唯一假SDK owner，錯誤注入可由個案設定。</summary>
    internal readonly FakeOrganizationService Service;
    /// <summary>本輪安全追蹤，Run前清除；不跨案例持有。</summary>
    internal readonly FakeTracingService Trace = new();
    /// <summary>真平台形狀的操作context；沒有動態代理或網路。</summary>
    internal readonly RemoteExecutionContext Context;
    /// <summary>只供深度阻擋與拒絕路徑斷言I/O次數。</summary>
    internal int Reads { get; private set; }

    /// <summary>從指定共用情境建立完整候選，頻道故意無效加密包以揭露Shadow誤解密。</summary>
    internal PresentRecordGoldenFixture(JObject scenario)
    {
        var seed = scenario["seed"]!.Children<JObject>().ToArray();
        var ids = seed.ToDictionary(x => (string)x["alias"]!, x => Id((string)x["alias"]!));
        Rows = seed.ToDictionary(x => (string)x["alias"]!, x => Make((string)x["entity"]!, ids[(string)x["alias"]!], (JObject)x["fields"]!, ids));
        Rows["church-a"]["new_linechannelid"] = new EntityReference("new_linechannel", Id("channel"));
        Rows["church-a"]["new_enabledlanguages"] = "zh-TW";
        Rows["channel"] = new Entity("new_linechannel", Id("channel")) { ["new_accesstokenencrypted"] = "synthetic-invalid-envelope" };
        Service = new FakeOrganizationService();
        Service.RetrieveHandler = (table, id, columns) =>
        {
            if (columns.AllColumns || columns.Columns.Contains("new_channel_access_token")) throw new InvalidOperationException("Unexpected data scope.");
            Reads++;
            var row = Rows.Values.Single(x => x.Id == id && x.LogicalName == table);
            var result = new Entity(table, id);
            foreach (var field in columns.Columns) if (row.Contains(field)) result[field] = row[field];
            return result;
        };
        var trigger = scenario["trigger"]!;
        Context = new RemoteExecutionContext
        {
            MessageName = (string)trigger["message"]!, PrimaryEntityName = "new_present_record", PrimaryEntityId = Id("present-1"),
            Depth = (int)trigger["depth"]!, Stage = 40, Mode = 1,
            OrganizationId = Id("organization"), OrganizationName = "synthetic-attendance", UserId = Id("user"), CorrelationId = Id("event")
        };
        Context.InputParameters["Target"] = Make("new_present_record", Context.PrimaryEntityId, (JObject)trigger["fields"]!, ids);
        Context.PreEntityImages["PreImage"] = Make("new_present_record", Context.PrimaryEntityId,
            (JObject)trigger["images"]!["pre"]!["PresentRecordLineImage"]!, ids);
    }

    /// <summary>只借本次provider與SDK，同步Execute排空後回傳，不建立非觀測工作。</summary>
    internal void Run(IPlugin? shell = null)
    {
        Trace.Entries.Clear();
        (shell ?? new PresentRecordPlugin("{\"mode\":\"Shadow\"}", "")).Execute(new FakeServiceProvider()
            .Add<ITracingService>(Trace).Add<IPluginExecutionContext>(Context)
            .Add<IOrganizationServiceFactory>(new Factory(Service, Context.UserId)));
    }
    /// <summary>嚴格解析所有安全影子行；end不當成操作。</summary>
    internal JObject[] Operations() => Trace.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
        .Select(x => RecordingOrganizationService.ReadProjection(x.Substring("SMV-SHADOW|1|".Length)))
        .Where(x => x["operation"] is not null).ToArray();
    /// <summary>釋放SDK回呼、假列及安全追蹤；可重複，沒有平台服務所有權。</summary>
    public void Dispose() { Service.Dispose(); Rows.Clear(); Trace.Entries.Clear(); }

    /// <summary>同份六案核對完整輸出；供一般測試及無xUnit的CAS probe共用。</summary>
    internal static void RunAll()
    {
        var scenarios = Read("scenarios")["cases"]!.Children<JObject>().ToArray();
        var expected = Read("expected"); var cases = expected["cases"]!.Children<JObject>().ToArray();
        Require((string?)expected["basis"] == "reading" && cases.Length == 6 && scenarios.Length == 6);
        for (var i = 0; i < cases.Length; i++)
        {
            Require((string?)cases[i]["name"] == (string?)scenarios[i]["name"]);
            using var f = new PresentRecordGoldenFixture(scenarios[i]); f.Run();
            var calls = cases[i]["lineCalls"]!.Children<JObject>().ToArray(); var actual = f.Operations();
            Require(calls.Length == actual.Length && f.Service.WriteRecords.Count == 0);
            for (var j = 0; j < calls.Length; j++)
            {
                var c = calls[j]; var push = actual[j];
                var hash = LinePushClient.ComputeMemberContentSha256(new ISendMessage[] { new TextMessage((string)c["messages"]![0]!["text"]!) }, "Synthetic Church助手");
                Require(hash == (string?)c["shadow"]!["contentSha256"] && (string?)push["operation"] == "Push" && (string?)push["contactId"] == Id((string)c["recipients"]![0]!).ToString("D") &&
                    (string?)push["retryKey"] == Id((string)c["shadow"]!["retryKey"]!).ToString("D") &&
                    (string?)push["templateKey"] == (string?)c["shadow"]!["templateKey"] && (int?)push["messageCount"] == 1 && (string?)push["contentSha256"] == hash);
            }
            if (f.Trace.Entries.Count != 0)
            {
                var end = RecordingOrganizationService.ReadProjection(f.Trace.Entries.Last().Substring("SMV-SHADOW|1|".Length));
                Require((bool?)end["end"] == true && (int?)end["count"] == actual.Length && end["overflow"] is null);
            }
            else Require((int)scenarios[i]["trigger"]!["depth"]! > 2);
        }
    }
    /// <summary>供個別故障測試借同一共用正常案例，不建立另一份產品期望。</summary>
    internal static JObject Normal() => (JObject)Read("scenarios")["cases"]![0]!;
    /// <summary>固定合成alias轉正式GUID，不作為產品身分或授權來源。</summary>
    internal static Guid Id(string alias) => DeterministicGuid.Create("P5AttendanceGolden", alias);
    /// <summary>有界嵌入資料讀取，禁止重複JSON欄位；stream與reader同呼叫釋放。</summary>
    private static JObject Read(string name)
    {
        var assembly = typeof(PresentRecordGoldenFixture).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".Golden.attendance." + name + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        Require(stream.Length <= 1048576);
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
    }
    /// <summary>只接受固定情境所用純值及lookup marker，不推測未知格式。</summary>
    private static Entity Make(string table, Guid id, JObject fields, Dictionary<string, Guid> ids)
    {
        var row = new Entity(table, id);
        foreach (var p in fields.Properties())
            row[p.Name] = p.Value is JObject o
                ? o["ref"] is not null ? new EntityReference((string)o["entity"]!, ids[(string)o["ref"]!]) { Name = (string?)o["name"] }
                    : o["line"] is not null ? "U" + new string('a', 32) : throw new InvalidDataException("Unknown fixture marker.")
                : ((JValue)p.Value).Value;
        return row;
    }
    /// <summary>CAS不依賴xUnit動態能力，固定失敗契約不含真實資料。</summary>
    private static void Require(bool valid) { if (!valid) throw new InvalidOperationException("Attendance golden contract mismatch."); }
    /// <summary>只借本案context使用者SDK，拒絕權限提升；fixture仍為唯一資源owner。</summary>
    private sealed class Factory(IOrganizationService sdk, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>不建連線，不轉移平台SDK所有權。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId) => userId == user ? sdk : throw new InvalidOperationException("Unexpected fixture user.");
    }
}

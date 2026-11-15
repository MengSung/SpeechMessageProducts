/*
 * 檔案整體說明
 *
 * 目的與定位：P5-05 真週報入口的共用六案執行器；嵌入資源直接連結 Core 的
 * scenarios／expected，不複製期望、不由產品輸出重生答案，也不宣稱舊程式實跑。
 * 主要功能與流程：有界讀取 reading 案例，以 alias 建正式合成身分及單案 SDK，
 * 執行兩個 Plugin 或固定 UTC 的真活動。完整比較所有寫入安全投影；推播核對
 * 正式收件者、事件重試鍵、範本、訊息數及包含 sender 的本文雜湊。
 * 使用邊界與維護：假查詢只為本六案提供一頁 seed，不冒充實機分頁；其他測試
 * 驗證實際完整翻頁。只有明列 volatile 欄位可正規化成有寫。context 採手寫委派，
 * 無 Moq 動態產碼，供受限域重用。每案服務、追蹤、reader、recorder 同步 using
 * 釋放；沒有秘密、網路、快取、背景工作或跨案例資料。未知型別及額外效果拒絕。
 */
extern alias Workflows;

using System;
using System.Activities;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessage.Dynamics.Plugins.WeeklyReports;
using SpeechMessage.Localization;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using SpeechMessageProducts.GoodShepherd.Core.WeeklyReports;
using Activity = Workflows::SpeechMessage.Dynamics.Workflows.WeeklyReports.WeeklyReportGenerationActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>單次同步驗證器，僅持方法區域候選；受限域失敗時回傳固定案例診斷。</summary>
internal static class WeeklyReportGoldenRunner
{
    /// <summary>逐案消費同一份閱讀期望；全部六案及完整結尾都吻合才正常返回。</summary>
    internal static void RunAll()
    {
        var scenarios = Read("scenarios");
        var expected = Read("expected");
        Require((string?)expected["basis"] == "reading", "basis");
        var cases = scenarios["cases"]!.Children<JObject>().ToArray();
        var desired = expected["cases"]!.Children<JObject>().ToArray();
        Require(cases.Length == 6 && desired.Length == 6, "case count");
        for (var index = 0; index < cases.Length; index++)
        {
            Require((string?)cases[index]["name"] == (string?)desired[index]["name"], "case order");
            Run(cases[index], desired[index]);
        }
    }

    /// <summary>以真外殼執行一案，完整比對之前不回報成功；SDK 僅含獨立合成資料。</summary>
    private static void Run(JObject scenario, JObject desired)
    {
        var seed = scenario["seed"]!.Children<JObject>().ToArray();
        var ids = seed.ToDictionary(row => (string)row["alias"]!, row => Id((string)row["alias"]!), StringComparer.Ordinal);
        var generated = (string)scenario["trigger"]!["type"]! == typeof(Activity).FullName;
        if (generated)
        {
            // 日期直接來自已審期望，固定主鍵僅作 alias 對照，不以 actual 決定期望欄位。
            foreach (var write in desired["writes"]!.Children<JObject>().Where(row =>
                (string?)row["entity"] == "new_group_present_weekly_report"))
            {
                var utc = DateTime.Parse((string)write["fields"]!["new_sunday_date"]!,
                    CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                var sunday = new ChurchClock().ToChurchTime(utc, (string)scenario["church"]!["timeZone"]!);
                ids.Add((string)write["alias"]!, WeeklyReportGenerationRule.ReportId(ids["church"], ids["list"], sunday));
            }
            foreach (var write in desired["writes"]!.Children<JObject>().Where(row => (string?)row["entity"] == "new_present_record"))
                ids.Add((string)write["alias"]!, WeeklyReportGenerationRule.AttendanceId(
                    ids[(string)write["fields"]!["new_group_present_weekly_report_prese"]!["ref"]!],
                    ids[(string)write["fields"]!["new_contact_new_present_record"]!["ref"]!]));
        }
        var rows = seed.Select(row => Entity((string)row["entity"]!, ids[(string)row["alias"]!],
            (JObject)row["fields"]!, ids)).ToArray();
        foreach (var channel in rows.Where(row => row.LogicalName == "new_linechannel"))
            channel["new_accesstokenencrypted"] = "synthetic-invalid-envelope";
        using var sdk = new FakeOrganizationService(rows);
        // 僅此固定小資料集；完整分頁及篩選語法由生成外殼回歸另驗，不放寬共用 fake。
        sdk.RetrieveMultipleHandler = query =>
        {
            var typed = query as QueryExpression ?? throw new InvalidDataException("Unexpected golden query.");
            Require(typed.PageInfo.PageNumber == 1, "single synthetic page");
            var source = rows.Where(row => row.LogicalName == typed.EntityName);
            return new EntityCollection(source.ToArray());
        };
        var trace = new FakeTracingService();
        try
        {
            var trigger = scenario["trigger"]!;
            var context = new RemoteExecutionContext
            {
                Depth = (int?)trigger["depth"] ?? 1, Stage = 40,
                Mode = ((string)trigger["type"]!).EndsWith("NotificationPlugin", StringComparison.Ordinal) ? 1 : 0,
                MessageName = generated ? "ExecuteWorkflow" : (string)trigger["message"]!,
                PrimaryEntityName = generated ? "list" : (string)trigger["entity"]!,
                PrimaryEntityId = generated ? ids["list"] : ids[(string)trigger["target"]!],
                OrganizationId = Id("organization"), OrganizationName = "synthetic-weekly",
                UserId = Id("user"), CorrelationId = Id("event")
            };
            if (generated)
            {
                var now = DateTimeOffset.Parse((string)scenario["now"]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                var invoker = new WorkflowInvoker(new Activity(new ChurchClock(new FixedClock(now))));
                invoker.Extensions.Add<IWorkflowContext>(() => new WorkflowContext(context));
                invoker.Extensions.Add<IOrganizationServiceFactory>(() => new Factory(sdk, context.UserId));
                invoker.Extensions.Add<ITracingService>(() => trace);
                invoker.Invoke(new Dictionary<string, object> { ["ExecutionMode"] = "Shadow" }, TimeSpan.FromSeconds(10));
            }
            else
            {
                context.InputParameters["Target"] = Entity(context.PrimaryEntityName, context.PrimaryEntityId,
                    (JObject)trigger["fields"]!, ids);
                foreach (var image in ((JObject)trigger["images"]!["pre"]!).Properties())
                    context.PreEntityImages[image.Name] = Entity(context.PrimaryEntityName, context.PrimaryEntityId,
                        (JObject)image.Value, ids);
                IPlugin plugin = ((string)trigger["type"]!).EndsWith("StatisticsPlugin", StringComparison.Ordinal)
                    ? new WeeklyReportStatisticsPlugin("{\"mode\":\"Shadow\"}", "")
                    : new WeeklyReportNotificationPlugin("{\"mode\":\"Shadow\"}", "");
                plugin.Execute(new FakeServiceProvider().Add<IPluginExecutionContext>(context)
                    .Add<IOrganizationServiceFactory>(new Factory(sdk, context.UserId)).Add<ITracingService>(trace));
            }
            var lines = trace.Entries.Where(line => line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(line => RecordingOrganizationService.ReadProjection(line.Substring("SMV-SHADOW|1|".Length))).ToArray();
            Require(lines.Length > 0 && (bool?)lines.Last()["end"] == true &&
                (int?)lines.Last()["count"] == lines.Length - 1 && lines.All(line => line["overflow"] is null), "complete trace");
            var actualWrites = lines.Where(line => line["operation"] is not null && (string?)line["operation"] != "Push").ToArray();
            var expectedWrites = desired["writes"]!.Children<JObject>().ToArray();
            Require(expectedWrites.Length == actualWrites.Length, "write count");
            using var projection = new RecordingOrganizationService(sdk);
            var volatileFields = scenario["volatile"]!.Values<string>().ToArray();
            for (var index = 0; index < expectedWrites.Length; index++)
            {
                var write = expectedWrites[index];
                var entity = Entity((string)write["entity"]!, ids[(string)write["alias"]!], (JObject)write["fields"]!, ids);
                if ((string)write["operation"]! == "Create") projection.Create(entity);
                else projection.Update(entity);
                var projectedWrites = projection.Writes;
                var expectedProjection = RecordingOrganizationService.ReadProjection(projectedWrites[projectedWrites.Count - 1].ProjectionJson);
                var actual = (JObject)actualWrites[index].DeepClone();
                actual.Remove("seq");
                foreach (var field in volatileFields)
                {
                    if (expectedProjection["fields"]![field!] is null) continue;
                    Require(actual["fields"]![field!] is not null, "volatile field absent");
                    actual["fields"]![field!] = "present";
                    expectedProjection["fields"]![field!] = "present";
                }
                Require(JToken.DeepEquals(expectedProjection, actual),
                    (string)scenario["name"]! + ": write " + index + " expected=" + expectedProjection + " actual=" + actual);
            }
            var pushes = lines.Where(line => (string?)line["operation"] == "Push").ToArray();
            var calls = desired["lineCalls"]!.Children<JObject>().ToArray();
            Require(calls.Length == pushes.Length, "push count");
            for (var index = 0; index < calls.Length; index++)
            {
                var call = calls[index];
                var contact = ids[(string)call["recipients"]![0]!];
                var message = (string)call["messages"]![0]!["text"]!;
                // 固定閱讀期望的 sender 模板輸出，不從執行結果或通知規則導出期望本文。
                var hash = LinePushClient.ComputeMemberContentSha256(
                    new ISendMessage[] { new TextMessage(message) }, "Synthetic Church助手");
                var push = pushes[index];
                Require((string?)push["contactId"] == contact.ToString("D") &&
                    (string?)push["templateKey"] == "WeeklyReport.ReplyNotification" &&
                    (string?)push["retryKey"] == WeeklyReportNotificationRule.RetryKey(
                        ids["church"], ids["report"], Id("event"), contact).ToString("D") &&
                    (int?)push["messageCount"] == (int?)call["messageCount"] &&
                    (string?)push["contentSha256"] == hash, "push content");
            }
            Require(sdk.WriteRecords.Count == 0, "real SDK writes");
        }
        finally { trace.Entries.Clear(); }
    }

    /// <summary>JSON marker 僅映射資料，不補業務預設；日期保留 UTC Kind，lookup Name 原樣傳入。</summary>
    private static Entity Entity(string table, Guid id, JObject fields, Dictionary<string, Guid> ids)
    {
        var row = new Entity(table, id);
        foreach (var field in fields.Properties())
        {
            if (field.Value is JObject marker)
            {
                if (marker["ref"] is JValue alias)
                    row[field.Name] = new EntityReference((string)marker["entity"]!, ids[(string)alias!])
                    { Name = (string?)marker["name"] };
                else if (marker["line"] is not null) row[field.Name] = "U" + new string('a', 32);
                else if (marker["option"] is not null) row[field.Name] = new OptionSetValue((int)marker["option"]!);
                else throw new InvalidDataException("Unknown weekly value marker.");
            }
            else if (field.Value.Type == JTokenType.Integer) row[field.Name] = (int)field.Value;
            else if (field.Value.Type == JTokenType.Float) row[field.Name] = (double)field.Value;
            else if (field.Value.Type == JTokenType.String && (field.Name == "new_sunday_date" || field.Name == "new_group_date"))
                row[field.Name] = DateTime.Parse((string)field.Value!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            else row[field.Name] = field.Value.Type == JTokenType.Null ? null : ((JValue)field.Value).Value;
        }
        return row;
    }

    /// <summary>每檔一 MiB、深度32、嚴格 UTF8；同案資源只讀取一次，reader 使用完立即釋放。</summary>
    private static JObject Read(string name)
    {
        var assembly = typeof(WeeklyReportGoldenRunner).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(value =>
            value.EndsWith(".Golden.weeklyreports." + name + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        Require(stream.Length <= 1_048_576, "fixture capacity");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true));
        using var reader = new JsonTextReader(text)
        { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal, MaxDepth = 32 };
        var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        Require(!reader.Read(), "trailing JSON");
        return result;
    }

    /// <summary>只供固定合成 alias 使用；不可作為授權來源或產品正式鍵的替代。</summary>
    private static Guid Id(string alias) => DeterministicGuid.Create("P5WeeklyGolden", alias);
    /// <summary>不用受限域中的動態測試框架；失敗只含合成資料及固定契約。</summary>
    private static void Require(bool valid, string contract)
    { if (!valid) throw new InvalidOperationException("Weekly golden mismatch: " + contract); }
    /// <summary>無資源固定時鐘，最長存活至單案完成，不保存教會或使用者資料。</summary>
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        /// <summary>固定情境瞬間，不讀程序時區或現在時間。</summary>
        public DateTimeOffset UtcNow => now;
    }
    /// <summary>單案 SDK factory；只借出當案使用者的服務，owner 仍為 Run。</summary>
    private sealed class Factory(IOrganizationService sdk, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>拒絕提升或沿用別的使用者；不建立連線、不轉移 Dispose。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId) => userId == user
            ? sdk : throw new InvalidOperationException("Golden user mismatch.");
    }
    /// <summary>手寫活動 extension，所有資料委派當案平台 context；無動態產碼或跨案快取。</summary>
    private sealed class WorkflowContext(RemoteExecutionContext value) : IWorkflowContext
    {
        /// <summary>本案階段名稱，不承載正式流程狀態。</summary>
        public string StageName => "PostOperation";
        /// <summary>合成流程分類。</summary>
        public int WorkflowCategory => 0;
        /// <summary>同步執行的合成流程模式。</summary>
        public int WorkflowMode => 0;
        /// <summary>本案沒有父活動，不保留其他執行範圍。</summary>
        public IWorkflowContext ParentContext => null!;
        /// <summary>以下委派皆只讀同一個操作區域 RemoteExecutionContext。</summary>
        public int Mode => value.Mode;
        /// <summary>沙箱隔離代號。</summary>
        public int IsolationMode => 2;
        /// <summary>本案深度。</summary>
        public int Depth => value.Depth;
        /// <summary>本案訊息。</summary>
        public string MessageName => value.MessageName;
        /// <summary>本案主表。</summary>
        public string PrimaryEntityName => value.PrimaryEntityName;
        /// <summary>本案可空請求識別。</summary>
        public Guid? RequestId => value.RequestId;
        /// <summary>本案次表。</summary>
        public string SecondaryEntityName => value.SecondaryEntityName;
        /// <summary>本案輸入集合，不跨執行共用。</summary>
        public ParameterCollection InputParameters => value.InputParameters;
        /// <summary>本案輸出集合，不跨執行共用。</summary>
        public ParameterCollection OutputParameters => value.OutputParameters;
        /// <summary>本案區域共享集合。</summary>
        public ParameterCollection SharedVariables => value.SharedVariables;
        /// <summary>平台執行使用者。</summary>
        public Guid UserId => value.UserId;
        /// <summary>本案起始使用者，不用於提高執行權限。</summary>
        public Guid InitiatingUserId => value.UserId;
        /// <summary>本案單位。</summary>
        public Guid BusinessUnitId => value.BusinessUnitId;
        /// <summary>本案組織正式識別。</summary>
        public Guid OrganizationId => value.OrganizationId;
        /// <summary>合成組織名稱。</summary>
        public string OrganizationName => value.OrganizationName;
        /// <summary>本案主列正式識別。</summary>
        public Guid PrimaryEntityId => value.PrimaryEntityId;
        /// <summary>本案前像集合。</summary>
        public EntityImageCollection PreEntityImages => value.PreEntityImages;
        /// <summary>本案後像集合。</summary>
        public EntityImageCollection PostEntityImages => value.PostEntityImages;
        /// <summary>本案擴充參照。</summary>
        public EntityReference OwningExtension => value.OwningExtension;
        /// <summary>本案平台事件。</summary>
        public Guid CorrelationId => value.CorrelationId;
        /// <summary>不模擬離線執行。</summary>
        public bool IsExecutingOffline => false;
        /// <summary>不模擬離線播放。</summary>
        public bool IsOfflinePlayback => false;
        /// <summary>只有測試的合成交易模型，不宣稱平台交易。</summary>
        public bool IsInTransaction => false;
        /// <summary>本案操作識別。</summary>
        public Guid OperationId => value.OperationId;
        /// <summary>本案操作時間。</summary>
        public DateTime OperationCreatedOn => value.OperationCreatedOn;
    }
}

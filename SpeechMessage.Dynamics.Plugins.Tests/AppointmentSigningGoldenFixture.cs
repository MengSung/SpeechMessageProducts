/*
 * 檔案整體說明
 *
 * 目的與定位：P5-08 真預約 Plugin／Workflow 的離線共用 owner；一般測試與 CAS
 * 探針都從本組件嵌入的 appointments 情境／reading 期望執行，不掃父工作樹、
 * 不呼叫舊程式、D365、HTTP 或產品訊息工廠來製造黃金答案。
 * 主要功能與流程：以代號建立正式合成 GUID，嚴格投影 SDK 欄位及查詢答案，
 * 執行真外殼後核對全部推播欄位、完整 Flex 加 server sender 的摘要、結尾及
 * 零真寫入。期望缺訊息／sender／retry／hash 時明確報缺口，不能降級成計數綠燈。
 * 資源與隔離：每案最多六十四列、一百二十八次讀取，單份 JSON 一 MiB、深度三十二；
 * reader／stream 逆序 using，SDK、來源、context、追蹤只保留至同步呼叫返回。
 * Dispose 排空回呼及集合；平台服務僅由本 fixture 擁有，不靠子域卸載取代清理。
 * 活動十秒為 WF 等待預算，不能強制中斷同步程式，工作量另以讀取次數限制。
 * 沒有 Session、秘密、連線、timer 或背景工作；不得把離線／CAS 證據當實機送達。
 */
extern alias Workflows;

using System;
using System.Activities;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Appointments;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using SigningActivity = Workflows::SpeechMessage.Dynamics.Workflows.Appointments.AppointmentSigningActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>單案獨佔的手寫 SDK 與平台內容；不使用 CAS 不允許的動態代理。</summary>
internal sealed class AppointmentSigningGoldenFixture : IDisposable
{
    /// <summary>固定前綴只選安全投影，錯誤型別追蹤另保留供失敗斷言。</summary>
    private const string Prefix = "SMV-SHADOW|1|";
    /// <summary>本次完整情境的 detached 快照，Dispose 清空，不能跨案共用。</summary>
    private readonly JObject scenario;
    /// <summary>代號對應正式合成識別；scope 區隔 A/B，不以顯示名稱去重。</summary>
    private readonly Dictionary<string, Guid> identities = new(StringComparer.Ordinal);
    /// <summary>本次唯一工廠，只按平台 UserId 借出假服務，不提供提權服務。</summary>
    private readonly Factory factory;
    /// <summary>結束後禁止重入，避免把已排空狀態重新當成有效案例。</summary>
    private bool disposed;
    /// <summary>可注入故障的單案來源，每次 SDK 回應由共用 fake 再深層複製。</summary>
    internal Dictionary<string, Entity> Rows { get; } = new(StringComparer.Ordinal);
    /// <summary>有界服務由本 fixture 唯一擁有，Dispose 解除所有回呼。</summary>
    internal FakeOrganizationService Service { get; } = new();
    /// <summary>只保留本次執行的合成安全摘要，執行前及 Dispose 都清空。</summary>
    internal FakeTracingService Trace { get; } = new();
    /// <summary>本案平台授權範圍，活動輸入不可覆蓋主記錄或使用者。</summary>
    internal RemoteExecutionContext Context { get; }
    /// <summary>故障注入：收件者重讀時的教會，可測授權在兩次讀取間變動。</summary>
    internal Guid? RecipientChurchOverride { get; set; }
    /// <summary>故障注入：來源 SDK 拋出的真例外，外殼必須保留其實例／型別。</summary>
    internal Exception? ReadFailure { get; set; }
    /// <summary>只在休假摘要查詢注入真故障，驗通知在完整載入前不能部分發布。</summary>
    internal Exception? QueryFailure { get; set; }
    /// <summary>只在本次 Run 期間計數，防止查詢回圈及多次隱藏重試。</summary>
    internal int Reads { get; private set; }
    /// <summary>區分 Plugin 正常進入與深度提早返回，不以沒有讀取誤認兩者相同。</summary>
    internal int ServiceLeases => factory.Calls;
    /// <summary>僅保存本次讀過的 schema 表名，用於拒絕路徑的零頻道與零摘要斷言。</summary>
    internal List<string> ReadTables { get; } = new();

    /// <summary>解析嵌入種子，不補缺欄遮掩規則錯誤；初始化失敗也清理已建立資源。</summary>
    internal AppointmentSigningGoldenFixture(JObject definition, string scope = "A")
    {
        scenario = (JObject)definition.DeepClone();
        var trigger = scenario["trigger"]!;
        var activity = (bool?)trigger["activity"] == true;
        var parameter = (string?)trigger["inputs"]?["InputParameter"] ?? "";
        var message = activity ? parameter.Contains("新增") ? "Create" : parameter.Contains("更新") ? "Update" : "ExecuteWorkflow"
            : (string)trigger["message"]!;
        Context = new RemoteExecutionContext
        {
            MessageName = message, PrimaryEntityName = activity ? "appointment" : (string)trigger["entity"]!,
            PrimaryEntityId = Id(scope, activity ? "record" : (string)trigger["target"]!), Depth = (int)trigger["depth"]!,
            Stage = 40, Mode = (int?)trigger["mode"] ?? 0, IsolationMode = 2, OrganizationId = Id(scope, "organization"),
            OrganizationName = (string)scenario["church"]!["organizationName"]!,
            UserId = Id(scope, "user"), InitiatingUserId = Id(scope, "user"), CorrelationId = Id(scope, "event")
        };
        factory = new Factory(Service, Context.UserId);
        try
        {
            var definitions = scenario["seed"]!.Children<JObject>().ToArray();
            Require(definitions.Length is > 0 and <= 64, "bounded seed");
            foreach (var seed in definitions)
                identities.Add((string)seed["alias"]!, Id(scope, (string)seed["alias"]!));
            foreach (var seed in definitions)
            {
                var alias = (string)seed["alias"]!;
                Rows.Add(alias, Make((string)seed["entity"]!, identities[alias], (JObject)seed["fields"]!));
            }
            Context.InputParameters["Target"] = Make(Context.PrimaryEntityName, Context.PrimaryEntityId, (JObject?)trigger["fields"] ?? new JObject());
            Service.RetrieveHandler = Retrieve;
            Service.RetrieveMultipleHandler = Query;
            Service.CreateHandler = _ => throw UnexpectedWrite();
            Service.UpdateHandler = _ => throw UnexpectedWrite();
            Service.DeleteHandler = (_, _) => throw UnexpectedWrite();
            Service.ExecuteHandler = _ => throw UnexpectedWrite();
            Service.AssociateHandler = (_, _, _, _) => throw UnexpectedWrite();
            Service.DisassociateHandler = (_, _, _, _) => throw UnexpectedWrite();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>代號只適用本案合成種子；未宣告識別不能自動建立或假裝正式列。</summary>
    internal Guid Identity(string alias) => identities[alias];

    /// <summary>真 Plugin 同步返回後才讀追蹤；重用外殼仍重新建立本次 provider。</summary>
    internal void RunPlugin(AppointmentPlugin? plugin = null)
    {
        Begin();
        (plugin ?? new AppointmentPlugin("{\"mode\":\"Shadow\"}", "")).Execute(
            new FakeServiceProvider().Add<IPluginExecutionContext>(Context)
                .Add<IOrganizationServiceFactory>(factory).Add<ITracingService>(Trace));
    }

    /// <summary>真活動每次 Invoke 求值；字典 finally 清空，不把平台內容寫入活動欄位。</summary>
    internal IDictionary<string, object> RunActivity(SigningActivity? activity = null, string? mode = "Shadow",
        EntityReference? target = null, bool omitTarget = false, string? parameter = null)
    {
        Begin();
        var arguments = new Dictionary<string, object>
        {
            ["InputParameter"] = parameter ?? (string?)scenario["trigger"]!["inputs"]?["InputParameter"] ?? "工作流程"
        };
        if (mode is not null) arguments["ExecutionMode"] = mode;
        if (!omitTarget) arguments["InputAppointment"] = target ?? new EntityReference("appointment", Context.PrimaryEntityId);
        var invoker = new WorkflowInvoker(activity ?? new SigningActivity());
        invoker.Extensions.Add<IWorkflowContext>(() => new WorkflowContext(Context));
        invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory);
        invoker.Extensions.Add<ITracingService>(() => Trace);
        try { return invoker.Invoke(arguments, TimeSpan.FromSeconds(10)); }
        finally { arguments.Clear(); }
    }

    /// <summary>讀完整安全投影並驗結尾／連續序號／容量，不能悄悄略過 overflow 或額外寫入。</summary>
    internal JObject[] Operations()
    {
        var lines = Trace.Entries.Where(x => x.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(x => RecordingOrganizationService.ReadProjection(x.Substring(Prefix.Length))).ToArray();
        Require(lines.Length > 0 && Trace.Entries.Sum(x => x.Length + 2) <= 8000 &&
            lines.All(x => x["overflow"] is null), "complete bounded shadow trace");
        var operations = lines.Take(lines.Length - 1).ToArray();
        Require((bool?)lines.Last()["end"] == true && (int?)lines.Last()["count"] == operations.Length &&
            lines.Count(x => x["end"] is not null) == 1, "single final end count");
        for (var i = 0; i < operations.Length; i++)
            Require((int?)operations[i]["seq"] == i + 1 && operations[i]["operation"] is not null, "continuous intent sequence");
        AssertNoWrites();
        return operations;
    }

    /// <summary>服務尚未 Dispose 前斷言所有已發生呼叫唯讀，避免清空計數造成假成功。</summary>
    internal void AssertNoWrites() => Require(Service.WriteRecords.Count == 0 && Service.EntityCount == 0 &&
        Service.Calls.All(x => x.Key is nameof(IOrganizationService.Retrieve) or nameof(IOrganizationService.RetrieveMultiple)),
        "zero real service writes");

    /// <summary>嵌入三至六案全部執行後彙總缺口；缺完整訊息不以摘要形狀或產品工廠自造期望。</summary>
    internal static void RunAll(string entrypoint = "scenario")
    {
        Require(entrypoint is "scenario" or "plugin" or "activity", "known entrypoint");
        var definitions = Read("scenarios")["cases"]!.Children<JObject>().ToArray();
        var baseline = Read("expected");
        var expected = baseline["cases"]!.Children<JObject>().ToArray();
        Require((string?)baseline["basis"] == "reading" && definitions.Length is >= 3 and <= 6 &&
            expected.Length == definitions.Length, "three through six reading cases");
        var failures = new List<string>();
        for (var i = 0; i < definitions.Length; i++)
        {
            var name = (string)definitions[i]["name"]!;
            using var fixture = new AppointmentSigningGoldenFixture(definitions[i]);
            try
            {
                Require(name == (string?)expected[i]["name"], "scenario and expected order");
                var selected = (string?)definitions[i]["trigger"]!["newModuleEntrypoint"] ??
                    (string?)definitions[i]["newModuleEntrypoint"] ?? (string?)definitions[i]["trigger"]!["type"] ?? "";
                var workflow = entrypoint == "activity" || (entrypoint == "scenario" &&
                    (selected.Contains("AppointmentSigningActivity") ||
                     (bool?)definitions[i]["trigger"]!["activity"] == true));
                IDictionary<string, object>? outputs = null;
                Exception? failure = null;
                try
                {
                    if (workflow) outputs = fixture.RunActivity();
                    else
                    {
                        if (fixture.Context.MessageName == "ExecuteWorkflow") fixture.Context.MessageName = "Create";
                        fixture.Context.Mode = 0;
                        fixture.RunPlugin();
                    }
                }
                catch (Exception exception) { failure = exception.GetBaseException(); }
                var expectedException = entrypoint == "plugin" ? null : (string?)expected[i]["exception"];
                Require(expectedException == failure?.GetType().Name,
                    "exception contract; actual=" + (failure?.GetType().Name ?? "none") +
                    (failure is null ? "" : "; " + failure.Message));
                if (workflow && failure is null)
                    Require(outputs is not null && (int)outputs["OutputResult"] == (int)expected[i]["outputs"]!["OutputResult"]!, "activity output");
                if (entrypoint == "plugin")
                {
                    Require(fixture.Operations().Length == 0 && fixture.Reads == 0 && fixture.ServiceLeases == 1,
                        "legacy synchronous plugin empty parameter produces no notification");
                }
                else fixture.Compare(expected[i]);
            }
            catch (Exception failure) { failures.Add(name + ": " + failure.Message); }
        }
        Require(failures.Count == 0, "complete reading comparison: " + string.Join(" | ", failures));
    }

    /// <summary>固定嵌入資源讀取；嚴格 UTF-8、拒絕重複欄位，所有 I/O 在本呼叫內釋放。</summary>
    internal static JObject Read(string kind)
    {
        Require(kind is "scenarios" or "expected", "known fixture resource");
        var assembly = typeof(AppointmentSigningGoldenFixture).Assembly;
        var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(".Golden.appointments." + kind + ".json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        Require(stream.Length <= 1048576, "fixture size");
        using var text = new StreamReader(stream, new UTF8Encoding(false, true), false);
        using var reader = new JsonTextReader(text)
        { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
        return JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
    }

    /// <summary>依固定用途產生合成資料庫主鍵；沒有使用者姓名或實機資料參與計算。</summary>
    internal static Guid Id(string scope, string alias) => DeterministicGuid.Create("P5AppointmentGolden", scope, alias);

    /// <summary>一般斷言可在 CAS 域執行，不讓 xUnit／Moq 加入受限產品鏈。</summary>
    internal static void Require(bool value, string contract)
    {
        if (!value) throw new InvalidOperationException("Appointment golden contract mismatch: " + contract);
    }

    /// <summary>全部推播欄位精確比對；完整訊息只從 reading 載入，含 sender 後計算摘要。</summary>
    private void Compare(JObject expected)
    {
        Require(expected["writes"] is JArray writes && writes.Count == 0, "explicit no-write baseline");
        var operations = Operations();
        var calls = expected["lineCalls"]!.Children<JObject>().ToArray();
        Require(calls.Length == operations.Length, "all push intents");
        for (var i = 0; i < calls.Length; i++)
        {
            var call = calls[i]; var actual = operations[i];
            Require((string?)actual["operation"] == "Push" &&
                (string?)actual["contactId"] == Identity((string)call["recipients"]![0]!).ToString("D") &&
                (string?)actual["templateKey"] == (string?)call["shadow"]?["templateKey"] &&
                (int?)actual["messageCount"] == (int?)call["messageCount"], "recipient template message count");
            Require(expected["outputs"]?["FlexMessages"]?.Type == JTokenType.String &&
                expected["outputs"]?["SenderName"]?.Type == JTokenType.String &&
                expected["outputs"]?["RetryKey"]?.Type == JTokenType.String &&
                call["shadow"]?["contentSha256"]?.Type == JTokenType.String,
                "INCOMPLETE_READING_EXPECTED: messages, senderName, shadow.retryKey, shadow.contentSha256 are required");
            var json = Resolve((string)expected["outputs"]!["FlexMessages"]!);
            using var text = new StringReader(json);
            using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            var messages = JArray.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            Require(messages.Count == (int)call["messageCount"]!, "full expected message count");
            foreach (var message in messages.Children<JObject>())
            {
                Require((string?)message["type"] == "flex" && (string?)message["contents"]?["type"] == "carousel",
                    "complete legacy carousel message");
                message["sender"] = new JObject { ["name"] = (string)expected["outputs"]!["SenderName"]! };
            }
            using var sha = SHA256.Create();
            var hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(messages.ToString(Formatting.None)))
                .Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
            var retry = Resolve((string)expected["outputs"]!["RetryKey"]!);
            Require(Guid.TryParse(retry, out var retryId) && retryId != Guid.Empty, "explicit expected retry GUID");
            Require(hash == (string?)call["shadow"]!["contentSha256"] &&
                hash == (string?)actual["contentSha256"] && retryId.ToString("D") == (string?)actual["retryKey"] &&
                actual.Properties().Count() == 7, "full Flex sender hash and exact push fields");
        }
    }

    /// <summary>只代換已宣告種子的正式 ID，不執行運算式、不接受產品輸出生成答案。</summary>
    private string Resolve(string value)
    {
        foreach (var identity in identities) value = value.Replace("{" + identity.Key + "}", identity.Value.ToString("D"));
        return value;
    }

    /// <summary>每次真入口前清前次追蹤及計數；已終止 fixture 不可重新執行。</summary>
    private void Begin()
    {
        if (disposed) throw new ObjectDisposedException(nameof(AppointmentSigningGoldenFixture));
        Trace.Entries.Clear(); Service.Calls.Clear(); ReadTables.Clear(); Reads = 0; factory.Calls = 0;
    }

    /// <summary>白名單列及最小欄位回應；禁止舊 token 欄位及 AllColumns，錯誤原樣注入。</summary>
    private Entity Retrieve(string table, Guid id, ColumnSet columns)
    {
        Require(++Reads <= 128 && !columns.AllColumns &&
            !columns.Columns.Contains("new_channel_access_token"), "bounded retrieve without legacy token");
        ReadTables.Add(table);
        if (ReadFailure is not null) throw ReadFailure;
        var row = Rows.Values.Single(x => x.Id == id && x.LogicalName == table);
        var result = new Entity(table, id);
        foreach (var field in columns.Columns)
            if (row.Attributes.TryGetValue(field, out var value)) result[field] = value;
        if (table == "contact" && columns.Columns.Contains("new_lineid") && RecipientChurchOverride.HasValue)
            result["parentcustomerid"] = new EntityReference("account", RecipientChurchOverride.Value);
        return result;
    }

    /// <summary>只接受情境明列的查詢答案；未列答案拒絕，不能任意回空掩蓋請假明細缺口。</summary>
    private EntityCollection Query(QueryBase query)
    {
        Require(++Reads <= 128 && query is QueryExpression, "bounded typed appointment query");
        var typed = (QueryExpression)query;
        Require(!typed.ColumnSet.AllColumns && !typed.ColumnSet.Columns.Contains("new_channel_access_token") &&
            typed.LinkEntities.Count == 0, "query without legacy token or hidden link columns");
        Require(typed.EntityName == "appointment" && typed.PageInfo.PageNumber == 1 &&
            typed.PageInfo.Count is > 0 and <= 257 && typed.Criteria.FilterOperator == LogicalOperator.And &&
            typed.Criteria.Filters.Count == 0, "bounded complete applicant summary query");
        Require(typed.Criteria.Conditions.Any(x => x.AttributeName == "new_applier_appointment" &&
            x.Operator == ConditionOperator.Equal && x.Values.Count == 1 && Equals(x.Values[0], Identity("applicant"))) &&
            typed.Criteria.Conditions.Any(x => x.AttributeName == "scheduledstart" && x.Operator == ConditionOperator.ThisYear) &&
            typed.Criteria.Conditions.Any(x => x.AttributeName == "new_leave_signing_status" &&
                x.Operator == ConditionOperator.In && x.Values.Count == 3 &&
                x.Values.Contains(100000001) && x.Values.Contains(100000004) && x.Values.Contains(100000007)) &&
            typed.Orders.Any(x => x.AttributeName == "subject" && x.OrderType == OrderType.Ascending),
            "legacy year status order and applicant scope");
        if (ReadFailure is not null) throw ReadFailure;
        if (QueryFailure is not null) throw QueryFailure;
        var index = Service.Calls[nameof(IOrganizationService.RetrieveMultiple)];
        var answer = scenario["queryResults"]?[index.ToString(CultureInfo.InvariantCulture)] as JArray;
        Require(answer is not null, "MISSING_QUERY_BASELINE: explicit queryResults for appointment details");
        var aliases = answer!.Values<string>().ToArray();
        var result = new EntityCollection();
        foreach (var alias in aliases)
        {
            var row = Rows[alias!];
            Require(row.LogicalName == typed.EntityName, "query result exact table");
            result.Entities.Add(row);
        }
        return result;
    }

    /// <summary>只解析情境的純值、正式 lookup、option 及 UTC 日期，不猜未知 marker。</summary>
    private Entity Make(string table, Guid id, JObject fields)
    {
        var result = new Entity(table, id);
        foreach (var field in fields.Properties())
        {
            object? value;
            if (field.Value is JObject marker)
                value = marker["ref"] is not null
                    ? new EntityReference((string)marker["entity"]!, identities[(string)marker["ref"]!]) { Name = (string?)marker["name"] }
                    : marker["line"] is not null ? "U" + identities[(string)marker["line"]!].ToString("N")
                    : marker["option"] is not null ? new OptionSetValue((int)marker["option"]!)
                    : throw new InvalidDataException("Unknown appointment fixture marker.");
            else if (field.Name is "scheduledstart" or "scheduledend")
                value = DateTime.Parse((string)field.Value!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            else if (field.Name == "new_days") value = (double)field.Value;
            else if (field.Value.Type == JTokenType.Integer) value = (int)field.Value;
            else value = ((JValue)field.Value).Value;
            result[field.Name] = value;
        }
        // 公開黃金不保存憑證欄位；僅離線假頻道回傳不會被解密的合成 envelope。
        if (table == "new_linechannel") result["new_accesstokenencrypted"] = "synthetic-envelope";
        return result;
    }

    /// <summary>真服務的任何副作用都拒絕；只回固定合成錯誤，不回顯來源資料。</summary>
    private static InvalidOperationException UnexpectedWrite() => new("Appointment fixture attempted a real service write.");

    /// <summary>確定排空 SDK 回呼及全部案例集合；可重複執行，並驗零保留基線。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try { Service.Dispose(); }
        finally
        {
            Rows.Clear(); identities.Clear(); scenario.RemoveAll(); Trace.Entries.Clear(); ReadTables.Clear();
            Context.InputParameters.Clear(); Context.OutputParameters.Clear(); Context.SharedVariables.Clear();
            Context.PreEntityImages.Clear(); Context.PostEntityImages.Clear();
            ReadFailure = null; QueryFailure = null; RecipientChurchOverride = null;
        }
        Require(Service.Calls.Count == 0 && Service.WriteRecords.Count == 0 && Service.EntityCount == 0 &&
            Service.AssociationCount == 0 && Service.QueryResultCount == 0 &&
            Service.RetrieveHandler is null && Service.RetrieveMultipleHandler is null &&
            Service.CreateHandler is null && Service.UpdateHandler is null && Service.DeleteHandler is null &&
            Service.ExecuteHandler is null && Service.AssociateHandler is null && Service.DisassociateHandler is null,
            "fixture drained");
    }

    /// <summary>只借本案 SDK，無 null 提權、跨使用者借用或服務所有權轉移。</summary>
    private sealed class Factory(IOrganizationService service, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>只計本次入口借用，Begin 重設以驗 depth 返回先於 SDK 工廠。</summary>
        internal int Calls { get; set; }
        /// <summary>當次平台 UserId 必須完全相同；不建立網路或額外狀態。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId)
        {
            Require(userId == user && ++Calls == 1, "single platform-user service lease");
            return service;
        }
    }

    /// <summary>手寫 WF extension，僅委派當案平台值，沒有 Moq、動態產碼或跨域服務。</summary>
    private sealed class WorkflowContext(RemoteExecutionContext value) : IWorkflowContext
    {
        /// <summary>固定合成階段，非實機註冊證據。</summary>
        public string StageName => "PostOperation";
        /// <summary>固定合成分類，不保存流程引擎。</summary>
        public int WorkflowCategory => 0;
        /// <summary>同步假活動模式，工作由 Invoke 排空。</summary>
        public int WorkflowMode => 0;
        /// <summary>沒有父活動，不保留其他執行範圍。</summary>
        public IWorkflowContext ParentContext => null!;
        /// <summary>當案平台模式。</summary>
        public int Mode => value.Mode;
        /// <summary>固定沙箱代號，實際 CAS 另由父測試限制。</summary>
        public int IsolationMode => 2;
        /// <summary>當案深度，不跨呼叫累計。</summary>
        public int Depth => value.Depth;
        /// <summary>當案固定訊息名稱。</summary>
        public string MessageName => value.MessageName;
        /// <summary>當案來源表，不由活動輸入決定。</summary>
        public string PrimaryEntityName => value.PrimaryEntityName;
        /// <summary>當案可空請求識別。</summary>
        public Guid? RequestId => value.RequestId;
        /// <summary>當案次要表。</summary>
        public string SecondaryEntityName => value.SecondaryEntityName;
        /// <summary>當案輸入，最長保留至 Run 的 finally。</summary>
        public ParameterCollection InputParameters => value.InputParameters;
        /// <summary>當案輸出，清理後不留合成資料。</summary>
        public ParameterCollection OutputParameters => value.OutputParameters;
        /// <summary>只供當案外殼使用的共享集合。</summary>
        public ParameterCollection SharedVariables => value.SharedVariables;
        /// <summary>本次服務工廠唯一接受的使用者。</summary>
        public Guid UserId => value.UserId;
        /// <summary>起始使用者與執行使用者一致，不模擬提權。</summary>
        public Guid InitiatingUserId => value.InitiatingUserId;
        /// <summary>當案單位值。</summary>
        public Guid BusinessUnitId => value.BusinessUnitId;
        /// <summary>合成組織正式識別，不讀真環境。</summary>
        public Guid OrganizationId => value.OrganizationId;
        /// <summary>固定非敏感名稱，不作授權來源。</summary>
        public string OrganizationName => value.OrganizationName;
        /// <summary>主要資料列的合成正式 GUID。</summary>
        public Guid PrimaryEntityId => value.PrimaryEntityId;
        /// <summary>當案前像，finally 清空。</summary>
        public EntityImageCollection PreEntityImages => value.PreEntityImages;
        /// <summary>當案後像，finally 清空。</summary>
        public EntityImageCollection PostEntityImages => value.PostEntityImages;
        /// <summary>當案擴充參照，不持有實機元件。</summary>
        public EntityReference OwningExtension => value.OwningExtension;
        /// <summary>當案相關識別，只用於安全診斷。</summary>
        public Guid CorrelationId => value.CorrelationId;
        /// <summary>不模擬離線同步功能。</summary>
        public bool IsExecutingOffline => false;
        /// <summary>不模擬離線重播。</summary>
        public bool IsOfflinePlayback => false;
        /// <summary>假 SDK 不宣稱實機交易。</summary>
        public bool IsInTransaction => false;
        /// <summary>當案操作識別，不保存其他事件。</summary>
        public Guid OperationId => value.OperationId;
        /// <summary>當案平台時間，不讀程序本地時鐘。</summary>
        public DateTime OperationCreatedOn => value.OperationCreatedOn;
    }
}

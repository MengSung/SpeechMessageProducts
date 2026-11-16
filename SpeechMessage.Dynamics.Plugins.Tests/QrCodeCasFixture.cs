/*
 * 檔案整體說明
 * 目的與定位：P5-09 QR 活動共用的離線執行器，以手寫 SDK/context 跑真 WorkflowInvoker，
 * 同時供完整信任及受限 AppDomain 測試；不使用 Moq、私有反射、舊 DLL 或網路。
 * 主要流程：建立合成 meeting、contact、account 與三十八筆出席，嚴格投影權威欄位，
 * 驗完整單筆圖片待辦或三十八欄統計 patch、精確來源及零真寫入。計數使用 1～38
 * 的獨立期望，避免全零或同值掩蓋錯誤映射；影子追蹤必須完整、無 overflow。
 * 資源與隔離：本 fixture 是資料、SDK callback、工廠及追蹤唯一 owner，最多四十二列，
 * 六十四次讀取與一頁查詢；不持有秘密、Session、timer 或背景工作。每次 Invoke
 * 同步排空，十秒等待預算不宣稱可中斷同步 SDK。Dispose 先驗零真寫再無條件清空，
 * 最後驗服務及 callback 零基線；父測試另以 finally 卸載子域。來源只屬當案，
 * A/B/A 使用同活動但獨立 fixture，不能由名稱、活動參數或組織字串代替正式身分。
 * 使用邊界與維護：本檔只驗離線平台協調，不宣稱 Dataverse 授權、唯一性或實機 CAS。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using MeetingActivity = Workflows::SpeechMessage.Dynamics.Workflows.QrCodes.MeetingQrCodeActivity;
using PersonalActivity = Workflows::SpeechMessage.Dynamics.Workflows.QrCodes.PersonalQrCodeActivity;
using ScanActivity = Workflows::SpeechMessage.Dynamics.Workflows.QrCodes.MeetingScanCountActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>單案合成服務及追蹤的有界 owner；所有公開於測試的資料只存活至 Dispose。</summary>
internal sealed class QrCodeCasFixture : IDisposable
{
    /// <summary>固定入口，不允許從 WF 字串選任意產品型別。</summary>
    private readonly string entrypoint;
    /// <summary>當案獨占的權威來源；未交給共用 fake 表，避免猜測特殊主鍵。</summary>
    private readonly Dictionary<Guid, Entity> rows = new();
    /// <summary>固定平台 context 的唯一 owner，清理時排空所有參數與映像。</summary>
    private readonly RemoteExecutionContext context;
    /// <summary>只接受本案執行使用者的單次服務借用工廠。</summary>
    private readonly Factory factory;
    /// <summary>防止二次清理或清理後重新執行。</summary>
    private bool disposed;
    /// <summary>合成平台主要記錄正式 ID，與 caller 的 InputParameter 無關。</summary>
    internal Guid RecordId { get; } = Guid.NewGuid();
    /// <summary>合成正式教會 ID，每案獨立，不由 org 名稱推論。</summary>
    internal Guid ChurchId { get; } = Guid.NewGuid();
    /// <summary>合成聯絡人 ID，meeting 經出席權威 lookup 取得。</summary>
    private readonly Guid contactId;
    /// <summary>真服務替身只接受讀取，寫入 callback 全部拒絕且納入清理前斷言。</summary>
    internal FakeOrganizationService Sdk { get; } = new();
    /// <summary>追蹤僅保留當案完整安全投影，Dispose 時清空。</summary>
    internal FakeTracingService Trace { get; } = new();
    /// <summary>只供讀取階段故障注入；保留原物件供 Assert.Same，不是產品錯誤處理。</summary>
    internal Exception? ReadFailure { get; set; }
    /// <summary>注入缺乏權威出席來源，驗圖片任務不得回落教會。</summary>
    internal bool NoAttendance { get; set; }
    /// <summary>注入其中一位會友跨教會，驗完整來源未驗完不得入列。</summary>
    internal bool MixedChurch { get; set; }
    /// <summary>保留完整影子追蹤大小供父測試輸出實測證據，不包含真環境資訊。</summary>
    internal int TraceCharacters => Trace.Entries.Sum(line => line.Length + 2);

    /// <summary>建立最多四十二列的獨立來源與精確查詢回呼，不讀真設定或借用其他案例資料。</summary>
    internal QrCodeCasFixture(string entrypoint, int depth = 1, string message = "ExecuteWorkflow", string? table = null)
    {
        this.entrypoint = entrypoint;
        Require(entrypoint is "meeting" or "personal" or "scan", "known entrypoint");
        contactId = entrypoint == "personal" ? RecordId : Guid.NewGuid();
        context = new RemoteExecutionContext
        {
            Depth = depth, MessageName = message, Stage = 40, Mode = 0, IsolationMode = 2,
            PrimaryEntityName = table ?? (entrypoint == "personal" ? "contact" : "new_meeting_statistics"),
            PrimaryEntityId = RecordId, UserId = Guid.NewGuid(), InitiatingUserId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(), OrganizationName = "synthetic-qr", CorrelationId = Guid.NewGuid()
        };
        factory = new Factory(Sdk, context.UserId);
        rows.Add(contactId, new Entity("contact", contactId)
        {
            ["fullname"] = "合成同名 @&?", ["parentcustomerid"] = new EntityReference("account", ChurchId)
        });
        rows.Add(ChurchId, new Entity("account", ChurchId) { ["name"] = "合成教會" });
        if (entrypoint != "personal") rows.Add(RecordId, new Entity("new_meeting_statistics", RecordId));
        var pairs = Pairs();
        for (var index = 0; index < 38; index++)
        {
            var row = new Entity("new_present_record", Guid.NewGuid())
            {
                ["new_meeting_statistics_new_present_re"] = new EntityReference("new_meeting_statistics", RecordId),
                ["new_contact_new_present_record"] = new EntityReference("contact", contactId)
            };
            // 第 i 個欄位在前 i+1 筆非空；值刻意只用固定 UTC 日期，不依測試執行時鐘。
            for (var field = index; field < pairs.Length; field++)
                row[pairs[field].Source] = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
            rows.Add(row.Id, row);
        }
        Sdk.RetrieveHandler = (name, id, columns) =>
        {
            if (ReadFailure is not null) throw ReadFailure;
            Require(Sdk.Calls[nameof(IOrganizationService.Retrieve)] <= 64 && !columns.AllColumns &&
                rows.ContainsKey(id) && rows[id].LogicalName == name, "bounded exact retrieve");
            Require(columns.Columns.All(column => column.IndexOf("token", StringComparison.OrdinalIgnoreCase) < 0),
                "no token retrieval");
            return Project(rows[id], columns);
        };
        Sdk.RetrieveMultipleHandler = query =>
        {
            if (ReadFailure is not null) throw ReadFailure;
            Require(Sdk.Calls[nameof(IOrganizationService.RetrieveMultiple)] <= 2 && query is QueryExpression,
                "bounded typed query");
            var typed = (QueryExpression)query;
            Require(!typed.ColumnSet.AllColumns, "explicit query columns");
            Require(typed.EntityName == "new_present_record" && typed.PageInfo.PageNumber == 1 &&
                typed.PageInfo.Count is > 0 and <= 500 && typed.Criteria.Conditions.Count == 1 &&
                typed.Criteria.Conditions[0].AttributeName == "new_meeting_statistics_new_present_re" &&
                typed.Criteria.Conditions[0].Operator == ConditionOperator.Equal &&
                typed.Criteria.Conditions[0].Values.Count == 1 &&
                Equals(typed.Criteria.Conditions[0].Values[0], RecordId), "exact meeting-scoped attendance");
            if (NoAttendance) return new EntityCollection();
            var attendance = rows.Values.Where(row => row.LogicalName == "new_present_record").ToArray();
            if (MixedChurch)
            {
                var other = Guid.NewGuid();
                rows[other] = new Entity("contact", other)
                { ["parentcustomerid"] = new EntityReference("account", Guid.NewGuid()) };
                attendance[attendance.Length - 1]["new_contact_new_present_record"] = new EntityReference("contact", other);
            }
            return new EntityCollection(attendance.Select(row => Project(row, typed.ColumnSet)).ToArray());
        };
        Sdk.CreateHandler = _ => throw UnexpectedWrite();
        Sdk.UpdateHandler = _ => throw UnexpectedWrite();
        Sdk.DeleteHandler = (_, _) => throw UnexpectedWrite();
        Sdk.ExecuteHandler = _ => throw UnexpectedWrite();
        Sdk.AssociateHandler = (_, _, _, _) => throw UnexpectedWrite();
        Sdk.DisassociateHandler = (_, _, _, _) => throw UnexpectedWrite();
    }

    /// <summary>僅直接 new 三個正式產品型別，受限域不透過反射或動態代理繞過執行。</summary>
    internal static CodeActivity CreateActivity(string entrypoint) => entrypoint switch
    {
        "meeting" => new MeetingActivity(),
        "personal" => new PersonalActivity(),
        "scan" => new ScanActivity(),
        _ => throw new ArgumentException("Unknown QR entrypoint.", nameof(entrypoint))
    };

    /// <summary>單次真 WF 執行，輸入可以是任意合成文字但不能改變平台來源或教會。</summary>
    internal IDictionary<string, object> Invoke(Activity activity, string input = "unused-synthetic", bool bindInputs = true)
    {
        Require(!disposed, "active fixture");
        var invoker = new WorkflowInvoker(activity);
        invoker.Extensions.Add<IWorkflowContext>(() => new WorkflowContext(context));
        invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory);
        invoker.Extensions.Add<ITracingService>(() => Trace);
        var inputs = bindInputs
            ? new Dictionary<string, object> { ["InputParameter"] = input, ["ExecutionMode"] = "Shadow" }
            : new Dictionary<string, object>();
        try { return invoker.Invoke(inputs, TimeSpan.FromSeconds(10)); }
        finally { inputs.Clear(); }
    }

    /// <summary>逐欄驗完整意圖、計數、正式來源及八千字限制，並確認只有讀取曾穿透真服務。</summary>
    internal JObject Verify(bool noOperation = false)
    {
        var lines = Trace.Entries.Where(line => line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(line => RecordingOrganizationService.ReadProjection(line.Substring("SMV-SHADOW|1|".Length))).ToArray();
        Require(lines.Length == (noOperation ? 1 : 2) && (bool?)lines.Last()["end"] == true &&
            (int?)lines.Last()["count"] == (noOperation ? 0 : 1) && lines.All(line => line["overflow"] is null) &&
            TraceCharacters <= 8000, "complete bounded shadow trace");
        Require(factory.Calls == 1 && NoRealWrites(), "single user lease and zero real writes");
        if (noOperation)
        {
            Require(Sdk.Calls.Count == 0, "no reads for unsupported trigger");
            return lines[0];
        }
        var intent = lines[0];
        Require((int?)intent["seq"] == 1, "first intent sequence");
        if (entrypoint == "scan")
        {
            var fields = new JObject();
            var pairs = Pairs();
            for (var i = 0; i < pairs.Length; i++) fields[pairs[i].Target] = i + 1;
            Require((string?)intent["operation"] == "Update" && (string?)intent["entity"] == "new_meeting_statistics" &&
                (string?)intent["id"] == RecordId.ToString("D") &&
                JToken.DeepEquals(fields, intent["fields"]), "all 38 independent scan counters");
        }
        else
        {
            Require((string?)intent["operation"] == "Create" && (string?)intent["entity"] == "new_outboundtask" &&
                Guid.TryParse((string?)intent["id"], out var task) && task != Guid.Empty, "single formal task create");
            var fields = (JObject)intent["fields"]!;
            Require((string?)fields["new_accountid"]!["entity"] == "account" &&
                (string?)fields["new_accountid"]!["id"] == ChurchId.ToString("D"), "authoritative church partition");
            var payload = new JObject
            {
                ["schema"] = 2, ["kind"] = entrypoint == "meeting" ? 0 : 1,
                ["recordId"] = RecordId.ToString("D"), ["contentVersion"] = 1,
                ["imageCount"] = entrypoint == "meeting" ? 38 : 1
            };
            Require(JToken.DeepEquals(payload, JObject.Parse((string)fields["new_payload"]!)), "exact five-field schema2 payload");
            VerifyTaskFields(fields, payload);
        }
        return intent;
    }

    /// <summary>
    /// 待辦逐欄驗完整映射，包含固定類型、狀態、原始排序 payload 及 name 隱私摘要。
    /// 事件鍵在此驗證正確 purpose、來源及版本的接線；獨立固定 GUID 向量由 Core 黃金測試負責。
    /// </summary>
    private void VerifyTaskFields(JObject fields, JObject payload)
    {
        var source = DeterministicGuid.Create("qrcodes." + entrypoint, RecordId, 1);
        var key = ChurchId.ToString("N") + "-100000001-" + source.ToString("N");
        var name = "QrImageGenerate|" + key;
        using var sha = SHA256.Create();
        var hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(name)).Select(value => value.ToString("x2", CultureInfo.InvariantCulture)));
        // payload 原始字串也必須符合 writer 的欄位排序；不從產品 Command 取答案。
        var canonical = new JObject(payload.Properties().OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new JProperty(property.Name, property.Value.DeepClone())));
        var expected = new JObject
        {
            ["new_accountid"] = new JObject { ["entity"] = "account", ["id"] = ChurchId.ToString("D") },
            ["new_attempts"] = 0, ["new_idempotencykey"] = key,
            ["new_name"] = new JObject { ["length"] = name.Length, ["sha256"] = hash },
            ["new_payload"] = canonical.ToString(Newtonsoft.Json.Formatting.None),
            ["new_taskstatus"] = 100000000, ["new_type"] = 100000001
        };
        if (entrypoint == "personal")
            expected["new_contactid"] = new JObject { ["entity"] = "contact", ["id"] = RecordId.ToString("D") };
        Require(JToken.DeepEquals(expected, fields), "complete task fields including deterministic source");
    }

    /// <summary>A/B/A 第二次執行 A 前只重設當案工廠計數，不改來源、使用者或產品狀態。</summary>
    internal void ResetInvocationLease() => factory.Reset();
    /// <summary>只有 SDK 讀取可以穿透記錄器；失敗的真寫入呼叫也視為違規。</summary>
    private bool NoRealWrites() => Sdk.WriteRecords.Count == 0 && Sdk.EntityCount == 0 &&
        Sdk.AssociationCount == 0 && Sdk.Calls.All(call =>
            call.Key is nameof(IOrganizationService.Retrieve) or nameof(IOrganizationService.RetrieveMultiple));

    /// <summary>成功與故障都先保存副作用判定，再確定性清空 SDK callback、集合與注入例外。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var noWrites = NoRealWrites();
        try { Sdk.Dispose(); }
        finally
        {
            rows.Clear(); Trace.Entries.Clear(); ReadFailure = null;
            context.InputParameters.Clear(); context.OutputParameters.Clear(); context.SharedVariables.Clear();
            context.PreEntityImages.Clear(); context.PostEntityImages.Clear();
        }
        Require(noWrites && Sdk.Calls.Count == 0 && Sdk.EntityCount == 0 && Sdk.QueryResultCount == 0 &&
            Sdk.AssociationCount == 0 && Sdk.WriteRecords.Count == 0 &&
            Sdk.RetrieveHandler is null && Sdk.RetrieveMultipleHandler is null && Sdk.CreateHandler is null &&
            Sdk.UpdateHandler is null && Sdk.DeleteHandler is null && Sdk.ExecuteHandler is null &&
            Sdk.AssociateHandler is null && Sdk.DisassociateHandler is null &&
            rows.Count == 0 && Trace.Entries.Count == 0, "fixture drained without real writes");
    }

    /// <summary>按明確欄位建立當次候選，共用 fake 會再深複製，不借出來源 Entity。</summary>
    private static Entity Project(Entity row, ColumnSet columns)
    {
        var result = new Entity(row.LogicalName, row.Id);
        foreach (var column in columns.Columns)
            if (row.Attributes.TryGetValue(column, out var value)) result[column] = value;
        return result;
    }
    /// <summary>所有真寫入均立即拒絕，不保存 request 或嘗試網路。</summary>
    private static InvalidOperationException UnexpectedWrite() => new("QR fixture attempted a real SDK write.");
    /// <summary>受限域不使用 xUnit；只回報固定契約名稱，不包括來源內容或機密。</summary>
    internal static void Require(bool condition, string contract)
    { if (!condition) throw new InvalidOperationException("QR fixture mismatch: " + contract); }

    /// <summary>reading 的十八固定及二十動態映射，獨立於產品私有陣列且每次產生獨占集合。</summary>
    private static (string Source, string Target)[] Pairs()
    {
        var result = new List<(string, string)>
        {
            ("new_sunday_first_qr_on_time", "new_sunday_first_qr_on_number"),
            ("new_sunday_second_qr_on_time", "new_sunday_second_qr_on_number"),
            ("new_saturday_worship_on_time", "new_saturday_worship_on_number"),
            ("new_yongmen_on_time", "new_yongmen_on_number"),
            ("new_prayer_meeting_on_time", "new_prayer_meeting_qr_on_number"),
            ("new_child_on_time", "new_child_on_number"),
            ("new_big_disciple_on_time", "new_big_disciple_qr_on_number"),
            ("new_leadership_small_lecture_on_time", "new_leadership_small_lecture_qr_on_number"),
            ("new_leaders_gather_on_time", "new_leaders_gather_qr_on_number"),
            ("new_sunday_first_qr_off", "new_sunday_first_qr_off_number"),
            ("new_sunday_second_qr_off_time", "new_sunday_second_qr_off_number"),
            ("new_saturday_worship_off_time", "new_saturday_worship_off_number"),
            ("new_yongmen_off_time", "new_yongmen_off_number"),
            ("new_prayer_meeting_off_time", "new_prayer_meeting_qr_off_number"),
            ("new_child_off_time", "new_child_off_number"),
            ("new_big_disciple_off_time", "new_big_disciple_qr_off_number"),
            ("new_leadership_small_lecture_off_time", "new_leadership_small_lecture_qr_off_number"),
            ("new_leaders_gather_off_time", "new_leaders_gather_qr_off_number")
        };
        for (var i = 1; i <= 10; i++)
        {
            var prefix = "new_" + i.ToString(CultureInfo.InvariantCulture) + "_sign_";
            result.Add((prefix + "on_time", prefix + "on_number"));
            result.Add((prefix + "off_time", prefix + "off_number"));
        }
        return result.ToArray();
    }

    /// <summary>手寫工廠只借用既有 SDK，以 UserId 精確比對，不能用 null 或 initiating user 提權。</summary>
    private sealed class Factory(IOrganizationService service, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>單案借用數，非跨活動累積診斷。</summary>
        internal int Calls { get; private set; }
        /// <summary>前一輪 Invoke 已同步排空後才允許測試重新計數，不給活動存取此重設入口。</summary>
        internal void Reset() => Calls = 0;
        /// <summary>只借用一次且不轉移 SDK owner；驗證失敗立即拒絕。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId)
        {
            Require(userId == user && ++Calls == 1, "single platform UserId service lease");
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
        /// <summary>刻意與執行使用者不同，工廠只能接受當次 UserId，不得改用此值提權。</summary>
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

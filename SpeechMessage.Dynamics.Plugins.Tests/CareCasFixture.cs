/*
 * 檔案整體說明
 * 目的與定位：P5-10 兩個正式 Care 活動的離線真 WF 執行器，以手寫 context／工廠與
 * 合成 SDK 保護生日、年資及新朋友 Create 分組接線；與 QR fixture 沿用相同平台委派模式。
 * 主要流程：每案建立三列正式 GUID 來源，嚴格限制欄位投影及最多十六次讀取，Shadow
 * 須完整產生單一 contact patch 與 end 記錄，無 overflow、無真寫入；不使用 Moq、網路
 * 或舊 DLL。生日採未知 sentinel 固定兩欄；年資採 2000-01-01，日期以 Invoke 前後教會
 * 日期夾住，驗證完整欄位及獨立天數，不另寫產品級距演算法。UserLocal／DateOnly
 * 寫入須明確為午夜 UTC，不以未標時區的本地日期字串冒充正確 SDK 日期。
 * 資源與隔離：fixture 是資料、回呼、SDK 與追蹤唯一 owner，using 在同步 Invoke 排空後
 * 清除所有集合、故障與 callback；SDK 僅借給活動，活動不可 Dispose 它。每次工廠只允許
 * 平台 UserId 一次借用，A/B/A 分案保有不同會友、教會及使用者。十秒等待預算不宣稱
 * 能中斷同步 SDK；父 CAS 測試 finally 卸載子域。本檔無 Session、秘密或背景資源，
 * 只證明離線平台協調與清理，不宣稱 Dataverse 實機或長時間 soak 結果。
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
using AnniversaryActivity = Workflows::SpeechMessage.Dynamics.Workflows.Care.ContactAnniversaryActivity;
using NewPersonActivity = Workflows::SpeechMessage.Dynamics.Workflows.Care.NewPersonCareActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>每案三列來源及單次 SDK 借用的 owner，成功或故障都須確定排空。</summary>
internal sealed class CareCasFixture : IDisposable
{
    /// <summary>只接受固定測試入口，不能從活動參數選任意型別。</summary>
    private readonly string entrypoint;
    /// <summary>當案權威來源，只由明確 GUID 查詢，不依名稱去重或共用。</summary>
    private readonly Dictionary<Guid, Entity> rows = new();
    /// <summary>平台值唯一 owner，Dispose 清除參數及映像。</summary>
    private readonly RemoteExecutionContext context;
    /// <summary>驗證一次 UserId 借用，不轉移 SDK 生命週期。</summary>
    private readonly Factory factory;
    /// <summary>阻止清理後重新執行。</summary>
    private bool disposed;
    /// <summary>當次 Invoke 前教會日期，供跨午夜有界驗證。</summary>
    private DateTime before;
    /// <summary>當次 Invoke 後教會日期，容許剛好跨午夜。</summary>
    private DateTime after;
    /// <summary>本案平台 contact 正式身分，非 caller 的 InputParameter。</summary>
    internal Guid RecordId { get; } = Guid.NewGuid();
    /// <summary>本案 contact 的正式父教會。</summary>
    internal Guid ChurchId { get; } = Guid.NewGuid();
    /// <summary>只讀借用服務；所有真寫入回呼立即拒絕。</summary>
    internal FakeOrganizationService Sdk { get; } = new();
    /// <summary>當案有界合成追蹤，由本 owner 清理。</summary>
    internal FakeTracingService Trace { get; } = new();
    /// <summary>只注入讀取原例外，驗證平台不包裝或洩漏內容。</summary>
    internal Exception? ReadFailure { get; set; }
    /// <summary>含 CRLF 的實測追蹤長度，不能超出平台八千字預算。</summary>
    internal int TraceCharacters => Trace.Entries.Sum(line => line.Length + 2);

    /// <summary>建立固定日期及正式 lookup；新朋友預設用 Create，避免混入尚在整合的出席分支。</summary>
    internal CareCasFixture(string entrypoint, int depth = 1, string? message = null, string table = "contact")
    {
        Require(entrypoint is "birthday" or "onboarding" or "newperson", "known care entrypoint");
        this.entrypoint = entrypoint;
        var listId = Guid.NewGuid();
        context = new RemoteExecutionContext
        {
            Depth = depth, MessageName = message ?? (entrypoint == "newperson" ? "Create" : "ExecuteWorkflow"),
            Stage = 40, Mode = 0, IsolationMode = 2, PrimaryEntityName = table, PrimaryEntityId = RecordId,
            UserId = Guid.NewGuid(), InitiatingUserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
            OrganizationName = "synthetic-care", CorrelationId = Guid.NewGuid()
        };
        factory = new Factory(Sdk, context.UserId);
        rows.Add(RecordId, new Entity("contact", RecordId)
        {
            ["parentcustomerid"] = new EntityReference("account", ChurchId),
            ["birthdate"] = new DateTime(1000, 1, 1), ["new_onboard_date"] = new DateTime(2000, 1, 1),
            ["new_cell_list_contact"] = new EntityReference("list", listId), ["new_small_group_log"] = ""
        });
        rows.Add(ChurchId, new Entity("account", ChurchId) { ["new_timezone"] = "Asia/Taipei" });
        rows.Add(listId, new Entity("list", listId) { ["listname"] = "合成關懷小組" });
        Sdk.RetrieveHandler = (name, id, columns) =>
        {
            if (ReadFailure is not null) throw ReadFailure;
            Require(Sdk.Calls[nameof(IOrganizationService.Retrieve)] <= 16 && !columns.AllColumns &&
                rows.ContainsKey(id) && rows[id].LogicalName == name, "bounded exact retrieve");
            var allowed = name switch
            {
                "account" => new[] { "new_timezone" },
                "list" => new[] { "listname" },
                _ => new[] { "birthdate", "new_onboard_date", "new_next_update_onboard_date", "parentcustomerid",
                    "new_next_birthdate", "new_birthday_month", "new_age_number", "new_age_level",
                    "new_on_board_year", "new_special_leave_days", "new_cell_list_contact", "new_small_group_log",
                    "new_care_date", "new_assigne_group_date" }
            };
            Require(columns.Columns.All(column => allowed.Contains(column, StringComparer.Ordinal)), "only approved columns, no tokens");
            return Project(rows[id], columns);
        };
        Sdk.RetrieveMultipleHandler = _ => throw new InvalidOperationException("Unexpected Care query.");
        Sdk.CreateHandler = _ => throw UnexpectedWrite();
        Sdk.UpdateHandler = _ => throw UnexpectedWrite();
        Sdk.DeleteHandler = (_, _) => throw UnexpectedWrite();
        Sdk.ExecuteHandler = _ => throw UnexpectedWrite();
        Sdk.AssociateHandler = (_, _, _, _) => throw UnexpectedWrite();
        Sdk.DisassociateHandler = (_, _, _, _) => throw UnexpectedWrite();
    }

    /// <summary>直接建立兩個正式活動，子域不使用反射或動態代理。</summary>
    internal static CodeActivity CreateActivity(string entrypoint) => entrypoint switch
    {
        "birthday" or "onboarding" => new AnniversaryActivity(),
        "newperson" => new NewPersonActivity(),
        _ => throw new ArgumentException("Unknown care entrypoint.", nameof(entrypoint))
    };

    /// <summary>以真 WF 在本次引數字典求值；不把種類或來源保存到活動實例。</summary>
    internal IDictionary<string, object> Invoke(Activity activity, string? kind = null, bool omitKind = false)
    {
        Require(!disposed, "active fixture");
        var invoker = new WorkflowInvoker(activity);
        invoker.Extensions.Add<IWorkflowContext>(() => new WorkflowContext(context));
        invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory);
        invoker.Extensions.Add<ITracingService>(() => Trace);
        var inputs = new Dictionary<string, object> { ["ExecutionMode"] = "Shadow" };
        if (entrypoint == "newperson") inputs["InputParameter"] = "caller-is-not-authority";
        else if (!omitKind) inputs["AnniversaryKind"] = kind ?? (entrypoint == "birthday" ? "Birthday" : "Onboarding");
        before = DateTime.UtcNow.AddHours(8).Date;
        try { return invoker.Invoke(inputs, TimeSpan.FromSeconds(10)); }
        finally { after = DateTime.UtcNow.AddHours(8).Date; inputs.Clear(); }
    }

    /// <summary>驗證完整單筆 patch 或零意圖結尾，零真寫入與八千字預算都必須成立。</summary>
    internal JObject Verify(bool noOperation = false)
    {
        var lines = Trace.Entries.Where(line => line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(line => RecordingOrganizationService.ReadProjection(line.Substring("SMV-SHADOW|1|".Length))).ToArray();
        Require(lines.Length == (noOperation ? 1 : 2) && (bool?)lines.Last()["end"] == true &&
            (int?)lines.Last()["count"] == (noOperation ? 0 : 1) && lines.All(line => line["overflow"] is null) &&
            TraceCharacters <= 8000 && factory.Calls == 1 && NoRealWrites(), "complete bounded projection and borrowed SDK");
        if (noOperation) { Require(Sdk.Calls.Count == 0, "unsupported trigger has no SDK reads"); return lines[0]; }
        var intent = lines[0];
        Require((int?)intent["seq"] == 1 && (string?)intent["operation"] == "Update" &&
            (string?)intent["entity"] == "contact" && (string?)intent["id"] == RecordId.ToString("D"), "single authoritative contact patch");
        var fields = (JObject)intent["fields"]!;
        JObject expected;
        if (entrypoint == "birthday") expected = new JObject { ["new_birthday_month"] = Hash("100000012"), ["new_age_level"] = 100000011 };
        else if (entrypoint == "onboarding")
        {
            var next = ReadDate(fields["new_next_update_onboard_date"]!);
            var today = next.AddDays(-1);
            Require(today >= before && today <= after, "onboarding uses church date within invocation");
            expected = new JObject { ["new_on_board_year"] = (today - new DateTime(2000, 1, 1)).Days,
                ["new_special_leave_days"] = 30, ["new_next_update_onboard_date"] = fields["new_next_update_onboard_date"]!.DeepClone() };
        }
        else
        {
            var today = ReadDate(fields["new_care_date"]!);
            Require(today >= before && today <= after, "assignment uses church date within invocation");
            var log = today.ToShortDateString() + ",合成關懷小組" + Environment.NewLine;
            expected = new JObject { ["new_care_date"] = fields["new_care_date"]!.DeepClone(),
                ["new_small_group_log"] = log };
        }
        Require(JToken.DeepEquals(expected, fields), "exact full field projection, no unrelated anniversary writes");
        Require(Sdk.Calls.Count == 1 && Sdk.Calls[nameof(IOrganizationService.Retrieve)] == (entrypoint == "newperson" ? 3 : 2),
            "exact authoritative reads only");
        return intent;
    }

    /// <summary>獨立驗證台北午夜為前日 16:00 UTC，再取教會日期，不呼叫產品日期轉換自比。</summary>
    private static DateTime ReadDate(JToken token)
    {
        var value = DateTime.Parse((string)token!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        Require(value.Kind == DateTimeKind.Utc && value.TimeOfDay == TimeSpan.FromHours(16),
            "UserLocal DateOnly is serialized as church midnight UTC");
        return value.AddHours(8).Date;
    }
    /// <summary>A/B/A 排空並驗過零真寫入後才重設單次計數，不修改權威資料或活動。</summary>
    internal void ResetInvocationLease()
    {
        Require(NoRealWrites(), "no real writes before reset");
        factory.Reset(); Sdk.Calls.Clear();
    }
    /// <summary>獨立重建 birthday 欄位的個資摘要；保守 birth 名稱政策會連選項值也做雜湊。</summary>
    private static JObject Hash(string value)
    {
        using var sha = SHA256.Create();
        var hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(item => item.ToString("x2", CultureInfo.InvariantCulture)));
        return new JObject { ["length"] = value.Length, ["sha256"] = hash };
    }
    /// <summary>連失敗的真寫入嘗試都算違規，不能只驗成功 audit。</summary>
    private bool NoRealWrites() => Sdk.WriteRecords.Count == 0 && Sdk.EntityCount == 0 && Sdk.AssociationCount == 0 &&
        Sdk.Calls.All(call => call.Key == nameof(IOrganizationService.Retrieve));

    /// <summary>先保存真寫入判定，再無條件清空所有資料與回呼，最後驗證零基線。</summary>
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
            Sdk.AssociationCount == 0 && Sdk.WriteRecords.Count == 0 && Sdk.RetrieveHandler is null &&
            Sdk.RetrieveMultipleHandler is null && Sdk.CreateHandler is null && Sdk.UpdateHandler is null &&
            Sdk.DeleteHandler is null && Sdk.ExecuteHandler is null && Sdk.AssociateHandler is null &&
            Sdk.DisassociateHandler is null && rows.Count == 0 && Trace.Entries.Count == 0, "fixture drained without real writes");
    }

    /// <summary>按明確欄位建立獨立投影，共用 fake 會再深複製可變 SDK 值。</summary>
    private static Entity Project(Entity row, ColumnSet columns)
    {
        var result = new Entity(row.LogicalName, row.Id);
        foreach (var column in columns.Columns) if (row.Attributes.TryGetValue(column, out var value)) result[column] = value;
        return result;
    }
    /// <summary>所有真寫入立即拒絕，讓 Shadow 偶發穿透也能被偵測。</summary>
    private static InvalidOperationException UnexpectedWrite() => new("Care fixture attempted a real SDK write.");
    /// <summary>CAS 子域不依賴 xUnit，只傳固定契約名稱回父域診斷。</summary>
    internal static void Require(bool condition, string contract)
    { if (!condition) throw new InvalidOperationException("Care fixture mismatch: " + contract); }

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

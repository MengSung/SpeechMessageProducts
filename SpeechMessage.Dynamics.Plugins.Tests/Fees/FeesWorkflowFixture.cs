/*
 * 檔案整體說明
 * 目的與定位：P5-11 真 FeeNotifyActivity 的離線 WF 與 CAS 執行 owner，以 Core
 * 同份情境／期望保護實際外殼接線，不用動態代理或純方法結果代替平台入口。
 * 主要流程：手寫平台 context 與使用者限定工廠，真 WorkflowInvoker 求值 Shadow；
 * 解析完整安全追蹤並核對 seq、正式 contact／fee ID、訊息數、內容摘要與 end，
 * 拒絕 overflow、額外欄位、原文洩漏或真寫入。故障仍必須輸出零意圖結尾。
 * 使用邊界與維護：單案 owns SDK、trace 與 context 集合，同步 Invoke 排空後
 * Dispose 清除所有資料；活動只借用服務，重用活動不保留前案資料。無網路、
 * token、Session、Timer 或背景資源；CAS 子域與父域只交換固定結果，不交換
 * SDK 或活動。不宣稱實機追蹤可用或 LINE 送達。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessageProducts.GoodShepherd.Core.Tests;
using FeeNotifyActivity = Workflows::SpeechMessage.Dynamics.Workflows.Fees.FeeNotifyActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests.Fees;

/// <summary>供全信任測試與受限子域共用的單案平台 owner，不使用 xUnit 或 Moq。</summary>
internal sealed class FeesWorkflowFixture : IDisposable
{
    private readonly RemoteExecutionContext context;
    private readonly Factory factory;
    /// <summary>本案正式 SDK 來源與資料 owner，不提供任何網路能力。</summary>
    internal FeesGoldenFixture Sdk { get; }
    /// <summary>只保存當案安全追蹤，Dispose 時清除。</summary>
    internal FakeTracingService Trace { get; } = new();

    /// <summary>平台身分刻意分離 UserId 與 InitiatingUserId；深度不虛構舊活動未有的早退。</summary>
    internal FeesWorkflowFixture(JToken scenario, int depth = 1)
    {
        Sdk = new FeesGoldenFixture(scenario);
        context = new RemoteExecutionContext
        {
            MessageName = Sdk.Message, PrimaryEntityName = "new_fee",
            PrimaryEntityId = FeesGoldenFixture.Id("record"), OrganizationId = Guid.NewGuid(),
            UserId = Guid.NewGuid(), InitiatingUserId = Guid.NewGuid(), CorrelationId = Guid.NewGuid(),
            Depth = depth, Mode = 0, OrganizationName = "synthetic-fees"
        };
        factory = new Factory(Sdk, context.UserId);
    }

    /// <summary>同一活動可反覆借用，但每次 Invoke 的資料及輸入字典皆獨立，十秒內同步排空。</summary>
    internal void Invoke(FeeNotifyActivity activity, string mode = "Shadow")
    {
        var invoker = new WorkflowInvoker(activity);
        invoker.Extensions.Add<IWorkflowContext>(() => new FeeContext(context));
        invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory);
        invoker.Extensions.Add<ITracingService>(() => Trace);
        var arguments = new Dictionary<string, object> { ["InputParameter"] = "", ["ExecutionMode"] = mode };
        try { invoker.Invoke(arguments, TimeSpan.FromSeconds(10)); }
        finally { arguments.Clear(); }
    }

    /// <summary>完整逐欄核對安全追蹤，包含零意圖情境及原例外收尾，不把摘要當成實機成功。</summary>
    internal void Verify(JToken contract)
    {
        var prefix = "SMV-SHADOW|1|";
        var lines = Trace.Entries.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => JObject.Parse(line.Substring(prefix.Length))).ToArray();
        var expected = (JArray)contract["lineCalls"]!;
        FeesGoldenFixture.Require(lines.Length == expected.Count + 1 && factory.Calls == 1 &&
            Sdk.WriteAttempts == 0 && Trace.Entries.Sum(line => line.Length + 2) <= 8000, "bounded complete shell trace");
        FeesGoldenFixture.Require(JToken.DeepEquals(lines.Last(),
            new JObject { ["end"] = true, ["count"] = expected.Count }), "exact end, no overflow");
        if (expected.Count == 1)
        {
            var call = expected[0];
            var wanted = new JObject
            {
                ["seq"] = 1, ["operation"] = "Push", ["contactId"] = FeesGoldenFixture.Id("member").ToString("D"),
                ["templateKey"] = call["shadow"]!["templateKey"]!.DeepClone(),
                ["retryKey"] = FeesGoldenFixture.Id("record").ToString("D"),
                ["messageCount"] = call["messageCount"]!.DeepClone(),
                ["contentSha256"] = call["shadow"]!["contentSha256"]!.DeepClone()
            };
            FeesGoldenFixture.Require(JToken.DeepEquals(wanted, lines[0]), "exact shared reading intent");
        }
        FeesGoldenFixture.Require(Trace.Entries.All(line => !line.Contains("合成會友") &&
            !line.Contains("U" + new string('a', 32)) && !line.Contains("Synthetic private fee failure.")), "no personal data or raw errors");
    }

    /// <summary>Invoke 已排空後才清除 owner 狀態；平台借用不改變 SDK 的所有權。</summary>
    public void Dispose()
    {
        Sdk.Dispose(); Trace.Entries.Clear();
        context.InputParameters.Clear(); context.OutputParameters.Clear(); context.SharedVariables.Clear();
        context.PreEntityImages.Clear(); context.PostEntityImages.Clear();
    }

    /// <summary>只允許當案 UserId 借用一次；不以 null 或起始使用者提權。</summary>
    private sealed class Factory(IOrganizationService sdk, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>單案借用數，無 static 或其他案例參照。</summary>
        internal int Calls { get; private set; }
        /// <summary>回傳借用服務，生命週期仍由 fixture using 擁有。</summary>
        public IOrganizationService CreateOrganizationService(Guid? id)
        {
            FeesGoldenFixture.Require(id == user && ++Calls == 1, "platform UserId lease");
            return sdk;
        }
    }

    /// <summary>手寫 WF 平台純值委派，無動態代理、授權推導或外部資源。</summary>
    private sealed class FeeContext(RemoteExecutionContext value) : IWorkflowContext
    {
        /// <summary>固定合成階段，非實機註冊證據。</summary>
        public string StageName => "PostOperation";
        /// <summary>固定活動分類，不保存流程引擎。</summary>
        public int WorkflowCategory => 0;
        /// <summary>同步測試由 Invoke 排空。</summary>
        public int WorkflowMode => 0;
        /// <summary>沒有父活動，不保留其他執行範圍。</summary>
        public IWorkflowContext ParentContext => null!;
        /// <summary>當案平台模式。</summary>
        public int Mode => value.Mode;
        /// <summary>固定沙箱代號；實際權限另由 CAS 測試限制。</summary>
        public int IsolationMode => 2;
        /// <summary>只屬本次活動的深度。</summary>
        public int Depth => value.Depth;
        /// <summary>平台訊息，不由輸入字串提供。</summary>
        public string MessageName => value.MessageName;
        /// <summary>當案正式來源表。</summary>
        public string PrimaryEntityName => value.PrimaryEntityName;
        /// <summary>當案可空請求識別。</summary>
        public Guid? RequestId => value.RequestId;
        /// <summary>當案次要表。</summary>
        public string SecondaryEntityName => value.SecondaryEntityName;
        /// <summary>當案輸入集合，Dispose 清除。</summary>
        public ParameterCollection InputParameters => value.InputParameters;
        /// <summary>當案輸出集合，不跨案例共享。</summary>
        public ParameterCollection OutputParameters => value.OutputParameters;
        /// <summary>當案平台共享集合，不存入 static。</summary>
        public ParameterCollection SharedVariables => value.SharedVariables;
        /// <summary>本次服務借用唯一接受的使用者。</summary>
        public Guid UserId => value.UserId;
        /// <summary>刻意不同的起始使用者，禁止用來提升權限。</summary>
        public Guid InitiatingUserId => value.InitiatingUserId;
        /// <summary>當案平台單位值。</summary>
        public Guid BusinessUnitId => value.BusinessUnitId;
        /// <summary>合成組織正式識別。</summary>
        public Guid OrganizationId => value.OrganizationId;
        /// <summary>固定合成名稱，不作授權來源。</summary>
        public string OrganizationName => value.OrganizationName;
        /// <summary>本案 fee 正式合成 ID。</summary>
        public Guid PrimaryEntityId => value.PrimaryEntityId;
        /// <summary>當案前像，owner 清除。</summary>
        public EntityImageCollection PreEntityImages => value.PreEntityImages;
        /// <summary>當案後像，不跨執行保留。</summary>
        public EntityImageCollection PostEntityImages => value.PostEntityImages;
        /// <summary>當案擴充參照，不讀實機。</summary>
        public EntityReference OwningExtension => value.OwningExtension;
        /// <summary>當案安全診斷識別。</summary>
        public Guid CorrelationId => value.CorrelationId;
        /// <summary>不模擬離線同步。</summary>
        public bool IsExecutingOffline => false;
        /// <summary>不模擬離線重播。</summary>
        public bool IsOfflinePlayback => false;
        /// <summary>假服務不宣稱資料庫交易。</summary>
        public bool IsInTransaction => false;
        /// <summary>當案業務操作識別。</summary>
        public Guid OperationId => value.OperationId;
        /// <summary>當案平台時間，不借用執行主機時區。</summary>
        public DateTime OperationCreatedOn => value.OperationCreatedOn;
    }
}

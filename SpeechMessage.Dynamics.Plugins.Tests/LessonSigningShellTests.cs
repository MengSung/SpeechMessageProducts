/*
 * 檔案整體說明
 * 目的與定位：P5-07 簽核 Plugin 與真正 WF 活動的離線回歸，保護舊版訊息／參數
 * 分派、正式學員與組長身分、跨教會拒絕，以及安全影子意圖的完整發布。
 * 主要功能與流程：每案建立獨立 SDK、平台 context、學員與不同 ID 的組長，透過
 * WorkflowInvoker 求值真活動引數，或執行 Plugin。以影子 contactId 判定實際收件者，
 * 注入付款分支需單筆核定缺口標記；Live普通通知只入列，付款拒絕且零讀寫；跨教會必須零通知，只讀本工作樹固定 reading 基準，不連 D365 或 LINE。
 * 使用邊界與維護：fixture 唯一擁有合成列、mock 與追蹤，同步呼叫返回後 Dispose
 * 清空所有集合及捕捉參照。參數不提供教會授權，來源列仍依平台使用者服務重讀；
 * 不保存 Session、credential、背景 Task 或 timer。這些測試不是實機送達證據。
 */
extern alias Workflows;
using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Lessons;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;
using SigningActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.StorLessonSigningActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>以真外殼及不同正式收件者驗證分派；每個測試自行釋放所有合成狀態。</summary>
public sealed class LessonSigningShellTests
{
    /// <summary>
    /// 逐案讀取獨立手寫 reading 期望；六案真活動驗證分派、輸出與付款／跨教會故障，
    /// 不由產品結果產生期望，不把安全摘要比對當作完整 Flex 等價或實機送達。
    /// </summary>
    [Fact]
    public void Lessons_Signing_ReadingBaseline_MatchesExpected()
    {
        var scenarios = ReadGolden("scenarios")["cases"]!.Children<JObject>().ToArray();
        var baseline = ReadGolden("expected");
        Assert.Equal("reading", (string?)baseline["basis"]);
        Assert.NotEmpty(baseline["declaredDifferences"]!);
        var expected = baseline["cases"]!.Children<JObject>().ToArray();
        Assert.Equal(scenarios.Length, expected.Length);
        for (var i = 0; i < scenarios.Length; i++)
        {
            using var fixture = new Fixture();
            var scenario = scenarios[i]; var result = expected[i];
            Assert.Equal((string?)scenario["name"], (string?)result["name"]);
            fixture.Seed(scenario["seed"]!);
            var trigger = scenario["trigger"]!;
            IDictionary<string, object>? outputs = null;
            Exception? failure = null;
            try
            {
                outputs = fixture.Workflow(new SigningActivity(), (string)trigger["message"]!,
                    (string)trigger["inputs"]!["InputParameter"]!);
            }
            catch (Exception exception) { failure = exception.GetBaseException(); }
            if ((string?)scenario["name"] == "workflow-fee-not-implemented")
            {
                // 此列保留舊付款行為；新程式只驗核定缺口，絕不把缺功能算成reading相等。
                Assert.Null(failure);
                Assert.Contains(result["source"]!.Children<JObject>(), source =>
                    (string?)source["file"] == "QPayProcessor.cs");
                Assert.DoesNotContain(baseline["declaredDifferences"]!.Children<JObject>(), difference =>
                    (string?)difference["case"] == "workflow-fee-not-implemented");
                fixture.AssertApprovedGap();
                continue;
            }
            Assert.Equal((string?)result["exception"], failure?.GetType().Name);
            var calls = new JArray(fixture.Pushes().Select(push => new JObject
            {
                ["contactAlias"] = (string?)push["contactId"] == fixture.MemberId.ToString("D") ? "member" :
                    (string?)push["contactId"] == fixture.LeaderId.ToString("D") ? "leader" : "invalid-recipient",
                ["templateKey"] = push["templateKey"], ["messageCount"] = push["messageCount"]
            }));
            Assert.True(JToken.DeepEquals(result["lineCalls"], calls), "Signing dispatch differs from reading baseline.");
            if (failure is null)
            {
                Assert.NotNull(outputs);
                Assert.Equal((int)result["outputs"]!["OutputResult"]!, (int)outputs["OutputResult"]);
            }
            fixture.Sdk.Verify(x => x.Create(It.IsAny<Entity>()), Times.Never);
            fixture.Sdk.Verify(x => x.Update(It.IsAny<Entity>()), Times.Never);
        }
    }

    /// <summary>
    /// 只在目前工作樹方案根尋找固定兩份 JSON；至多上溯十二層且在第一個方案根停止。
    /// 單檔最多 1MiB、深度32、嚴格UTF-8；stream／reader 由本次方法逆序 using 釋放。
    /// </summary>
    private static JObject ReadGolden(string kind)
    {
        if (kind is not ("scenarios" or "expected")) throw new ArgumentException("Unknown signing fixture.", nameof(kind));
        var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (var depth = 0; depth < 12 && directory is not null; depth++, directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "SpeechMessageProducts.sln"))) continue;
            var path = Path.Combine(directory.FullName, "SpeechMessageProducts.GoodShepherd.Core.Tests",
                "Golden", "Legacy", "lessonssigning", kind + ".json");
            using var stream = File.OpenRead(path);
            if (stream.Length > 1048576) throw new InvalidDataException("Signing fixture exceeds capacity.");
            using var text = new StreamReader(stream, new UTF8Encoding(false, true));
            using var reader = new JsonTextReader(text) { MaxDepth = 32, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
            return JObject.Load(reader);
        }
        throw new InvalidDataException("Signing fixture worktree root is unavailable.");
    }

    /// <summary>舊 Plugin 自行提供新增／更新參數，分別通知本人及不同 ID 的組長。</summary>
    [Theory]
    [InlineData("Create", false)]
    [InlineData("Update", true)]
    public void Lessons_Signing_ShadowPlugin_UsesVerifiedRecipient(string message, bool leader)
    {
        using var fixture = new Fixture();
        fixture.Plugin(message);
        fixture.AssertRecipient(leader ? fixture.LeaderId : fixture.MemberId);
    }

    /// <summary>真活動的參數需於本次 Invoke 求值；課程簽核優先，新增本人、更新組長。</summary>
    [Theory]
    [InlineData("ExecuteWorkflow", "課程簽核", true)]
    [InlineData("Create", "新增", false)]
    [InlineData("Update", "更新", true)]
    [InlineData("Create", "新增課程簽核", true)]
    [InlineData("Update", "更新課程簽核", true)]
    public void Lessons_Signing_WorkflowParameter_DispatchesLegacyRecipient(string message, string parameter, bool leader)
    {
        using var fixture = new Fixture();
        fixture.Workflow(new SigningActivity(), message, parameter);
        fixture.AssertRecipient(leader ? fixture.LeaderId : fixture.MemberId);
    }

    /// <summary>舊付款fallback在Shadow只記一筆核定缺口；零查詢、零付款／通知，不能冒充reading差異零。</summary>
    [Theory]
    [InlineData("ExecuteWorkflow", "付款")]
    [InlineData("ExecuteWorkflow", "新增")]
    [InlineData("Create", "更新")]
    [InlineData("Update", "新增")]
    [InlineData("Update", "未知")]
    [InlineData("Create", "")]
    [InlineData("ExecuteWorkflow", "")]
    public void Lessons_Signing_UnsupportedFeeBranch_RecordsApprovedGap(string message, string parameter)
    {
        using var fixture = new Fixture();
        fixture.Workflow(new SigningActivity(), message, parameter);
        fixture.AssertApprovedGap();
    }

    /// <summary>來源參數僅為相容參考，舊版未用它；錯配正式記錄應拒絕且不跨列查詢。</summary>
    [Fact]
    public void Lessons_Signing_WorkflowTargetMismatch_RejectsBeforeReads()
    {
        using var fixture = new Fixture();
        Assert.ThrowsAny<Exception>(() => fixture.Workflow(new SigningActivity(), "Create", "新增",
            target: new EntityReference("new_stor_lessons", Guid.NewGuid())));
        Assert.Empty(fixture.Pushes());
        fixture.Sdk.Verify(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
    }

    /// <summary>舊活動來源參考不是必要參數；省略仍只使用平台主要記錄，不擅自產生替代 ID。</summary>
    [Fact]
    public void Lessons_Signing_OptionalSourceReference_UsesPlatformIdentity()
    {
        using var fixture = new Fixture();
        fixture.Workflow(new SigningActivity(), "Create", "新增", omitTarget: true);
        fixture.AssertRecipient(fixture.MemberId);
    }

    /// <summary>不同教會的組長必須在組訊息前拒絕；不能因 lookup 鏈合法而越過通知授權。</summary>
    [Fact]
    public void Lessons_Signing_CrossChurchLeader_DoesNotPublish()
    {
        using var fixture = new Fixture();
        fixture.Rows[fixture.LeaderId]["parentcustomerid"] = new EntityReference("account", Guid.NewGuid());
        fixture.Plugin("Update");
        Assert.Empty(fixture.Pushes());
    }

    /// <summary>同一活動重用時重新求值參數，A/B/A 不得沿用先前收件者或分派。</summary>
    [Fact]
    public void Lessons_Signing_WorkflowReuse_ReevaluatesArguments()
    {
        using var fixture = new Fixture();
        var activity = new SigningActivity();
        foreach (var parameter in new[] { "新增", "課程簽核", "新增" })
        {
            fixture.Trace.Entries.Clear();
            fixture.Workflow(activity, "Create", parameter);
            fixture.AssertRecipient(parameter == "新增" ? fixture.MemberId : fixture.LeaderId);
        }
    }

    /// <summary>正常簽核Live只入列，付款仍在查詢前明確拒絕，真例外經共用活動失敗邊界保留並留下安全型別供彙總告警。</summary>
    /// <param name="message">固定平台訊息，不涉及真D365。</param>
    /// <param name="parameter">涵蓋正常簽核與舊付款fallback的合成分派字串。</param>
    [Theory]
    [InlineData("Create", "新增")]
    [InlineData("Update", "更新")]
    [InlineData("ExecuteWorkflow", "課程簽核")]
    [InlineData("ExecuteWorkflow", "付款")]
    public void Lessons_Signing_WorkflowLiveWithoutKey_RejectsBeforeReads(string message, string parameter)
    {
        if (parameter != "付款")
        {
            RelayShellTests.RunActivity(1, "Live", message, parameter);
            return;
        }
        using var fixture = new Fixture();
        var failure = Assert.ThrowsAny<Exception>(() => fixture.Workflow(new SigningActivity(), message, parameter, mode: "Live"));
        var actual = Assert.IsType<InvalidPluginExecutionException>(failure.GetBaseException());
        Assert.Contains("P5-PROP-006", actual.Message, StringComparison.Ordinal);
        Assert.Contains(fixture.Trace.Entries, entry => entry == "ExceptionType=" + actual.GetType().FullName);
        Assert.DoesNotContain("未實作：P5-PROP-006", fixture.Trace.Entries);
        Assert.Empty(fixture.Pushes());
        fixture.Sdk.Verify(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
    }

    /// <summary>Live Plugin 未提供安全設定時查詢前拒絕；不能以空 key、來源列或固定 credential 回落。</summary>
    [Fact]
    public void Lessons_Signing_PluginLiveWithoutKey_RejectsBeforeReads()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidPluginExecutionException>(() => fixture.Plugin("Create", mode: "Live"));
        Assert.Empty(fixture.Pushes());
        fixture.Sdk.Verify(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()), Times.Never);
    }

    /// <summary>metadata 保留舊活動的目標表、必要字串與可省略來源參考，不增設必填條件。</summary>
    [Fact]
    public void Lessons_Signing_WorkflowMetadata_MatchesLegacy()
    {
        var property = typeof(SigningActivity).GetProperty("InputStorLesson")!;
        var target = Assert.Single(property.GetCustomAttributes(typeof(ReferenceTargetAttribute), false).Cast<ReferenceTargetAttribute>());
        Assert.Equal("new_stor_lessons", target.EntityName);
        Assert.Empty(property.GetCustomAttributes(typeof(RequiredArgumentAttribute), false));
    }

    /// <summary>所有 SDK、mock 與追蹤由單案 owner 持有，Dispose 解除捕捉參照以回到空基線。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>平台主要課程紀錄的正式合成 ID，不由活動參數改寫。</summary>
        public Guid StorId { get; } = Guid.NewGuid();
        /// <summary>學員正式合成 ID，與組長不同以揭露錯誤分派。</summary>
        public Guid MemberId { get; } = Guid.NewGuid();
        /// <summary>小組長正式合成 ID，依 list lookup 決定而非顯示名稱。</summary>
        public Guid LeaderId { get; } = Guid.NewGuid();
        /// <summary>本案例來源教會，收件者必須在同一正式邊界。</summary>
        private Guid ChurchId { get; } = Guid.NewGuid();
        /// <summary>本次課程正式合成 ID，僅由 stor lookup 取得。</summary>
        private Guid CourseId { get; } = Guid.NewGuid();
        /// <summary>合成主要小組 ID，建立 member 到 leader 的可驗證鏈。</summary>
        private Guid ListId { get; } = Guid.NewGuid();
        /// <summary>教會 channel 合成 ID，Shadow 僅需測試 envelope 不解密。</summary>
        private Guid ChannelId { get; } = Guid.NewGuid();
        /// <summary>單案可變來源集合，故障注入後仍由 Dispose 清空。</summary>
        public Dictionary<Guid, Entity> Rows { get; } = new();
        /// <summary>嚴格唯讀 SDK 替身，未宣告寫入一律失敗。</summary>
        public Mock<IOrganizationService> Sdk { get; } = new(MockBehavior.Strict);
        /// <summary>單案安全追蹤集合，Dispose 排空防止資料延留。</summary>
        public FakeTracingService Trace { get; } = new();
        /// <summary>本案例唯一 SDK factory，僅提供合成授權服務且在 Dispose 重設。</summary>
        private readonly Mock<IOrganizationServiceFactory> factory = new(MockBehavior.Strict);

        /// <summary>建立不同學員／組長 ID 的合成來源；不讀 row token，所有讀取均驗證要求欄位。</summary>
        public Fixture()
        {
            Rows.Add(StorId, new Entity("new_stor_lessons", StorId)
            {
                ["new_contact_new_stor_lessons"] = new EntityReference("contact", MemberId),
                ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", CourseId)
            });
            foreach (var id in new[] { MemberId, LeaderId })
                Rows.Add(id, new Entity("contact", id)
                {
                    ["fullname"] = id == MemberId ? "學員" : "組長",
                    ["new_cell_list_contact"] = new EntityReference("list", ListId),
                    ["parentcustomerid"] = new EntityReference("account", ChurchId),
                    ["new_equipment_status"] = "已完成",
                    ["new_lineid"] = "U00000000000000000000000000000000",
                    ["new_preferredlanguage"] = "zh-TW"
                });
            Rows.Add(ListId, new Entity("list", ListId) { ["new_contact_family_leader_list"] = new EntityReference("contact", LeaderId) });
            Rows.Add(CourseId, new Entity("new_disciple_lessons", CourseId)
            {
                ["new_name"] = "課程", ["new_picture_link"] = "https://image.example/course.png"
            });
            Rows.Add(ChurchId, new Entity("account", ChurchId)
            {
                ["name"] = "合成教會", ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW",
                ["new_linechannelid"] = new EntityReference("new_linechannel", ChannelId)
            });
            Rows.Add(ChannelId, new Entity("new_linechannel", ChannelId) { ["new_accesstokenencrypted"] = "synthetic-envelope" });
            Sdk.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
                .Returns<string, Guid, ColumnSet>((table, id, columns) =>
                {
                    Assert.False(columns.AllColumns);
                    Assert.DoesNotContain("new_channel_access_token", columns.Columns);
                    if (!Rows.TryGetValue(id, out var row) || row.LogicalName != table)
                        throw new InvalidOperationException("Unexpected retrieve.");
                    return row;
                });
            factory.Setup(x => x.CreateOrganizationService(It.IsAny<Guid>())).Returns(Sdk.Object);
        }

        /// <summary>只接受固定合成 alias，從基準重建每列來源；不沿用程式預設種子冒充讀取情境。</summary>
        public void Seed(JToken definitions)
        {
            var ids = new Dictionary<string, Guid>(StringComparer.Ordinal)
            {
                ["record"] = StorId, ["member"] = MemberId, ["leader"] = LeaderId, ["church-a"] = ChurchId,
                ["course"] = CourseId, ["list"] = ListId, ["channel"] = ChannelId, ["church-b"] = Guid.NewGuid()
            };
            Rows.Clear();
            foreach (var definition in definitions.Children<JObject>())
            {
                var row = new Entity((string)definition["entity"]!, ids[(string)definition["alias"]!]);
                foreach (var field in ((JObject)definition["fields"]!).Properties())
                    row[field.Name] = field.Value is JObject reference
                        ? new EntityReference((string)reference["entity"]!, ids[(string)reference["ref"]!])
                        : ((JValue)field.Value).Value;
                Rows.Add(row.Id, row);
            }
        }

        /// <summary>借用同一案的服務執行真 Plugin；來源識別完全由合成平台 context 提供。</summary>
        public void Plugin(string message, string mode = "Shadow")
        {
            var context = new RemoteExecutionContext
            {
                MessageName = message, PrimaryEntityName = "new_stor_lessons", PrimaryEntityId = StorId,
                Stage = 40, Mode = 1, Depth = 1, UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
                CorrelationId = Guid.NewGuid(), OrganizationName = "synthetic"
            };
            new StorLessonSigningPlugin("{\"mode\":\"" + mode + "\"}", "").Execute(
                new FakeServiceProvider().Add<IPluginExecutionContext>(context)
                    .Add<IOrganizationServiceFactory>(factory.Object).Add<ITracingService>(Trace));
        }

        /// <summary>真正 WF Invoke 每次重新求值引數，不把 context 放入產品欄位；十秒為呼叫等待預算。</summary>
        public IDictionary<string, object> Workflow(SigningActivity activity, string message, string parameter,
            string mode = "Shadow", EntityReference? target = null, bool omitTarget = false)
        {
            var context = new FakeWorkflowContext(messageName: message, primaryEntityName: "new_stor_lessons", primaryEntityId: StorId);
            try
            {
                var invoker = new WorkflowInvoker(activity);
                invoker.Extensions.Add<IWorkflowContext>(() => context.Object);
                invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory.Object);
                invoker.Extensions.Add<ITracingService>(() => Trace);
                var arguments = new Dictionary<string, object>
                {
                    ["InputParameter"] = parameter, ["ExecutionMode"] = mode
                };
                if (!omitTarget) arguments.Add("InputStorLesson", target ?? new EntityReference("new_stor_lessons", StorId));
                return invoker.Invoke(arguments, TimeSpan.FromSeconds(10));
            }
            finally { context.Mock.Reset(); }
        }

        /// <summary>只解析共用安全投影，不保留訊息、LINE ID 或 token 原文。</summary>
        public JObject[] Pushes() => Trace.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length)))
            .Where(x => (string?)x["templateKey"] == "Lessons.Signing").ToArray();

        /// <summary>付款核定缺口只發布固定標記一次及空意圖收尾，沒有SDK讀寫或真推播。</summary>
        public void AssertApprovedGap()
        {
            Assert.Equal("未實作：P5-PROP-006", Assert.Single(Trace.Entries.Where(entry => entry == "未實作：P5-PROP-006")));
            Assert.Empty(Pushes());
            Assert.Contains(Trace.Entries, entry => entry.IndexOf("\"end\":true,\"count\":0", StringComparison.Ordinal) >= 0);
            Sdk.VerifyNoOtherCalls();
        }

        /// <summary>以正式 contactId 決定性檢查分派與完整收尾，且不得寫入真服務。</summary>
        public void AssertRecipient(Guid recipient)
        {
            Assert.Equal(recipient.ToString("D"), (string?)Assert.Single(Pushes())["contactId"]);
            Assert.Contains(Trace.Entries, x => x.IndexOf("\"end\":true", StringComparison.Ordinal) >= 0);
            Sdk.Verify(x => x.Create(It.IsAny<Entity>()), Times.Never);
            Sdk.Verify(x => x.Update(It.IsAny<Entity>()), Times.Never);
        }

        /// <summary>同步工作已排空後清理所有案例狀態，不 Dispose 平台借用介面。</summary>
        public void Dispose() { Sdk.Reset(); factory.Reset(); Rows.Clear(); Trace.Entries.Clear(); }
    }
}

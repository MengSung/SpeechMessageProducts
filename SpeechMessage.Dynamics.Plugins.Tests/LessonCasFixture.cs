/*
 * 檔案整體說明
 *
 * 目的與定位：P5-07 部分信任測試專用的六案執行器，以正式課程 Plugin／Workflow
 * 加上既有手寫 FakeOrganizationService、provider 與 tracing 驗證真 Shadow 鏈。
 * 主要功能與流程：單案建立七筆合成來源，精確核對查詢身分與使用者；QR 沿用
 * reading 期望逐欄比較 31／32 張待辦，設備比較本人 E2 更新，簽核比較正式組長、
 * 重試鍵及完整 Flex／server sender 摘要。組長與學員使用不同 ID，避免錯收件者
 * 仍通過；簽核故意提供無效 envelope，任何誤走解密／Live 都不能產生預期意圖。
 * 進階以正式教會credit-score-v1設定執行同步Update，核對預估學分與分數完整寫入。
 * 資源與隔離：資料、回呼、context 只屬本次同步呼叫，最多七列、三十二次讀取、
 * 一頁課程；任何真寫入／Execute／關聯直接拒絕。每案 finally Dispose SDK 並清空
 * 追蹤與來源，再斷言服務及回呼回到零基線；父域另負責 finally 卸載整個子域。
 * 活動提供十秒 Invoke 等待預算，不能強制中斷同步程式；工作量依固定讀取上限
 * 約束。沒有自建 Task、timer、連線、Session、快取或秘密。
 * 使用邊界與維護：共用 fake 未登錄課程主鍵，因此使用明確讀取 callback，
 * 沿用其深層複製，絕不猜主鍵或放寬共用 fake。只驗正常離線案例，不代表實機
 * 授權、並行唯一性、故障復原或 soak 結果；子域不使用 xUnit 或 Moq。
 */
extern alias Workflows;

using System;
using System.Activities;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using Line.Messaging;
using LineMessagingProcessor.Core;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk.Workflow;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Infrastructure;
using SpeechMessage.Dynamics.Plugins.Lessons;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using SpeechMessageProducts.GoodShepherd.Core.Rules;
using SpeechMessageProducts.GoodShepherd.Core.Tests;
using EquipmentActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.EquipmentStatusActivity;
using QrActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.LessonQrCodeActivity;
using SigningActivity = Workflows::SpeechMessage.Dynamics.Workflows.Lessons.StorLessonSigningActivity;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>單次同步驗證器，只持方法區域資料；所有契約失敗使用一般例外回報。</summary>
internal static class LessonCasFixture
{
    /// <summary>
    /// 以固定有限來源直接跑正式入口，完整驗意圖後才返回；服務及來源即使規則、
    /// 序列化或斷言失敗也確定性清除，不把子域卸載當作唯一清理方式。
    /// </summary>
    internal static void Run(string entrypoint)
    {
        Require(entrypoint is "LessonLinkPlugin" or "StorLessonSigningPlugin" or
            "LessonQrCodeActivity" or "EquipmentStatusActivity" or "StorLessonSigningActivity" or
            "LessonProgressionPlugin", "known entrypoint");
        var rows = Seed();
        var sdk = new FakeOrganizationService();
        var trace = new FakeTracingService();
        var context = new RemoteExecutionContext
        {
            Depth = 1, Stage = 40, Mode = entrypoint == "LessonProgressionPlugin" ? 0 : 1, IsolationMode = 2,
            MessageName = entrypoint == "LessonLinkPlugin" ? "Create" : "Update",
            PrimaryEntityName = entrypoint is "LessonLinkPlugin" or "LessonQrCodeActivity" ? "new_disciple_lessons" : "new_stor_lessons",
            PrimaryEntityId = Id(entrypoint is "LessonLinkPlugin" or "LessonQrCodeActivity" ? "lesson" : "record"),
            UserId = Id("user"), InitiatingUserId = Id("user"), OrganizationId = Id("organization"),
            OrganizationName = "synthetic-lessons", CorrelationId = Id("event")
        };
        Exception? executionFailure = null;
        Exception? cleanupFailure = null;
        try
        {
            sdk.RetrieveHandler = (table, id, columns) =>
            {
                Require(sdk.Calls[nameof(IOrganizationService.Retrieve)] <= 32 &&
                    !columns.AllColumns && rows.TryGetValue(id, out _), "bounded exact retrieve");
                var row = rows[id];
                Require(row.LogicalName == table, "retrieve table and identity");
                // 共用 fake 會再深層複製，這裡僅投影明列欄位，不讓缺欄被其他種子值遮蔽。
                var result = new Entity(table, id);
                foreach (var column in columns.Columns)
                    if (row.Attributes.TryGetValue(column, out var value)) result[column] = value;
                return result;
            };
            sdk.RetrieveMultipleHandler = query =>
            {
                Require(entrypoint == "EquipmentStatusActivity" &&
                    sdk.Calls[nameof(IOrganizationService.RetrieveMultiple)] == 1 &&
                    query is QueryExpression, "single equipment query");
                var typed = (QueryExpression)query;
                Require(typed.EntityName == "new_stor_lessons" && typed.PageInfo.PageNumber == 1 &&
                    typed.PageInfo.Count == 500 && typed.Criteria.Conditions.Count == 1 &&
                    typed.Criteria.Conditions[0].AttributeName == "new_contact_new_stor_lessons" &&
                    typed.Criteria.Conditions[0].Operator == ConditionOperator.Equal &&
                    typed.Criteria.Conditions[0].Values.Count == 1 &&
                    Equals(typed.Criteria.Conditions[0].Values[0], Id("member")), "member-scoped query");
                return new EntityCollection(new[] { rows[Id("record")] });
            };
            sdk.CreateHandler = _ => throw UnexpectedWrite();
            sdk.UpdateHandler = _ => throw UnexpectedWrite();
            sdk.DeleteHandler = (_, _) => throw UnexpectedWrite();
            sdk.ExecuteHandler = _ => throw UnexpectedWrite();
            sdk.AssociateHandler = (_, _, _, _) => throw UnexpectedWrite();
            sdk.DisassociateHandler = (_, _, _, _) => throw UnexpectedWrite();
            var factory = new Factory(sdk, context.UserId);
            if (entrypoint is "LessonLinkPlugin" or "StorLessonSigningPlugin" or "LessonProgressionPlugin")
            {
                context.InputParameters["Target"] = new Entity(context.PrimaryEntityName, context.PrimaryEntityId);
                IPlugin plugin = entrypoint switch
                {
                    "LessonLinkPlugin" => new LessonLinkPlugin("{\"mode\":\"Shadow\"}", ""),
                    "LessonProgressionPlugin" => new LessonProgressionPlugin("{\"mode\":\"Shadow\"}", ""),
                    _ => new StorLessonSigningPlugin("{\"mode\":\"Shadow\"}", "")
                };
                plugin.Execute(new FakeServiceProvider().Add<IPluginExecutionContext>(context)
                    .Add<IOrganizationServiceFactory>(factory).Add<ITracingService>(trace));
            }
            else
            {
                // 簽核活動用 Update，才能核對與學員不同的正式組長；其他活動用平台執行訊息。
                if (entrypoint != "StorLessonSigningActivity") context.MessageName = "ExecuteWorkflow";
                CodeActivity activity = entrypoint switch
                {
                    "LessonQrCodeActivity" => new QrActivity(),
                    "EquipmentStatusActivity" => new EquipmentActivity(),
                    _ => new SigningActivity()
                };
                var inputs = new Dictionary<string, object>
                { ["InputParameter"] = entrypoint == "StorLessonSigningActivity" ? "更新" : "synthetic", ["ExecutionMode"] = "Shadow" };
                if (entrypoint == "StorLessonSigningActivity")
                    inputs.Add("InputStorLesson", new EntityReference("new_stor_lessons", Id("record")));
                var invoker = new WorkflowInvoker(activity);
                invoker.Extensions.Add<IWorkflowContext>(() => new WorkflowContext(context));
                invoker.Extensions.Add<IOrganizationServiceFactory>(() => factory);
                invoker.Extensions.Add<ITracingService>(() => trace);
                try { invoker.Invoke(inputs, TimeSpan.FromSeconds(10)); }
                finally { inputs.Clear(); }
            }
            var lines = trace.Entries.Where(line => line.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
                .Select(line => RecordingOrganizationService.ReadProjection(line.Substring("SMV-SHADOW|1|".Length))).ToArray();
            Require(lines.Length == 2 && (bool?)lines[1]["end"] == true && (int?)lines[1]["count"] == 1 &&
                (int?)lines[0]["seq"] == 1 && lines.All(line => line["overflow"] is null) &&
                trace.Entries.Sum(line => line.Length + 2) <= 8000, "one complete bounded intent");
            if (entrypoint is "LessonLinkPlugin" or "LessonQrCodeActivity")
                VerifyQr(lines[0], entrypoint == "LessonQrCodeActivity");
            else if (entrypoint == "EquipmentStatusActivity")
                Require((string?)lines[0]["operation"] == "Update" && (string?)lines[0]["entity"] == "contact" &&
                    (string?)lines[0]["id"] == Id("member").ToString("D") &&
                    JToken.DeepEquals(lines[0]["fields"], new JObject { ["new_equipment_status"] = "E2門徒營" }), "equipment intent");
            else if (entrypoint == "LessonProgressionPlugin")
                Require((string?)lines[0]["operation"] == "Update" && (string?)lines[0]["entity"] == "new_stor_lessons" &&
                    (string?)lines[0]["id"] == Id("record").ToString("D") &&
                    JToken.DeepEquals(lines[0]["fields"], new JObject { ["new_estimated_credit"] = 0d, ["new_score"] = 0d }),
                    "credit progression complete update");
            else VerifySigning(lines[0]);
            Require(factory.Calls == 1 && sdk.Calls.TryGetValue(nameof(IOrganizationService.Retrieve), out var reads) &&
                reads > 0 && sdk.Calls.All(call => call.Key is nameof(IOrganizationService.Retrieve) or nameof(IOrganizationService.RetrieveMultiple)) &&
                sdk.WriteRecords.Count == 0 && sdk.EntityCount == 0 && sdk.AssociationCount == 0, "zero real service writes");
        }
        catch (Exception failure)
        {
            executionFailure = failure;
        }
        finally
        {
            try
            {
                // 故障路徑也先保存零真寫斷言，再清空服務；不能因 Dispose 擦掉計數而誤判。
                var noWrites = sdk.WriteRecords.Count == 0 && sdk.Calls.All(call =>
                    call.Key is nameof(IOrganizationService.Retrieve) or nameof(IOrganizationService.RetrieveMultiple));
                try { sdk.Dispose(); }
                finally
                {
                    rows.Clear(); trace.Entries.Clear();
                    context.InputParameters.Clear(); context.OutputParameters.Clear(); context.SharedVariables.Clear();
                    context.PreEntityImages.Clear(); context.PostEntityImages.Clear();
                }
                Require(noWrites, "zero real writes including failure path");
                Require(sdk.EntityCount == 0 && sdk.WriteRecords.Count == 0 && sdk.Calls.Count == 0 &&
                    sdk.AssociationCount == 0 && sdk.QueryResultCount == 0 &&
                    sdk.RetrieveHandler is null && sdk.RetrieveMultipleHandler is null && sdk.CreateHandler is null &&
                    sdk.UpdateHandler is null && sdk.DeleteHandler is null && sdk.ExecuteHandler is null &&
                    sdk.AssociateHandler is null && sdk.DisassociateHandler is null &&
                    rows.Count == 0 && trace.Entries.Count == 0, "fixture drained");
            }
            catch (Exception failure)
            {
                cleanupFailure = failure;
            }
        }
        // finally 只負責清理；離開清理範圍後保留首個錯誤堆疊，雙重故障則一起回報。
        if (executionFailure is not null && cleanupFailure is not null)
            throw new AggregateException("Lesson CAS execution and cleanup failed.", executionFailure, cleanupFailure);
        if (executionFailure is not null) ExceptionDispatchInfo.Capture(executionFailure).Throw();
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    /// <summary>QR 完整欄位取自既有 reading 期望，只排除影子新建列的非空 GUID 與序號。</summary>
    private static void VerifyQr(JObject intent, bool poll)
    {
        var expected = LessonLinkGoldenFixture.Read("expected");
        Require((string?)expected["basis"] == "reading", "QR reading basis");
        var name = poll ? "link-activity-with-poll" : "link-create";
        var write = expected["cases"]!.Children<JObject>().Single(row => (string?)row["name"] == name)["writes"]![0]!;
        var fields = (JObject)write["fields"]!.DeepClone();
        fields["new_accountid"] = new JObject { ["entity"] = "account", ["id"] = Id("church").ToString("D") };
        Require((string?)intent["operation"] == "Create" && (string?)intent["entity"] == "new_outboundtask" &&
            Guid.TryParse((string?)intent["id"], out var id) && id != Guid.Empty &&
            JToken.DeepEquals(fields, intent["fields"]), "QR full reading intent");
    }

    /// <summary>
    /// 獨立組合閱讀期望的 Flex 模型，不呼叫產品 LessonSigningMessageFactory 生成答案；
    /// 真外殼必須到達完整序列化並使用正式組長、事件重試鍵及來源教會 sender。
    /// </summary>
    private static void VerifySigning(JObject intent)
    {
        var image = "https://image.example/course.png";
        var suffix = "&EntityId=" + Id("record").ToString("D") + "&簽核者=組長";
        var message = new FlexMessage("有一個課程簽核的動作等待您的決定....")
        {
            Contents = new BubbleContainer
            {
                Hero = new ImageComponent(image)
                {
                    Size = ComponentSize.Full, AspectRatio = AspectRatio._20_13, AspectMode = AspectMode.Cover,
                    Action = new UriTemplateAction("說明網頁/填寫表單", image)
                },
                Body = new BoxComponent(BoxLayout.Vertical)
                {
                    Contents =
                    {
                        new TextComponent("課程簽核") { Weight = Weight.Bold, Size = ComponentSize.Md },
                        new BoxComponent(BoxLayout.Baseline)
                        {
                            Margin = Spacing.Md,
                            Contents =
                            {
                                Star(), Star(), Star(), Star(), Star(),
                                new TextComponent("5.0") { Size = ComponentSize.Sm, Margin = Spacing.Md, Flex = 0, Color = "#999999" }
                            }
                        },
                        new BoxComponent(BoxLayout.Baseline)
                        {
                            Spacing = Spacing.Sm,
                            Contents =
                            {
                                new TextComponent("課程名稱: 課程\r\n學員姓名: 學員\r\n裝備狀態: 已完成")
                                { Wrap = true, Size = ComponentSize.Sm, Color = "#666666", Flex = 5 }
                            }
                        }
                    }
                },
                Footer = new BoxComponent(BoxLayout.Vertical)
                {
                    Spacing = Spacing.Sm, Flex = 0,
                    Contents =
                    {
                        new ButtonComponent(new PostbackTemplateAction("同意", "訊息類別=課程簽核&動作=同意" + suffix, "您簽核同意")) { Style = ButtonStyle.Link, Height = ButtonHeight.Sm },
                        new ButtonComponent(new PostbackTemplateAction("退回", "訊息類別=課程簽核&動作=退回" + suffix, "您簽核退回")) { Style = ButtonStyle.Link, Height = ButtonHeight.Sm },
                        new SpacerComponent { Size = ComponentSize.Sm }
                    }
                },
                Styles = new BubbleStyles
                {
                    Body = new BlockStyle { BackgroundColor = "#C0C8C8", Separator = true, SeparatorColor = "#9400D3" },
                    Footer = new BlockStyle { BackgroundColor = "#FFFFF0" }
                }
            }
        };
        Require((string?)intent["operation"] == "Push" &&
            (string?)intent["contactId"] == Id("leader").ToString("D") &&
            (string?)intent["templateKey"] == "Lessons.Signing" && (int?)intent["messageCount"] == 1 &&
            (string?)intent["retryKey"] == DeterministicGuid.Create("lessons.signing", Id("record"), Id("leader")).ToString("D") &&
            (string?)intent["contentSha256"] == LinePushClient.ComputeMemberContentSha256(
                new ISendMessage[] { message }, "合成教會助手"), "verified signing intent and full content");
    }

    /// <summary>reading來源中的獨立星號期望，每次建立新模型而不重用產品工廠。</summary>
    private static IconComponent Star() => new()
    {
        Url = "https://scdn.line-apps.com/n/channel_devcenter/img/fx/review_gold_star_28.png", Size = ComponentSize.Sm
    };

    /// <summary>固定七筆合成來源，無真 token；每次回傳全新集合及 lookup，不跨案例發布。</summary>
    private static Dictionary<Guid, Entity> Seed()
    {
        var rows = new[]
        {
            new Entity("new_disciple_lessons", Id("lesson"))
            {
                ["new_contact_new_disciple_lessons"] = new EntityReference("contact", Id("member")),
                ["new_now_stage_name"] = "E2門徒營", ["new_name"] = "課程",
                ["new_picture_link"] = "https://image.example/course.png"
            },
            new Entity("new_stor_lessons", Id("record"))
            {
                ["new_contact_new_stor_lessons"] = new EntityReference("contact", Id("member")),
                ["new_account_new_stor_lessons"] = new EntityReference("account", Id("church")),
                ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", Id("lesson")),
                ["new_current_complete"] = true
            },
            new Entity("contact", Id("member"))
            {
                ["fullname"] = "學員", ["parentcustomerid"] = new EntityReference("account", Id("church")),
                ["new_cell_list_contact"] = new EntityReference("list", Id("list")), ["new_equipment_status"] = "已完成"
            },
            new Entity("contact", Id("leader"))
            {
                ["fullname"] = "組長", ["parentcustomerid"] = new EntityReference("account", Id("church")),
                ["new_lineid"] = "U00000000000000000000000000000000", ["new_preferredlanguage"] = "zh-TW"
            },
            new Entity("list", Id("list")) { ["new_contact_family_leader_list"] = new EntityReference("contact", Id("leader")) },
            new Entity("account", Id("church"))
            {
                ["name"] = "合成教會", ["new_defaultlanguage"] = "zh-TW", ["new_enabledlanguages"] = "zh-TW",
                ["new_lessonprogressionprofile"] = "credit-score-v1",
                ["new_linechannelid"] = new EntityReference("new_linechannel", Id("channel"))
            },
            new Entity("new_linechannel", Id("channel")) { ["new_accesstokenencrypted"] = "synthetic-invalid-envelope" }
        };
        return rows.ToDictionary(row => row.Id);
    }

    /// <summary>重用同一 reading fixture 的合成 alias 身分規則，不能拿來代替伺服器授權。</summary>
    private static Guid Id(string alias) => LessonLinkGoldenFixture.Id(alias);
    /// <summary>固定失敗不包含 SDK request 或欄位值；任何真服務副作用都必須立即拒絕。</summary>
    private static InvalidOperationException UnexpectedWrite() => new("Lesson CAS attempted a real service write.");
    /// <summary>子域只用一般例外，不讓 xUnit 或動態代理介入部分信任執行鏈。</summary>
    private static void Require(bool valid, string contract)
    { if (!valid) throw new InvalidOperationException("Lesson CAS mismatch: " + contract); }

    /// <summary>單案借用工廠，只接受平台當次使用者，不接受 null 提權或其他使用者。</summary>
    private sealed class Factory(IOrganizationService sdk, Guid user) : IOrganizationServiceFactory
    {
        /// <summary>只計算當次借用次數，不持有歷史 request 或回應；Run 返回後丟棄工廠。</summary>
        internal int Calls { get; private set; }
        /// <summary>借用服務不轉移擁有權；只可呼叫一次，錯身分或重複借用明確失敗。</summary>
        public IOrganizationService CreateOrganizationService(Guid? userId)
        {
            Require(userId == user && ++Calls == 1, "single platform-user service lease");
            return sdk;
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

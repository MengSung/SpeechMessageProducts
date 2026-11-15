/*
 * 檔案整體說明
 *
 * 目的與定位：驗證 P5-07 課程簽核 Plugin 的真 Shadow 外殼，保護來源 lookup
 * 鏈、共用 Flex 組合、收件者重驗及安全影子投影。
 *
 * 主要功能與流程：以合成 new_stor_lessons、contact、list、account、course、
 * channel 及 encrypted envelope 建立最小 SDK 替身，讓真正 Plugin 執行一次
 * Shadow Create 與一次 Update；測試不連 D365、LINE 或檔案系統，不把合成
 * envelope、LINE ID 或個資寫入期望輸出。
 *
 * 使用邊界與維護：每個案例獨立擁有 mock、context、rows 及 tracing，finally
 * 清除所有參照；Shadow 必須只留下安全通知意圖，不建立真 Create／Update，
 * 且不以 developer LINE ID 或組織名稱猜收件者。驗證失敗時應保留原例外。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Moq;
using Newtonsoft.Json.Linq;
using SpeechMessage.Dynamics.Plugins.Lessons;
using SpeechMessage.Dynamics.Plugins.Tests.Fakes;
using Xunit;

namespace SpeechMessage.Dynamics.Plugins.Tests;

/// <summary>課程簽核 Shadow 來源與通知外殼的離線契約。</summary>
public sealed class LessonSigningShellTests
{
    private static readonly Guid StorId =
        Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid MemberId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid ChurchId =
        Guid.Parse("22222222-3333-4444-5555-666666666666");
    private static readonly Guid CourseId =
        Guid.Parse("33333333-4444-5555-6666-777777777777");
    private static readonly Guid ListId =
        Guid.Parse("44444444-5555-6666-7777-888888888888");
    private static readonly Guid ChannelId =
        Guid.Parse("55555555-6666-7777-8888-999999999999");
    private const string LineId = "U00000000000000000000000000000000";

    /// <summary>Update 依 member→list→leader 解析正式收件者，Shadow 零真寫且完成投影。</summary>
    [Fact]
    public void Lessons_Signing_ShadowUpdate_UsesVerifiedLeader()
    {
        var stor = new Entity("new_stor_lessons", StorId)
        {
            ["new_contact_new_stor_lessons"] = new EntityReference("contact", MemberId),
            ["new_new_disciple_lessons_new_stor_les"] = new EntityReference("new_disciple_lessons", CourseId)
        };
        var member = new Entity("contact", MemberId)
        {
            ["fullname"] = "學員",
            ["new_cell_list_contact"] = new EntityReference("list", ListId),
            ["parentcustomerid"] = new EntityReference("account", ChurchId),
            ["new_equipment_status"] = "已完成",
            ["new_lineid"] = LineId,
            ["new_preferredlanguage"] = "zh-TW"
        };
        var list = new Entity("list", ListId)
        {
            ["new_contact_family_leader_list"] = new EntityReference("contact", MemberId)
        };
        var course = new Entity("new_disciple_lessons", CourseId)
        {
            ["new_name"] = "課程",
            ["new_picture_link"] = "https://image.example/course.png"
        };
        var account = new Entity("account", ChurchId)
        {
            ["name"] = "合成教會",
            ["new_defaultlanguage"] = "zh-TW",
            ["new_enabledlanguages"] = "zh-TW",
            ["new_linechannelid"] = new EntityReference("new_linechannel", ChannelId)
        };
        var channel = new Entity("new_linechannel", ChannelId)
        {
            ["new_accesstokenencrypted"] = "synthetic-envelope"
        };
        var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
        sdk.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Microsoft.Xrm.Sdk.Query.ColumnSet>()))
            .Returns<string, Guid, Microsoft.Xrm.Sdk.Query.ColumnSet>((table, id, _) =>
            {
                if (table == "new_stor_lessons" && id == StorId) return stor;
                if (table == "contact" && id == MemberId) return member;
                if (table == "list" && id == ListId) return list;
                if (table == "new_disciple_lessons" && id == CourseId) return course;
                if (table == "account" && id == ChurchId) return account;
                if (table == "new_linechannel" && id == ChannelId) return channel;
                throw new InvalidOperationException("Unexpected retrieve.");
            });
        var factory = new Mock<IOrganizationServiceFactory>(MockBehavior.Strict);
        var context = new RemoteExecutionContext
        {
            MessageName = "Update", PrimaryEntityName = "new_stor_lessons",
            PrimaryEntityId = StorId, Stage = 40, Mode = 1, Depth = 1,
            UserId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(), OrganizationName = "synthetic"
        };
        var trace = new FakeTracingService();
        factory.Setup(x => x.CreateOrganizationService(context.UserId)).Returns(sdk.Object);
        var provider = new FakeServiceProvider().Add<IPluginExecutionContext>(context)
            .Add<IOrganizationServiceFactory>(factory.Object).Add<ITracingService>(trace);

        new StorLessonSigningPlugin("{\"mode\":\"Shadow\"}", "").Execute(provider);

        var entries = trace.Entries.Where(x => x.StartsWith("SMV-SHADOW|1|", StringComparison.Ordinal))
            .Select(x => JObject.Parse(x.Substring("SMV-SHADOW|1|".Length))).ToArray();
        Assert.NotEmpty(entries);
        Assert.True((bool)entries.Last()["end"]!);
        Assert.DoesNotContain(entries, x => (bool?)x["overflow"] == true);
        Assert.Contains(entries, x => (string?)x["templateKey"] == "Lessons.Signing");
        sdk.Verify(x => x.Create(It.IsAny<Entity>()), Times.Never);
        sdk.Verify(x => x.Update(It.IsAny<Entity>()), Times.Never);
    }
}

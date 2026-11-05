using System.Collections;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.ActivityOperations;
using Xunit;

namespace ToolUtility.Tests.ActivityOperations
{
    /// <summary>
    /// 在Activity仍位於原host時鎖定三類成功行為：分組查詢／順序、純ID解析、兩組狀態請求。
    /// 只替代SDK，所有ID與內容為合成值，不連D365、不發LINE；不把已知安全缺陷當作相容行為。
    /// </summary>
    public sealed class ActivityCharacterizationTests
    {
        private static readonly Guid FirstId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid UserId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid LastId = new Guid("33333333-3333-3333-3333-333333333333");

        /// <summary>
        /// contact/systemuser分組各查一次AllColumns，回應逆序仍依原參與者順序append並保留既有前綴。
        /// 沒有partyid但帶addressused的外部收件者沿用略過語義；同名不同正式ID都保留，借入SDK不Dispose。
        /// </summary>
        [Fact]
        public void PartyList_PreservesBatchGoldenOrderAndExistingPrefix()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var first = new Entity("contact", FirstId) { ["fullname"] = "合成同名" };
            var user = new Entity("systemuser", UserId) { ["fullname"] = "合成使用者" };
            var last = new Entity("contact", LastId) { ["fullname"] = "合成同名" };
            var queries = new List<XElement>();
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                var condition = Assert.Single(query.Criteria.Conditions);
                queries.Add(new XElement("query", new XAttribute("entity", query.EntityName),
                    new XAttribute("all-columns", query.ColumnSet.AllColumns),
                    new XAttribute("columns", query.ColumnSet.Columns.Count),
                    new XElement("condition", new XAttribute("field", condition.AttributeName),
                        new XAttribute("operator", condition.Operator),
                        condition.Values.Select(value => new XElement("id", Assert.IsType<Guid>(value))))));
                Assert.Empty(query.LinkEntities);
                Assert.Empty(query.Orders);
                Assert.Null(query.TopCount);
                return query.EntityName == "contact"
                    ? new EntityCollection(new[] { last, first }) : new EntityCollection(new[] { user });
            });
            var prefix = new Entity("contact", new Guid("44444444-4444-4444-4444-444444444444"));
            var parties = new ArrayList { prefix };
            var types = new ArrayList { "contact" };
            new ActivityService(new object(), sdk.Object).GetActivityPartyList(Activity(), "to", parties, types);
            GoldenQueryAssertions.Equal("activity-party-query.xml", new XElement("queries", queries).ToString());
            Assert.Equal(new[] { prefix, first, user, last }, parties.Cast<Entity>());
            Assert.Equal(new[] { "contact", "contact", "systemuser", "contact" }, types.Cast<string>());
            Assert.Same(first, parties[1]);
            Assert.Same(last, parties[3]);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 純ID入口不查SDK，順序及型別與ID一一對齊，原前綴不被Clear。
        /// 缺partyid的外部地址不偽造資料庫ID；這不表示本文已驗證寄送或外部收件者授權。
        /// </summary>
        [Fact]
        public void PartyIds_PreserveOrderPrefixAndSkipUnresolvedAddressWithoutIo()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var prefix = new Guid("44444444-4444-4444-4444-444444444444");
            var ids = new ArrayList { prefix };
            var types = new ArrayList { "contact" };
            new ActivityService(new object(), sdk.Object).GetActivityPartyIdList(Activity(), "to", ids, types);
            Assert.Equal(new[] { prefix, FirstId, UserId, LastId }, ids.Cast<Guid>());
            Assert.Equal(new[] { "contact", "contact", "systemuser", "contact" }, types.Cast<string>());
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// default/external兩多載實際只呼叫所選SDK一次；鎖定活動1/4、約會3/5與完整EntityMoniker。
        /// 不臆測不同組織的狀態碼，不更動任何真實活動，不把SDK所有權轉交給工具。
        /// </summary>
        /// <param name="appointment">是否使用約會排程請求。</param>
        /// <param name="external">是否使用明確外部SDK多載。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void SetState_PreservesGoldenAndSelectedSdk(bool appointment, bool external)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var externalSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var selected = external ? externalSdk : ctorSdk;
            var other = external ? ctorSdk : externalSdk;
            var disposable = selected.As<IDisposable>();
            selected.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Callback<OrganizationRequest>(input =>
            {
                var request = Assert.IsType<SetStateRequest>(input);
                Assert.Equal(3, request.Parameters.Count);
                var xml = new XElement("set-state", new XAttribute("request", request.RequestName),
                    new XAttribute("entity", request.EntityMoniker.LogicalName),
                    new XAttribute("id", request.EntityMoniker.Id),
                    new XAttribute("state", request.State.Value),
                    new XAttribute("status", request.Status.Value));
                GoldenQueryAssertions.Equal(appointment ? "activity-schedule.xml" : "activity-complete.xml", xml.ToString());
            }).Returns(new SetStateResponse());
            var service = new ActivityService(new object(), ctorSdk.Object);
            if (appointment)
            {
                if (external) service.SetAppointmentStatusToScheduled(FirstId, externalSdk.Object);
                else service.SetAppointmentStatusToScheduled(FirstId);
            }
            else
            {
                if (external) service.SetActivityStatusToCompleted("email", FirstId, externalSdk.Object);
                else service.SetActivityStatusToCompleted("email", FirstId);
            }
            selected.Verify(s => s.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
            disposable.Verify(d => d.Dispose(), Times.Never);
            selected.VerifyNoOtherCalls();
            other.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 合成參與者含同名但不同ID聯絡人及外部地址；partyid參照既有資料庫記錄，
        /// activityparty容器只供解析、不作持久化列發布或補造ID。
        /// </summary>
        private static Entity Activity()
        {
            return new Entity("email", new Guid("55555555-5555-5555-5555-555555555555"))
            {
                ["to"] = new EntityCollection(new[]
                {
                    new Entity("activityparty") { ["partyid"] = new EntityReference("contact", FirstId) },
                    new Entity("activityparty") { ["addressused"] = "synthetic@example.invalid" },
                    new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", UserId) },
                    new Entity("activityparty") { ["partyid"] = new EntityReference("contact", LastId) }
                })
            };
        }
    }
}

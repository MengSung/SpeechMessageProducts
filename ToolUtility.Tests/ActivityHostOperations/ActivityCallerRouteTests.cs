/*
 * 檔案整體說明
 *
 * 目的與定位：執行真實 ToolUtilityClass→Facade→Lazy→Core 跨組件委派與 Dispose；只替代外部 SDK I/O。 使用正常建構式、空設定及無輸出 tracer，不讀機密、不連線或發 
 * LINE；不是實機／授權驗收。
 *
 * 主要功能與流程：
 * 本檔宣告 ActivityCallerRouteTests。
 * - 跨表同 GUID 在公開入口保持不同資料，第二組故障不部分發布；確認 lazy 實載 Core， 工具 Dispose 確實停止已建立的子服務且不得 Dispose 借入 SDK。
 * - 與真實 AppointmentsDownUpLoader 相同，連續 requiredattendees／optionalattendees 追加不清前綴； 兩個入口都沒有 SDK I/O，處理完由原工具 owner 
 * 確定性 Dispose。
 * - 公開兩種狀態方法經 facade 外部多載只執行一次正確請求；真故障仍保留同一 InnerException。
 * - 正常 ctor 僅注入合成 SDK／空設定，不反序列化未初始化工具，也不讀取任何真實設定。
 * 測試案例入口包含 PublicPartyList_UsesCoreAndPropagatesFailureWithoutPartialPublication、
 * PublicPartyIds_PreservesRequiredThenOptionalAttendeeAppendFlow、
 * PublicState_UsesExpectedBorrowedSdkAndPreservesOriginalException、Create、GetCreatedChild、Input。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Collections;
using System.Reflection;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.ActivityOperations;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Tests.ActivityHostOperations
{
    /// <summary>
    /// 執行真實 ToolUtilityClass→Facade→Lazy→Core 跨組件委派與 Dispose；只替代外部 SDK I/O。
    /// 使用正常建構式、空設定及無輸出 tracer，不讀機密、不連線或發 LINE；不是實機／授權驗收。
    /// </summary>
    public sealed class ActivityCallerRouteTests
    {
        private static readonly Guid Identity = new Guid("11111111-1111-1111-1111-111111111111");

        /// <summary>
        /// 跨表同 GUID 在公開入口保持不同資料，第二組故障不部分發布；確認 lazy 實載 Core，
        /// 工具 Dispose 確實停止已建立的子服務且不得 Dispose 借入 SDK。
        /// </summary>
        /// <param name="fault">是否在第二個表注入 SDK 故障。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicPartyList_UsesCoreAndPropagatesFailureWithoutPartialPublication(bool fault)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.As<IDisposable>();
            var original = new TimeoutException("synthetic_private_fault");
            sdk.Setup(service => service.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase source) =>
            {
                var query = Assert.IsType<QueryExpression>(source);
                if (fault && query.EntityName == "systemuser") throw original;
                return new EntityCollection(new[] { new Entity(query.EntityName, Identity) });
            });
            IActivityService child;
            using (var utility = Create(sdk.Object))
            {
                var values = new ArrayList();
                var types = new ArrayList();
                var input = Input("to", new EntityReference("contact", Identity), new EntityReference("systemuser", Identity));
                if (fault)
                {
                    var error = Assert.Throws<InvalidOperationException>(() => utility.GetActivityPartyList(input, "to", values, types));
                    Assert.Same(original, error.InnerException);
                    Assert.Equal("The activity parties could not be retrieved.", error.Message);
                    Assert.Empty(values);
                    Assert.Empty(types);
                }
                else
                {
                    utility.GetActivityPartyList(input, "to", values, types);
                    Assert.Equal(new[] { "contact", "systemuser" }, values.Cast<Entity>().Select(row => row.LogicalName));
                    Assert.Equal(new[] { "contact", "systemuser" }, types.Cast<string>());
                }
                child = GetCreatedChild(utility);
                Assert.Equal("ToolUtility.Core", child.GetType().Assembly.GetName().Name);
            }
            Assert.Throws<ObjectDisposedException>(() => child.GetActivityPartyIdList(new Entity("email"), "to", new ArrayList(), new ArrayList()));
            sdk.Verify(service => service.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(2));
            sdk.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 與真實 AppointmentsDownUpLoader 相同，連續 requiredattendees／optionalattendees 追加不清前綴；
        /// 兩個入口都沒有 SDK I/O，處理完由原工具 owner 確定性 Dispose。
        /// </summary>
        [Fact]
        public void PublicPartyIds_PreservesRequiredThenOptionalAttendeeAppendFlow()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var second = new Guid("22222222-2222-2222-2222-222222222222");
            var activity = Input("requiredattendees", new EntityReference("contact", Identity));
            activity["optionalattendees"] = ((EntityCollection)Input("to", new EntityReference("contact", second))["to"]);
            using (var utility = Create(sdk.Object))
            {
                var values = new ArrayList();
                var types = new ArrayList();
                utility.GetActivityPartyIdList(activity, "requiredattendees", values, types);
                utility.GetActivityPartyIdList(activity, "optionalattendees", values, types);
                Assert.Equal(new[] { Identity, second }, values.Cast<Guid>());
                Assert.Equal(new[] { "contact", "contact" }, types.Cast<string>());
            }
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>公開兩種狀態方法經 facade 外部多載只執行一次正確請求；真故障仍保留同一 InnerException。</summary>
        /// <param name="appointment">是否測 appointment 排程。</param>
        /// <param name="fault">是否注入 SDK 自身參數例外。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void PublicState_UsesExpectedBorrowedSdkAndPreservesOriginalException(bool appointment, bool fault)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.As<IDisposable>();
            var original = new ArgumentException("synthetic_private_fault");
            sdk.Setup(service => service.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest source) =>
            {
                var request = Assert.IsType<SetStateRequest>(source);
                Assert.Equal(3, request.Parameters.Count);
                Assert.Equal(Identity, request.EntityMoniker.Id);
                Assert.Equal(appointment ? "appointment" : "email", request.EntityMoniker.LogicalName);
                Assert.Equal(appointment ? 3 : 1, request.State.Value);
                Assert.Equal(appointment ? 5 : 4, request.Status.Value);
                if (fault) throw original;
                return new SetStateResponse();
            });
            using (var utility = Create(sdk.Object))
            {
                Action action = () =>
                {
                    if (appointment) utility.SetAppointmentStatusToScheduled(Identity);
                    else utility.SetActivityStatusToCompleted("email", Identity);
                };
                if (fault)
                {
                    var error = Assert.Throws<InvalidOperationException>(action);
                    Assert.Same(original, error.InnerException);
                }
                else action();
                Assert.Equal("ToolUtility.Core", GetCreatedChild(utility).GetType().Assembly.GetName().Name);
            }
            sdk.Verify(service => service.Execute(It.IsAny<OrganizationRequest>()), Times.Once);
            sdk.As<IDisposable>().Verify(owner => owner.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>正常 ctor 僅注入合成 SDK／空設定，不反序列化未初始化工具，也不讀取任何真實設定。</summary>
        private static ToolUtilityClass Create(IOrganizationService sdk)
            => new ToolUtilityClass(sdk, new NullToolUtilityTracer(), new ConfigurationBuilder().Build());

        /// <summary>
        /// 僅取已建立的 facade／lazy 具名欄位以檢查實際組件及 owner 清除，不讀取或輸出憑證欄位。
        /// 這是測試觀測點，不以反射替代產品呼叫或偽造 lazy 值。
        /// </summary>
        private static IActivityService GetCreatedChild(ToolUtilityClass utility)
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var facade = typeof(ToolUtilityClass).GetField("_facade", flags)!.GetValue(utility)!;
            var lazy = Assert.IsType<Lazy<IActivityService>>(facade.GetType().GetField("_activityService", flags)!.GetValue(facade));
            Assert.True(lazy.IsValueCreated);
            return lazy.Value;
        }

        /// <summary>本次操作擁有的合成活動；partyid 指向固定正式測試識別，不補造缺失資料。</summary>
        private static Entity Input(string field, params EntityReference[] references)
            => new Entity("email") { [field] = new EntityCollection(references.Select(reference => new Entity("activityparty") { ["partyid"] = reference }).ToArray()) };
    }
}

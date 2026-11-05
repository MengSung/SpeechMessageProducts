using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace ToolUtility.Tests.AttachmentOperations
{
    /// <summary>
    /// 實際建立 ToolUtilityClass 並執行公開入口→Facade→Lazy→Core 的完整附件委派鏈。
    /// 僅替代 SDK I/O，使用空設定與無輸出 tracer，不讀憑證、不連線或發 LINE；
    /// 此證據保護跨組件路徑，不冒充 D365 實機或 host 的授權驗收。
    /// </summary>
    public sealed class AttachmentCallerRouteTests
    {
        private static readonly Guid ParentId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid NoteId = new Guid("22222222-2222-2222-2222-222222222222");

        /// <summary>
        /// 下載必須抵達 ref SDK 而非建構 SDK，正常頁保留；錯 parent 整頁拒絕並保留故障。
        /// using 排空 host 工具後仍不得處置任一借入 SDK，無快取或背景資源殘留。
        /// </summary>
        /// <param name="invalid">是否注入錯 parent。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicDownload_RoutesRefSdkAndRejectsWrongParent(bool invalid)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var ctorDisposable = ctorSdk.As<IDisposable>();
            var disposable = sdk.As<IDisposable>();
            var page = new EntityCollection(new[]
            {
                new Entity("annotation", NoteId)
                {
                    ["objectid"] = new EntityReference("contact", invalid ? NoteId : ParentId),
                    ["documentbody"] = "AQID"
                }
            });
            sdk.Setup(s => s.RetrieveMultiple(It.Is<QueryBase>(q =>
                q is QueryExpression && ((QueryExpression)q).EntityName == "annotation"))).Returns(page);
            using (var utility = new ToolUtilityClass(ctorSdk.Object, new NullToolUtilityTracer(),
                new ConfigurationBuilder().Build()))
            {
                var borrowed = sdk.Object;
                if (invalid)
                {
                    var error = Assert.Throws<InvalidOperationException>(() => utility.DownloadAnAttachment(ref borrowed, ParentId));
                    Assert.Equal("The attachments could not be downloaded.", error.Message);
                    Assert.IsType<InvalidOperationException>(error.InnerException);
                }
                else Assert.Same(page, utility.DownloadAnAttachment(ref borrowed, ParentId));
                Assert.Same(sdk.Object, borrowed);
            }
            ctorSdk.VerifyNoOtherCalls();
            ctorDisposable.Verify(d => d.Dispose(), Times.Never);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 上傳在真實公開委派鏈保持七欄及 ref SDK；注入 SDK 參數例外仍保存同一 InnerException。
        /// Dispose 只結束工具自有生命週期，不擁有借入 SDK，不把故障偽裝成功或做隱藏重試。
        /// </summary>
        /// <param name="fault">是否注入 SDK 故障。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PublicUpload_RoutesSevenFieldsAndPreservesFault(bool fault)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var ctorDisposable = ctorSdk.As<IDisposable>();
            var disposable = sdk.As<IDisposable>();
            var original = new ArgumentException("synthetic_private_fault");
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity row) =>
            {
                Assert.Equal("annotation", row.LogicalName);
                Assert.Equal(7, row.Attributes.Count);
                Assert.Equal(ParentId, Assert.IsType<EntityReference>(row["objectid"]).Id);
                Assert.Equal("AQID", row["documentbody"]);
                if (fault) throw original;
                return NoteId;
            });
            using (var utility = new ToolUtilityClass(ctorSdk.Object, new NullToolUtilityTracer(),
                new ConfigurationBuilder().Build()))
            {
                var borrowed = sdk.Object;
                if (fault)
                {
                    var error = Assert.Throws<InvalidOperationException>(() => utility.UploadAnAttachment(ref borrowed,
                        "contact", "合成", "", "", "", new byte[] { 1, 2, 3 }, ParentId));
                    Assert.Equal("The attachment could not be uploaded.", error.Message);
                    Assert.Same(original, error.InnerException);
                }
                else utility.UploadAnAttachment(ref borrowed, "contact", "合成", "", "", "", new byte[] { 1, 2, 3 }, ParentId);
                Assert.Same(sdk.Object, borrowed);
            }
            ctorSdk.VerifyNoOtherCalls();
            ctorDisposable.Verify(d => d.Dispose(), Times.Never);
            disposable.Verify(d => d.Dispose(), Times.Never);
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }
    }
}

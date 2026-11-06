/*
 * 檔案整體說明
 *
 * 目的與定位：在原 host 鎖定兩個附件入口的完整 SDK 查詢／七欄寫入及 ref 參照語義。 全部內容為合成資料，不連 D365、不讀設定，也不把正常特性與新增安全拒絕混為一談。
 *
 * 主要功能與流程：
 * 本檔宣告 AttachmentCharacterizationTests。
 * - 下載保留 annotation AllColumns、objectid 的字串值型別及完整分頁； 純文字附註與同名不同 ID 都返回，實際只借用 ref SDK，不呼叫或替換 ctor SDK。
 * - 空檔案保持空 Base64；非空檔案及特殊文字完整原樣寫入七欄，不作 XML／檔名或MIME內容轉換。 黃金檔保護實際 SDK Entity，而非只驗證模擬服務有被呼叫。
 * 測試案例入口包含 Download_PreservesGoldenQueryPageAndRefSdk、Upload_PreservesGoldenSevenFieldsAndRefSdk。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.AttachmentOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在原 host 鎖定兩個附件入口的完整 SDK 查詢／七欄寫入及 ref 參照語義。
    /// 全部內容為合成資料，不連 D365、不讀設定，也不把正常特性與新增安全拒絕混為一談。
    /// </summary>
    public sealed class AttachmentCharacterizationTests
    {
        private static readonly Guid ParentId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid NoteId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid FileId = new Guid("33333333-3333-3333-3333-333333333333");

        /// <summary>
        /// 下載保留 annotation AllColumns、objectid 的字串值型別及完整分頁；
        /// 純文字附註與同名不同 ID 都返回，實際只借用 ref SDK，不呼叫或替換 ctor SDK。
        /// </summary>
        [Fact]
        public void Download_PreservesGoldenQueryPageAndRefSdk()
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var page = new EntityCollection { MoreRecords = true, PagingCookie = "synthetic-next", TotalRecordCount = 3 };
            var note = new Entity("annotation", NoteId)
                { ["objectid"] = new EntityReference("contact", ParentId), ["subject"] = "合成同名" };
            var file = new Entity("annotation", FileId)
                { ["objectid"] = new EntityReference("contact", ParentId), ["subject"] = "合成同名", ["documentbody"] = "AAEC" };
            page.Entities.Add(note);
            page.Entities.Add(file);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Callback<QueryBase>(input =>
            {
                var query = Assert.IsType<QueryExpression>(input);
                var condition = Assert.Single(query.Criteria.Conditions);
                var xml = new XElement("query", new XAttribute("entity", query.EntityName),
                    new XAttribute("all-columns", query.ColumnSet.AllColumns), new XAttribute("columns", query.ColumnSet.Columns.Count),
                    new XAttribute("criteria", query.Criteria.FilterOperator), new XAttribute("orders", query.Orders.Count),
                    new XAttribute("links", query.LinkEntities.Count), new XAttribute("top", query.TopCount?.ToString() ?? "none"),
                    new XElement("condition", new XAttribute("field", condition.AttributeName),
                        new XAttribute("operator", condition.Operator),
                        new XAttribute("type", Assert.Single(condition.Values).GetType().FullName!),
                        condition.Values[0]));
                GoldenQueryAssertions.Equal("attachment-download.xml", xml.ToString());
            }).Returns(page);
            var service = new AttachmentService(new object(), ctorSdk.Object);
            var borrowed = sdk.Object;
            var result = service.DownloadAttachment(ref borrowed, ParentId);
            Assert.Same(sdk.Object, borrowed);
            Assert.Same(page, result);
            Assert.Same(note, result.Entities[0]);
            Assert.Same(file, result.Entities[1]);
            Assert.False(note.Contains("documentbody"));
            Assert.Equal("AAEC", file["documentbody"]);
            Assert.True(result.MoreRecords);
            Assert.Equal("synthetic-next", result.PagingCookie);
            Assert.Equal(3, result.TotalRecordCount);
            ctorSdk.VerifyNoOtherCalls();
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            disposable.Verify(s => s.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 空檔案保持空 Base64；非空檔案及特殊文字完整原樣寫入七欄，不作 XML／檔名或MIME內容轉換。
        /// 黃金檔保護實際 SDK Entity，而非只驗證模擬服務有被呼叫。
        /// </summary>
        /// <param name="empty">是否使用合法零位元組檔案。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Upload_PreservesGoldenSevenFieldsAndRefSdk(bool empty)
        {
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var bytes = empty ? Array.Empty<byte>() : new byte[] { 0, 1, 2, 127, 255 };
            var original = bytes.ToArray();
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Callback<Entity>(entity =>
            {
                Assert.Equal("annotation", entity.LogicalName);
                Assert.Equal(Guid.Empty, entity.Id);
                Assert.Equal(7, entity.Attributes.Count);
                var parent = Assert.IsType<EntityReference>(entity["objectid"]);
                var xml = new XElement("annotation",
                    new XElement("objectid", new XAttribute("name", parent.LogicalName), new XAttribute("id", parent.Id)));
                foreach (var key in new[] { "objecttypecode", "subject", "notetext", "documentbody", "mimetype", "filename" })
                    xml.Add(new XElement(key, entity[key]));
                GoldenQueryAssertions.Equal(empty ? "attachment-upload-empty.xml" : "attachment-upload-bytes.xml", xml.ToString());
                Assert.Equal(bytes, Convert.FromBase64String(Assert.IsType<string>(entity["documentbody"])));
            }).Returns(NoteId);
            var service = new AttachmentService(new object(), ctorSdk.Object);
            var borrowed = sdk.Object;
            service.UploadAttachment(ref borrowed, "contact", "合成<&\"", "附註<&\"", "合成<&\".bin",
                "application/x-synthetic", bytes, ParentId);
            Assert.Same(sdk.Object, borrowed);
            Assert.Equal(original, bytes);
            ctorSdk.VerifyNoOtherCalls();
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            disposable.Verify(s => s.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：在抽取前保護屬性工具的缺值、別名與就地更新，以及 SDK 擴充的完整請求／分頁契約。 僅用合成實體與受控 SDK 邊界；不連線、不配置 logger 或外部資源。
 *
 * 主要功能與流程：
 * 本檔宣告 AttributeExtensionCharacterizationTests。
 * - 一般值、別名值與真正缺值維持舊文字語義，不把合法的空資料當成故障。
 * - 移除欄位只改原 Entity 的指定屬性；缺欄位合法、其他值與實體參照不能被替換。
 * - 設 null 保留屬性存在，才能表達 SDK 清欄位語義；不得誤做成移除屬性而漏送更新。
 * - 真實擴充入口只 Execute 一次原請求，保留選項、Query 與分頁欄位；合法同名不同 ID 不可被合併。 SDK 只借用，測試結束不會配置或留下外部連線。
 * 測試案例入口包含 AttributeRead_PreservesValueAndMissingSemantics、AttributeRemove_PreservesEntityAndUnrelatedValues、
 * AttributeSetNull_PreservesExplicitNullAndOtherValues、ExtensionExecute_PreservesRequestOptionsAndCompletePage。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.Extensions;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在抽取前保護屬性工具的缺值、別名與就地更新，以及 SDK 擴充的完整請求／分頁契約。
    /// 僅用合成實體與受控 SDK 邊界；不連線、不配置 logger 或外部資源。
    /// </summary>
    public sealed class AttributeExtensionCharacterizationTests
    {
        /// <summary>一般值、別名值與真正缺值維持舊文字語義，不把合法的空資料當成故障。</summary>
        /// <param name="shape">測試限定的欄位形狀。</param>
        /// <param name="expected">獨立指定的預期文字，不由產品方法推導。</param>
        [Theory]
        [InlineData("plain", "合成欄位")]
        [InlineData("alias", "合成別名")]
        [InlineData("alias-null", "")]
        [InlineData("null", "")]
        [InlineData("missing", "")]
        [InlineData("empty-name", "")]
        [InlineData("null-name", "")]
        public void AttributeRead_PreservesValueAndMissingSemantics(string shape, string expected)
        {
            var entity = new Entity("contact");
            if (shape == "plain") entity["synthetic_value"] = "合成欄位";
            if (shape == "alias") entity["synthetic_value"] = new AliasedValue("contact", "fullname", "合成別名");
            if (shape == "alias-null") entity["synthetic_value"] = new AliasedValue("contact", "fullname", null);
            if (shape == "null") entity["synthetic_value"] = null;
            var service = new EntityAttributeUtilityService(new object());
            Assert.Equal(expected, service.GetAttributeValue(entity,
                shape == "empty-name" ? "" : shape == "null-name" ? null : "synthetic_value"));
            Assert.Equal("contact", entity.LogicalName);
        }

        /// <summary>移除欄位只改原 Entity 的指定屬性；缺欄位合法、其他值與實體參照不能被替換。</summary>
        /// <param name="present">候選是否原本含該欄位。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AttributeRemove_PreservesEntityAndUnrelatedValues(bool present)
        {
            var entity = new Entity("contact") { ["synthetic_keep"] = "保留" };
            if (present) entity["synthetic_remove"] = "刪除此欄";
            var original = entity;
            new EntityAttributeUtilityService(new object()).RemoveAttribute(ref entity, "synthetic_remove");
            Assert.Same(original, entity);
            Assert.False(entity.Contains("synthetic_remove"));
            Assert.Equal("保留", entity.GetAttributeValue<string>("synthetic_keep"));
        }

        /// <summary>設 null 保留屬性存在，才能表達 SDK 清欄位語義；不得誤做成移除屬性而漏送更新。</summary>
        /// <param name="present">欄位是否已存在。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AttributeSetNull_PreservesExplicitNullAndOtherValues(bool present)
        {
            var entity = new Entity("contact") { ["synthetic_keep"] = "保留" };
            if (present) entity["synthetic_clear"] = "清除此值";
            var original = entity;
            new EntityAttributeUtilityService(new object()).SetEntityAttributeToNull(ref entity, "synthetic_clear");
            Assert.Same(original, entity);
            Assert.True(entity.Contains("synthetic_clear"));
            Assert.Null(entity["synthetic_clear"]);
            Assert.Equal("保留", entity.GetAttributeValue<string>("synthetic_keep"));
        }

        /// <summary>
        /// 真實擴充入口只 Execute 一次原請求，保留選項、Query 與分頁欄位；合法同名不同 ID 不可被合併。
        /// SDK 只借用，測試結束不會配置或留下外部連線。
        /// </summary>
        /// <param name="empty">是否為合法空頁。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExtensionExecute_PreservesRequestOptionsAndCompletePage(bool empty)
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("fullname"),
                PageInfo = new PagingInfo { PageNumber = 7, Count = 17, PagingCookie = "synthetic-query-cookie" }
            };
            var request = new RetrieveMultipleRequest { Query = query };
            request["ReturnNotifications"] = true;
            var page = new EntityCollection
            {
                EntityName = "contact", MoreRecords = true, PagingCookie = "synthetic-cookie", TotalRecordCount = 2
            };
            if (!empty)
            {
                page.Entities.Add(new Entity("contact", new Guid("11111111-1111-1111-1111-111111111111")) { ["fullname"] = "合成同名" });
                page.Entities.Add(new Entity("contact", new Guid("22222222-2222-2222-2222-222222222222")) { ["fullname"] = "合成同名" });
            }
            var response = new RetrieveMultipleResponse();
            response.Results["EntityCollection"] = page;
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(value => value.Execute(request)).Returns(response);

            var actual = sdk.Object.ExecuteRetrieveMultiple(request);

            Assert.Same(page, actual);
            Assert.Same(query, request.Query);
            Assert.Equal("contact", query.EntityName);
            Assert.False(query.ColumnSet.AllColumns);
            Assert.Equal("fullname", Assert.Single(query.ColumnSet.Columns));
            Assert.Equal(7, query.PageInfo.PageNumber);
            Assert.Equal(17, query.PageInfo.Count);
            Assert.Equal("synthetic-query-cookie", query.PageInfo.PagingCookie);
            Assert.True((bool)request["ReturnNotifications"]);
            Assert.Equal(empty ? 0 : 2, actual.Entities.Count);
            Assert.True(actual.MoreRecords);
            Assert.Equal("synthetic-cookie", actual.PagingCookie);
            Assert.Equal(2, actual.TotalRecordCount);
            sdk.Verify(value => value.Execute(request), Times.Once);
            sdk.VerifyNoOtherCalls();
        }
    }
}

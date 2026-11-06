/*
 * 檔案整體說明
 *
 * 目的與定位：在原 host 先鎖定三個代表查詢及舊登入／資訊字串；只替代 SDK 網路，不以產品程式產生預期值。 全部欄位、ID 與內容均為合成，golden 固定條件／投影／排序，搬移後兩宿主必須保持一致。
 *
 * 主要功能與流程：
 * 本檔宣告 ContactCharacterizationTests。
 * - 合法同名但不同資料庫身份；測試必須保留而不能按姓名合併。
 * - LINE 單筆保持 Top=1、AllColumns、等值與啟用條件，回原正式 ID。
 * - 原奉獻前綴保留 ExecuteRetrieveMultiple、top=3、完整欄位及 pager 反向排序。
 * - 多條件奉獻查詢保留 OR/AND 邊界、六項條件及 top=100，不能漏掉狀態或改成 AND。
 * 測試案例入口包含 LineLookup_PreservesTypedQueryGolden、DedicationPrefix_PreservesFetchAndResponse、
 * DedicationSearch_PreservesFullGolden、LoginAndInformation_PreserveLegacyText、Create、Rows、FetchSdk。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.ContactOperations;
using Xunit;
#if NETFRAMEWORK
using Subject = ToolUtilityNameSpace.ContactOperations.ContactDataService;
#else
using Subject = ToolUtilityNameSpace.ContactOperations.ContactService;
#endif

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 在原 host 先鎖定三個代表查詢及舊登入／資訊字串；只替代 SDK 網路，不以產品程式產生預期值。
    /// 全部欄位、ID 與內容均為合成，golden 固定條件／投影／排序，搬移後兩宿主必須保持一致。
    /// </summary>
    public sealed class ContactCharacterizationTests
    {
        /// <summary>第一筆合成資料庫身份；只供固定斷言，不以顯示文字推導。</summary>
        internal static readonly Guid FirstId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        /// <summary>合法同名但不同資料庫身份；測試必須保留而不能按姓名合併。</summary>
        internal static readonly Guid SecondId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        /// <summary>LINE 單筆保持 Top=1、AllColumns、等值與啟用條件，回原正式 ID。</summary>
        [Fact]
        public void LineLookup_PreservesTypedQueryGolden()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
            {
                var query = Assert.IsType<QueryByAttribute>(input);
                GoldenQueryAssertions.Equal("contact-line.xml", new XElement("query",
                    new XAttribute("entity", query.EntityName), new XAttribute("top", query.TopCount!),
                    new XAttribute("all", query.ColumnSet.AllColumns),
                    query.Attributes.Select((field, index) => new XElement("condition",
                        new XAttribute("field", field), new XAttribute("value", query.Values[index])))).ToString());
                return Rows();
            });
            Assert.Equal(FirstId, Create(sdk.Object).RetrieveByLineId("synthetic-line")!.Id);
            sdk.VerifyAll();
        }

        /// <summary>原奉獻前綴保留 ExecuteRetrieveMultiple、top=3、完整欄位及 pager 反向排序。</summary>
        [Fact]
        public void DedicationPrefix_PreservesFetchAndResponse()
        {
            var sdk = FetchSdk("contact-prefix.xml");
            var result = Create(sdk.Object).QueryContatsByStartedDedicationNumber("D");
            Assert.Equal(FirstId, Assert.Single(result.Entities).Id);
            sdk.VerifyAll();
        }

        /// <summary>多條件奉獻查詢保留 OR/AND 邊界、六項條件及 top=100，不能漏掉狀態或改成 AND。</summary>
        [Fact]
        public void DedicationSearch_PreservesFullGolden()
        {
            var sdk = FetchSdk("contact-dedication.xml");
            var result = Create(sdk.Object).QueryDediccationContatsByFetchXml("D", "N", "H", "M", "I", "S");
            Assert.Equal(FirstId, Assert.Single(result.Entities).Id);
            sdk.VerifyAll();
        }

        /// <summary>舊文字屬公開相容契約，不得因國際化抽取而改登入結果或資訊標籤；只回 caller、不記錄。</summary>
        [Theory]
        [InlineData("missing", "帳號錯誤")]
        [InlineData("unset", "系統沒有設定密碼")]
        [InlineData("wrong", "密碼錯誤")]
        [InlineData("correct", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
        public void LoginAndInformation_PreserveLegacyText(string mode, string expected)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(() =>
            {
                if (mode == "missing") return new EntityCollection();
                var row = new Entity("contact", FirstId);
                if (mode != "unset") row["new_app_pass"] = "synthetic-password";
                return new EntityCollection(new[] { row });
            });
            var service = Create(sdk.Object);
            Assert.Equal(expected, service.AccountLogin("synthetic-account",
                mode == "correct" ? "synthetic-password" : "synthetic-wrong"));
            var infoSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            infoSdk.Setup(item => item.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(new EntityCollection(new[]
            {
                new Entity("contact", FirstId)
                {
                    ["fullname"] = "synthetic", ["build_customer_id"] = "C",
                    ["telephone1"] = "T", ["emailaddress1"] = "E"
                }
            }));
            Assert.Equal("姓名:synthetic" + Environment.NewLine + "客戶字號:C" + Environment.NewLine
                + "電話號碼:T" + Environment.NewLine + "電子郵件:E" + Environment.NewLine,
                Create(infoSdk.Object).GetContactInfoByContactId("C"));
        }

        /// <summary>兩宿主呼叫同一資料契約；搬前 net10 指向原 host，搬後 Framework 直接指向純 Core。</summary>
        internal static IContactService Create(IOrganizationService sdk) => new Subject(new object(), sdk);

        /// <summary>每次新建獨占的合成 SDK 結果，不以顯示內容作為正式身份。</summary>
        internal static EntityCollection Rows() => new EntityCollection(new[]
        {
            new Entity("contact", FirstId) { ["fullname"] = "synthetic" }
        });

        /// <summary>只在真實 SDK Execute 邊界檢查完整 golden，回應型別及 metadata 由測試 owner 持有至案例結束。</summary>
        private static Mock<IOrganizationService> FetchSdk(string file)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(item => item.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest input) =>
            {
                var request = Assert.IsType<RetrieveMultipleRequest>(input);
                GoldenQueryAssertions.Equal(file, Assert.IsType<FetchExpression>(request.Query).Query);
                var response = new RetrieveMultipleResponse();
                response.Results["EntityCollection"] = Rows();
                return response;
            });
            return sdk;
        }
    }
}

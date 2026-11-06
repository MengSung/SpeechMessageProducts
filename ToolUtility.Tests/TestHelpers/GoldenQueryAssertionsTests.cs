/*
 * 檔案整體說明
 *
 * 目的與定位：驗證搬移守門自身確實拒絕語義變更，不會只因 XML 能解析就放行。 所有內容為固定合成字串；測試不使用檔案系統、網路、真實識別碼或共用可變狀態。
 *
 * 主要功能與流程：
 * 本檔宣告 GoldenQueryAssertionsTests。
 * - 只改引號及縮排應通過；若 helper 又開始比較無語義空白，此案例會失敗。
 * - 注入欄位改名、遺漏排序或變更元素順序，均須斷言失敗，不能降低查詢黃金檔強度。
 * - 注入 DTD，即使實體是內嵌文字也拒絕解析；防止日後無意啟用外部實體解析。
 * - 注入超過 1 MiB 的短期合成 XML；reader 必須以容量錯誤拒絕，不能保留無界輸入。
 * 測試案例入口包含 Equal_OnlyWhitespaceAndQuoteStyleDiffer_Passes、Equal_QueryContractChanges_Rejects、
 * Equal_Dtd_RejectsBeforeComparison、Equal_OversizedDocument_RejectsBeforeComparison。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Xml;
using Xunit;
using Xunit.Sdk;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 驗證搬移守門自身確實拒絕語義變更，不會只因 XML 能解析就放行。
    /// 所有內容為固定合成字串；測試不使用檔案系統、網路、真實識別碼或共用可變狀態。
    /// </summary>
    public sealed class GoldenQueryAssertionsTests
    {
        /// <summary>
        /// 只改引號及縮排應通過；若 helper 又開始比較無語義空白，此案例會失敗。
        /// </summary>
        [Fact]
        public void Equal_OnlyWhitespaceAndQuoteStyleDiffer_Passes()
        {
            GoldenQueryAssertions.Equal("comparison-contract.xml",
                "<fetch><entity name='synthetic'><attribute name='syntheticid'/><order attribute='syntheticid' descending='false'/></entity></fetch>");
        }

        /// <summary>
        /// 注入欄位改名、遺漏排序或變更元素順序，均須斷言失敗，不能降低查詢黃金檔強度。
        /// </summary>
        /// <param name="actualXml">只有一個明確契約差異的合成查詢。</param>
        [Theory]
        [InlineData("<fetch><entity name='other'><attribute name='syntheticid'/><order attribute='syntheticid' descending='false'/></entity></fetch>")]
        [InlineData("<fetch><entity name='synthetic'><attribute name='syntheticid'/></entity></fetch>")]
        [InlineData("<fetch><entity name='synthetic'><order attribute='syntheticid' descending='false'/><attribute name='syntheticid'/></entity></fetch>")]
        public void Equal_QueryContractChanges_Rejects(string actualXml)
        {
            Assert.ThrowsAny<XunitException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml", actualXml));
        }

        /// <summary>
        /// 注入 DTD，即使實體是內嵌文字也拒絕解析；防止日後無意啟用外部實體解析。
        /// </summary>
        [Fact]
        public void Equal_Dtd_RejectsBeforeComparison()
        {
            Assert.Throws<XmlException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml",
                "<!DOCTYPE fetch [<!ENTITY value 'synthetic'>]><fetch><entity name='&value;'/></fetch>"));
        }

        /// <summary>
        /// 注入超過 1 MiB 的短期合成 XML；reader 必須以容量錯誤拒絕，不能保留無界輸入。
        /// </summary>
        [Fact]
        public void Equal_OversizedDocument_RejectsBeforeComparison()
        {
            var oversized = "<fetch>" + new string('x', 1024 * 1024) + "</fetch>";
            Assert.Throws<XmlException>(() => GoldenQueryAssertions.Equal("comparison-contract.xml", oversized));
        }
    }
}

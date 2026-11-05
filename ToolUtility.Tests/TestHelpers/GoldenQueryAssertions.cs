using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace ToolUtility.Tests.TestHelpers
{
    /// <summary>
    /// 比對內嵌的合成查詢黃金資料，鎖定跨目標搬移前後的 XML 條件樹。
    /// 不比較無語義的引號與縮排；元素、順序、屬性及欄位值必須完全相同。
    /// </summary>
    internal static class GoldenQueryAssertions
    {
        /// <summary>
        /// 以固定資源名稱讀取預期查詢，拒絕 DTD 與過大 XML 後比較正規化內容。
        /// 每次讀取獨立擁有資源串流及 reader，成功或斷言失敗皆確定釋放，不保存查詢資料。
        /// </summary>
        /// <param name="fileName">測試作者指定的資源檔名，不接受使用者輸入。</param>
        /// <param name="actualXml">受測服務實際送到 SDK 替身的 XML。</param>
        internal static void Equal(string fileName, string actualXml)
        {
            using (var stream = typeof(GoldenQueryAssertions).Assembly.GetManifestResourceStream(
                "ToolUtility.Tests.Golden.ToolUtility." + fileName))
            {
                Assert.NotNull(stream);
                using (var reader = XmlReader.Create(stream!, CreateSettings()))
                {
                    var expected = XDocument.Load(reader).ToString(SaveOptions.DisableFormatting);
                    using (var text = new StringReader(actualXml))
                    using (var actual = XmlReader.Create(text, CreateSettings()))
                    {
                        Assert.Equal(expected, XDocument.Load(actual).ToString(SaveOptions.DisableFormatting));
                    }
                }
            }
        }

        /// <summary>
        /// 建立單次 reader 的安全設定；固定上限足以容納測試查詢，禁止外部解析。
        /// </summary>
        private static XmlReaderSettings CreateSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreWhitespace = true,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            };
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using ToolUtility.Tests.TestHelpers;
using ToolUtilityNameSpace.Constants;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 鎖住21組既有欄位名稱及順序，再注入 caller 修改以證明不會污染下一個使用者的查詢。
    /// 只檢查結構名稱，不含業務資料；反射同時支援舊 field 與新 property 的來源相容搬移。
    /// </summary>
    public sealed class ColumnContractTests
    {
        /// <summary>把實際所有欄位定義完整投影後與搬前黃金檔比對，缺欄、改序、新增或刪除組都不能靜默通過。</summary>
        [Fact]
        public void ColumnsCharacterization_AllDefinitions_PreserveExactOrder()
        {
            var root = new XElement("columnSets");
            foreach (var type in typeof(CrmEntityColumns).GetNestedTypes().OrderBy(t => t.Name, StringComparer.Ordinal))
                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m is FieldInfo || m is PropertyInfo).OrderBy(m => m.Name, StringComparer.Ordinal))
                    root.Add(new XElement("set", new XAttribute("type", type.Name), new XAttribute("name", member.Name),
                        Read(type.Name, member.Name).Select(column => new XElement("column", column))));
            Assert.Equal(21, root.Elements().Count());
            GoldenQueryAssertions.Equal("columns-all.xml", root.ToString());
        }

        /// <summary>
        /// 修改操作A的陣列後，操作B必須仍取得原內容與不同陣列。舊版共享欄位的紅燈在 finally 還原，
        /// 不讓重現測試本身污染同程序其他案例；新版只還原caller獨占複本。
        /// </summary>
        /// <param name="type">固定的結構分類，不是動態租戶值。</param>
        /// <param name="member">固定的欄位集合名稱。</param>
        [Theory]
        [InlineData("Contact", "Basic")]
        [InlineData("Contact", "Extended")]
        [InlineData("Contact", "Full")]
        [InlineData("Contact", "FollowUp")]
        [InlineData("List", "Basic")]
        [InlineData("List", "Extended")]
        [InlineData("PresentRecord", "Basic")]
        [InlineData("PresentRecord", "Extended")]
        [InlineData("PresentRecord", "FollowUp")]
        [InlineData("WeeklyReport", "Basic")]
        [InlineData("WeeklyReport", "Extended")]
        [InlineData("DedicationBooking", "Basic")]
        [InlineData("DedicationBooking", "Extended")]
        [InlineData("Fee", "Basic")]
        [InlineData("Fee", "Extended")]
        [InlineData("StorLessons", "Basic")]
        [InlineData("StorLessons", "Extended")]
        [InlineData("Account", "Basic")]
        [InlineData("Account", "Extended")]
        [InlineData("Task", "Basic")]
        [InlineData("Task", "Extended")]
        public void ColumnsSafety_CallerMutation_DoesNotCrossBoundary(string type, string member)
        {
            var first = Read(type, member);
            var original = first[0];
            try
            {
                first[0] = "synthetic_caller_mutation";
                var second = Read(type, member);
                Assert.NotSame(first, second);
                Assert.Equal(original, second[0]);
            }
            finally { first[0] = original; }
        }

        /// <summary>一千個獨立操作同時讀改基本欄位；陣列只在外層測試Task內存活，全部await後不得留下污染。</summary>
        /// <returns>全部操作排空的Task；產品本身不建立任何非同步工作。</returns>
        [Fact]
        public async Task ColumnsSafety_ConcurrentCopies_RemainIndependent()
        {
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var columns = CrmEntityColumns.Contact.Basic;
                Assert.Equal("contactid", columns[0]);
                columns[0] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            })).ToArray();
            try
            {
                await Task.WhenAll(tasks);
                Assert.All(tasks, task => Assert.True(task.IsCompleted));
                Assert.Equal("contactid", CrmEntityColumns.Contact.Basic[0]);
            }
            finally { CrmEntityColumns.Contact.Basic[0] = "contactid"; }
        }

        /// <summary>只讀指定公開成員的實際陣列；不複製或過濾，否則會把產品的共享陣列缺陷藏掉。</summary>
        private static string[] Read(string typeName, string member)
        {
            var type = typeof(CrmEntityColumns).GetNestedType(typeName);
            Assert.NotNull(type);
            var value = type!.GetField(member)?.GetValue(null) ?? type.GetProperty(member)?.GetValue(null);
            return Assert.IsType<string[]>(value);
        }
    }
}

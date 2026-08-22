// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport.MemberInfo.Tests/Models/LegacyToolUtilityFactoryCollection.cs
// 測試責任：將會設定或清除 ToolUtilityFactory 程序級 static 狀態的模型測試序列化。
// 保護契約：不同測試不可在同一時間覆寫 configuration、tracer 或 ambient gateway，否則測試
//           可能把已釋放的服務提供者帶入其他案例，造成假的跨 scope 成功或非決定性失敗。
// 資源生命週期：Collection 本身不建立資源；各測試仍必須以 using/finally 擁有並釋放其 provider、
//               tracer 與 Factory 重設動作。
// 編碼要求：本檔案需維持 UTF-8 without BOM、CRLF 與 final CRLF。
// ============================================================================
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Models;

/// <summary>
/// 宣告 legacy Factory static 狀態的非平行測試集合。
/// </summary>
/// <remarks>
/// ToolUtilityFactory 是產品相容性所需的程序級單例，測試只能在限定集合內暫時設定它。
/// DisableParallelization 不改變生產程式並行能力，只避免測試裝置本身造成跨案例污染。
/// </remarks>
[CollectionDefinition("LegacyToolUtilityFactory", DisableParallelization = true)]
public sealed class LegacyToolUtilityFactoryCollection
{
}

/*
 * 檔案整體說明
 *
 * 目的與定位：宣告 legacy Factory static 狀態的非平行測試集合。
 *
 * 主要功能與流程：
 * 本檔宣告 LegacyToolUtilityFactoryCollection。
 * 資料／選項欄位包含 LegacyToolUtilityFactoryCollection，名稱、型別與序列化規約須與消費端保持一致。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
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

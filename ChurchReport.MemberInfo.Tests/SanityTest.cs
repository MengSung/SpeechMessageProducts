/*
 * 檔案整體說明
 *
 * 目的與定位：僅提供測試宿主可執行的最小True斷言，不能作為業務正確性、隔離或資源生命週期的驗證證據。
 *
 * 主要功能與流程：
 * 本檔宣告 SanityTest。
 * 測試案例入口包含 Sanity_TrueIsTrue。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using Xunit;
using FluentAssertions;

namespace ChurchReport.MemberInfo.Tests;

public class SanityTest
{
    [Fact]
    public void Sanity_TrueIsTrue()
    {
        true.Should().BeTrue();
    }
}

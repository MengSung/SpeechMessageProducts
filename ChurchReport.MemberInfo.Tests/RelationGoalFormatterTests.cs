/*
 * 檔案整體說明
 *
 * 目的與定位：驗證關係目標與角色配對保留順序、處理空角色及大小寫不敏感的重複配對，維持詳細頁與表格文字一致。
 *
 * 主要功能與流程：
 * 本檔宣告 RelationGoalFormatterTests。
 * 測試案例入口包含 Format_PreservesPairOrderAndDeduplicatesCaseInsensitively、
 * Format_MatchesMemberDetailTextWhenRoleIsBlank、Format_NullInputReturnsEmptyStrings。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using ChurchReport.Services.MemberInfo;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests;

public class RelationGoalFormatterTests
{
    [Fact]
    public void Format_PreservesPairOrderAndDeduplicatesCaseInsensitively()
    {
        var items = new[]
        {
            (Role: "妻子", TargetName: "小組長甲"),
            (Role: "門徒", TargetName: "小組長乙"),
            (Role: "妻子", TargetName: "小組長甲")
        };

        var result = RelationGoalFormatter.Format(items);

        result.Should().Be("妻子: 小組長甲、門徒: 小組長乙");
    }

    [Fact]
    public void Format_MatchesMemberDetailTextWhenRoleIsBlank()
    {
        var result = RelationGoalFormatter.Format(new[]
        {
            (Role: "", TargetName: "小組長甲"),
            (Role: "門徒", TargetName: "小組長乙"),
            (Role: "", TargetName: "")
        });

        result.Should().Be("小組長甲、門徒: 小組長乙");
    }

    [Fact]
    public void Format_NullInputReturnsEmptyStrings()
    {
        RelationGoalFormatter.Format(null).Should().BeEmpty();
    }
}

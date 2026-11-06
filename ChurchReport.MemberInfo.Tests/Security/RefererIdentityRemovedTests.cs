/*
 * 檔案整體說明
 *
 * 目的與定位：以來源契約確保從Referer推導LINE身分的舊入口已移除，防止可偽造header成為認證來源。
 *
 * 主要功能與流程：
 * 本檔宣告 RefererIdentityRemovedTests。
 * 測試案例入口包含 TryGetLineUserIdFromRequest_IsDeleted。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Reflection;
using ChurchReport.Controllers;
using FluentAssertions;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Security
{
    public class RefererIdentityRemovedTests
    {
        [Fact]
        public void TryGetLineUserIdFromRequest_IsDeleted()
        {
            var method = typeof(BaseChurchController).GetMethod(
                "TryGetLineUserIdFromRequest",
                BindingFlags.NonPublic | BindingFlags.Instance);

            method.Should().BeNull(
                "identity must never be derived from the client-controlled Referer header");
        }
    }
}

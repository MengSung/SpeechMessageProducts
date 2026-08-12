// ============================================================================
// 檔案：ChurchReport.MemberInfo.Tests/Payments/DonationFeeAuditAccessResolverTests.cs
// 目的：保護奉獻稽核 AJAX 端點的伺服器端授權邊界。
//
// 本測試只建立記憶體中的 CRM Entity 快照，不會發出 CRM 查詢、建立 Session、使用快取或保留
// 背景工作。受保護的契約是：瀏覽器傳來的目標 contact GUID 絕不可成為授權依據；只有已由
// 伺服器登入流程建立、具有有效識別與既有會計職稱的登入 contact 才能進入後續費用讀取。
// 每個 Entity 都只活在單一測試方法，藉此避免測試基礎設施本身遮蔽 A/B 隔離缺陷。
// ============================================================================

using ChurchReport.Services.Donation;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace ChurchReport.MemberInfo.Tests.Payments;

/// <summary>
/// 驗證奉獻稽核讀取的純授權解析器。
/// 解析器只能檢查伺服器已解析的登入 contact snapshot；它不得接收目標 contact ID、不得
/// 讀取 CRM、不得寫入 Session 或快取，因此在任何外部 I/O、profile 選擇或 DTO 查詢前，
/// 均可用固定成本明確拒絕缺少或不具會計職責的登入者。
/// </summary>
public sealed class DonationFeeAuditAccessResolverTests
{
    /// <summary>
    /// 有有效 server login contact ID 且職稱符合既有會計規則時，才允許進入奉獻稽核讀取。
    /// decisive assertion 是 resolver 不需要、更不接受瀏覽器指定的目標 GUID；測試注入的
    /// Entity 僅模擬登入流程已完成的 request-local snapshot，並沒有 CRM SDK I/O。
    /// </summary>
    [Fact]
    public void Server_accounting_login_snapshot_is_authorized()
    {
        var loginContact = new Entity("contact", Guid.NewGuid())
        {
            ["new_church_jobtitle"] = "會計同工"
        };

        DonationFeeAuditAccessResolver.CanAccessFeeAudit(loginContact)
            .Should().BeTrue();
    }

    /// <summary>
    /// 缺少 login snapshot、空白 CRM ID、缺少職稱或非會計職稱時必須全部 fail closed。
    /// 這些 fault injection 模擬 Session 尚未完成登入、過期或不具稽核權限；決定性斷言是
    /// 不會因為目標 contact GUID 格式正確而取得權限，避免 IDOR 在後續查詢前發生。
    /// </summary>
    [Fact]
    public void Missing_or_non_accounting_server_snapshot_is_denied()
    {
        var emptyId = new Entity("contact");
        var missingRole = new Entity("contact", Guid.NewGuid());
        var nonAccounting = new Entity("contact", Guid.NewGuid())
        {
            ["new_church_jobtitle"] = "小組長"
        };

        DonationFeeAuditAccessResolver.CanAccessFeeAudit(null).Should().BeFalse();
        DonationFeeAuditAccessResolver.CanAccessFeeAudit(emptyId).Should().BeFalse();
        DonationFeeAuditAccessResolver.CanAccessFeeAudit(missingRole).Should().BeFalse();
        DonationFeeAuditAccessResolver.CanAccessFeeAudit(nonAccounting).Should().BeFalse();
    }
}

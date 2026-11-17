/*
檔案整體說明
目的與定位：保護P7切換共用狀態表與組織分級優先序，使用純值合成組織，不建立SDK或檔案資源。
主要流程：逐列驗證off/shadow/live與live/dormant交叉結果，另注入相反模組與型別決定，斷言型別優先。
資源與隔離：資料只屬單次案例，沒有秘密、真實組織、共用可變集合或待排空資源；不能宣稱實機通過。
失敗判準：錯誤啟用dormant、Shadow留下金鑰佔位或低優先序蓋過型別決定都必須使案例失敗。
*/
using System;
using Xunit;
using SpeechMessage.Dynamics.Deployment.Switching;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以合成純值驗證唯一切換狀態函式；不依賴CLI或遠端服務。</summary>
public sealed class SwitchTargetTests
{
    /// <summary>完整狀態矩陣含有／無金鑰兩種步驟，避免僅驗預設值而漏掉安全設定清除。</summary>
    [Theory]
    [InlineData("off", "live", false, "Shadow", "")]
    [InlineData("off", "dormant", false, "Shadow", "")]
    [InlineData("shadow", "live", true, "Shadow", "")]
    [InlineData("shadow", "dormant", false, "Shadow", "")]
    [InlineData("live", "live", true, "Live", "${secret:TokenKey}")]
    [InlineData("live", "dormant", false, "Shadow", "")]
    public void SwitchTarget_StateAndTierTable_MatchesSpecification(string state, string tier, bool enabled, string mode, string secure)
    {
        var result = SwitchTarget.Resolve(state, tier, true);
        Assert.Equal(enabled, result.Enabled);
        Assert.Equal(mode, result.Mode);
        Assert.Equal(secure, result.SecureConfiguration);
        Assert.Equal("", SwitchTarget.Resolve(state, tier, false).SecureConfiguration);
    }

    /// <summary>模組live與型別dormant相衝突時，型別必勝；其他型別仍套模組、其他模組沿清單。</summary>
    [Fact]
    public void SwitchPlan_TierOverride_TypeBeatsModule()
    {
        var organization = new OrganizationDefinition("synthetic", "production", "synthetic.invalid", "off")
        {
            TierOverrides = Array.AsReadOnly(new[]
            {
                new OrganizationTierOverride("lessons", "live", "decision", "使用者", "time"),
                new OrganizationTierOverride("Example.Activity", "dormant", "decision", "使用者", "time")
            })
        };
        Assert.Equal("dormant", SwitchTarget.EffectiveTier(organization, "lessons", "Example.Activity", "live"));
        Assert.Equal("live", SwitchTarget.EffectiveTier(organization, "lessons", "Example.Other", "dormant"));
        Assert.Equal("dormant", SwitchTarget.EffectiveTier(organization, "care", "Example.Other", "dormant"));
    }

    /// <summary>非法state/tier不得默認off掩蓋輸入錯誤，應在任何遠端操作前失敗。</summary>
    [Theory]
    [InlineData("enabled", "live")]
    [InlineData("off", "unknown")]
    public void InvalidStateOrTier_FailsClosed(string state, string tier) =>
        Assert.Throws<System.IO.InvalidDataException>(() => SwitchTarget.Resolve(state, tier, false));
}

/*
檔案整體說明
目的與定位：階段7組織切換唯一狀態表，供plan、執行及verify共同推導步驟的啟用、mode與金鑰佔位。
主要流程：先驗state與有效tier，再純值計算五列表；EffectiveTier以完整型別決定優先模組決定，再沿清單預設。
安全與範圍：不讀秘密、不接受真金鑰、不建立連線或授權；diagnostics由呼叫端排除，不參與此表。
資源生命週期：只回傳本次命令的不可變純值，沒有SDK、可變共用快取、訂閱或需要Dispose的資源。
失敗處理與驗證：不認得的state/tier fail closed，不用off掩蓋錯誤；SwitchTargetTests逐列驗證及衝突override。
*/
using System;
using System.IO;
using System.Linq;
namespace SpeechMessage.Dynamics.Deployment.Switching;

/// <summary>單步驟的切換期望純值；安全欄位只有固定佔位或空字串，不包含秘密內容。</summary>
/// <param name="Enabled">是否應啟用此新步驟。</param>
/// <param name="Mode">唯一核定的Shadow或Live模式。</param>
/// <param name="SecureConfiguration">TokenKey佔位或空字串。</param>
internal sealed record SwitchTarget(bool Enabled, string Mode, string SecureConfiguration)
{
    /// <summary>驗證完整state/tier後推導五列表；所有Shadow與dormant路徑必須沒有金鑰。</summary>
    internal static SwitchTarget Resolve(string state, string tier, bool tokenKey)
    {
        if (state is not ("off" or "shadow" or "live") || tier is not ("live" or "dormant"))
            throw new InvalidDataException("Invalid switch state or tier.");
        var enabled = state != "off" && tier == "live";
        var live = enabled && state == "live";
        return new SwitchTarget(enabled, live ? "Live" : "Shadow", live && tokenKey ? "${secret:TokenKey}" : "");
    }

    /// <summary>完整型別優先模組再預設；組織決定已由catalog驗證，本函式不提升或推測授權。</summary>
    internal static string EffectiveTier(OrganizationDefinition organization, string module, string type, string defaultTier)
    {
        ArgumentNullException.ThrowIfNull(organization);
        var tier = organization.TierOverrides.SingleOrDefault(item => item.Target == type)?.Tier
            ?? organization.TierOverrides.SingleOrDefault(item => item.Target == module)?.Tier ?? defaultTier;
        _ = Resolve("off", tier, false);
        return tier;
    }
}

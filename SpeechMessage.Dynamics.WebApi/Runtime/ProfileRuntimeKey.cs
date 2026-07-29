// ============================================================================
// 檔案：SpeechMessage.Dynamics.WebApi/Runtime/ProfileRuntimeKey.cs
// 目的：提供不含秘密、使用者或 Session 的 Profile Generation 身分，供 readiness、diagnostics、
//       replace-and-drain 與 execution lease 比對使用。
// ============================================================================

using SpeechMessage.Dynamics.WebApi.Capacity;

namespace SpeechMessage.Dynamics.WebApi.Runtime;

/// <summary>
/// 一個不可變 Dynamics Profile Generation 的非秘密身分。
/// Key 只包含伺服器端核准的 Alias、單調遞增 Generation、CE 版本與 Canonical Organization Identity；
/// 不得加入 User、LINE ID、Browser Session、JWT、Access／Refresh Token、Password、Credential Reference、
/// Correlation ID 或 Request 參數，避免 Runtime Pool／Cache 因終端身分而跨 Session 保留資料。
/// </summary>
/// <param name="ProfileAlias">由部署設定擁有且已通過嚴格語法驗證的 Profile Alias。</param>
/// <param name="Generation">同一 Alias 內單調遞增的 Runtime Generation 編號。</param>
/// <param name="CeVersion">明確固定的 CE 版本，目前只允許 8.2 或 9.1。</param>
/// <param name="CanonicalOrganizationKey">實體 Organization GUID 與正規化 Base URI 的 Typed Tuple。</param>
public readonly record struct ProfileRuntimeKey(
    string ProfileAlias,
    long Generation,
    string CeVersion,
    CanonicalOrganizationCapacityKey CanonicalOrganizationKey)
{
    /// <summary>
    /// 產生 bounded、非秘密的診斷字串。此格式只供記錄與測試，不可作 Durable Store Key；
    /// 跨程序持久化必須使用另外版本化、長度前綴的 Canonical Encoding。
    /// </summary>
    public override string ToString()
        => $"{ProfileAlias}@{Generation}:{CeVersion}:{CanonicalOrganizationKey}";
}

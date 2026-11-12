/*
 * 檔案整體說明
 *
 * 目的與定位：提供 GoodShepherd 費用流程以教會 account 正式 ID 讀取永豐設定檔名稱的唯一入口。
 * 本類只從 account.new_sinopacprofile 讀取一個欄位，不讀教會名稱、其他教會資料或呼叫端 payload；
 * 取得的 profile 名稱仍由付款核心 resolver 在 Payment:Profiles 中驗證，不在此處選預設值。
 *
 * 主要功能與流程：呼叫端傳入 server-owned churchId 與借用的 IOrganizationService，方法先檢查取消、
 * 空 ID，再以 Retrieve(account, churchId, ColumnSet(new_sinopacprofile)) 取得該筆欄位。
 * 僅回傳 1 至 64 個 ASCII 英數、底線、連字號或句點組成的名稱；空值、錯誤型別、錯置實體／ID
 * 或非法名稱以固定訊息拒絕，不把教會名稱、欄位內容或個資放進例外。
 *
 * 隔離與生命週期：類別沒有可變靜態狀態、快取或跨呼叫資料；每次只處理傳入的一個教會 ID，
 * 局部 Entity 與 profile 字串在同步呼叫返回後即離開本方法。IOrganizationService 由呼叫端擁有，
 * 本方法不 Dispose、不建立背景工作；SDK 同步 Retrieve 無法中途中斷，故在呼叫前後檢查 caller token。
 * D365 暫時性故障原樣傳遞，不以 catch-all 轉成設定錯誤，確保呼叫端可依真實故障分類。
 *
 * 維護與驗證：不可改成 RetrieveMultiple、讀取 caller 指定欄位、Trim 名稱或回退 DefaultProfile。
 * 以雙目標 ChurchPaymentProfileTests 驗證 account A/B 隔離、100 次交錯並行、缺失 ID、名稱文法
 * 與單欄查詢；這些合成測試不代表已連線或修改 D365。
 */
using System;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace SpeechMessageProducts.GoodShepherd.Core.Fees;

/// <summary>
/// 依 server-owned 教會 account ID 讀取該教會所選的永豐 profile 名稱。
/// 不保存資料、不選預設商店，也不自行建立或釋放 SDK 連線。
/// </summary>
public static class ChurchPaymentProfile
{
    private const string ProfileAttribute = "new_sinopacprofile";

    /// <summary>
    /// 只從指定 account 讀取永豐 profile 名稱並驗證其 ASCII 文法；無值或格式錯誤時 fail closed。
    /// 傳入的服務由呼叫端持有；同步 SDK 呼叫不能被 token 中斷，因此在呼叫前後檢查取消狀態。
    /// </summary>
    /// <param name="sdk">呼叫端擁有並借用於本次讀取的 Dataverse 服務。</param>
    /// <param name="churchId">由伺服器待辦／領域資料解析出的正式 account ID，不接受 payload 作為授權依據。</param>
    /// <param name="ct">呼叫端取消權杖；取消不會中止已進入的同步 SDK 呼叫，但返回後不交付 profile 名稱。</param>
    /// <returns>該教會欄位中的有效設定檔名稱；結果不快取且僅屬本次呼叫。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sdk"/> 為 null。</exception>
    /// <exception cref="InvalidOperationException">教會 ID 或欄位資料缺漏、回應身份錯置或設定檔名稱不符合文法。</exception>
    /// <exception cref="OperationCanceledException">caller token 在 SDK 呼叫前或返回後已取消。</exception>
    public static string Read(IOrganizationService sdk, Guid churchId, CancellationToken ct)
    {
#if NET10_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(sdk);
#else
        // net462 尚未提供 ThrowIfNull，保留相同的參數失敗契約。
        if (sdk is null)
        {
            throw new ArgumentNullException(nameof(sdk));
        }
#endif
        if (churchId == Guid.Empty)
        {
            throw new InvalidOperationException("Church payment profile is unavailable.");
        }

        ct.ThrowIfCancellationRequested();
        var account = sdk.Retrieve("account", churchId, new ColumnSet(ProfileAttribute));
        ct.ThrowIfCancellationRequested();

        if (account is null || account.LogicalName != "account" || account.Id != churchId
            || !account.Attributes.TryGetValue(ProfileAttribute, out var rawProfile)
            || rawProfile is not string profileName
            || !HasValidProfileName(profileName))
        {
            throw new InvalidOperationException("Church payment profile is unavailable.");
        }

        return profileName;
    }

    /// <summary>以有界逐字元檢查保證只有 ASCII 字母、數字、底線、連字號與句點，且不正規化或保留輸入。</summary>
    private static bool HasValidProfileName(string value)
    {
        if (value.Length is < 1 or > 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is >= 'A' and <= 'Z'
                or >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '_' or '-' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}

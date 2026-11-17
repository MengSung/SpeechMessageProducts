/*
 * 檔案整體說明
 * 目的與定位：定義階段7註冊清單的切換分級及Live實機證據摘要，供loader完整驗證後
 * 交給後續plan/switch/verify使用；diagnostics不參與此模型。步驟與活動使用不同型別，
 * 使活動攜帶tokenKey時由嚴格JSON反序列化立即拒絕，不以false或null隱藏未知欄位。
 * 主要流程：讀取tier、提案理由與檔名/雜湊/時間/結果四項安全證據；本檔只保存純資料，
 * 不解析秘密、不連線、不讀取證據檔。空結果代表未驗收，不能當作Live通過或組織授權。
 * 邊界與生命週期：模型只屬一次命令的結構候選，沒有SDK、HttpContext、Session、憑證、
 * 可變共用集合、快取或背景資源；不擁有Dispose資源。分級不取代伺服器端組織及現況核對。
 * 驗證：ManifestLiveTests以完整控制組注入缺欄與偽證據，斷言loader fail closed；
 * 本模型的pass文字只是一筆證據宣告，正式切換仍須依後續plan的完整安全關卡判定。
 */
using System.Text.Json.Serialization;

namespace SpeechMessage.Dynamics.Deployment;

/// <summary>活動的純資料切換分級；只有完整候選通過loader後才發布，沒有執行中的組織狀態。</summary>
internal record LiveRegistration
{
    /// <summary>只能是live或dormant；前置盤點可降級，不因空證據自動升級。</summary>
    [JsonRequired]
    public string Tier { get; init; } = "live";

    /// <summary>dormant必須以提案編號說明原因；不保存會友、流程本文或秘密。</summary>
    [JsonRequired]
    public string Reason { get; init; } = "";

    /// <summary>單項實機報告的安全定位資訊，預設尚未驗收，不代表零差異。</summary>
    [JsonRequired]
    public LiveEvidence Evidence { get; init; } = new();
}

/// <summary>步驟另有安全設定需求旗標；此布林只宣告是否需要金鑰佔位，不能攜帶金鑰值。</summary>
internal sealed record StepLiveRegistration : LiveRegistration
{
    /// <summary>Live時需由部署寫入瞬間解析TokenKey的步驟為true；停用或Shadow仍須清空安全設定。</summary>
    [JsonRequired]
    public bool TokenKey { get; init; }
}

/// <summary>檔名、SHA-256與台北時間的不可變摘要；無原始trace、個資、訊息內容或可釋放資源。</summary>
internal sealed record LiveEvidence
{
    /// <summary>本機實機報告位置，只作證據定位，不在本模型開檔或執行路徑。</summary>
    [JsonRequired]
    public string File { get; init; } = "";

    /// <summary>通過時為64位十六進位SHA-256，供後續核對原始證據，不能當作加密或匿名化。</summary>
    [JsonRequired]
    public string Sha256 { get; init; } = "";

    /// <summary>實際驗證時間採YYYY-MM-DD HH:mm Asia/Taipei (UTC+08:00)，不得填入猜測時間。</summary>
    [JsonRequired]
    public string At { get; init; } = "";

    /// <summary>只有空白字串或pass；空白表示未驗收，失敗項目須保留診斷並維持dormant。</summary>
    [JsonRequired]
    public string Result { get; init; } = "";
}

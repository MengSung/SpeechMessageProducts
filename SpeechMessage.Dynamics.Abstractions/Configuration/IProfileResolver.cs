namespace SpeechMessage.Dynamics.Abstractions.Configuration;

/// <summary>
/// 定義以 Profile Alias 查詢部署端不可變 Profile generation 的界面。解析不得建立 CRM
/// 連線、Worker、Permit、Timer 或背景工作；失敗時只回傳穩定錯誤碼，讓呼叫端可在任何資源
/// 所有權開始前 fail-closed。
/// </summary>
public interface IProfileResolver
{
    /// <summary>
    /// 嘗試解析 Profile Alias。成功時輸出不可變 snapshot；失敗時 profile 為 null，且 error
    /// 為可安全記錄的穩定分類碼，不包含端點、Credential、Token 或 Organization GUID。
    /// </summary>
    bool TryResolve(string profileAlias, out ResolvedProfile? profile, out string error);
}

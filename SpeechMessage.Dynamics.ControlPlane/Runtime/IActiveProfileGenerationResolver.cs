namespace SpeechMessage.Dynamics.ControlPlane.Runtime;

/// <summary>
/// 提供 deployment-owned ProfileAlias 對應之目前 Active Runtime generation 的唯讀查詢。
/// 查詢只回傳不含秘密的 <see cref="ProfileRuntimeKey"/>，不會建立 Worker、Pipe、Permit、Timer、
/// Credential 或 Session，也不會讓呼叫端取得 Runtime 物件的強引用。
/// </summary>
public interface IActiveProfileGenerationResolver
{
    /// <summary>
    /// 取得指定 Alias 目前可接受新工作之 Active generation。未知、未 Ready、Draining 或已停止 Alias
    /// 一律回傳 false，讓後續 Connector Router 在任何資源取得前 fail closed。
    /// </summary>
    /// <param name="profileAlias">已由服務端授權的 Profile Alias。</param>
    /// <param name="key">成功時回傳 bounded、非秘密的 Runtime key。</param>
    /// <returns>是否存在可接受新工作且仍由 Manager 擁有的 Active generation。</returns>
    bool TryGetActiveRuntimeKey(string profileAlias, out ProfileRuntimeKey key);
}

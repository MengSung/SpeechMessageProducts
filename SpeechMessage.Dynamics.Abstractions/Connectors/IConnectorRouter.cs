using SpeechMessage.Dynamics.Abstractions.Configuration;

namespace SpeechMessage.Dynamics.Abstractions.Connectors;

/// <summary>
/// 依部署端已解析的 <see cref="ResolvedProfile"/> 路由至對應的 Connector Pool。
/// Router 只能相信 ProfileResolver 的輸出，不接受請求指定 Connector、端點、Credential 或 OrganizationId，
/// 並且對未登錄或不相容 ConnectorKind 必須 fail closed，絕不可自動 fallback。
/// </summary>
public interface IConnectorRouter
{
    /// <summary>解析 Profile Generation 對應的 Pool；找不到時必須拋出受控失敗而不是改用其他 Connector。</summary>
    IConnectorPool Resolve(ResolvedProfile profile);
}

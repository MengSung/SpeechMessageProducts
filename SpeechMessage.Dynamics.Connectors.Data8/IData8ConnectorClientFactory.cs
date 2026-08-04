using SpeechMessage.Dynamics.Abstractions.Configuration;
using SpeechMessage.Dynamics.Abstractions.Connectors;

namespace SpeechMessage.Dynamics.Connectors.Data8;

/// <summary>
/// 定義建立 Data8 Connector Client 的組合根接點。
/// Factory 可在受控部署邊界內解析 CredentialReference 並建立 OnPremiseClient，但 Pool 僅接收 SDK-free Client；
/// 因此 Credential、Token、WCF 型別與連線建立細節不會進入 Abstractions、Pool Key 或 idle queue。
/// </summary>
public interface IData8ConnectorClientFactory
{
    /// <summary>
    /// 為指定且不可變的 Profile Generation 建立一個新的 Client。
    /// 若建立失敗或取消，Factory 必須自行回復其已擁有的中間資源；成功後 Client 的唯一 Dispose 責任轉移至 Lease。
    /// </summary>
    Task<IConnectorClient> CreateAsync(ResolvedProfile profile, CancellationToken cancellationToken);
}

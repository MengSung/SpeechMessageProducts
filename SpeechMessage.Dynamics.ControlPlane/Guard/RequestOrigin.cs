namespace SpeechMessage.Dynamics.ControlPlane.Guard;

/// <summary>
/// 宣告進入共用 RequestGuard 的主機邊界。來源僅供一致套用規則與後續審計分類；它不能攜帶
/// 呼叫者 Session、Credential 或 endpoint，也不能改變 Profile/Connector 的部署端選擇。
/// </summary>
public enum RequestOrigin
{
    /// <summary>產品內 Embedded Host Adapter。</summary>
    Embedded = 0,

    /// <summary>產品專屬 Dedicated Gateway。</summary>
    DedicatedGateway = 1,

    /// <summary>多產品共用 Central Gateway。</summary>
    CentralGateway = 2
}

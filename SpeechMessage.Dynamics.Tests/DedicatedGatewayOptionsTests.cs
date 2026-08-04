using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SpeechMessage.Dynamics.Gateway;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 驗證 Dedicated Gateway 的 deployment-owned host 選擇契約。
/// 這些測試只讀取記憶體組態，絕不啟動 Kestrel、SQL、Worker 或 Data8 client；保護的核心是 Dedicated
/// 模式不能因為缺少或含糊的組態而偷偷退回 Official Worker 或 SQL coordinator。
/// </summary>
public sealed class DedicatedGatewayOptionsTests
{
    /// <summary>
    /// 保護 Dedicated 模式的明確 opt-in。故障注入為缺少 ProfileAlias；決定性斷言是驗證直接失敗，
    /// 因此 Gateway 不會在不知道要擁有哪一個 profile runtime 的情況下啟動或保留跨 profile 狀態。
    /// </summary>
    [Fact]
    public void BindAndValidate_rejects_dedicated_mode_without_a_profile_alias()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsGateway:DeploymentMode"] = "DedicatedGateway"
            })
            .Build();

        var act = () => DedicatedGatewayOptions.BindAndValidate(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ProfileAlias*");
    }

    /// <summary>
    /// 保護 Dedicated 模式不得接受未知 deployment mode。故障注入為 OfficialWorker 字串；這可避免
    /// P5 的本機 Data8 host 在設定錯誤時意外建立 worker process、SQL coordinator 或其資源擁有者。
    /// </summary>
    [Fact]
    public void BindAndValidate_rejects_any_mode_other_than_dedicated_gateway()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicsGateway:DeploymentMode"] = "OfficialWorker",
                ["DynamicsGateway:Dedicated:ProfileAlias"] = "sunnyvalechback"
            })
            .Build();

        var act = () => DedicatedGatewayOptions.BindAndValidate(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DedicatedGateway*");
    }
}

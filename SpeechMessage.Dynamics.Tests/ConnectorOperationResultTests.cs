using FluentAssertions;
using SpeechMessage.Dynamics.Abstractions.Connectors;
using SpeechMessage.Dynamics.Abstractions.Operations;

namespace SpeechMessage.Dynamics.Tests;

/// <summary>
/// 固定 Connector Pool/Lease 可承載的唯一成功資料仍是既有封閉 <see cref="OperationResponseData"/>。
/// 此測試不建立 connector、Worker 或 CRM 呼叫；它防止 P6 為了接入 Official Worker 而新增 raw SDK/
/// JSON/endpoint payload 通道，確保資料與資源都在既有 typed response boundary 內結束。
/// </summary>
public sealed class ConnectorOperationResultTests
{
    /// <summary>
    /// 驗證成功 Connector 結果可攜帶已驗證的 WhoAmI response branch，而非要求 Official Worker 把
    /// 受控 DTO 降級成無類型 dictionary。決定性斷言是同一封閉 instance 被保留，沒有額外序列化、
    /// stream、SDK object 或可變 transport state。
    /// </summary>
    [Fact]
    public void Result_can_carry_a_bounded_operation_response_data_branch()
    {
        var data = OperationResponseData.ForWhoAmI(
            OperationIds.RuntimeHealthWhoAmI,
            "9.1",
            new WhoAmIResponseData
            {
                UserId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                BusinessUnitId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                OrganizationId = Guid.Parse("33333333-3333-3333-3333-333333333333")
            });

        var result = new ConnectorOperationResult(true) { Data = data };

        result.Data.Should().BeSameAs(data);
    }
}

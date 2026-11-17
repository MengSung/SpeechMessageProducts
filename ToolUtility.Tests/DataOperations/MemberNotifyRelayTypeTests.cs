/*
 * 檔案整體說明
 * 目的與定位：保護07A第四種MemberNotifyRelay待辦的雙目標純資料契約，避免新增類型時
 * 放寬未知選項或文字payload白名單。只測真OutboundTaskCommand/Record，不連D365或LINE。
 * 主要流程：以固定選項100000003建立四欄合成payload及完整正式快照，斷言類型/事件鍵；
 * 另以未核定選項與文字故障注入，確認先拒絕候選，不把任意文字或新增第五類偷偷放行。
 * 使用邊界與維護：每案只擁有純值與本機字典，無SDK、秘密、共享狀態、timer或背景工作；
 * 沒有需Dispose資源，案例結束即釋放候選。這是離線格式測試，不是實機選項新增或入列證據。
 */
using System;
using System.Collections.Generic;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace ToolUtility.Tests.DataOperations;

/// <summary>同時在net462產品/net48宿主及net10產品驗證新增類型，保持原有拒絕邊界。</summary>
public sealed class MemberNotifyRelayTypeTests
{
    /// <summary>合法第四類可建立完整command與record，保留四欄數值/識別碼及同教會事件鍵。</summary>
    [Fact]
    public void OutboundTask_MemberNotifyRelay_TypeAccepted()
    {
        var command = Command(Payload());
        Assert.Equal(100000003, (int)command.Type);
        var record = new OutboundTaskRecord(Guid.NewGuid(), "1", command.ChurchId, null,
            command.Type, command.PayloadJson, command.IdempotencyKey, OutboundTaskStatus.Pending, 0, null, null);
        Assert.Equal(command.Type, record.Type);
        Assert.Contains("-100000003-", record.IdempotencyKey, StringComparison.Ordinal);
        Assert.Contains("\"kind\":3", record.PayloadJson, StringComparison.Ordinal);
    }

    /// <summary>未知第五類仍拒絕；不得用Enum範圍或單一大於最小值檢查放寬閉合集合。</summary>
    [Fact]
    public void OutboundTask_UnknownType_StillRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundTaskCommand((OutboundTaskType)100000004,
            Guid.NewGuid(), null, Guid.NewGuid(), Payload()));
    }

    /// <summary>先接受相同合法四欄控制組，再把route換文字，確定拒絕原因是payload而非不認得類型。</summary>
    [Fact]
    public void OutboundTask_RelayPayload_RejectsText()
    {
        var payload = Payload();
        _ = Command(payload);
        payload["route"] = "synthetic-message-body";
        var failure = Assert.Throws<ArgumentException>(() => Command(payload));
        Assert.DoesNotContain("synthetic-message-body", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>每案建立獨立四欄數值/識別碼，不含文字訊息、個資或機密。</summary>
    private static Dictionary<string, object> Payload() => new()
    {
        ["kind"] = 3, ["recordId"] = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        ["message"] = 1, ["route"] = 0
    };

    /// <summary>使用獨立合成教會與來源事件，不把來源ID冒充待辦正式ID或授權資料。</summary>
    private static OutboundTaskCommand Command(IReadOnlyDictionary<string, object> payload) =>
        new((OutboundTaskType)100000003, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), null,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), payload);
}

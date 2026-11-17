/*
檔案整體說明
目的與定位：P7觀察唯讀計數契約，以合成系統作業／trace／待辦保護輸出隱私與不可用資料語意。
主要流程：真reader只能取核定metadata及counts，SDK列含敏感sentinel但回傳JSON不能含名稱、GUID或本文。
資源與隔離：每案獨占假SDK，無網路、真組織或秘密；使用唯讀裝飾器的八個入口防止觀察路徑意外寫入。
決定性斷言：精確數量、trace unavailable不是0、production觀察可用且所有寫入為0，不把離線結果當實機。
*/
using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using SpeechMessage.Dynamics.Deployment.Live;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以實際結果純值驗證counts，欄位本文即使額外返回也不得出現在報告。</summary>
public sealed class ObservationReaderTests
{
    /// <summary>只計核定step與時間窗，其他組件作業不混入；輸出不得含敏感本文或正式記錄GUID。</summary>
    [Fact]
    public void Observe_CountsOnly_NoRecordValuesInOutput()
    {
        using var service = new MemoryOrganization();
        var step = Guid.NewGuid(); var org = Guid.NewGuid(); var now = DateTimeOffset.Parse("2026-11-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        service.Seed(new Entity("organization", org) { ["plugintracelogsetting"] = new OptionSetValue(2) });
        service.Seed(new Entity("asyncoperation", Guid.NewGuid()) { ["owningextensionid"] = new EntityReference("sdkmessageprocessingstep", step),
            ["statecode"] = new OptionSetValue(3), ["statuscode"] = new OptionSetValue(31), ["createdon"] = now.UtcDateTime, ["message"] = "SENTINEL-SECRET-BODY" });
        service.Seed(new Entity("asyncoperation", Guid.NewGuid()) { ["owningextensionid"] = new EntityReference("sdkmessageprocessingstep", Guid.NewGuid()),
            ["statecode"] = new OptionSetValue(3), ["statuscode"] = new OptionSetValue(31), ["createdon"] = now.UtcDateTime });
        service.Seed(new Entity("new_outboundtask", Guid.NewGuid()) { ["new_type"] = new OptionSetValue(100000003), ["new_taskstatus"] = new OptionSetValue(100000003), ["createdon"] = now.AddMinutes(-40).UtcDateTime, ["new_payload"] = "SENTINEL-SECRET-BODY" });
        var report = ObservationReader.Read(service, "production", org, now.AddHours(-1), now, new[] { new ObservationStep(step, "Synthetic.Type") }, Array.Empty<Guid>(), true);
        Assert.Equal(1, Assert.Single(report.Steps).Failed);
        Assert.Equal(1, Assert.Single(report.Tasks).Count);
        Assert.DoesNotContain("SENTINEL", JsonSerializer.Serialize(report), StringComparison.Ordinal);
        Assert.DoesNotContain(step.ToString("D"), JsonSerializer.Serialize(report), StringComparison.Ordinal);
        Assert.Empty(service.Writes);
    }

    /// <summary>production不受測試種別限制；唯讀wrapper所有寫入及未知Execute都直接拒絕。</summary>
    [Fact]
    public void Observe_ProductionAllowed_ReadOnlyNoWrites()
    {
        using var service = new MemoryOrganization();
        var org = Guid.NewGuid();
        service.Seed(new Entity("organization", org) { ["plugintracelogsetting"] = new OptionSetValue(0) });
        var now = DateTimeOffset.Parse("2026-11-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var report = ObservationReader.Read(service, "production", org, now.AddMinutes(-1), now, Array.Empty<ObservationStep>(), Array.Empty<Guid>(), true);
        Assert.True(report.MutualExclusionPassed);
        Assert.Empty(service.Writes);
        var wrapper = new ReadOnlyOrganizationService(service);
        Assert.Throws<InvalidOperationException>(() => wrapper.Create(new Entity("account")));
        Assert.Throws<InvalidOperationException>(() => wrapper.Execute(new OrganizationRequest("SetState")));
    }

    /// <summary>trace關閉為null與available=false；開啟且查詢成功的空結果才是available=true／0。</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(2, true)]
    public void Observe_TraceUnavailable_ReportsNoDataNotZero(int setting, bool available)
    {
        using var service = new MemoryOrganization(); var org = Guid.NewGuid();
        service.Seed(new Entity("organization", org) { ["plugintracelogsetting"] = new OptionSetValue(setting) });
        var now = DateTimeOffset.Parse("2026-11-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var report = ObservationReader.Read(service, "test", org, now.AddMinutes(-1), now, Array.Empty<ObservationStep>(), Array.Empty<Guid>(), true);
        Assert.Equal(available, report.TraceAvailable);
        Assert.Equal(available ? 0 : (int?)null, report.TraceExceptions);
    }
}

/*
檔案整體說明
目的與定位：P7實際切換執行器離線回復契約，記憶體SDK保存正式ID／設定／XAML，任何舊流程啟用都只發生在合成替身。
主要流程：planner產計畫後executor實際修改SDK；注入中途故障、送達後故障與取消，再讀回完整原始狀態。
邊界與生命週期：每案獨占temp根、SDK、取消來源；finally刪除本案檔案，無網路、真秘密或背景工作。
決定性斷言：設定、secure列、啟用狀態、原始XAML逐字相同，原故障保留，hash／過期拒絕不得有任何寫入。
*/
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Switching;
using Xunit;
namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以真executor與假SDK斷言寫入及回復；替身不包含產品規劃邏輯。</summary>
public sealed class SwitchExecutorTests
{
    /// <summary>整體切換再回復必須等於匯出原況，且不將合成秘密寫進任何plan/log。</summary>
    [Fact]
    public void Switch_ThenRollback_StateEqualsExport()
    {
        using var f = new RunFixture();
        var before = f.State();
        var log = f.Execute();
        Assert.Equal(0, f.Step.GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Equal(1, f.OldStep.GetAttributeValue<OptionSetValue>("statecode").Value);
        var evidence = SwitchLog.Read(log);
        Assert.All(evidence.Entries, entry => { Assert.NotNull(entry.BeforeState); Assert.NotNull(entry.AfterState); });
        f.Executor.Rollback(log, "synthetic");
        Assert.Equal(before, f.State());
        Assert.All(Directory.GetFiles(f.Root), path => Assert.DoesNotContain(Fixture.SecretValue, File.ReadAllText(path), StringComparison.Ordinal));
    }

    /// <summary>核定的檔案hash不符必須在備份或SDK更新前拒絕。</summary>
    [Fact]
    public void Switch_UnreviewedPlanHashMismatch_Rejected()
    {
        using var f = new RunFixture();
        Assert.Throws<InvalidDataException>(() => f.Executor.Execute(f.Plan, "bad", f.Bytes));
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>伺服器或metadata改變使鎖內重讀不同，過期計畫不可觸發任何寫入。</summary>
    [Fact]
    public void Switch_StalePlanOrganizationChanged_Rejected()
    {
        using var f = new RunFixture();
        f.Step["statecode"] = new OptionSetValue(0);
        Assert.Throws<InvalidDataException>(() => f.Execute());
        Assert.Empty(f.Service.Writes);
    }

    /// <summary>已送達SDK後才拋出故障，write-ahead紀錄仍須回復該次操作及更早操作。</summary>
    [Theory]
    [InlineData("Create:sdkmessageprocessingstepsecureconfig")]
    [InlineData("Update:sdkmessageprocessingstep")]
    [InlineData("Update:workflow")]
    [InlineData("SetState:workflow")]
    public void Switch_FailureMidRun_RollsBackExecutedOperations(string point)
    {
        using var f = new RunFixture();
        var before = f.State();
        f.Fault.Point = point;
        Assert.Throws<InvalidOperationException>(() => f.Execute());
        Assert.Equal(before, f.State());
        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepsecureconfig"));
    }

    /// <summary>取消由SDK送達後觸發；回復以None token讀取與寫入，結束後原OCE仍回傳。</summary>
    [Fact]
    public void Switch_CancelledMidRun_RollsBackWithUncancelledToken()
    {
        using var f = new RunFixture();
        using var cancellation = new CancellationTokenSource();
        var before = f.State();
        f.Fault.After = cancellation.Cancel;
        Assert.Throws<OperationCanceledException>(() => f.Execute(cancellation.Token));
        Assert.Equal(before, f.State());
    }

    /// <summary>流程改指後維持原草稿或啟用狀態，且原XAML在rollback逐字還原。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Switch_WorkflowRepoint_PreservesActivationState(int state)
    {
        using var f = new RunFixture(state);
        var log = f.Execute();
        Assert.Equal(state, f.Workflow.GetAttributeValue<OptionSetValue>("statecode").Value);
        Assert.Contains("New.Activity", f.Workflow.GetAttributeValue<string>("xaml"), StringComparison.Ordinal);
        f.Executor.Rollback(log, "synthetic");
        Assert.Equal(state, f.Workflow.GetAttributeValue<OptionSetValue>("statecode").Value);
    }

    /// <summary>原始空白、CRLF與引號形式不是XML重序列化輸出，必須以log原字串逐位元組回復。</summary>
    [Fact]
    public void Rollback_FromSwitchLog_RestoresConfigStateAndXamlBytes()
    {
        using var f = new RunFixture();
        var bytes = Encoding.UTF8.GetBytes(f.Workflow.GetAttributeValue<string>("xaml"));
        var log = f.Execute();
        f.Executor.Rollback(log, "synthetic");
        Assert.Equal(bytes, Encoding.UTF8.GetBytes(f.Workflow.GetAttributeValue<string>("xaml")));
        Assert.Equal("{\"mode\":\"Shadow\"}", f.Step.GetAttributeValue<string>("configuration"));
    }

    /// <summary>回Shadow須清空lookup並刪除本次建立且已無引用的secure列，不只把模式改名。</summary>
    [Fact]
    public void Rollback_ClearsSecureConfigOfSteps_BackToShadow()
    {
        using var f = new RunFixture();
        var log = f.Execute();
        Assert.Single(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepsecureconfig"));
        f.Executor.Rollback(log, "synthetic");
        Assert.Null(f.Step.GetAttributeValue<EntityReference>("sdkmessageprocessingstepsecureconfigid"));
        Assert.Empty(f.Service.Rows.Where(row => row.LogicalName == "sdkmessageprocessingstepsecureconfig"));
    }

    /// <summary>單案例唯一資源owner；偽SDK列由案例清理，所有stream由產品呼叫返回前關閉。</summary>
    private sealed class RunFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p7-switch-" + Guid.NewGuid().ToString("N"));
        internal MemoryOrganization Service { get; } = new();
        internal FaultService Fault { get; }
        internal SwitchExecutor Executor { get; }
        internal SwitchPlan Plan { get; }
        internal byte[] Bytes { get; }
        internal Entity Step => Service.Rows.Single(row => row.Id == source.Steps[0].Id);
        internal Entity OldStep => Service.Rows.Single(row => row.Id == source.LegacySteps[0].Id);
        internal Entity Workflow => Service.Rows.Single(row => row.Id == source.Workflows[0].Id);
        private readonly SwitchSnapshot source;
        /// <summary>純假現況與SDK資料一致建構，沒有借用其他測試的可變狀態。</summary>
        internal RunFixture(int workflowState = 0)
        {
            Directory.CreateDirectory(Root);
            source = SwitchPlanTests.Fixture();
            source = source with { LegacySteps = Array.AsReadOnly(new[] { source.LegacySteps[0] with { Enabled = false } }),
                Workflows = Array.AsReadOnly(new[] { source.Workflows[0] with { State = workflowState,
                    Xaml = "<Activity>\r\n  <A AssemblyQualifiedName='Old.Activity, Old.Assembly' />\r\n</Activity>" } }) };
            Service.Seed(new Entity("sdkmessageprocessingstep", source.Steps[0].Id) { ["statecode"] = new OptionSetValue(1), ["configuration"] = source.Steps[0].Configuration });
            Service.Seed(new Entity("sdkmessageprocessingstep", source.LegacySteps[0].Id) { ["statecode"] = new OptionSetValue(1) });
            Service.Seed(new Entity("workflow", source.Workflows[0].Id) { ["statecode"] = new OptionSetValue(workflowState), ["xaml"] = source.Workflows[0].Xaml });
            Fault = new FaultService(Service);
            Executor = new SwitchExecutor(Fault, Root, Read, () => File.WriteAllText(Path.Combine(Root, "export.json"), "synthetic export"), _ => Fixture.SecretValue);
            Plan = SwitchPlanner.Create(Read(CancellationToken.None), "live");
            Bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Plan);
        }
        /// <summary>從SDK真實列重建候選與指紋；驗證取消token是執行器傳來的值，不偷改為None。</summary>
        private SwitchSnapshot Read(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = source with { Steps = Array.AsReadOnly(new[] { source.Steps[0] with
                { Enabled = Step.GetAttributeValue<OptionSetValue>("statecode").Value == 0, Configuration = Step.GetAttributeValue<string>("configuration"),
                    SecureId = Step.GetAttributeValue<EntityReference>("sdkmessageprocessingstepsecureconfigid")?.Id } }),
                LegacySteps = Array.AsReadOnly(new[] { source.LegacySteps[0] with { Enabled = OldStep.GetAttributeValue<OptionSetValue>("statecode").Value == 0 } }),
                Workflows = Array.AsReadOnly(new[] { source.Workflows[0] with { State = Workflow.GetAttributeValue<OptionSetValue>("statecode").Value, Xaml = Workflow.GetAttributeValue<string>("xaml") } }) };
            return result with { Fingerprint = State() };
        }
        /// <summary>獨立觀察產品結果，不使用planner的fingerprint helper自證。</summary>
        internal string State() => string.Join("|", Service.Rows.OrderBy(row => row.Id).Select(row => row.Id + ":" + row.LogicalName + ":" +
            (row.GetAttributeValue<OptionSetValue>("statecode")?.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "") + ":" +
            row.GetAttributeValue<string>("configuration") + ":" + row.GetAttributeValue<string>("xaml") + ":" + row.GetAttributeValue<EntityReference>("sdkmessageprocessingstepsecureconfigid")?.Id));
        /// <summary>由同一序列化buffer計算核定SHA，防止讀兩次檔案產生TOCTOU。</summary>
        internal string Execute(CancellationToken token = default) => Executor.Execute(Plan, Convert.ToHexString(SHA256.HashData(Bytes)), Bytes, token);
        /// <summary>返回後沒有背景工作，確定關閉SDK測試owner並刪除唯一temp根。</summary>
        public void Dispose() { Service.Dispose(); Directory.Delete(Root, true); }
    }

    /// <summary>送達後故障注入器；只借用SDK，不擁有client或背景工作，不模擬規劃及回復。</summary>
    private sealed class FaultService(IOrganizationService inner) : IOrganizationService
    {
        internal string? Point { get; set; }
        internal Action? After { get; set; }
        /// <summary>同一故障只觸發一次，回復可正常使用底層SDK；取消回呼亦只觸發一次。</summary>
        private void Delivered(string point)
        {
            var callback = After; After = null; callback?.Invoke();
            if (Point == point) { Point = null; throw new InvalidOperationException("Delivered then failed"); }
        }
        /// <summary>先真建立再注入故障，保護孤兒secure清理。</summary>
        public Guid Create(Entity entity) { var id = inner.Create(entity); Delivered("Create:" + entity.LogicalName); return id; }
        /// <summary>先真更新再注入故障，不能只測未送達。</summary>
        public void Update(Entity entity) { inner.Update(entity); Delivered("Update:" + entity.LogicalName); }
        /// <summary>讀取仍由SDK處理，不偽造回復成功。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => inner.Retrieve(entityName, id, columnSet);
        /// <summary>查詢仍由SDK處理，secure引用檢查不可繞過。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => inner.RetrieveMultiple(query);
        /// <summary>精確刪除實體，不吞失敗。</summary>
        public void Delete(string entityName, Guid id) => inner.Delete(entityName, id);
        /// <summary>正式SetState亦注入送達後故障。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        { var result = inner.Execute(request); Delivered(request.RequestName + ":" + ((EntityReference)request["EntityMoniker"]).LogicalName); return result; }
        /// <summary>本案無關聯寫入需求，意外呼叫必須失敗。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本案無解除關聯需求，意外呼叫必須失敗。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

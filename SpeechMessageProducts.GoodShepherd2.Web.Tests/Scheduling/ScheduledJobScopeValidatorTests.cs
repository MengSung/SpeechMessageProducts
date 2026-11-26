/*
檔案整體說明
目的與定位：F4產品執行前重驗的離線安全與生命週期測試，保護固定產品、權威組織與租戶即時狀態，禁止重用上次授權結果。
主要功能與流程：真實validator及啟動守門借用Moq SDK／manager／lease；租戶旗標直接讀平台清單的choice選項建立SDK列，另以合成WhoAmI及生命週期metadata區分伺服器選項值與標籤。故障注入越界組織、停用／保留／結束、缺欄、未知或重複metadata、重複列、SDK及清理例外；決定性斷言是拒絕結果、查詢次數、原始例外身分與fault→release順序。
使用邊界與維護：所有GUID、生命週期數值及名稱只屬單案，不是正式schema。不讀User Secrets、不連D365、SQL或LINE。受控阻塞由測試finally解除並等待真實工作排空，訊號及CancellationTokenSource當案Dispose；fixture不擁有真實連線、timer或背景owner。並行測試每次Acquire建立獨立租約，確認不同租戶不共享查詢候選且active租約回零；不把離線證據冒稱實機驗收。
*/
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SpeechMessage.Scheduling;
using SpeechMessageProducts.GoodShepherd2.Web.Scheduling;
using ToolUtilityNameSpace.Dataverse;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd2.Web.Tests.Scheduling;

/// <summary>每次執行重新讀權威來源的實際產品validator測試；不測試核心SQL或Hangfire行為。</summary>
public sealed class ScheduledJobScopeValidatorTests
{
    /// <summary>平台清單的「是」以SDK選項型別進入真實validator時必須准許；布林專用判斷會使本案失敗，完成時租約須歸零。</summary>
    [Fact]
    public async Task PlatformTenantChoice_ValidAccountCanExecute()
    {
        var f = new Fixture();
        f.Row["new_istenant"] = new OptionSetValue(PlatformTenantChoice("是"));
        Assert.True(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(1, f.MetadataReads);
        Assert.Equal(1, f.Released);
        Assert.Equal(0, f.Active);
    }

    /// <summary>平台的「否」、未知選項與錯誤SDK型別均拒絕；特別注入bool true，防止舊合成布林fixture掩蓋結構不相容。</summary>
    /// <param name="fault">只屬當案的旗標故障種類，不是呼叫端可選的授權設定。</param>
    [Theory]
    [InlineData("no")]
    [InlineData("unknown")]
    [InlineData("bool-true")]
    [InlineData("bool-false")]
    [InlineData("integer")]
    [InlineData("string")]
    public async Task TenantFlagWithWrongTypeOrValue_FailsClosed(string fault)
    {
        var f = new Fixture();
        f.Row["new_istenant"] = fault switch
        {
            "no" => new OptionSetValue(PlatformTenantChoice("否")),
            "unknown" => new OptionSetValue(-98765),
            "bool-true" => true,
            "bool-false" => false,
            "integer" => PlatformTenantChoice("是"),
            _ => "100000001"
        };
        Assert.False(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(0, f.MetadataReads);
        Assert.Equal(1, f.Released);
        Assert.Equal(0, f.Active);
    }

    /// <summary>錯產品或錯部署組織必須在Acquire前拒絕，caller的JobScope不能成為組織權限。</summary>
    /// <param name="wrongProduct">true改產品；false只改組織。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WrongProductOrOrganization_IsRejectedBeforeLease(bool wrongProduct)
    {
        var f = new Fixture();
        var scope = wrongProduct ? f.Scope with { Product = "GoodButler" } :
            f.Scope with { OrganizationId = Guid.NewGuid() };
        Assert.False(await f.Validator.CanExecuteAsync(scope, CancellationToken.None));
        Assert.Equal(0, f.Acquired);
    }

    /// <summary>同一validator第二次執行時停用租戶立即被拒；兩次均重新WhoAmI與讀account，沒有身分或結果cache。</summary>
    [Fact]
    public async Task Job_TenantDisabledAfterSchedule_NotExecuted()
    {
        var f = new Fixture();
        Assert.True(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        f.Row["statecode"] = new OptionSetValue(1);
        Assert.False(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(2, f.TenantReads);
        Assert.Equal(2, f.IdentityReads);
        Assert.Equal(2, f.Released);
        Assert.Equal(0, f.Active);
    }

    /// <summary>生命週期數值以本案metadata決定；保留中與已結束拒絕，其他計畫已知狀態才准執行。</summary>
    /// <param name="index">合成metadata中的選項位置，不是正式產品數值。</param>
    /// <param name="expected">該計畫狀態的准許結果。</param>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public async Task Lifecycle_UsesPublishedMetadataAndRejectsRetainedOrEnded(int index, bool expected)
    {
        var f = new Fixture();
        f.Row["new_lifecyclestatus"] = new OptionSetValue(ChoiceValue(index));
        Assert.Equal(expected, await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(1, f.MetadataReads);
        Assert.Equal(1, f.Released);
    }

    /// <summary>租戶必要欄位不能由default補成可執行，缺資料或非租戶均拒絕並釋放租約。</summary>
    /// <param name="field">從權威回應移除的必要欄位；not-tenant改成平台的「否」選項。</param>
    [Theory]
    [InlineData("new_istenant")]
    [InlineData("statecode")]
    [InlineData("new_lifecyclestatus")]
    [InlineData("not-tenant")]
    public async Task MissingTenantData_FailsClosed(string field)
    {
        var f = new Fixture();
        if (field == "not-tenant") f.Row["new_istenant"] = new OptionSetValue(100000000);
        else f.Row.Attributes.Remove(field);
        Assert.False(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(1, f.Released);
    }

    /// <summary>租戶已刪除或空GUID不是組織級工作；查無列回false，空GUID在Acquire前拒絕。</summary>
    [Fact]
    public async Task MissingOrEmptyTenant_FailsClosed()
    {
        var f = new Fixture { ReadRows = _ => new EntityCollection() };
        Assert.False(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.False(await f.Validator.CanExecuteAsync(f.Scope with { TenantId = Guid.Empty }, CancellationToken.None));
        Assert.Equal(1, f.Acquired);
        Assert.Equal(1, f.Released);
    }

    /// <summary>組織級scope仍每次WhoAmI驗實際組織，沒有租戶時不查account；本產品尚未註冊任何handler。</summary>
    [Fact]
    public async Task OrganizationScope_RevalidatesManagerIdentityWithoutTenantQuery()
    {
        var f = new Fixture();
        Assert.True(await f.Validator.CanExecuteAsync(f.Scope with { TenantId = null }, CancellationToken.None));
        Assert.Equal(1, f.IdentityReads);
        Assert.Equal(0, f.TenantReads);
        Assert.Equal(1, f.Released);
    }

    /// <summary>metadata欠缺、未核定label、重複數值或未知當前選項，均不能猜測保留／結束的數字。</summary>
    /// <param name="fault">合成metadata或account的故障種類。</param>
    [Theory]
    [InlineData("missing")]
    [InlineData("unknown-label")]
    [InlineData("duplicate-value")]
    [InlineData("missing-label")]
    [InlineData("unknown-current")]
    [InlineData("wrong-field")]
    public async Task UnknownLifecycleMapping_FailsClosed(string fault)
    {
        var f = new Fixture();
        switch (fault)
        {
            case "missing": f.Metadata = null; break;
            case "unknown-label": f.Metadata!.OptionSet.Options[0].Label = new Label("未核定狀態", 1028); break;
            case "duplicate-value": f.Metadata!.OptionSet.Options[0].Value = f.Metadata.OptionSet.Options[1].Value; break;
            case "missing-label": f.Metadata!.OptionSet.Options[0].Label = new Label("Pending", 1033); break;
            case "unknown-current": f.Row["new_lifecyclestatus"] = new OptionSetValue(-98765); break;
            case "wrong-field": f.Metadata!.LogicalName = "synthetic_wrong"; break;
        }
        Assert.False(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(1, f.Released);
    }

    /// <summary>單列查詢即使收到兩筆相同ID，也必須診斷衝突並淘汰租約，不能靜默Distinct或任選。</summary>
    [Fact]
    public async Task DuplicateTenantIdentity_ThrowsAndDrains()
    {
        var f = new Fixture();
        f.ReadRows = _ => new EntityCollection([f.Row, f.Row]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(["fault", "release"], f.Order);
        Assert.Equal(0, f.Active);
    }

    /// <summary>租約實際組織不同時停止讀租戶，拋固定驗證故障並淘汰／歸還；GUID設定本身不具權威。</summary>
    [Fact]
    public async Task ManagerOrganizationMismatch_ThrowsBeforeTenantRead()
    {
        var f = new Fixture { ActualOrganization = Guid.NewGuid() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(0, f.TenantReads);
        Assert.Equal(["fault", "release"], f.Order);
    }

    /// <summary>啟動守門在核心server啟動前查同一manager的WhoAmI，成功／失配都不碰租戶或SQL。</summary>
    /// <param name="matches">實際組織是否符合固定部署組織。</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Startup_VerifiesActualOrganizationBeforeRuntime(bool matches)
    {
        var f = new Fixture();
        if (!matches) f.ActualOrganization = Guid.NewGuid();
        var startup = new GoodShepherd2SchedulingOrganizationStartup(f.Manager.Object, f.Options);
        if (matches) await startup.StartingAsync(CancellationToken.None);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => startup.StartingAsync(CancellationToken.None));
        Assert.Equal(1, f.IdentityReads);
        Assert.Equal(0, f.TenantReads);
        Assert.Equal(1, f.Released);
        Assert.Equal(0, f.Active);
    }

    /// <summary>呼叫端已取消時不借連線；正常取消不會因服務內部改寫token而成為允許結果。</summary>
    [Fact]
    public async Task CancelBeforeAcquire_DoesNotBorrow()
    {
        var f = new Fixture();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Validator.CanExecuteAsync(f.Scope, caller.Token));
        Assert.Equal(0, f.Acquired);
    }

    /// <summary>同步SDK進入後取消不能提早釋放租約；測試釋放訊號後才淘汰、歸還並傳遞取消。</summary>
    [Fact]
    public async Task CancelInsideSdk_DrainsBeforeLeaseRelease()
    {
        var f = new Fixture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var caller = new CancellationTokenSource();
        f.ReadRows = _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic SDK drain deadline.");
            return new EntityCollection([f.Row]);
        };
        var operation = Task.Run(() => f.Validator.CanExecuteAsync(f.Scope, caller.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            await caller.CancelAsync();
            Assert.False(operation.IsCompleted);
            Assert.Equal(1, f.Active);
            Assert.Equal(0, f.Released);
        }
        finally
        {
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        Assert.Equal(["fault", "release"], f.Order);
        Assert.Equal(0, f.Active);
    }

    /// <summary>真實SDK故障保留原例外；fault標記或歸還再失敗也不能覆蓋首因，後續操作仍可用新租約重試。</summary>
    [Fact]
    public async Task SdkAndCleanupFaults_PreservePrimaryAndDrainBeforeRetry()
    {
        var f = new Fixture();
        var original = new IOException("synthetic-query-failure");
        f.ReadRows = _ => throw original;
        f.FaultFailure = new IOException("synthetic-mark-failure");
        f.ReleaseFailure = new IOException("synthetic-release-failure");
        Assert.Same(original, await Assert.ThrowsAsync<IOException>(() => f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None)));
        Assert.Equal(["fault", "release"], f.Order);
        Assert.Equal(0, f.Active);
        f.ReadRows = null;
        f.FaultFailure = null;
        f.ReleaseFailure = null;
        Assert.True(await f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None));
        Assert.Equal(2, f.Released);
    }

    /// <summary>成功候選若歸還失敗仍必須拋清理故障，不能讓處理器執行或借用未完成釋放的連線。</summary>
    [Fact]
    public async Task ReleaseFailure_DoesNotPublishAuthorization()
    {
        var f = new Fixture { ReleaseFailure = new IOException("synthetic-release") };
        Assert.Same(f.ReleaseFailure, await Assert.ThrowsAsync<IOException>(() =>
            f.Validator.CanExecuteAsync(f.Scope, CancellationToken.None)));
        Assert.Equal(0, f.Active);
    }

    /// <summary>以雙入場訊號確保A／B同時持有不同租約，重複十六輪各自query ID取資料；每輪與最終active均回零。</summary>
    [Fact]
    public async Task ConcurrentTenants_KeepQueryIdentitySeparateAndDrain()
    {
        var f = new Fixture();
        var allowed = f.Row.Id;
        var denied = Guid.NewGuid();
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        f.ReadRows = id =>
        {
            entered.Signal();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Synthetic concurrent drain deadline.");
            var row = Fixture.Account(id);
            row["new_istenant"] = new OptionSetValue(id == allowed ? 100000001 : 100000000);
            return new EntityCollection([row]);
        };
        for (var iteration = 0; iteration < 16; iteration++)
        {
            entered.Reset(2);
            release.Reset();
            var first = Task.Run(() => f.Validator.CanExecuteAsync(f.Scope with { TenantId = allowed }, CancellationToken.None));
            var second = Task.Run(() => f.Validator.CanExecuteAsync(f.Scope with { TenantId = denied }, CancellationToken.None));
            var operations = Task.WhenAll(first, second);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal(2, f.Active);
            }
            finally
            {
                release.Set();
                await operations.WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.True(await first);
            Assert.False(await second);
            Assert.Equal(0, f.Active);
        }
        Assert.Equal(32, f.Released);
        Assert.Equal(0, f.Active);
    }

    /// <summary>選項數字故意不連續且不以零開始，只是單案權威metadata，防止產品依順序猜值。</summary>
    private static int ChoiceValue(int index) => 410071 + index * 13;

    /// <summary>只讀本工作樹平台清單並取得唯一指定選項，讓產品消費測試與FS權威型別連動；JSON文件由本次呼叫Dispose，不接觸部署或秘密。</summary>
    /// <param name="label">平台清單既有繁中選項名稱，只由測試常值提供。</param>
    /// <param name="sourcePath">編譯器提供的本測試來源位置，限定同工作樹而不搜尋父工作樹。</param>
    /// <returns>平台權威選項值；清單缺項、重複或非choice直接使測試失敗。</returns>
    private static int PlatformTenantChoice(string label, [CallerFilePath] string sourcePath = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "..",
            "d365", "schema", "platform", "00-core.schema.json"));
        using var schema = JsonDocument.Parse(File.ReadAllBytes(path));
        var account = Assert.Single(schema.RootElement.GetProperty("entities").EnumerateArray(),
            entity => entity.GetProperty("logicalName").GetString() == "account");
        var field = Assert.Single(account.GetProperty("fields").EnumerateArray(),
            field => field.GetProperty("logicalName").GetString() == "new_istenant");
        Assert.Equal("choice", field.GetProperty("type").GetString());
        return Assert.Single(field.GetProperty("options").EnumerateArray(),
            option => option.GetProperty("label").GetString() == label).GetProperty("value").GetInt32();
    }

    /// <summary>單案合成SDK owner；每次Acquire建立不同lease，計數只觀察清理，不模擬真正連線池。</summary>
    private sealed class Fixture
    {
        /// <summary>已驗部署快照使用的合成組織。</summary>
        internal Guid Organization { get; } = Guid.NewGuid();
        /// <summary>當案服務回傳的組織，可注入與部署不一致。</summary>
        internal Guid ActualOrganization { get; set; }
        /// <summary>只屬單案的完整account資料列。</summary>
        internal Entity Row { get; } = Account(Guid.NewGuid());
        /// <summary>只屬單案的published choice metadata；null注入尚未部署欄位。</summary>
        internal PicklistAttributeMetadata? Metadata { get; set; } = Choices();
        /// <summary>可按查詢ID回傳不同租戶或注入同步阻塞及SDK故障。</summary>
        internal Func<Guid, EntityCollection>? ReadRows { get; set; }
        /// <summary>合成fault標記失敗，不包含任何外部例外訊息。</summary>
        internal Exception? FaultFailure { get; set; }
        /// <summary>合成歸還失敗，用以確認成功不能提早發布。</summary>
        internal Exception? ReleaseFailure { get; set; }
        /// <summary>每次操作只有fault及release符號；ConcurrentQueue不保留資料列或連線。</summary>
        internal System.Collections.Concurrent.ConcurrentQueue<string> Order { get; } = new();
        /// <summary>產品借用的manager替身，不擁有任何真實連線。</summary>
        internal Mock<IDataverseConnectionManager> Manager { get; } = new(MockBehavior.Strict);
        /// <summary>固定純值部署設定，不開SQL。</summary>
        internal SchedulingOptions Options { get; }
        /// <summary>單案真正產品validator，可安全跨獨立執行觀察不快取行為。</summary>
        internal GoodShepherd2ScheduledJobScopeValidator Validator { get; }
        /// <summary>本案合法工作範圍，不以顯示名稱識別租戶。</summary>
        internal JobScope Scope => new("GoodShepherd2", Organization, Row.Id);
        /// <summary>Acquire次數，亦為建立lease總數。</summary>
        internal int Acquired => Volatile.Read(ref acquired);
        /// <summary>所有已結束的Dispose次數。</summary>
        internal int Released => Volatile.Read(ref released);
        /// <summary>當下仍借出的lease數量，完成時必須歸零。</summary>
        internal int Active => Acquired - Released;
        /// <summary>當案WhoAmI真實呼叫次數。</summary>
        internal int IdentityReads => Volatile.Read(ref identityReads);
        /// <summary>當案account真實查詢次數。</summary>
        internal int TenantReads => Volatile.Read(ref tenantReads);
        /// <summary>當案metadata真實查詢次數。</summary>
        internal int MetadataReads => Volatile.Read(ref metadataReads);
        /// <summary>多工作者共用的純觀察計數，使用Interlocked避免測試競態。</summary>
        private int acquired, released, identityReads, tenantReads, metadataReads;

        /// <summary>建立本案SDK與每次獨立lease；所有回應都由純資料製造，沒有外部I/O。</summary>
        internal Fixture()
        {
            ActualOrganization = Organization;
            Options = new("GoodShepherd2", Organization,
                "Server=synthetic.invalid;Database=GoodShepherd2Platform;Integrated Security=true;Encrypt=true;Enlist=false");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(value => value.Execute(It.IsAny<OrganizationRequest>())).Returns((OrganizationRequest request) =>
            {
                if (request is WhoAmIRequest)
                {
                    Interlocked.Increment(ref identityReads);
                    return new WhoAmIResponse
                    {
                        Results = new ParameterCollection
                        {
                            ["UserId"] = Guid.NewGuid(), ["BusinessUnitId"] = Guid.NewGuid(),
                            ["OrganizationId"] = ActualOrganization
                        }
                    };
                }
                var metadataRequest = Assert.IsType<RetrieveAttributeRequest>(request);
                Assert.Equal("account", metadataRequest.EntityLogicalName);
                Assert.Equal("new_lifecyclestatus", metadataRequest.LogicalName);
                Assert.False(metadataRequest.RetrieveAsIfPublished);
                Interlocked.Increment(ref metadataReads);
                return new RetrieveAttributeResponse { Results = new ParameterCollection { ["AttributeMetadata"] = Metadata! } };
            });
            sdk.Setup(value => value.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) =>
            {
                Interlocked.Increment(ref tenantReads);
                var expression = Assert.IsType<QueryExpression>(query);
                Assert.Equal("account", expression.EntityName);
                Assert.False(expression.ColumnSet.AllColumns);
                Assert.Equal(2, expression.TopCount);
                var condition = Assert.Single(expression.Criteria.Conditions);
                Assert.Equal("accountid", condition.AttributeName);
                var id = Assert.IsType<Guid>(Assert.Single(condition.Values));
                return ReadRows is null ? new EntityCollection([Row]) : ReadRows(id);
            });
            Manager.Setup(value => value.Acquire(It.IsAny<CancellationToken>())).Returns(() =>
            {
                Interlocked.Increment(ref acquired);
                var lease = new Mock<IClientLease>(MockBehavior.Strict);
                lease.SetupGet(value => value.Service).Returns(sdk.Object);
                lease.Setup(value => value.MarkFaulted()).Callback(() =>
                {
                    Order.Enqueue("fault");
                    if (FaultFailure is not null) throw FaultFailure;
                });
                lease.Setup(value => value.Dispose()).Callback(() =>
                {
                    Order.Enqueue("release");
                    Interlocked.Increment(ref released);
                    if (ReleaseFailure is not null) throw ReleaseFailure;
                });
                return lease.Object;
            });
            Validator = new(Manager.Object, Options, NullLogger<GoodShepherd2ScheduledJobScopeValidator>.Instance);
        }

        /// <summary>建立完整account；租戶旗標採平台choice值，生命週期來自本案合成metadata，兩者不能誤用bool或顯示順序。</summary>
        internal static Entity Account(Guid id) => new("account", id)
        {
            ["new_istenant"] = new OptionSetValue(100000001), ["statecode"] = new OptionSetValue(0),
            ["new_lifecyclestatus"] = new OptionSetValue(ChoiceValue(3))
        };

        /// <summary>計畫七個繁中label映射為不連續的測試數值，順序不具業務意義。</summary>
        private static PicklistAttributeMetadata Choices()
        {
            var metadata = new PicklistAttributeMetadata { LogicalName = "new_lifecyclestatus", OptionSet = new OptionSetMetadata() };
            var labels = new[] { "申請中", "待啟用", "試用中", "進行中", "待配發名額", "保留中", "已結束" };
            for (var index = labels.Length - 1; index >= 0; index--)
                metadata.OptionSet.Options.Add(new OptionMetadata(new Label(labels[index], 1028), ChoiceValue(index)));
            return metadata;
        }
    }
}

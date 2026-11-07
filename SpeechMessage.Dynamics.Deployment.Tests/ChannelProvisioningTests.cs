/*
 * 檔案整體說明
 *
 * 目的與定位：從真DeploymentCli驗P2-03的Channel安全寫入，只替代外部Organization
 * 服務；所有stdin、key及Channel ID均為公開合成值，沒有User Secrets或實機I/O。
 * 主要功能與流程：驗protect-token只讀兩行stdin、以new_channelid替代鍵Upsert兩個
 * 加密欄位，重跑保持同一資料庫ID；assign-channel只能把教會lookup指向實際查得的ID。
 * 拒絕未知／重複選項、秘密命令列參數、非法ID、非白名單組織、缺key與過大stdin；
 * 真正SDK失敗先釋放lease與同組織寫鎖，再把同一Exception交共用report。
 * 資源擁有權與隔離：每案唯一暫存目錄、writer與reader，用using釋放後刪除；假服務
 * 只在本fixture保留合成列，不是資料庫唯一性的實機證據。取消與鎖拒絕不留下client。
 * 維護與驗證：必須保留原P0/P1命令回歸；本檔不授權部署或覆寫既有Channel，
 * 真實執行前仍須確認測試組織、原況備份／回復與必要憑證。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using LineMessagingProcessor.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>使用真parser、加密、SDK request與寫鎖；只有SDK傳輸是單案記憶體替身。</summary>
[Collection("Deployment working directory")]
public sealed class ChannelProvisioningTests
{
    /// <summary>合成教會資料庫ID；不是姓名、顯示字或操作重試鍵。</summary>
    private static readonly Guid AccountId = new("11111111-2222-3333-4444-555555555555");

    /// <summary>產品只可送出的三個公開欄位名稱；不保存測試或使用者資料。</summary>
    private static readonly string[] EncryptedFields = { "new_name", "new_accesstokenencrypted", "new_channelsecretencrypted" };

    /// <summary>每次複製後才交 parser 的固定有效命令，避免測試修改共用陣列。</summary>
    private static string[] ProtectArguments() =>
        new[] { "protect-token", "--org", "goodshepherdback", "--channel-id", "1234567890" };

    /// <summary>合成64-byte key供離線解密檢查，絕不是部署憑證。</summary>
    private static byte[] Key() => Enumerable.Range(0, 64).Select(x => (byte)x).ToArray();

    /// <summary>兩次命令須以相同替代鍵更新同一ID；只有密文可交SDK，stdout不得回顯任何秘密。</summary>
    [Fact]
    public void Protect_UpsertsEncryptedFields_WithStableAlternateKey()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("protect-token", "synthetic-token\nsynthetic-secret\n"));
        var originalId = fixture.Connector.Service.Channel.Id;
        Assert.Equal(0, fixture.Run("protect-token", "synthetic-token-next\r\nsynthetic-secret-next\r\n"));
        Assert.Equal(originalId, fixture.Connector.Service.Channel.Id);
        Assert.Equal(2, fixture.Connector.Service.Upserts);
        var request = fixture.Connector.Service.LastUpsert!;
        Assert.Empty(request.Target.Attributes.Keys.Except(EncryptedFields));
        Assert.Equal("1234567890", request.Target.KeyAttributes["new_channelid"]);
        Assert.Equal("synthetic-token-next", ChannelTokenProtector.Unprotect(
            (string)request.Target["new_accesstokenencrypted"], Key()));
        Assert.Equal("synthetic-secret-next", ChannelTokenProtector.Unprotect(
            (string)request.Target["new_channelsecretencrypted"], Key()));
        Assert.Equal(2, fixture.Connector.Returned);
        Assert.Empty(fixture.Failures);
        Assert.DoesNotContain("synthetic-", fixture.Output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("v1:", fixture.Output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Key()), fixture.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>教會查閱只能使用server回傳的Channel資料庫ID；不得拿channel代碼或caller值當GUID。</summary>
    [Fact]
    public void Assign_UsesActualChannelDatabaseId_AndMinimalColumns()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("assign-channel", ""));
        var retrieve = fixture.Connector.Service.LastRetrieve!;
        Assert.Equal("new_linechannel", retrieve.Target.LogicalName);
        Assert.Equal("1234567890", retrieve.Target.KeyAttributes["new_channelid"]);
        Assert.False(retrieve.ColumnSet.AllColumns);
        Assert.Empty(retrieve.ColumnSet.Columns);
        var update = fixture.Connector.Service.LastUpdate!;
        Assert.Equal("account", update.LogicalName);
        Assert.Equal(AccountId, update.Id);
        Assert.Single(update.Attributes);
        var reference = Assert.IsType<EntityReference>(update["new_linechannelid"]);
        Assert.Equal("new_linechannel", reference.LogicalName);
        Assert.Equal(fixture.Connector.Service.Channel.Id, reference.Id);
        Assert.Equal(0, fixture.SecretReads);
        Assert.Equal(1, fixture.Connector.Returned);
    }

    /// <summary>錯誤組織、ID及未知／重複／敏感參數都必須在stdin、secrets與Connect前拒絕。</summary>
    /// <param name="fault">合成parser故障種類。</param>
    [Theory]
    [InlineData("org")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("empty-channel")]
    [InlineData("oversized-channel")]
    [InlineData("unsafe-channel")]
    [InlineData("secret-argument")]
    [InlineData("invalid-account")]
    [InlineData("empty-account")]
    public void InvalidArguments_RejectBeforeReadingAnySecretOrConnecting(string fault)
    {
        using var fixture = new Fixture();
        var args = new List<string> { "protect-token", "--org", "goodshepherdback", "--channel-id", "1234567890" };
        switch (fault)
        {
            case "org": args[2] = "production"; break;
            case "unknown": args.Add("--schema"); args.Add("private"); break;
            case "duplicate": args.Add("--channel-id"); args.Add("private"); break;
            case "empty-channel": args[4] = ""; break;
            case "oversized-channel": args[4] = new string('1', 21); break;
            case "unsafe-channel": args[4] = "../private"; break;
            case "secret-argument": args.Add("--token"); args.Add("synthetic-private"); break;
            case "invalid-account": args[0] = "assign-channel"; args.Add("--account"); args.Add("private"); break;
            case "empty-account": args[0] = "assign-channel"; args.AddRange(new[] { "--account", Guid.Empty.ToString("D") }); break;
            default: throw new InvalidOperationException();
        }
        using var reader = new RejectingReader();
        Assert.Equal(2, fixture.Run(args.ToArray(), reader));
        Assert.Equal(0, fixture.Connector.Opened);
        Assert.Equal(0, fixture.SecretReads);
        Assert.Empty(fixture.Failures);
        Assert.DoesNotContain("private", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>缺key、無效key、缺stdin、第三行或密文超過secret欄位上限，都不能先取得SDK lease。</summary>
    /// <param name="fault">注入的key或stdin故障。</param>
    [Theory]
    [InlineData("missing-key")]
    [InlineData("bad-key")]
    [InlineData("empty")]
    [InlineData("one-line")]
    [InlineData("third-line")]
    [InlineData("oversized")]
    [InlineData("secret-field-overflow")]
    public void InvalidSecretInput_FailsClosedBeforeConnect(string fault)
    {
        using var fixture = new Fixture();
        var input = "synthetic-token\nsynthetic-secret\n";
        if (fault == "missing-key") fixture.TokenKey = "";
        if (fault == "bad-key") fixture.TokenKey = "synthetic-private";
        if (fault == "empty") input = "";
        if (fault == "one-line") input = "synthetic-token\n";
        if (fault == "third-line") input += "synthetic-private\n";
        if (fault == "oversized") input = new string('x', 6000);
        if (fault == "secret-field-overflow") input = "token\n" + new string('s', 700) + "\n";
        Assert.Equal(1, fixture.Run("protect-token", input));
        Assert.Equal(0, fixture.Connector.Opened);
        Assert.Single(fixture.Failures);
        Assert.DoesNotContain("synthetic-", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>SDK失敗後lease及寫鎖必須先歸還再上報；同一原例外一次，原訊息不得進stdout。</summary>
    [Fact]
    public void SdkFailure_ReleasesResourcesBeforeReportingOriginalException()
    {
        using var fixture = new Fixture();
        var failure = new IOException("synthetic-private-sdk");
        fixture.Connector.Service.Failure = failure;
        fixture.BeforeReport = exception =>
        {
            Assert.Equal(fixture.Connector.Opened, fixture.Connector.Returned);
            using var released = OrganizationWriteLock.Acquire(fixture.OrganizationRoot, TimeSpan.Zero);
        };
        Assert.Equal(1, fixture.Run("protect-token", "token\nsecret\n"));
        Assert.Same(failure, Assert.Single(fixture.Failures));
        Assert.Equal(1, fixture.Connector.Returned);
        Assert.DoesNotContain("synthetic-private", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>正常caller取消在輸入／Connect前停止，不告警；內部已取消token的例外仍須上報失敗。</summary>
    [Fact]
    public void CallerCancellation_AndInternalTimeout_HaveDistinctResults()
    {
        using var fixture = new Fixture();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var reader = new RejectingReader();
        Assert.Equal(130, fixture.Run(ProtectArguments(),
            reader, cancelled.Token));
        Assert.Equal(0, fixture.Connector.Opened);
        Assert.Empty(fixture.Failures);
        fixture.Connector.Service.Failure = new OperationCanceledException(cancelled.Token);
        Assert.Equal(1, fixture.Run("assign-channel", ""));
        Assert.Single(fixture.Failures);
        Assert.Equal(1, fixture.Connector.Returned);
    }

    /// <summary>沿用現有寫鎖，同組織已在部署時新指令不得連線或寫入，鎖等待為零以確定性驗拒絕。</summary>
    [Fact]
    public void ExistingDeploymentLock_PreventsChannelMutation()
    {
        using var fixture = new Fixture();
        using var held = OrganizationWriteLock.Acquire(fixture.OrganizationRoot, TimeSpan.Zero);
        Assert.Equal(1, fixture.Run("protect-token", "token\nsecret\n"));
        Assert.IsType<TimeoutException>(Assert.Single(fixture.Failures));
        Assert.Equal(0, fixture.Connector.Opened);
    }

    /// <summary>遺失或不一致的server身分不可變成教會指派；不以空GUID或不同實體補成成功。</summary>
    [Theory]
    [InlineData("empty-id")]
    [InlineData("wrong-entity")]
    public void Assign_InvalidReturnedIdentity_DoesNotUpdate(string fault)
    {
        using var fixture = new Fixture();
        fixture.Connector.Service.Channel = fault == "empty-id" ? new Entity("new_linechannel") : new Entity("account", Guid.NewGuid());
        Assert.Equal(1, fixture.Run("assign-channel", ""));
        Assert.Null(fixture.Connector.Service.LastUpdate);
        Assert.Equal(1, fixture.Connector.Returned);
        Assert.Single(fixture.Failures);
    }

    /// <summary>兩行來源可用LF／CRLF或不帶最後換行；不得修剪Token中合法空白或Unicode。</summary>
    /// <param name="suffix">來源的最後換行選擇。</param>
    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void FiniteInput_PreservesSecretExactly(string suffix)
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Run("protect-token", " token🙂 \r\n secret繁中 " + suffix));
        var target = fixture.Connector.Service.LastUpsert!.Target;
        Assert.Equal(" token🙂 ", ChannelTokenProtector.Unprotect((string)target["new_accesstokenencrypted"], Key()));
        Assert.Equal(" secret繁中 ", ChannelTokenProtector.Unprotect((string)target["new_channelsecretencrypted"], Key()));
    }

    /// <summary>取得非預期正式身分的Upsert回應必須失敗，不因傳輸成功就宣告資料已正確寫入。</summary>
    [Fact]
    public void Protect_InvalidReturnedIdentity_IsFailure()
    {
        using var fixture = new Fixture();
        fixture.Connector.Service.Channel = new Entity("new_linechannel");
        Assert.Equal(1, fixture.Run("protect-token", "token\nsecret\n"));
        Assert.Single(fixture.Failures);
        Assert.Equal(1, fixture.Connector.Returned);
    }

    /// <summary>真組合根依組織命名空間取得key，SDK故障交真共用日誌；原訊息與秘密均不可落檔。</summary>
    [Fact]
    public void Program_ReportsOriginalSdkFailure_WithoutSecretContent()
    {
        using var fixture = new Fixture();
        fixture.Connector.Service.Failure = new IOException("synthetic-private-sdk");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Deployment:Organizations:goodshepherdback:TokenKey"] = fixture.TokenKey }).Build();
        using var configurationOwner = (IDisposable)configuration;
        using var reader = new StringReader("synthetic-token\nsynthetic-secret\n");
        var logs = Path.Combine(fixture.Root, "Logs");
        Assert.Equal(1, Program.RunCommandWithInput(ProtectArguments(), fixture.Connector,
            fixture.Output, configuration, logs, reader, fixture.Root, default));
        Assert.Equal(1, fixture.Connector.Returned);
        var file = Assert.Single(Directory.GetFiles(logs, "Exception.log"));
        var contents = File.ReadAllText(file);
        Assert.Single(File.ReadAllLines(file));
        Assert.Contains(nameof(IOException), contents, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-", contents, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.TokenKey, contents, StringComparison.Ordinal);
        Assert.DoesNotContain("v1:", contents, StringComparison.Ordinal);
    }

    /// <summary>SDK與租約關閉同時失敗時，必須保留兩個原物件，仍釋放寫鎖且不可先宣稱完成。</summary>
    [Fact]
    public void SdkAndDisposeFailure_PreservesBoth_AndReleasesWriteLock()
    {
        using var fixture = new Fixture();
        var primary = new IOException("synthetic-private-primary");
        var cleanup = new IOException("synthetic-private-cleanup");
        fixture.Connector.Service.Failure = primary;
        fixture.Connector.ReleaseFailure = cleanup;
        Assert.Equal(1, fixture.Run("protect-token", "token\nsecret\n"));
        var failure = Assert.IsType<AggregateException>(Assert.Single(fixture.Failures));
        Assert.Collection(failure.InnerExceptions, first => Assert.Same(primary, first), second => Assert.Same(cleanup, second));
        using var released = OrganizationWriteLock.Acquire(fixture.OrganizationRoot, TimeSpan.Zero);
        Assert.Equal(1, fixture.Connector.Returned);
        Assert.DoesNotContain("completed", fixture.Output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-", fixture.Output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>每案writer／reader與檔案根的唯一owner；不切換cwd，不影響其他測試／部署。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>本案專屬目錄，沒有其他測試可寫入；Dispose於所有lease歸還後刪除。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "p203-cli-" + Guid.NewGuid().ToString("N"));
        /// <summary>沿真產品固定白名單組成鎖根，不以caller秘密組成路徑。</summary>
        internal string OrganizationRoot => Path.Combine(Root, "goodshepherdback");
        /// <summary>本案唯一外部SDK替身來源。</summary>
        internal Connector Connector { get; } = new();
        /// <summary>只保存安全摘要，Dispose時釋放writer。</summary>
        internal StringWriter Output { get; } = new(CultureInfo.InvariantCulture);
        /// <summary>本案原始故障清單，用於身分及上報次數斷言，不交真告警。</summary>
        internal List<Exception> Failures { get; } = new();
        /// <summary>在report當刻驗證lease及寫鎖都已釋放的單案鉤子。</summary>
        internal Action<Exception>? BeforeReport { get; set; }
        /// <summary>合成公開key，依單案故障注入可覆寫，不讀User Secrets。</summary>
        internal string TokenKey { get; set; } = Convert.ToBase64String(Key());
        /// <summary>合成secretReader呼叫次數，拒絕語法及assign應為零。</summary>
        internal int SecretReads { get; private set; }

        /// <summary>每案建立獨立根，所有寫鎖檔都位於此根下。</summary>
        internal Fixture() => Directory.CreateDirectory(Root);

        /// <summary>只把兩行合成資料交給真CLI；assign不使用reader，也不讀key。</summary>
        internal int Run(string command, string input)
        {
            using var reader = new StringReader(input);
            var args = new List<string> { command, "--org", "goodshepherdback", "--channel-id", "1234567890" };
            if (command == "assign-channel") args.AddRange(new[] { "--account", AccountId.ToString("D") });
            return Run(args.ToArray(), reader);
        }

        /// <summary>注入SDK與資料來源，不替代parser／加密／寫鎖；report可檢查釋放順序。</summary>
        internal int Run(string[] args, TextReader reader, CancellationToken cancellation = default) =>
            DeploymentCli.RunWithInput(args, Connector, Output, exception => { BeforeReport?.Invoke(exception); Failures.Add(exception); },
                reader, Root, name => { Assert.Equal("TokenKey", name); SecretReads++; return TokenKey; },
                TimeSpan.Zero, cancellation);

        /// <summary>CLI歸還鎖後才刪專屬目錄；writer與假列也清理，不跨案保存。</summary>
        public void Dispose()
        {
            Output.Dispose();
            Connector.Service.Channel.Attributes.Clear();
            Directory.Delete(Root, recursive: true);
        }
    }

    /// <summary>每命令建立不同lease；計數證明返還，沒有真實連線或credential。</summary>
    private sealed class Connector : IOrganizationConnector
    {
        /// <summary>本案唯一記憶體SDK，只有lease借用、不跨案保存。</summary>
        internal ChannelService Service { get; } = new();
        /// <summary>實際Connect次數，不包含parser拒絕。</summary>
        internal int Opened { get; private set; }
        /// <summary>lease release回呼次數，所有入場命令結束必須等於Opened。</summary>
        internal int Returned { get; private set; }
        /// <summary>注入釋放已嘗試後的故障，確保主故障不被finally覆蓋，寫鎖仍能歸還。</summary>
        internal Exception? ReleaseFailure { get; set; }
        /// <summary>只接受產品已驗證的白名單，release不關閉別的命令或共用外部資源。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal("goodshepherdback", organization);
            Opened++;
            return new OrganizationLease(Service, () =>
            {
                Returned++;
                if (ReleaseFailure is not null) throw ReleaseFailure;
            });
        }
    }

    /// <summary>拒絕意外stdin讀取；沒有外部handle，Dispose由案例負責。</summary>
    private sealed class RejectingReader : StringReader
    {
        /// <summary>以空合成來源建立，不開外部handle。</summary>
        internal RejectingReader() : base("") { }
        /// <summary>parser或正常取消若仍讀stdin，立即使真CLI測試失敗。</summary>
        public override int Read() => throw new InvalidOperationException("Unexpected input read.");
    }

    /// <summary>只支援Upsert／alternate-key Retrieve及account Update；未知操作立即拒絕，避免寬鬆假服務掩蓋錯路由。</summary>
    private sealed class ChannelService : IOrganizationService
    {
        /// <summary>本案正式身分替身；故障案例可替換成空ID或錯誤實體。</summary>
        internal Entity Channel { get; set; } = new("new_linechannel", Guid.NewGuid());
        /// <summary>最後一次原子Upsert，供精確payload斷言。</summary>
        internal UpsertRequest? LastUpsert { get; private set; }
        /// <summary>最後一次替代鍵讀取，供零欄位投影斷言。</summary>
        internal RetrieveRequest? LastRetrieve { get; private set; }
        /// <summary>最後一次account更新；讀取或身分拒絕時必須為null。</summary>
        internal Entity? LastUpdate { get; private set; }
        /// <summary>SDK入場故障，保留原物件給release-before-report斷言。</summary>
        internal Exception? Failure { get; set; }
        /// <summary>成功執行Upsert次數，只證明協定往返而非真資料庫唯一性。</summary>
        internal int Upserts { get; private set; }
        /// <summary>保留精確SDK request供邊界斷言；同替代鍵固定同資料庫ID，不靠display文字去重。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (Failure is not null) throw Failure;
            if (request is UpsertRequest upsert)
            {
                LastUpsert = upsert;
                Assert.Equal("new_linechannel", upsert.Target.LogicalName);
                Assert.Equal("1234567890", Assert.Single(upsert.Target.KeyAttributes).Value);
                Upserts++;
                foreach (var pair in upsert.Target.Attributes) Channel[pair.Key] = pair.Value;
                return new UpsertResponse { Results = { ["Target"] = Channel.ToEntityReference(), ["RecordCreated"] = Upserts == 1 } };
            }
            LastRetrieve = Assert.IsType<RetrieveRequest>(request);
            return new RetrieveResponse { Results = { ["Entity"] = Channel } };
        }
        /// <summary>只記account精確ID更新，若產品錯用Create／查詢則其他入口直接拒絕。</summary>
        public void Update(Entity entity) => LastUpdate = entity;
        /// <summary>本協定禁止以Create後去重取代原子Upsert。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>本測試不授權刪除任何資料。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>Channel必須由alternate-key RetrieveRequest查得。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>不得以無界全表掃描查找Channel。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query) => throw new NotSupportedException();
        /// <summary>本協定不建立多對多關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本協定不解除多對多關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

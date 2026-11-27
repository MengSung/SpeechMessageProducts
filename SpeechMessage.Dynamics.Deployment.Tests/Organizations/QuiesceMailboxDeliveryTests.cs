/*
檔案整體說明
目的與定位：保護 FS quiesce 信箱投遞的離線安全契約，以真實 SDK Entity／QueryExpression
承載合成資料，獨立判讀查詢條件，驗證 incoming／outgoing None=0、ACT None=2。
主要流程：每案建立自己的合成信箱與 SDK，經正式 reader 比對候選、更新內容及重新盤點；
任一投遞欄位缺失／null 均屬未知並納入候選，測試假件獨立模擬 Null 與 NotEqual 的 OR 條件；
合法 incoming=3 與 ACT=0／1／2 都必須依實際數值處理，不以產品常數產生預期值。
故障與驗證：測試只注入記憶體 SDK 或本案暫存輸出，不讀 Secrets、email、name、password、
OAuth，不建立網路連線、VM 工作或環境鎖；離線通過不代表目標 9.1 metadata 已驗證。
寫前證據：合成 owner 使用真正 CLI writer，在第一筆更新之前保存同次完整 before-image、
預期新值與檔案 SHA；落檔／雜湊側檔失敗必須零寫入，部分失敗與取消保留完整原值供逐 ID 對帳。
資源與隔離：SDK 與列只屬單案，沒有共用快取或背景工作；呼叫者擁有並釋放所有測試資源。
測試 owner 在 finally 歸還借用租約並只清自己的隨機暫存根；不取得任何環境或組織鎖。
本檔的決定性斷言保護正式 ID、三欄目標值與回讀一致，不以合成信箱案例證明 goodshepherd20 的實機 H1 已結案。
apply 案例明確提供「沒有 Plugin／workflow 管理來源」的 02A 版本 2 政策；其 source fingerprint
仍依既有 canonical 版本 1 空來源契約取得，不額外盤點本案信箱、不改寫前原像。來源綁定另由 QuiesceSourceBindingTests
以真公開 dry-run／apply 驗證；本檔既有故障與 0／0／2 斷言完整保留。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Organizations;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>只經合成 SDK 驗證官方投遞數值契約；不執行任何現場 apply 或讀取目標 metadata。</summary>
public sealed class QuiesceMailboxDeliveryTests
{
    /// <summary>錯把 ACT=0 當 None 會漏選 Outlook、誤選 ACT=2；其他合法 delivery 邊界不得被排除。</summary>
    /// <param name="incoming">合成原始收信方式，3 是合法 ForwardMailbox 邊界。</param>
    /// <param name="outgoing">合成原始寄信方式。</param>
    /// <param name="act">合成原始活動／聯絡人／工作方式。</param>
    /// <param name="count">按獨立官方數值契約手算的候選數。</param>
    [Theory]
    [InlineData(0, 0, 0, 1)]
    [InlineData(0, 0, 1, 1)]
    [InlineData(0, 0, 2, 0)]
    [InlineData(1, 0, 2, 1)]
    [InlineData(2, 0, 2, 1)]
    [InlineData(3, 0, 2, 1)]
    [InlineData(0, 1, 2, 1)]
    [InlineData(0, 2, 2, 1)]
    public void Run_DeliveryBoundary_SelectsOnlyNonNone(int incoming, int outgoing, int act, int count)
    {
        var service = new MailboxService();
        service.Rows.Add(Mailbox(1, incoming, outgoing, act));
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default);
        Assert.Equal(count, result.MailboxConfigurationsToDisable);
        Assert.Equal(count, result.Candidates.Count);
        Assert.Empty(service.Updates);
    }

    /// <summary>缺少持久化 owner 仍允許 Update 就是缺陷；apply 必須明確拒絕且所有列維持原值。</summary>
    [Fact]
    public void Run_ApplyWithoutSnapshotOwner_RefusesEveryWrite()
    {
        var service = new MailboxService();
        service.Rows.Add(Mailbox(1, 3, 2, 1));
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(service, Target(), true, default,
            EmptySourcePolicy()));
        Assert.Empty(service.Updates);
    }

    /// <summary>缺值不可猜 0；只保留正式 ID、nullable 原值與 state/status，SDK mutation 不得改寫已發布快照。</summary>
    [Fact]
    public void Run_BeforeImage_PreservesNullAndDetachesSdkValues()
    {
        var service = new MailboxService();
        var row = Mailbox(1, 3, 2, 1);
        row["incomingemaildeliverymethod"] = null;
        row.Attributes.Remove("actdeliverymethod");
        row["statecode"] = null;
        row.Attributes.Remove("statuscode");
        service.Rows.Add(row);
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default);
        row.GetAttributeValue<OptionSetValue>("outgoingemaildeliverymethod").Value = 0;
        using var json = JsonSerializer.SerializeToDocument(result, JsonSerializerOptions.Web);
        var candidate = Assert.Single(json.RootElement.GetProperty("candidates").EnumerateArray());
        Assert.True(candidate.TryGetProperty("mailboxBeforeImage", out var before), "Mailbox before-image was not published.");
        Assert.Equal(row.Id, before.GetProperty("mailboxId").GetGuid());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("incomingEmailDeliveryMethod").ValueKind);
        Assert.Equal(2, before.GetProperty("outgoingEmailDeliveryMethod").GetInt32());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("actDeliveryMethod").ValueKind);
        Assert.Throws<NotSupportedException>(() => ((IList<QuiesceCandidate>)result.Candidates).Clear());
    }

    /// <summary>任一投遞欄位缺失或為 null 都是未知狀態，必須納入候選並保留原值再明確停用。</summary>
    /// <param name="omitAttribute">true 模擬 SDK 省略欄位；false 模擬 SDK 明確回傳 null。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_MissingDeliveryMethod_IsIncludedForSafeDisable(bool omitAttribute)
    {
        var service = new MailboxService();
        var row = Mailbox(1, 0, 0, 2);
        if (omitAttribute)
            row.Attributes.Remove("outgoingemaildeliverymethod");
        else
            row["outgoingemaildeliverymethod"] = null;
        service.Rows.Add(row);

        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default);

        Assert.Equal(1, result.MailboxConfigurationsToDisable);
        Assert.Null(Assert.Single(result.Candidates, candidate => candidate.Entity == "mailbox")
            .MailboxBeforeImage!.OutgoingEmailDeliveryMethod);
        Assert.Empty(service.Updates);
    }

    /// <summary>第一筆 Update 必須已能獨占開啟完整落盤 JSON 及比對 SHA；同次快照只查一次且回讀 None 為零候選。</summary>
    [Fact]
    public void Run_ExactSnapshotFlushedBeforeFirstUpdate_AndReadbackIsNone()
    {
        using var owner = new SnapshotOwner();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 0));
        owner.Service.Rows.Add(Mailbox(2, 0, 1, 1));
        ClonedOrganizationQuiesceResult? saved = null;
        owner.Service.BeforeUpdate = _ =>
        {
            Assert.Equal(1, owner.Service.MailboxReads);
            owner.AssertDurableEvidence(2);
        };
        var result = owner.Apply(snapshot =>
        {
            saved = snapshot;
            Assert.Empty(owner.Service.Updates);
            Assert.Equal(1, owner.Service.MailboxReads);
            owner.Persist(snapshot);
        });
        Assert.Same(saved, result);
        Assert.Equal(1, owner.Returned);
        Assert.Equal(2, owner.Service.Updates.Count);
        var after = ClonedOrganizationQuiesce.Run(owner.Service, Target(), false, default);
        Assert.Equal(0, after.MailboxConfigurationsToDisable);
        using var json = owner.Inventory();
        var rows = json.RootElement.GetProperty("inventory").GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(3, rows[0].GetProperty("mailboxBeforeImage").GetProperty("incomingEmailDeliveryMethod").GetInt32());
        Assert.Equal(0, rows[0].GetProperty("mailboxBeforeImage").GetProperty("actDeliveryMethod").GetInt32());
        Assert.Equal(1, rows[1].GetProperty("mailboxBeforeImage").GetProperty("actDeliveryMethod").GetInt32());
    }

    /// <summary>庫存或 SHA 側檔 CreateNew 衝突都必須在首筆 SDK Update 前失敗；不可覆寫既有證據。</summary>
    /// <param name="blockedName">注入已存在檔案，分別驗證主證據與雜湊側檔的失敗。</param>
    [Theory]
    [InlineData("quiesce-inventory.json")]
    [InlineData("quiesce-inventory-sha256.json")]
    public void Run_PersistenceFailure_ZeroWritesAndReturnsLease(string blockedName)
    {
        using var owner = new SnapshotOwner();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 1));
        var path = Path.Combine(owner.Root, blockedName);
        File.WriteAllText(path, "synthetic-existing-evidence");
        Assert.Throws<IOException>(() => owner.Apply(owner.Persist));
        Assert.Empty(owner.Service.Updates);
        Assert.Equal("synthetic-existing-evidence", File.ReadAllText(path));
        Assert.Equal(1, owner.Returned);
        Assert.Empty(Directory.GetFiles(owner.Root, "*.tmp"));
    }

    /// <summary>第二筆 Update 未送出即故障，完整 before-image 仍可按正式 ID 對帳；不重試、不重新開放外送。</summary>
    [Fact]
    public void Run_PartialFailure_KeepsAllBeforeImagesForReconciliation()
    {
        using var owner = new SnapshotOwner();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 0));
        owner.Service.Rows.Add(Mailbox(2, 0, 1, 1));
        var fault = new IOException("Synthetic update failure.");
        owner.Service.BeforeUpdate = row =>
        {
            if (row.Id == owner.Service.Rows[1].Id) throw fault;
        };
        Assert.Same(fault, Assert.Throws<IOException>(() => owner.Apply(owner.Persist)));
        owner.AssertDurableEvidence(2);
        Assert.Single(owner.Service.Updates);
        Assert.Equal(1, owner.Returned);
        using var json = owner.Inventory();
        var saved = json.RootElement.GetProperty("inventory").GetProperty("candidates").EnumerateArray().ToArray();
        Assert.Equal(owner.Service.Rows[0].Id, saved[0].GetProperty("id").GetGuid());
        Assert.Equal(owner.Service.Rows[1].Id, saved[1].GetProperty("id").GetGuid());
        Assert.Equal(0, saved[0].GetProperty("mailboxBeforeImage").GetProperty("actDeliveryMethod").GetInt32());
        Assert.Equal(1, saved[1].GetProperty("mailboxBeforeImage").GetProperty("actDeliveryMethod").GetInt32());
        var expected = json.RootElement.GetProperty("expectedMailboxDelivery");
        // 重新按落盤正式 ID 讀取，不以「目前候選」略過已成功變成 None 的第一列。
        for (var index = 0; index < saved.Length; index++)
        {
            var current = owner.Service.Retrieve("mailbox", saved[index].GetProperty("id").GetGuid(),
                new ColumnSet("incomingemaildeliverymethod", "outgoingemaildeliverymethod", "actdeliverymethod"));
            var expectedRow = index == 0 ? expected : saved[index].GetProperty("mailboxBeforeImage");
            Assert.Equal(expectedRow.GetProperty("incomingEmailDeliveryMethod").GetInt32(),
                current.GetAttributeValue<OptionSetValue>("incomingemaildeliverymethod").Value);
            Assert.Equal(expectedRow.GetProperty("outgoingEmailDeliveryMethod").GetInt32(),
                current.GetAttributeValue<OptionSetValue>("outgoingemaildeliverymethod").Value);
            Assert.Equal(expectedRow.GetProperty("actDeliveryMethod").GetInt32(),
                current.GetAttributeValue<OptionSetValue>("actdeliverymethod").Value);
        }
    }

    /// <summary>SDK 已套用首筆但回應失敗時不得盲重試或回復；原像仍涵蓋所有列，owner 可按 ID 判定不確定送達。</summary>
    [Fact]
    public void Run_UpdateAppliedThenThrows_PreservesOriginalSnapshotWithoutRetry()
    {
        using var owner = new SnapshotOwner();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 0));
        owner.Service.Rows.Add(Mailbox(2, 0, 1, 1));
        var fault = new IOException("Synthetic response lost after update.");
        owner.Service.AfterUpdate = () => throw fault;
        Assert.Same(fault, Assert.Throws<IOException>(() => owner.Apply(owner.Persist)));
        owner.AssertDurableEvidence(2);
        Assert.Single(owner.Service.Updates);
        var readback = owner.Service.Retrieve("mailbox", owner.Service.Rows[0].Id, new ColumnSet("actdeliverymethod"));
        Assert.Equal(2, readback.GetAttributeValue<OptionSetValue>("actdeliverymethod").Value);
        using var json = owner.Inventory();
        Assert.Equal(0, json.RootElement.GetProperty("inventory").GetProperty("candidates")[0]
            .GetProperty("mailboxBeforeImage").GetProperty("actDeliveryMethod").GetInt32());
        Assert.Equal(1, owner.Returned);
    }

    /// <summary>caller 在落盤後或首筆成功後取消必須立即停止；完整原值保留、租約歸還且不得自動回復投遞。</summary>
    /// <param name="afterFirstUpdate">false 在寫前回呼取消；true 在首筆同步 Update 返回時取消。</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_Cancellation_KeepsBeforeImageAndStopsFurtherUpdates(bool afterFirstUpdate)
    {
        using var owner = new SnapshotOwner();
        using var cancellation = new CancellationTokenSource();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 0));
        owner.Service.Rows.Add(Mailbox(2, 0, 1, 1));
        if (afterFirstUpdate) owner.Service.AfterUpdate = cancellation.Cancel;
        Assert.Throws<OperationCanceledException>(() => owner.Apply(snapshot =>
        {
            owner.Persist(snapshot);
            if (!afterFirstUpdate) cancellation.Cancel();
        }, cancellation.Token));
        owner.AssertDurableEvidence(2);
        Assert.Equal(afterFirstUpdate ? 1 : 0, owner.Service.Updates.Count);
        Assert.Equal(1, owner.Returned);
    }

    /// <summary>取消在開始前已發生時，reader 不讀 SDK、不呼叫落盤 owner，仍歸還唯一 lease。</summary>
    [Fact]
    public void Run_PreCancelled_DoesNotReadOrPersist()
    {
        using var owner = new SnapshotOwner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var invoked = false;
        Assert.Throws<OperationCanceledException>(() => owner.Apply(_ => invoked = true, cancellation.Token));
        Assert.False(invoked);
        Assert.Equal(0, owner.Service.MailboxReads);
        Assert.Empty(owner.Service.Updates);
        Assert.Empty(Directory.GetFiles(owner.Root));
        Assert.Equal(1, owner.Returned);
    }

    /// <summary>metadata 後續查詢修改原 SDK 選項，仍不得改寫先前讀到的信箱原像；不要求 managed 即可保留。</summary>
    [Fact]
    public void Run_LaterSdkMutation_CannotChangeMailboxBeforeImage()
    {
        var service = new MailboxService { UnclassifiedPlugin = true };
        service.Rows.Add(Mailbox(1, 3, 2, 0));
        service.BeforeRead = table =>
        {
            if (table == "plugintype")
                service.Rows[0].GetAttributeValue<OptionSetValue>("actdeliverymethod").Value = 1;
        };
        var result = ClonedOrganizationQuiesce.Run(service, Target(), false, default, includePluginIdentities: true);
        Assert.Equal(0, Assert.Single(result.Candidates, row => row.Entity == "mailbox").MailboxBeforeImage!.ActDeliveryMethod);
        Assert.Equal(1, result.UnclassifiedPluginSteps);
        Assert.Empty(service.Updates);
    }

    /// <summary>持有純值原像不能延長 SDK service、信箱列或原選項生命週期；沒有靜態 owner 或回呼保留。</summary>
    [Fact]
    public void Run_ResultDoesNotRetainMailboxSdkGraph()
    {
        var (result, sources) = ReadWithWeakSources();
        for (var attempt = 0; attempt < 3; attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.All(sources, source => Assert.False(source.IsAlive));
        Assert.Equal(3, Assert.Single(result.Candidates).MailboxBeforeImage!.IncomingEmailDeliveryMethod);
        GC.KeepAlive(result);
    }

    /// <summary>隔開合成 SDK owner 的 stack lifetime，只返回純值與不持有來源的弱參照供生命週期斷言。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ClonedOrganizationQuiesceResult Result, WeakReference[] Sources) ReadWithWeakSources()
    {
        var service = new MailboxService();
        var row = Mailbox(1, 3, 2, 0);
        service.Rows.Add(row);
        return (ClonedOrganizationQuiesce.Run(service, Target(), false, default),
            [new WeakReference(service), new WeakReference(row),
             new WeakReference(row.GetAttributeValue<OptionSetValue>("actdeliverymethod"))]);
    }

    /// <summary>同次完整來源與分類驗證必須先完成；未分類 Plugin 在快照回呼與任何 Update 前拒絕。</summary>
    [Fact]
    public void Run_UnclassifiedPlugin_DoesNotInvokeSnapshotOwner()
    {
        using var owner = new SnapshotOwner();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 1));
        owner.Service.UnclassifiedPlugin = true;
        var invoked = false;
        Assert.Throws<InvalidDataException>(() => owner.Apply(_ => invoked = true));
        Assert.False(invoked);
        Assert.Empty(owner.Service.Updates);
        Assert.Equal(1, owner.Returned);
    }

    /// <summary>空／重複正式 ID、非 SDK 選項或多餘敏感欄位都應在完整候選發布前拒絕，不能落盤後才驗。</summary>
    /// <param name="fault">本案獨立注入的身分或欄位違約。</param>
    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("empty-id")]
    [InlineData("wrong-option")]
    [InlineData("extra-column")]
    public void Run_InvalidMailbox_RejectsBeforePersistence(string fault)
    {
        using var owner = new SnapshotOwner();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 1));
        owner.Service.AfterPage = page =>
        {
            var row = Assert.Single(page.Entities);
            if (fault == "duplicate-id") page.Entities.Add(row);
            if (fault == "empty-id") row.Id = Guid.Empty;
            if (fault == "wrong-option") row["incomingemaildeliverymethod"] = 3;
            if (fault == "extra-column") row["emailaddress"] = "synthetic-forbidden";
        };
        var invoked = false;
        Assert.Throws<InvalidDataException>(() => owner.Apply(_ => invoked = true));
        Assert.False(invoked);
        Assert.Empty(owner.Service.Updates);
        Assert.Empty(Directory.GetFiles(owner.Root));
        Assert.Equal(1, owner.Returned);
    }

    /// <summary>真公開 dry-run 必須寫出 delivery 原值並歸還租約；本案不進入 apply 的鎖路徑。</summary>
    [Fact]
    public void Cli_DryRun_PublishesBeforeImageAndReturnsLease()
    {
        using var owner = new SnapshotOwner();
        owner.Service.Rows.Add(Mailbox(1, 3, 2, 0));
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var failures = new List<Exception>();
        Assert.Equal(0, DeploymentCli.RunWithCatalog(["quiesce", "--org", "goodshepherd20"],
            OrganizationCatalog.LoadDefault(), owner, output, failures.Add, null, owner.Root,
            secretReader: _ => throw new InvalidOperationException("Secrets must not be read.")));
        using var json = JsonDocument.Parse(File.ReadAllText(Assert.Single(
            Directory.GetFiles(owner.Root, "quiesce-inventory.json", SearchOption.AllDirectories))));
        var row = Assert.Single(json.RootElement.GetProperty("inventory").GetProperty("candidates").EnumerateArray());
        Assert.True(row.TryGetProperty("mailboxBeforeImage", out var before), "CLI did not persist delivery values.");
        Assert.Equal(3, before.GetProperty("incomingEmailDeliveryMethod").GetInt32());
        Assert.Equal(0, before.GetProperty("actDeliveryMethod").GetInt32());
        Assert.Equal(1, owner.Returned);
        Assert.Empty(failures);
        Assert.Empty(owner.Service.Updates);
    }

    /// <summary>
    /// 本案快照與租約唯一 owner，直接測真 reader／CLI writer，不取得環境鎖；
    /// 無論取消、SDK 或落盤故障，using 都歸還 lease，最後僅刪自己建立的隨機暫存根。
    /// </summary>
    private sealed class SnapshotOwner : IOrganizationConnector, IDisposable
    {
        /// <summary>本案獨占根供 CreateNew、flush 與失敗保留證據，不使用 repo artifacts。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "fs-mailbox-" + Guid.NewGuid().ToString("N"));
        /// <summary>單案 SDK 資料來源；沒有網路連線或認證。</summary>
        internal MailboxService Service { get; } = new();
        /// <summary>成功或故障之後均必須準確歸還一次的租約計數。</summary>
        internal int Returned { get; private set; }
        /// <summary>使用固定合成 server ID 綁定測試輸出，與實際組織資料無關。</summary>
        internal static Guid OrganizationId => new("42000000-0000-0000-0000-000000000001");
        /// <summary>只建立本案唯一目錄，沒有環境鎖或其他共用 owner。</summary>
        internal SnapshotOwner() => Directory.CreateDirectory(Root);
        /// <summary>直接借用 SDK 執行 apply，明確提供本案寫前 owner；不進入 CLI 組織鎖或任何真實連線路徑。</summary>
        internal ClonedOrganizationQuiesceResult Apply(Action<ClonedOrganizationQuiesceResult> beforeWrite,
            CancellationToken token = default)
        {
            using var lease = Connect("goodshepherd20");
            return ClonedOrganizationQuiesce.Run(lease.Service, Target(), true, token, EmptySourcePolicy(), beforeWrite: beforeWrite);
        }
        /// <summary>直接呼叫產品同一有界寫前入口；使用真正 CreateNew／flush／SHA，而非假 writer。</summary>
        internal void Persist(ClonedOrganizationQuiesceResult snapshot) =>
            DeploymentCli.WriteQuiesceBeforeImage(Root, OrganizationId, "goodshepherd20", snapshot, CancellationToken.None);
        /// <summary>從已關閉的完整檔案解析純值；文件由呼叫者 using 釋放。</summary>
        internal JsonDocument Inventory() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "quiesce-inventory.json")));
        /// <summary>在 SDK Update 當下以獨占讀證明 writer 已關閉；比對實際 bytes SHA 與完整候選及固定 None 目標。</summary>
        internal void AssertDurableEvidence(int count)
        {
            string hash;
            using (var stream = new FileStream(Path.Combine(Root, "quiesce-inventory.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                hash = Convert.ToHexString(SHA256.HashData(stream));
            using var sidecar = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "quiesce-inventory-sha256.json")));
            Assert.Equal(hash, sidecar.RootElement.GetProperty("sha256").GetString());
            using var json = Inventory();
            Assert.Equal(OrganizationId, json.RootElement.GetProperty("organizationId").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("operationId").GetString()));
            Assert.NotEqual(default, json.RootElement.GetProperty("capturedAtUtc").GetDateTimeOffset());
            Assert.Equal(count, json.RootElement.GetProperty("inventory").GetProperty("candidates").GetArrayLength());
            var expected = json.RootElement.GetProperty("expectedMailboxDelivery");
            Assert.Equal(0, expected.GetProperty("incomingEmailDeliveryMethod").GetInt32());
            Assert.Equal(0, expected.GetProperty("outgoingEmailDeliveryMethod").GetInt32());
            Assert.Equal(2, expected.GetProperty("actDeliveryMethod").GetInt32());
        }
        /// <summary>只向核定的合成組織借出 service；本案計數並在 lease Dispose 時歸還。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal("goodshepherd20", organization);
            return new OrganizationLease(Service, () => Returned++);
        }
        /// <summary>命令已同步返回、檔案與租約均釋放後，僅刪除此 owner 建立的暫存根。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>從明確 test catalog 取得核定描述；不建立 connector 或憑證 owner。</summary>
    private static OrganizationDefinition Target() => OrganizationCatalog.LoadDefault().GetTest("goodshepherd20");

    /// <summary>
    /// 本檔只測沒有 active Plugin／workflow 的合成信箱來源；固定 SHA-256 對應
    /// {"version":1,"plugins":[],"workflows":[]}，不是以目前候選重新猜測或自動核准 H1 政策。
    /// 使用獨立純值讓每次 apply 仍只查本案信箱一次，保留同次候選與寫前 flush 的決定性斷言。
    /// </summary>
    private static QuiescePolicy EmptySourcePolicy() => new()
    {
        PolicyVersion = QuiescePolicy.CurrentPolicyVersion,
        OrganizationId = SnapshotOwner.OrganizationId,
        SourceFingerprint = "3BFC71DC1BDF07610330D19AAF6A957DF71DD7A21DF28C84308C0DC69EEF8AD4"
    };

    /// <summary>建立獨立正式主鍵與投遞純值；即使 display name 相同也只依資料庫 ID 區分。</summary>
    private static Entity Mailbox(int index, int incoming, int outgoing, int act) => new("mailbox", new Guid(index, 0, 0, new byte[8]))
    {
        ["statecode"] = new OptionSetValue(0),
        ["statuscode"] = new OptionSetValue(1),
        ["incomingemaildeliverymethod"] = new OptionSetValue(incoming),
        ["outgoingemaildeliverymethod"] = new OptionSetValue(outgoing),
        ["actdeliverymethod"] = new OptionSetValue(act)
    };

    /// <summary>
    /// 單案記憶體 SDK，只實作信箱查詢與三欄 Update；獨立白名單拒絕敏感欄位與意外副作用。
    /// 沒有連線、訂閱、背景工作或 IDisposable；外層測試唯一持有列與回呼直到測試結束。
    /// </summary>
    private sealed class MailboxService : IOrganizationService
    {
        /// <summary>每案的權威合成信箱資料；產品只能取得欄位投影，不能直接取得此集合。</summary>
        internal List<Entity> Rows { get; } = new();
        /// <summary>每次成功 Update 的獨立值複本，供確認正式 ID 與沒有自動回復。</summary>
        internal List<Entity> Updates { get; } = new();
        /// <summary>實際信箱查詢次數，驗證 apply 不先存一份再用第二份寫入。</summary>
        internal int MailboxReads { get; private set; }
        /// <summary>每筆 Update 前可注入故障或檢查證據；同步返回後不保留任何工作。</summary>
        internal Action<Entity>? BeforeUpdate { get; set; }
        /// <summary>首筆成功後取消的同步注入點，檢驗後續寫入停止與原值證據保留。</summary>
        internal Action? AfterUpdate { get; set; }
        /// <summary>只修改本案當頁以注入身分與型別違約。</summary>
        internal Action<EntityCollection>? AfterPage { get; set; }
        /// <summary>後續 metadata 讀取可修改先前 SDK 來源圖，驗證 reader 已即時 detach 信箱選項。</summary>
        internal Action<string>? BeforeRead { get; set; }
        /// <summary>合成未分類啟用 Plugin，保護分類先於寫前回呼的關卡。</summary>
        internal bool UnclassifiedPlugin { get; set; }

        /// <summary>獨立判讀 OR／NotEqual／Null 的 SQL 缺值語意，並按要求欄位回傳真 SDK 投影。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            BeforeRead?.Invoke(expression.EntityName);
            if (expression.EntityName != "mailbox")
            {
                var tables = new[] { "workflow", "asyncoperation", "sdkmessageprocessingstep", "plugintype", "pluginassembly" };
                Assert.Contains(expression.EntityName, tables);
                var empty = new EntityCollection { EntityName = expression.EntityName };
                if (UnclassifiedPlugin && expression.EntityName == "sdkmessageprocessingstep")
                    empty.Entities.Add(new Entity("sdkmessageprocessingstep", new Guid("43000000-0000-0000-0000-000000000001"))
                    { ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1) });
                return empty;
            }
            var allowed = new[] { "mailboxid", "statecode", "statuscode", "incomingemaildeliverymethod",
                "outgoingemaildeliverymethod", "actdeliverymethod" };
            Assert.False(expression.ColumnSet.AllColumns);
            Assert.All(expression.ColumnSet.Columns, column => Assert.Contains(column, allowed));
            Assert.Equal(LogicalOperator.Or, expression.Criteria.FilterOperator);
            Assert.Equal(6, expression.Criteria.Conditions.Count);
            Assert.Empty(expression.Criteria.Filters);
            Assert.Empty(expression.LinkEntities);
            Assert.Equal(5000, expression.PageInfo.Count);
            Assert.Equal("mailboxid", Assert.Single(expression.Orders).AttributeName);
            var expected = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["incomingemaildeliverymethod"] = 0,
                ["outgoingemaildeliverymethod"] = 0,
                ["actdeliverymethod"] = 2
            };
            var selected = Rows.Where(row => expression.Criteria.Conditions.Any(condition =>
            {
                Assert.Contains(condition.AttributeName, expected.Keys);
                if (condition.Operator == ConditionOperator.Null)
                {
                    Assert.Empty(condition.Values);
                    return !row.Attributes.TryGetValue(condition.AttributeName, out var raw) || raw is null;
                }
                Assert.Equal(ConditionOperator.NotEqual, condition.Operator);
                var comparison = Assert.IsType<int>(Assert.Single(condition.Values));
                Assert.Equal(expected[condition.AttributeName], comparison);
                return row.GetAttributeValue<OptionSetValue>(condition.AttributeName) is { } value
                    && value.Value != comparison;
            }));
            MailboxReads++;
            var page = new EntityCollection(selected.Select(source =>
            {
                var row = new Entity("mailbox", source.Id);
                foreach (var column in expression.ColumnSet.Columns)
                    if (source.Attributes.TryGetValue(column, out var value)) row[column] = value;
                return row;
            }).ToList()) { EntityName = "mailbox" };
            AfterPage?.Invoke(page);
            return page;
        }

        /// <summary>只允許三種投遞都設為 None，保留正式主鍵與原 state/status；不接受復原或其他欄位。</summary>
        public void Update(Entity entity)
        {
            BeforeUpdate?.Invoke(entity);
            Assert.Equal("mailbox", entity.LogicalName);
            Assert.Equal(3, entity.Attributes.Count);
            Assert.Equal(0, entity.GetAttributeValue<OptionSetValue>("incomingemaildeliverymethod").Value);
            Assert.Equal(0, entity.GetAttributeValue<OptionSetValue>("outgoingemaildeliverymethod").Value);
            Assert.Equal(2, entity.GetAttributeValue<OptionSetValue>("actdeliverymethod").Value);
            var source = Assert.Single(Rows, row => row.Id == entity.Id);
            var copy = new Entity("mailbox", entity.Id);
            foreach (var (name, value) in entity.Attributes)
            {
                source[name] = new OptionSetValue(((OptionSetValue)value).Value);
                copy[name] = new OptionSetValue(((OptionSetValue)value).Value);
            }
            Updates.Add(copy);
            AfterUpdate?.Invoke();
        }
        /// <summary>信箱停用不得建立資料。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>信箱停用不得刪除資料或證據。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>故障後只按已保存正式信箱 ID 重讀三欄投遞，用完整原像對帳；不讀 email 或認證。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Assert.Equal("mailbox", entityName);
            Assert.False(columnSet.AllColumns);
            var allowed = new[] { "incomingemaildeliverymethod", "outgoingemaildeliverymethod", "actdeliverymethod" };
            Assert.All(columnSet.Columns, column => Assert.Contains(column, allowed));
            var source = Assert.Single(Rows, row => row.Id == id);
            var copy = new Entity("mailbox", id);
            foreach (var column in columnSet.Columns)
                if (source.GetAttributeValue<OptionSetValue>(column) is { } value) copy[column] = new OptionSetValue(value.Value);
            return copy;
        }
        /// <summary>公開 dry-run 只允許純合成組織核身，拒絕任何其他訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            Assert.IsType<RetrieveCurrentOrganizationRequest>(request);
            return new RetrieveCurrentOrganizationResponse
            {
                Results = { ["Detail"] = new OrganizationDetail
                { OrganizationId = SnapshotOwner.OrganizationId, UniqueName = "goodshepherd20" } }
            };
        }
        /// <summary>停用不建立關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>停用不解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

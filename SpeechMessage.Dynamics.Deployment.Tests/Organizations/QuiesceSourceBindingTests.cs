/*
檔案整體說明
目的與定位：保護 FS Step 3b 的來源指紋，以及 02A policyVersion=2 的停用分類。
經公開 CLI／Run 注入同一正式 step 的 handler、型別、組件及 workflow 來源漂移，驗證 keep 正式 ID
不能掩蓋來源改變；另驗證只有完整簽章身分符合的 F3 平台 Plugin 兩個登錄型別可保留。
主要流程：只用每案合成 SDK 管理列，先公開 dry-run 產生非秘密來源指紋，再建立本案政策；
真正 apply 必須重讀同次完整來源、核對指紋，成功後才交 beforeWrite，以 SetStateRequest 停用 Workflow／Plugin step，
並以 Update 將信箱投遞方式改為 0／0／2；兩種 SDK 寫入前都必須已完成寫前主檔與 SHA 側檔。
故障與斷言：缺／錯指紋、來源替換、晚頁故障與未分類均須零 SetStateRequest／Update，直接 Run 的寫前 owner
亦不可呼叫；SetStateRequest 的 OrganizationServiceFault 須保留原例外、停止後續寫入並歸還 CLI 租約，
公開 CLI 不得發布 before-image 或 SHA 側檔。合法控制驗證相同來源不同列次序
不漂移、完整續頁只讀一次、原像保持原值，真正 SDK 例外與 caller 取消仍各依原契約處理。
信任邊界：fingerprint 只描述已讀管理來源穩定，不把 managed、Complete 或同一來源當成安全
保留證明，不證明目標 goodshepherd20 已符合停用政策或 H1 已結案，實機 apply／verify 仍待完成；不讀 configuration、secureconfig、workflow name／本文、
組件 content、userId 或 Channel。查詢欄位由本檔獨立 oracle 驗證，不借用產品白名單。
資源與隔離：SDK、列、政策與回呼全由單案擁有，沒有共用 cache、連線或背景工作；CLI 租約
逐次歸還，檔案只在指定證據根的隨機子目錄，Dispose 只清本案目錄。純值結果不保留 SDK 圖。
維護驗證：先執行 Quiesce 定向離線案例，再跑 F3 全方案閘門；RED／GREEN 與來源 SHA 另存指定證據根；
合成來源與 mailbox 數值契約不能宣稱目標 9.1、真正政策分類或實機停用已通過。
*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Organization;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Organizations;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests.Organizations;

/// <summary>使用真公開命令與 reader，驗證 F0 分類必須綁定管理來源而不只綁定 step 主鍵。</summary>
[Collection("Deployment working directory")]
public sealed class QuiesceSourceBindingTests
{
    /// <summary>
    /// 重現原安全缺口：先盤點安全 handler，再把同 ID 的 keep step 換成 serviceendpoint；
    /// 現行 CLI 必須因來源指紋漂移先拒絕，before-image 與 Update 都不得發生。缺少 policyVersion 的舊格式另由專用案例拒絕。
    /// </summary>
    [Fact]
    public void Cli_ReviewedPolicySameIdHandlerReplacement_RejectsBeforeEvidenceAndEveryUpdate()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.Policy(fixture.Fingerprint());
        fixture.Service.Change("handler");
        var exit = fixture.Cli(true);
        Assert.True(exit == 1 && fixture.Service.StateChanges.Count == 0 && fixture.Service.Updates.Count == 0,
            $"Expected rejection before writes; exit={exit}; state changes={fixture.Service.StateChanges.Count}; updates={fixture.Service.Updates.Count}.");
        fixture.AssertRejectedApply();
    }

    /// <summary>
    /// 02A 停用規則必須使用版本化政策；缺少 policyVersion 的舊政策即使有正確組織與來源指紋，
    /// 也須在第二次借出 SDK、產生寫前證據及任何 Update 前拒絕，避免沿用舊分類語意。
    /// </summary>
    [Fact]
    public void Cli_UnversionedPolicy_RejectsBeforeApplyLeaseEvidenceAndEveryUpdate()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.LegacyPolicy(fixture.Fingerprint());
        fixture.Service.Reads.Clear();

        Assert.Equal(2, fixture.Cli(true));

        Assert.Equal(1, fixture.Returned);
        Assert.Empty(fixture.Service.Reads);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
        Assert.Empty(fixture.ApplyInventories());
        Assert.Empty(Directory.GetFiles(fixture.Root, "quiesce-inventory-sha256.json", SearchOption.AllDirectories));
    }

    /// <summary>
    /// 02A 不把 managed=true 當成 Microsoft 系統身分：合成自訂組件即使 managed，
    /// 仍必須列入停用；錯列 keep 時，寫前 owner 與所有 Update 都不得發生。
    /// </summary>
    [Fact]
    public void Run_02APolicy_RejectsManagedNonMicrosoftKeepBeforeEvidenceAndEveryUpdate()
    {
        using var fixture = new BindingFixture();
        var policy = fixture.ReviewedPolicy() with
        {
            DisablePluginStepIds = Array.Empty<Guid>(),
            KeepPluginStepIds = [fixture.Service.Step.Id]
        };
        var invoked = false;

        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(fixture.Service, Target(),
            true, default, policy, beforeWrite: _ => invoked = true));

        Assert.False(invoked);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
    }

    /// <summary>
    /// Microsoft.*、經完整 managed／Microsoft strong-name 身分確認的 ActivityFeeds，
    /// 以及本計畫 manifest 精確列出的 F3 平台 Plugin 可保留；同一正式來源指紋下都不能誤列為停用候選。
    /// </summary>
    [Theory]
    [InlineData("microsoft-system")]
    [InlineData("microsoft-missing-token")]
    [InlineData("activityfeeds-system")]
    [InlineData("platform-slot-request")]
    [InlineData("platform-channel-created")]
    public void Run_02APolicy_PreservesAllowedSystemAndF3Plugins(string identity)
    {
        using var fixture = new BindingFixture();
        fixture.Service.Change(identity);
        var policy = fixture.ReviewedPolicy() with
        {
            DisablePluginStepIds = Array.Empty<Guid>(),
            KeepPluginStepIds = [fixture.Service.Step.Id]
        };

        _ = ClonedOrganizationQuiesce.Run(fixture.Service, Target(), true, default, policy,
            beforeWrite: _ => { });

        Assert.DoesNotContain(fixture.Service.Updates, row => row.LogicalName == "sdkmessageprocessingstep");
        Assert.DoesNotContain(fixture.Service.StateChanges,
            request => request.EntityMoniker.LogicalName == "sdkmessageprocessingstep");
        Assert.Contains(fixture.Service.Updates, row => row.LogicalName == "mailbox");
    }

    /// <summary>
    /// 平台例外不得退化成組件名稱前綴或任一已簽署型別即可保留；錯誤簽章、未登錄型別及近似組件名稱
    /// 都必須在 beforeWrite 與所有 Update 前拒絕，避免不相關的自訂 Plugin 躲過 02A 停用政策。
    /// </summary>
    [Theory]
    [InlineData("platform-wrong-token")]
    [InlineData("platform-unregistered-type")]
    [InlineData("platform-lookalike-assembly")]
    public void Run_02APolicy_RejectsUnregisteredPlatformLookalikesBeforeOwnerAndEveryUpdate(string identity)
    {
        using var fixture = new BindingFixture();
        fixture.Service.Change(identity);
        var policy = fixture.ReviewedPolicy() with
        {
            DisablePluginStepIds = Array.Empty<Guid>(),
            KeepPluginStepIds = [fixture.Service.Step.Id]
        };
        var invoked = false;

        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(fixture.Service, Target(),
            true, default, policy, beforeWrite: _ => invoked = true));

        Assert.False(invoked);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
    }

    /// <summary>
    /// 02A 明確不動 Business Rule 與 Business Process Flow；category=2／4 即使不在 workflow 保留 ID 清冊，
    /// 也不能被列為停用候選或傳給 SDK Update。正值遵循官方 workflow_category 選項。
    /// </summary>
    [Theory]
    [InlineData("workflow-business-rule", 2)]
    [InlineData("workflow-bpf", 4)]
    public void Run_02APolicy_ProtectedWorkflowCategoriesAreNeverSelectedOrUpdated(string identity, int category)
    {
        using var fixture = new BindingFixture();
        fixture.Service.Change(identity);
        var policy = fixture.ReviewedPolicy() with { KeepWorkflowIds = Array.Empty<Guid>() };

        var result = ClonedOrganizationQuiesce.Run(fixture.Service, Target(), true, default, policy,
            beforeWrite: _ => { });

        Assert.Equal(0, result.WorkflowDefinitionsToDisable);
        Assert.Equal(category, Assert.Single(result.WorkflowIdentities).Category);
        Assert.DoesNotContain(fixture.Service.StateChanges,
            request => request.EntityMoniker.LogicalName == "workflow");
        Assert.DoesNotContain(fixture.Service.Updates, row => row.LogicalName == "workflow");
    }

    /// <summary>直接 Run 注入同 ID handler 替換且政策缺少 fingerprint；必須先拒絕，不能呼叫 beforeWrite 或 Update。</summary>
    [Fact]
    public void Run_MissingFingerprintSameIdHandlerReplacement_RejectsBeforeOwnerAndEveryUpdate()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        var policy = fixture.PolicyValue(null);
        fixture.Service.Change("handler");
        var invoked = false;
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(fixture.Service, Target(),
            true, default, policy, beforeWrite: _ => invoked = true));
        Assert.False(invoked);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
    }

    /// <summary>正常公開 dry-run 不要求已存在政策，輸出可複製到嚴格政策欄位的 canonical SHA-256。</summary>
    [Fact]
    public void Cli_DryRunWithoutPolicy_PublishesSourceFingerprintAndNeverWrites()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        var fingerprint = fixture.Fingerprint();
        Assert.Matches("^[0-9A-F]{64}$", fingerprint);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
        Assert.Empty(fixture.Failures);
        Assert.Equal(1, fixture.Returned);
        Assert.Equal(1, fixture.Service.Reads["plugintype"]);
        Assert.Equal(1, fixture.Service.Reads["pluginassembly"]);
        Assert.Equal(1, fixture.Service.Reads["solution"]);
        Assert.Equal(1, fixture.Service.Reads["publisher"]);
    }

    /// <summary>同正式 ID 的每一種管理來源漂移都須在 Run 的首筆 beforeWrite 及 Update 前拒絕。</summary>
    /// <param name="change">獨立改動的正式關聯或已審管理純值；不以秘密／流程本文作驗證。</param>
    [Theory]
    [InlineData("handler")]
    [InlineData("handler-id")]
    [InlineData("type-name")]
    [InlineData("assembly-id")]
    [InlineData("assembly-name")]
    [InlineData("public-key-token")]
    [InlineData("assembly-managed")]
    [InlineData("workflow-solution")]
    [InlineData("solution-name")]
    [InlineData("solution-managed")]
    [InlineData("publisher-id")]
    [InlineData("publisher-name")]
    [InlineData("publisher-prefix")]
    [InlineData("workflow-mode")]
    [InlineData("workflow-customizable")]
    [InlineData("source-missing")]
    public void Run_SameIdsSourceDrift_RejectsBeforeSnapshotOwner(string change)
    {
        using var fixture = new BindingFixture();
        var policy = fixture.ReviewedPolicy();
        fixture.Service.Change(change);
        fixture.Service.Reads.Clear();
        var invoked = false;
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(fixture.Service, Target(),
            true, default, policy, beforeWrite: _ => invoked = true));
        Assert.False(invoked);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
        Assert.Equal(1, fixture.Service.Reads["sdkmessageprocessingstep"]);
        Assert.Equal(1, fixture.Service.Reads["workflow"]);
    }

    /// <summary>真正公開 CLI 也要讀來源；不是只保護直接 reader 的測試路徑，漂移不得發布寫前檔案。</summary>
    /// <param name="change">公開 CLI 套用前替換的來源；涵蓋 step、型別、組件與 workflow 正式來源。</param>
    [Theory]
    [InlineData("handler")]
    [InlineData("type-name")]
    [InlineData("assembly-name")]
    [InlineData("workflow-solution")]
    public void Cli_SourceDrift_RejectsBeforeBeforeImageAndEveryUpdate(string change)
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.Policy(fixture.Fingerprint());
        fixture.Service.Change(change);
        Assert.Equal(1, fixture.Cli(true));
        fixture.AssertRejectedApply();
    }

    /// <summary>新欄位必須是非空 canonical 大寫 64 位十六進位；缺失、null 或錯格式不得靠空集合放行。</summary>
    /// <param name="fingerprint">null 代表省略欄位，其餘為獨立無效值。</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public void Run_MissingOrMalformedFingerprint_RejectsBeforeSnapshotOwner(string? fingerprint)
    {
        using var fixture = new BindingFixture();
        var policy = fixture.PolicyValue(fingerprint);
        var invoked = false;
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(fixture.Service, Target(),
            true, default, policy, beforeWrite: _ => invoked = true));
        Assert.False(invoked);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
    }

    /// <summary>
    /// 公開政策解析必須接受且驗證精確 sourceFingerprint 欄位；JSON null、錯格式及格式合法但
    /// 未經審核的另一雜湊皆不能退回舊 ID 政策。拒絕時 beforeWrite 主檔、側檔與所有 Update 為零。
    /// </summary>
    /// <param name="value">獨立 JSON 欄位值；不透過產品 serializer 補值或正規化。</param>
    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [InlineData("\"GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG\"")]
    [InlineData("\"0000000000000000000000000000000000000000000000000000000000000000\"")]
    public void Cli_InvalidFingerprintValue_RejectsBeforeEvidenceAndEveryUpdate(string value)
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.PolicyWithMember("\"sourceFingerprint\":" + value);
        Assert.Equal(1, fixture.Cli(true));
        fixture.AssertRejectedApply();
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
    }

    /// <summary>
    /// 新欄位沿用真正公開 loader 的嚴格結構契約，錯 JSON 型別、大小寫錯字、未知欄位與重複欄位
    /// 均在借出第二份 SDK 租約之前拒絕；不得用最後一值覆寫來隱藏政策衝突或產生寫前證據。
    /// </summary>
    /// <param name="member">直接注入的 JSON 成員；每案使用自己的政策與輸出目錄。</param>
    [Theory]
    [InlineData("\"sourceFingerprint\":1")]
    [InlineData("\"sourceFingerprint\":{}")]
    [InlineData("\"sourceFingerprint\":[]")]
    [InlineData("\"SourceFingerprint\":null")]
    [InlineData("\"unexpectedSourceFingerprint\":null")]
    [InlineData("\"sourceFingerprint\":null,\"sourceFingerprint\":null")]
    public void Cli_InvalidFingerprintMember_RejectsBeforeApplyLease(string member)
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.PolicyWithMember(member);
        fixture.Service.Reads.Clear();
        Assert.Equal(1, fixture.Cli(true));
        Assert.Equal(1, fixture.Returned);
        Assert.Empty(fixture.Service.Reads);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
        Assert.Empty(fixture.ApplyInventories());
        Assert.Empty(Directory.GetFiles(fixture.Root, "quiesce-inventory-sha256.json", SearchOption.AllDirectories));
        Assert.IsType<InvalidDataException>(Assert.Single(fixture.Failures));
    }

    /// <summary>合法已審來源只在同次完整管理讀取後發布原像；owner 看到的原值及真正更新固定為 3／2／1 與 0／0／2。</summary>
    [Fact]
    public void Run_MatchingFingerprint_ReadsExactCandidateOnceBeforeOwnerAndUpdate()
    {
        using var fixture = new BindingFixture();
        var policy = fixture.ReviewedPolicy() with { KeepWorkflowIds = Array.Empty<Guid>() };
        fixture.Service.Reads.Clear();
        ClonedOrganizationQuiesceResult? saved = null;
        var result = ClonedOrganizationQuiesce.Run(fixture.Service, Target(), true, default, policy,
            beforeWrite: snapshot =>
            {
                saved = snapshot;
                Assert.Empty(fixture.Service.StateChanges);
                Assert.Empty(fixture.Service.Updates);
                Assert.All(fixture.Service.Reads.Values, count => Assert.Equal(1, count));
                var before = Assert.Single(snapshot.Candidates, row => row.Entity == "mailbox").MailboxBeforeImage;
                Assert.NotNull(before);
                Assert.Equal(3, before.IncomingEmailDeliveryMethod);
                Assert.Equal(2, before.OutgoingEmailDeliveryMethod);
                Assert.Equal(1, before.ActDeliveryMethod);
            });
        Assert.Same(saved, result);
        Assert.Equal(1, fixture.Service.Reads["plugintype"]);
        Assert.Equal(1, fixture.Service.Reads["pluginassembly"]);
        var pluginState = Assert.Single(fixture.Service.StateChanges,
            request => request.EntityMoniker.LogicalName == "sdkmessageprocessingstep");
        Assert.Equal(fixture.Service.Step.Id, pluginState.EntityMoniker.Id);
        Assert.Equal(1, pluginState.State.Value);
        Assert.Equal(2, pluginState.Status.Value);
        var workflowState = Assert.Single(fixture.Service.StateChanges,
            request => request.EntityMoniker.LogicalName == "workflow");
        Assert.Equal(new Guid("00000004-0000-0000-0000-000000000000"), workflowState.EntityMoniker.Id);
        Assert.Equal(0, workflowState.State.Value);
        Assert.Equal(1, workflowState.Status.Value);
        var mailboxUpdate = Assert.Single(fixture.Service.Updates, row => row.LogicalName == "mailbox");
        Assert.Equal(0, mailboxUpdate.GetAttributeValue<OptionSetValue>("incomingemaildeliverymethod").Value);
        Assert.Equal(0, mailboxUpdate.GetAttributeValue<OptionSetValue>("outgoingemaildeliverymethod").Value);
        Assert.Equal(2, mailboxUpdate.GetAttributeValue<OptionSetValue>("actdeliverymethod").Value);
        Assert.DoesNotContain(fixture.Service.Updates,
            row => row.LogicalName is "workflow" or "sdkmessageprocessingstep");
    }

    /// <summary>合法公開 apply 的寫前主檔與 SHA 側檔必須已關閉，且同一指紋及完整原值先持久化才可 Update。</summary>
    [Fact]
    public void Cli_MatchingFingerprint_FlushesSameCandidateBeforeUpdateAndReturnsLease()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        var fingerprint = fixture.Fingerprint();
        fixture.Policy(fingerprint);
        fixture.Service.Reads.Clear();
        fixture.Service.BeforeWrite = () =>
        {
            var path = Assert.Single(fixture.ApplyInventories());
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.True(stream.Length > 0);
            var sidecar = Assert.Single(Directory.GetFiles(fixture.Root, "quiesce-inventory-sha256.json", SearchOption.AllDirectories));
            using (var stream = File.Open(sidecar, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.True(stream.Length > 0);
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            Assert.Equal(fingerprint, document.RootElement.GetProperty("inventory").GetProperty("sourceFingerprint").GetString());
            Assert.Equal(1, fixture.Service.Reads["mailbox"]);
        };
        Assert.Equal(0, fixture.Cli(true));
        var pluginState = Assert.Single(fixture.Service.StateChanges,
            request => request.EntityMoniker.LogicalName == "sdkmessageprocessingstep");
        Assert.Equal(new Guid("00000001-0000-0000-0000-000000000000"), pluginState.EntityMoniker.Id);
        Assert.Equal(1, pluginState.State.Value);
        Assert.Equal(2, pluginState.Status.Value);
        Assert.Equal(1, fixture.Service.UpdateAttempts);
        Assert.Single(fixture.Service.Updates, row => row.LogicalName == "mailbox");
        Assert.Equal(2, fixture.Returned);
        Assert.Empty(fixture.Failures);
    }

    /// <summary>相同來源跨頁次序改變仍是同一 canonical 集合；不同正式 ID 即使管理文字相同也不能被合併。</summary>
    [Fact]
    public void Run_SameSourcesDifferentOrderAndPaging_DoNotDriftOrDeduplicateIds()
    {
        using var fixture = new BindingFixture();
        fixture.Service.AddSameNameSources();
        var policy = fixture.ReviewedPolicy();
        fixture.Service.Reverse = true;
        fixture.Service.Paged = true;
        fixture.Service.Reads.Clear();
        var result = ClonedOrganizationQuiesce.Run(fixture.Service, Target(), true, default, policy, beforeWrite: snapshot =>
        {
            Assert.Empty(fixture.Service.StateChanges);
            Assert.Empty(fixture.Service.Updates);
            Assert.Equal(2, snapshot.PluginIdentities.Count);
            Assert.Equal(2, snapshot.WorkflowIdentities.Count);
        });
        Assert.Equal(2, fixture.Service.Reads["sdkmessageprocessingstep"]);
        Assert.Equal(2, fixture.Service.Reads["workflow"]);
        Assert.Equal(2, fixture.Service.Reads["plugintype"]);
        Assert.Equal(2, fixture.Service.Reads["pluginassembly"]);
        Assert.Equal(2, fixture.Service.Reads["solution"]);
        Assert.Equal(2, fixture.Service.Reads["publisher"]);
        Assert.Equal(2, result.PluginIdentities.Select(row => row.StepId).ToHashSet().Count);
        Assert.Equal(2, fixture.Service.StateChanges.Count);
        Assert.Equal(2, fixture.Service.StateChanges
            .Select(request => request.EntityMoniker.Id).ToHashSet().Count);
        Assert.All(fixture.Service.StateChanges, request =>
        {
            Assert.Equal("sdkmessageprocessingstep", request.EntityMoniker.LogicalName);
            Assert.Equal(1, request.State.Value);
            Assert.Equal(2, request.Status.Value);
        });
        Assert.Single(fixture.Service.Updates, row => row.LogicalName == "mailbox");
    }

    /// <summary>重複正式 ID 即使內容相同也不得 hash 去重；完整來源驗證失敗時不交寫前 owner。</summary>
    [Fact]
    public void Run_DuplicateSourceIdentity_RejectsBeforeSnapshotOwner()
    {
        using var fixture = new BindingFixture();
        var policy = fixture.ReviewedPolicy();
        fixture.Service.Tables["plugintype"].Add(fixture.Service.Tables["plugintype"][0]);
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(fixture.Service, Target(), true,
            default, policy, beforeWrite: _ => throw new Xunit.Sdk.XunitException("Unexpected beforeWrite.")));
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
    }

    /// <summary>來源指紋有效不代替完整分類；新增未分類步驟即使有 keep 控制列仍必須拒絕。</summary>
    [Fact]
    public void Run_UnclassifiedStepWithMatchingFingerprint_NeverUsesKeepToEraseUnknown()
    {
        using var fixture = new BindingFixture();
        fixture.Service.AddSameNameSources();
        var policy = fixture.ReviewedPolicy() with { KeepPluginStepIds = [fixture.Service.Step.Id] };
        var invoked = false;
        Assert.Throws<InvalidDataException>(() => ClonedOrganizationQuiesce.Run(fixture.Service, Target(), true,
            default, policy, beforeWrite: _ => invoked = true));
        Assert.False(invoked);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Empty(fixture.Service.Updates);
    }

    /// <summary>來源讀取真正故障仍交原 Exception；不是指紋 mismatch 的合成例外，CLI 要歸還租約並零寫入。</summary>
    [Fact]
    public void Cli_SourceReadFailure_PreservesRealExceptionAndReturnsLease()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.Policy(fixture.Fingerprint());
        var failure = new IOException("Synthetic management read failure.");
        fixture.Service.BeforeRead = table => { if (table == "pluginassembly") throw failure; };
        Assert.Equal(1, fixture.Cli(true));
        Assert.Same(failure, Assert.Single(fixture.Failures));
        fixture.AssertRejectedApply();
    }

    /// <summary>
    /// 重現 02A 前次 apply 的 FaultException 類型：注入首個 SetStateRequest 後，
    /// quiesce catch 必須在共用回報後輸出有限數字碼，保留原例外、寫前原像與租約清理；
    /// 不得退回一般 Update，也不得印出 SDK 的 Message、TraceText 或 FaultReason。
    /// </summary>
    [Fact]
    public void Cli_ApplyDataverseFault_ReportsOnlyBoundedCodesAndReturnsLease()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.Policy(fixture.Fingerprint());
        const string sensitiveText = "synthetic-sensitive-dataverse-fault";
        var failure = new System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault>(
            new Microsoft.Xrm.Sdk.OrganizationServiceFault
            {
                ErrorCode = unchecked((int)0x80040224),
                Message = sensitiveText,
                TraceText = sensitiveText
            },
            new System.ServiceModel.FaultReason(sensitiveText));
        fixture.Service.SetStateFailure = failure;

        Assert.Equal(1, fixture.Cli(true));

        Assert.Same(failure, Assert.Single(fixture.Failures));
        Assert.Contains("Dataverse fault code[0]: 0x80040224", fixture.LastOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(sensitiveText, fixture.LastOutput, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Service.SetStateAttempts);
        Assert.Empty(fixture.Service.StateChanges);
        Assert.Equal(0, fixture.Service.UpdateAttempts);
        Assert.Empty(fixture.Service.Updates);
        Assert.Equal(2, fixture.Returned);
        Assert.Single(fixture.ApplyInventories());
        Assert.Single(Directory.GetFiles(fixture.Root, "quiesce-inventory-sha256.json", SearchOption.AllDirectories));
    }

    /// <summary>caller 取消在新增來源讀取返回時仍回 130、不告警；source binding 不改取消分類或保留借用 SDK。</summary>
    [Fact]
    public void Cli_CallerCancelledDuringSourceRead_StopsBeforeEvidenceWithoutReporting()
    {
        using var fixture = new BindingFixture();
        Assert.Equal(0, fixture.Cli(false));
        fixture.Policy(fixture.Fingerprint());
        using var cancellation = new CancellationTokenSource();
        fixture.Service.BeforeRead = table => { if (table == "pluginassembly") cancellation.Cancel(); };
        Assert.Equal(130, fixture.Cli(true, cancellation.Token));
        Assert.Empty(fixture.Failures);
        fixture.AssertRejectedApply();
    }

    /// <summary>只解析目前 repository 的核定 test 描述；沒有連線或使用者秘密。</summary>
    private static OrganizationDefinition Target() => OrganizationCatalog.LoadDefault().GetTest("goodshepherd20");

    /// <summary>單案檔案與 SDK 租約 owner；政策為合成資料，RunWithCatalog 不需要全域 cwd 或真連線。</summary>
    private sealed class BindingFixture : IOrganizationConnector, IDisposable
    {
        /// <summary>所有案例輸出位於使用者指定證據根的隨機子目錄，不接觸產品 artifacts 或他人資料。</summary>
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "3P-FS-QuiesceBinding-20261126",
            "cases", Guid.NewGuid().ToString("N"));
        /// <summary>本案唯一管理資料庫，沒有共享 mutable SDK 或 cache。</summary>
        internal BindingService Service { get; } = new();
        /// <summary>CLI 收到的原始例外，僅在單案 assertions 存活。</summary>
        internal List<Exception> Failures { get; } = new();
        /// <summary>每次 CLI 都必須歸還恰好一份 SDK 租約。</summary>
        internal int Returned { get; private set; }
        /// <summary>只保存本次合成命令的固定摘要，供斷言安全 fault 數字碼而不讀原始例外內容。</summary>
        internal string LastOutput { get; private set; } = string.Empty;
        /// <summary>只建立本案路徑，清理由 Dispose 確定完成。</summary>
        internal BindingFixture() => Directory.CreateDirectory(Root);
        /// <summary>只替換 connector，真正公開解析、寫鎖、核身、讀取、落盤與 verification 仍由產品執行。</summary>
        internal int Cli(bool apply, CancellationToken cancellationToken = default)
        {
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            string[] arguments = apply ? ["quiesce", "--org", "goodshepherd20", "--apply"]
                : ["quiesce", "--org", "goodshepherd20"];
            var exitCode = DeploymentCli.RunWithCatalog(arguments, OrganizationCatalog.LoadDefault(), this, output,
                Failures.Add, null, Root, secretReader: _ => throw new InvalidOperationException("Forbidden Secrets read."),
                cancellationToken: cancellationToken);
            LastOutput = output.ToString();
            return exitCode;
        }
        /// <summary>從公開 dry-run 實際輸出取指紋，不由測試重做產品雜湊以掩蓋接線失敗。</summary>
        internal string Fingerprint()
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Assert.Single(
                Directory.GetFiles(Root, "quiesce-inventory.json", SearchOption.AllDirectories))));
            var inventory = document.RootElement.GetProperty("inventory");
            Assert.True(inventory.TryGetProperty("sourceFingerprint", out var fingerprint), "Source fingerprint was not published.");
            return Assert.IsType<string>(fingerprint.GetString());
        }
        /// <summary>測試 Run 前仍經公開 dry-run 取得已審來源純值；不讀配置本文或接觸外部服務。</summary>
        internal QuiescePolicy ReviewedPolicy()
        {
            Assert.Equal(0, Cli(false));
            return PolicyValue(Fingerprint());
        }
        /// <summary>把目前正式 ID 分類與指定指紋轉為純值政策；null 明確省略新欄位以重現舊政策漏洞。</summary>
        internal QuiescePolicy PolicyValue(string? fingerprint) =>
            JsonSerializer.Deserialize<QuiescePolicy>(PolicyJson(fingerprint, includeVersion: true))!;
        /// <summary>政策只有非秘密的 server 組織 ID、正式來源 ID 與指紋，且每案只建立一次。</summary>
        internal void Policy(string? fingerprint)
        {
            var directory = Path.Combine(Root, "goodshepherd20");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "quiesce-policy.json"),
                PolicyJson(fingerprint, includeVersion: true));
        }
        /// <summary>只供回歸舊格式拒絕的故障注入；缺版政策不作任何成功控制或寫入依據。</summary>
        internal void LegacyPolicy(string? fingerprint)
        {
            var directory = Path.Combine(Root, "goodshepherd20");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "quiesce-policy.json"),
                PolicyJson(fingerprint, includeVersion: false));
        }
        /// <summary>
        /// 僅供嚴格公開 JSON 故障案例：從不帶指紋的獨立政策插入指定成員，保留重複鍵及錯型別原貌。
        /// 不修改 SDK 或他人檔案；每案唯一目錄最後由 Dispose 清理，產品仍經真正檔案 loader 讀取。
        /// </summary>
        internal void PolicyWithMember(string member)
        {
            var directory = Path.Combine(Root, "goodshepherd20");
            Directory.CreateDirectory(directory);
            var original = PolicyJson(null, includeVersion: true);
            File.WriteAllText(Path.Combine(directory, "quiesce-policy.json"), original[..^1] + "," + member + "}");
        }
        /// <summary>獨立 JSON 輸入保護新欄位接線；不依賴產品 serializer 自動補足未知／缺失欄位。</summary>
        private string PolicyJson(string? fingerprint, bool includeVersion)
        {
            var values = new Dictionary<string, object>
            {
                ["organizationId"] = BindingService.OrganizationId,
                ["keepWorkflowIds"] = Service.Tables["workflow"].Select(row => row.Id).ToArray(),
                ["keepPluginStepIds"] = Array.Empty<Guid>(),
                ["disablePluginStepIds"] = Service.Tables["sdkmessageprocessingstep"].Select(row => row.Id).ToArray()
            };
            if (includeVersion) values.Add("policyVersion", QuiescePolicy.CurrentPolicyVersion);
            if (fingerprint is not null) values.Add("sourceFingerprint", fingerprint);
            return JsonSerializer.Serialize(values);
        }
        /// <summary>只選出 apply 寫前 owner 的證據；正常 dry-run 清冊沒有操作 ID，不誤當寫前落盤。</summary>
        internal string[] ApplyInventories() => Directory.GetFiles(Root, "quiesce-inventory.json", SearchOption.AllDirectories)
            .Where(path =>
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                return document.RootElement.TryGetProperty("operationId", out _);
            }).ToArray();
        /// <summary>拒絕時連首筆 beforeWrite 都不應發生；原 dry-run 清冊保留，寫前主檔／SHA 則皆不存在。</summary>
        internal void AssertRejectedApply()
        {
            Assert.Empty(Service.StateChanges);
            Assert.Empty(Service.Updates);
            Assert.Empty(ApplyInventories());
            Assert.Empty(Directory.GetFiles(Root, "quiesce-inventory-sha256.json", SearchOption.AllDirectories));
            Assert.Equal(2, Returned);
        }
        /// <summary>只有核定的合成 test 身分可借出 SDK；沒有認證或 network owner。</summary>
        public OrganizationLease Connect(string organization)
        {
            Assert.Equal("goodshepherd20", organization);
            return new OrganizationLease(Service, () => Returned++);
        }
        /// <summary>產品同步返回後所有 stream 與 lease 均已釋放，只清理本案隨機子目錄。</summary>
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    /// <summary>只實作已審管理來源及信箱的單案 SDK，獨立限制 ColumnSet 並保留正式 ID；不讀任何秘密或業務表。</summary>
    private sealed class BindingService : IOrganizationService
    {
        /// <summary>固定合成 server ID，僅供公開 CLI 身分核對。</summary>
        internal static Guid OrganizationId => new("51000000-0000-0000-0000-000000000001");
        /// <summary>合成管理列全部只由本案持有；同名不同 ID 不作去重。</summary>
        internal Dictionary<string, List<Entity>> Tables { get; } = new(StringComparer.Ordinal);
        /// <summary>本案原始 step 可在公開盤點後注入同 ID handler 改變。</summary>
        internal Entity Step => Tables["sdkmessageprocessingstep"][0];
        /// <summary>逐表查詢次數驗證完整單次候選與新增來源接線。</summary>
        internal Dictionary<string, int> Reads { get; } = new(StringComparer.Ordinal);
        /// <summary>成功 Update 的純值複本，只允許 mailbox／asyncoperation 欄位更新。</summary>
        internal List<Entity> Updates { get; } = new();
        /// <summary>成功 SetStateRequest 的 detached 純值副本；每案獨立持有，不保存連線或跨測試狀態。</summary>
        internal List<SetStateRequest> StateChanges { get; } = new();
        /// <summary>改變回傳次序但不改來源純值。</summary>
        internal bool Reverse { get; set; }
        /// <summary>有兩列的來源各拆成兩頁，驗完整分頁後才允許寫入。</summary>
        internal bool Paged { get; set; }
        /// <summary>同步故障與 caller 取消注入，不建立或保留背景工作。</summary>
        internal Action<string>? BeforeRead { get; set; }
        /// <summary>每種 SDK 寫入前檢查真正已 flush 並關閉的 CLI 證據；同步呼叫後即釋放回呼。</summary>
        internal Action? BeforeWrite { get; set; }
        /// <summary>只模擬第一筆 SetStateRequest 的原始 Dataverse FaultException；只由本案測試持有。</summary>
        internal Exception? SetStateFailure { get; set; }
        /// <summary>記錄本案 SetStateRequest 嘗試數，包括被注入故障拒絕的請求。</summary>
        internal int SetStateAttempts { get; private set; }
        /// <summary>記錄本案同步 Update 次數，包括被注入故障拒絕的請求。</summary>
        internal int UpdateAttempts { get; private set; }
        /// <summary>建構正式來源關聯齊全的合成控制，管理名稱不是 workflow name 或業務資料。</summary>
        internal BindingService()
        {
            Tables.Add("asyncoperation", []);
            Tables.Add("sdkmessageprocessingstep", [new Entity("sdkmessageprocessingstep", Id(1))
            { ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1),
                ["eventhandler"] = new EntityReference("plugintype", Id(2)) }]);
            Tables.Add("plugintype", [new Entity("plugintype", Id(2))
            { ["typename"] = "Synthetic.SafeHandler", ["pluginassemblyid"] = new EntityReference("pluginassembly", Id(3)) }]);
            Tables.Add("pluginassembly", [new Entity("pluginassembly", Id(3))
            { ["name"] = "Synthetic.Assembly", ["publickeytoken"] = "0123456789abcdef", ["ismanaged"] = true }]);
            Tables.Add("workflow", [new Entity("workflow", Id(4))
            { ["statecode"] = new OptionSetValue(1), ["statuscode"] = new OptionSetValue(2),
                ["ismanaged"] = true, ["iscustomizable"] = new BooleanManagedProperty(false),
                ["category"] = new OptionSetValue(0), ["mode"] = new OptionSetValue(0),
                ["type"] = new OptionSetValue(1), ["ondemand"] = false, ["scope"] = new OptionSetValue(4),
                ["solutionid"] = Id(5) }]);
            Tables.Add("solution", [new Entity("solution", Id(5))
            { ["uniquename"] = "SyntheticSolution", ["publisherid"] = new EntityReference("publisher", Id(6)), ["ismanaged"] = true }]);
            Tables.Add("publisher", [new Entity("publisher", Id(6))
            { ["uniquename"] = "SyntheticPublisher", ["customizationprefix"] = "synthetic" }]);
            Tables.Add("mailbox", [new Entity("mailbox", Id(7))
            { ["statecode"] = new OptionSetValue(0), ["statuscode"] = new OptionSetValue(1),
                ["incomingemaildeliverymethod"] = new OptionSetValue(3), ["outgoingemaildeliverymethod"] = new OptionSetValue(2),
                ["actdeliverymethod"] = new OptionSetValue(1) }]);
        }
        /// <summary>每案只改一種管理身分／來源純值；保留 step、workflow 正式主鍵以重現同 ID 漂移。</summary>
        internal void Change(string change)
        {
            switch (change)
            {
                case "handler": Step["eventhandler"] = new EntityReference("serviceendpoint", Id(8)); break;
                case "handler-id": Step["eventhandler"] = new EntityReference("plugintype", Id(8)); break;
                case "type-name": Tables["plugintype"][0]["typename"] = "Synthetic.OutboundHandler"; break;
                case "assembly-id": Tables["plugintype"][0]["pluginassemblyid"] = new EntityReference("pluginassembly", Id(8)); break;
                case "assembly-name": Tables["pluginassembly"][0]["name"] = "Synthetic.ReplacedAssembly"; break;
                case "microsoft-system":
                    Tables["pluginassembly"][0]["name"] = "Microsoft.Crm.ObjectModel";
                    Tables["pluginassembly"][0]["publickeytoken"] = "31bf3856ad364e35";
                    Tables["pluginassembly"][0]["ismanaged"] = true;
                    break;
                case "microsoft-missing-token":
                    Tables["pluginassembly"][0]["name"] = "Microsoft.Crm.ObjectModel";
                    Tables["pluginassembly"][0]["publickeytoken"] = null;
                    Tables["pluginassembly"][0]["ismanaged"] = true;
                    break;
                case "activityfeeds-system":
                    Tables["pluginassembly"][0]["name"] = "ActivityFeeds.Plugins";
                    Tables["pluginassembly"][0]["publickeytoken"] = "31bf3856ad364e35";
                    Tables["pluginassembly"][0]["ismanaged"] = true;
                    break;
                case "platform-slot-request":
                case "platform-channel-created":
                    Tables["plugintype"][0]["typename"] = change == "platform-slot-request"
                        ? "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin"
                        : "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.ChannelCreatedPlugin";
                    Tables["pluginassembly"][0]["name"] = "SpeechMessage.Dynamics.Platform.Plugins";
                    Tables["pluginassembly"][0]["publickeytoken"] = "D7B85A8E2A2C25F8";
                    Tables["pluginassembly"][0]["ismanaged"] = true;
                    break;
                case "platform-wrong-token":
                case "platform-unregistered-type":
                case "platform-lookalike-assembly":
                    Tables["plugintype"][0]["typename"] = "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.SlotRequestPlugin";
                    Tables["pluginassembly"][0]["name"] = change == "platform-lookalike-assembly"
                        ? "SpeechMessage.Dynamics.Platform.Plugins.Extension"
                        : "SpeechMessage.Dynamics.Platform.Plugins";
                    Tables["pluginassembly"][0]["publickeytoken"] = change == "platform-wrong-token"
                        ? "0123456789abcdef"
                        : "D7B85A8E2A2C25F8";
                    Tables["pluginassembly"][0]["ismanaged"] = true;
                    if (change == "platform-unregistered-type")
                        Tables["plugintype"][0]["typename"] = "SpeechMessage.Dynamics.Platform.Plugins.ChannelPool.OtherPlugin";
                    break;
                case "workflow-business-rule": Tables["workflow"][0]["category"] = new OptionSetValue(2); break;
                case "workflow-bpf": Tables["workflow"][0]["category"] = new OptionSetValue(4); break;
                case "public-key-token": Tables["pluginassembly"][0]["publickeytoken"] = "fedcba9876543210"; break;
                case "assembly-managed": Tables["pluginassembly"][0]["ismanaged"] = false; break;
                case "workflow-solution": Tables["workflow"][0]["solutionid"] = Id(8); break;
                case "solution-name": Tables["solution"][0]["uniquename"] = "ReplacedSolution"; break;
                case "solution-managed": Tables["solution"][0]["ismanaged"] = false; break;
                case "publisher-id": Tables["solution"][0]["publisherid"] = new EntityReference("publisher", Id(8)); break;
                case "publisher-name": Tables["publisher"][0]["uniquename"] = "ReplacedPublisher"; break;
                case "publisher-prefix": Tables["publisher"][0]["customizationprefix"] = "replaced"; break;
                case "workflow-mode": Tables["workflow"][0]["mode"] = new OptionSetValue(1); break;
                case "workflow-customizable": Tables["workflow"][0]["iscustomizable"] = new BooleanManagedProperty(true); break;
                case "source-missing": Tables["pluginassembly"].Clear(); break;
                default: throw new InvalidDataException("Unknown synthetic fault.");
            }
        }
        /// <summary>複製合法同名來源但分配不同正式 ID；完整關聯維持獨立，不能合併只留下第一筆。</summary>
        internal void AddSameNameSources()
        {
            foreach (var table in new[] { "sdkmessageprocessingstep", "plugintype", "pluginassembly", "workflow", "solution", "publisher" })
            {
                var row = Copy(Tables[table][0], new ColumnSet(Tables[table][0].Attributes.Keys.ToArray()));
                row.Id = Id(table switch { "sdkmessageprocessingstep" => 11, "plugintype" => 12, "pluginassembly" => 13,
                    "workflow" => 14, "solution" => 15, _ => 16 });
                Tables[table].Add(row);
            }
            Tables["sdkmessageprocessingstep"][1]["eventhandler"] = new EntityReference("plugintype", Id(12));
            Tables["plugintype"][1]["pluginassemblyid"] = new EntityReference("pluginassembly", Id(13));
            Tables["workflow"][1]["solutionid"] = Id(15);
            Tables["solution"][1]["publisherid"] = new EntityReference("publisher", Id(16));
        }
        /// <summary>依獨立 oracle 限制每表管理欄位；可倒序與跨頁，所有投影僅返回本案管理純值。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);
            var allowed = expression.EntityName switch
            {
                "asyncoperation" => new[] { "asyncoperationid", "statecode", "statuscode" },
                "sdkmessageprocessingstep" => ["sdkmessageprocessingstepid", "statecode", "statuscode", "eventhandler"],
                "workflow" => ["workflowid", "statecode", "statuscode", "ismanaged", "iscustomizable", "category", "mode", "type", "ondemand", "scope", "solutionid"],
                "mailbox" => ["mailboxid", "statecode", "statuscode", "incomingemaildeliverymethod", "outgoingemaildeliverymethod", "actdeliverymethod"],
                "plugintype" => ["plugintypeid", "typename", "pluginassemblyid"],
                "pluginassembly" => ["pluginassemblyid", "name", "publickeytoken", "ismanaged"],
                "solution" => ["solutionid", "uniquename", "publisherid", "ismanaged"],
                "publisher" => ["publisherid", "uniquename", "customizationprefix"],
                _ => throw new InvalidDataException("Forbidden table.")
            };
            Assert.False(expression.ColumnSet.AllColumns);
            Assert.All(expression.ColumnSet.Columns, column => Assert.Contains(column, allowed));
            Assert.Empty(expression.LinkEntities);
            Assert.Equal(expression.EntityName + "id", Assert.Single(expression.Orders).AttributeName);
            Reads[expression.EntityName] = Reads.GetValueOrDefault(expression.EntityName) + 1;
            BeforeRead?.Invoke(expression.EntityName);
            IEnumerable<Entity> rows = Tables[expression.EntityName];
            if (expression.EntityName == "sdkmessageprocessingstep")
                rows = rows.Where(row => row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0);
            if (expression.EntityName == "workflow")
                rows = rows.Where(row => row.GetAttributeValue<OptionSetValue>("statecode")?.Value == 1);
            if (expression.EntityName == "mailbox")
                rows = rows.Where(row => row.GetAttributeValue<OptionSetValue>("incomingemaildeliverymethod")?.Value != 0
                    || row.GetAttributeValue<OptionSetValue>("outgoingemaildeliverymethod")?.Value != 0
                    || row.GetAttributeValue<OptionSetValue>("actdeliverymethod")?.Value != 2);
            if (Reverse) rows = rows.Reverse();
            var values = rows.ToArray();
            var paged = Paged && values.Length > 1;
            if (paged && expression.PageInfo.PageNumber == 2)
                Assert.Equal(expression.EntityName + "-first", expression.PageInfo.PagingCookie);
            return new EntityCollection((paged ? values.Skip(expression.PageInfo.PageNumber - 1).Take(1) : values)
                .Select(row => Copy(row, expression.ColumnSet)).ToList())
            {
                EntityName = expression.EntityName, MoreRecords = paged && expression.PageInfo.PageNumber == 1,
                PagingCookie = paged && expression.PageInfo.PageNumber == 1 ? expression.EntityName + "-first" : null
            };
        }
        /// <summary>只接受 mailbox／asyncoperation 欄位更新；Workflow／Plugin step 必須另走 SetStateRequest。</summary>
        public void Update(Entity entity)
        {
            UpdateAttempts++;
            BeforeWrite?.Invoke();
            if (entity.LogicalName == "mailbox")
            {
                Assert.Equal(0, entity.GetAttributeValue<OptionSetValue>("incomingemaildeliverymethod").Value);
                Assert.Equal(0, entity.GetAttributeValue<OptionSetValue>("outgoingemaildeliverymethod").Value);
                Assert.Equal(2, entity.GetAttributeValue<OptionSetValue>("actdeliverymethod").Value);
            }
            else if (entity.LogicalName == "asyncoperation")
            {
                Assert.Equal(3, entity.GetAttributeValue<OptionSetValue>("statecode").Value);
                Assert.Equal(32, entity.GetAttributeValue<OptionSetValue>("statuscode").Value);
            }
            else
                throw new InvalidDataException("State transitions must use SetStateRequest.");
            Updates.Add(Copy(entity, new ColumnSet(entity.Attributes.Keys.ToArray())));
            var stored = Assert.Single(Tables[entity.LogicalName], row => row.Id == entity.Id);
            foreach (var (name, value) in entity.Attributes)
                stored[name] = value switch
                {
                    OptionSetValue option => new OptionSetValue(option.Value),
                    _ => value
                };
        }
        /// <summary>公開核身只能回本案組織，不能執行其他請求或觸發真正外送。</summary>
        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request is SetStateRequest setState)
            {
                SetStateAttempts++;
                BeforeWrite?.Invoke();
                if (SetStateFailure is { } stateFailure) throw stateFailure;

                var entityName = setState.EntityMoniker.LogicalName;
                if (entityName is not ("workflow" or "sdkmessageprocessingstep"))
                    throw new InvalidDataException("Unexpected synthetic SetStateRequest target.");
                var (expectedState, expectedStatus) = entityName == "workflow" ? (0, 1) : (1, 2);
                Assert.Equal(expectedState, setState.State.Value);
                Assert.Equal(expectedStatus, setState.Status.Value);
                var stored = Assert.Single(Tables[entityName], row => row.Id == setState.EntityMoniker.Id);
                stored["statecode"] = new OptionSetValue(setState.State.Value);
                stored["statuscode"] = new OptionSetValue(setState.Status.Value);
                StateChanges.Add(new SetStateRequest
                {
                    EntityMoniker = new EntityReference(entityName, setState.EntityMoniker.Id),
                    State = new OptionSetValue(setState.State.Value),
                    Status = new OptionSetValue(setState.Status.Value)
                });
                return new SetStateResponse();
            }
            Assert.IsType<RetrieveCurrentOrganizationRequest>(request);
            return new RetrieveCurrentOrganizationResponse
            { Results = { ["Detail"] = new OrganizationDetail { UniqueName = "goodshepherd20", OrganizationId = OrganizationId } } };
        }
        /// <summary>同步複製管理值，供 fixture 保持列隔離；不把 SDK 對照邏輯當 fingerprint oracle。</summary>
        private static Entity Copy(Entity source, ColumnSet columns)
        {
            var result = new Entity(source.LogicalName, source.Id);
            foreach (var column in columns.Columns)
                if (source.Attributes.TryGetValue(column, out var value))
                    result[column] = value switch
                    {
                        EntityReference reference => new EntityReference(reference.LogicalName, reference.Id),
                        OptionSetValue option => new OptionSetValue(option.Value),
                        BooleanManagedProperty property => new BooleanManagedProperty(property.Value),
                        _ => value
                    };
            return result;
        }
        /// <summary>獨立合成正式 ID，不是以顯示名稱或列位置替代產品身分。</summary>
        private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
        /// <summary>停用不建立資料，任何呼叫均視為超出本案契約。</summary>
        public Guid Create(Entity entity) => throw new NotSupportedException();
        /// <summary>停用不刪除資料或既有證據。</summary>
        public void Delete(string entityName, Guid id) => throw new NotSupportedException();
        /// <summary>來源必須完整分頁讀取，不能改用逐列 N+1 或任意讀取。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>本案不授權任何關聯寫入。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>本案不授權任何解除關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship,
            EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

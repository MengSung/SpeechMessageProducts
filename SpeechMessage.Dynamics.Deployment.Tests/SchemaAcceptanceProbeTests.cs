/*
 * 檔案整體說明
 * 目的與定位：驗證P1-12合成資料探測只接受真正唯一鍵拒絕，並在成功、取消、故障及不確定Create後清理自己資料。
 * 主要流程：使用嚴格合成SDK資料庫，獨立實作account與localizedtext的正式ID／三欄唯一鍵；不呼叫真憑證或D365。
 * 故障注入：第二筆錯誤碼、第一筆建立後回應遺失、caller取消、Delete失敗及既有非本次資料；斷言原例外保真、殘留計數及操作紀錄。
 * 資源擁有權：每案擁有GUID目錄與CancellationTokenSource，SDK純記憶體沒有連線；finally釋放來源並刪本案目錄。
 * 證據限制：替身只證明清理與錯誤分類；真正索引與實機清理仍須獨立live smoke證據。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using SpeechMessage.Dynamics.Deployment.Schema;
using Xunit;

namespace SpeechMessage.Dynamics.Deployment.Tests;

/// <summary>以嚴格合成業務列測試受限探測；保護正式ID、精確重複鍵故障與finally清理，不代表實機通過。</summary>
public sealed class SchemaAcceptanceProbeTests
{
    /// <summary>D3659.1回通用DuplicateRecord且隱藏SQL本文時，不可只按錯誤碼通過；須拒絕已提交重複、錯集合與控制列故障，只有同鍵不同文化可寫且原tuple唯一才通過。</summary>
    /// <param name="mode">獨立合成資料庫注入的差分驗證情境。</param>
    [Theory]
    [InlineData("valid")]
    [InlineData("control-fault")]
    [InlineData("wrong-tuple")]
    [InlineData("duplicate-committed")]
    [InlineData("control-response-lost")]
    public void Run_LegacyIntegrityFault_RequiresPersistedTupleAndDifferentialControl(string mode)
    {
        using var fixture = new Fixture();
        var original = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237) });
        fixture.Service.DuplicateFailure = original;
        fixture.Service.LegacyFault = mode;
        if (mode == "valid")
        {
            SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default);
            Assert.Equal(1, fixture.Service.ControlAttempts);
            Assert.Equal(3, fixture.Service.Deletes);
            Assert.Contains("0x80040237", File.ReadAllText(Path.Combine(fixture.Directory, "acceptance-result.json")), StringComparison.Ordinal);
        }
        else
        {
            var failure = Assert.ThrowsAny<Exception>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default));
            Assert.Contains(original, Assert.IsType<AggregateException>(failure).Flatten().InnerExceptions);
            Assert.False(File.Exists(Path.Combine(fixture.Directory, "acceptance-result.json")));
        }
        Assert.Empty(fixture.Service.Rows);
    }

    /// <summary>差分控制列已提交後caller取消也必須清掉全部三筆自身資料，保持OperationCanceledException分類，不因原預期SQL拒絕誤送失敗通知。</summary>
    [Fact]
    public void Run_LegacyControlCancellation_CleansBeforeReturningCancellation()
    {
        using var fixture = new Fixture();using var cancellation = new CancellationTokenSource();
        fixture.Service.DuplicateFailure = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040237) });
        fixture.Service.AfterControlCreate = cancellation.Cancel;
        Assert.Throws<OperationCanceledException>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", cancellation.Token));
        Assert.Equal(4, fixture.Service.Creates);
        Assert.Equal(3, fixture.Service.Deletes);
        Assert.Empty(fixture.Service.Rows);
    }

    /// <summary>成功拒第二筆相同鍵後只清自己的資料；既有同名文字／不同正式ID不會被刪，證據必須先於Create落檔。</summary>
    [Fact]
    public void Run_DuplicateKeyRejected_CleansOnlyOwnRowsAndKeepsEvidence()
    {
        using var fixture = new Fixture();
        var existing = new Entity("account", Guid.NewGuid()) { ["name"] = "synthetic-existing" };
        fixture.Service.Rows.Add(existing);
        SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default);
        Assert.Same(existing, Assert.Single(fixture.Service.Rows));
        Assert.Equal(3, fixture.Service.Creates);
        Assert.Equal(2, fixture.Service.Deletes);
        Assert.Equal(1, fixture.Service.Duplicates);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "acceptance-ownership.json")));
    }

    /// <summary>任何其他SDK故障不能冒充唯一鍵拒絕；保留同例外並在finally清理先前成功建立的兩列。</summary>
    [Fact]
    public void Run_DifferentFault_PreservesOriginalAndCleansRows()
    {
        using var fixture = new Fixture();
        var failure = new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80040220) });
        fixture.Service.DuplicateFailure = failure;
        Assert.Same(failure, Assert.Throws<FaultException<OrganizationServiceFault>>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default)));
        Assert.Empty(fixture.Service.Rows);
        Assert.Equal(2, fixture.Service.Deletes);
    }

    /// <summary>建立已被server接受但回應遺失時，仍依預先記錄的正式候選ID與標記查回自身列清理，不能留下不確定寫入。</summary>
    [Fact]
    public void Run_CreateCommittedThenThrows_CleansUncertainOwnId()
    {
        using var fixture = new Fixture();
        var failure = new TimeoutException("synthetic-response-lost");
        fixture.Service.AfterAccountCreate = () => throw failure;
        Assert.Same(failure, Assert.Throws<TimeoutException>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default)));
        Assert.Empty(fixture.Service.Rows);
        Assert.Equal(1, fixture.Service.Deletes);
    }

    /// <summary>caller在第一個Create後取消，停止後續建立但清理不接受已取消token，確定歸零後才將取消往外傳。</summary>
    [Fact]
    public void Run_CallerCancelsAfterCreate_DrainsAndCleansBeforeReturning()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Service.AfterAccountCreate = cancellation.Cancel;
        Assert.Throws<OperationCanceledException>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", cancellation.Token));
        Assert.Empty(fixture.Service.Rows);
        Assert.Equal(1, fixture.Service.Creates);
        Assert.Equal(1, fixture.Service.Deletes);
    }

    /// <summary>清理失敗是驗收失敗而非吞例外；須嘗試剩餘自身列、保留可接續ownership紀錄與真正cleanup例外。</summary>
    [Fact]
    public void Run_CleanupFailure_IsReportedAndDoesNotClaimSuccess()
    {
        using var fixture = new Fixture();
        var failure = new IOException("synthetic-delete-failure");
        fixture.Service.DeleteFailure = failure;
        var actual = Assert.Throws<AggregateException>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default));
        Assert.Contains(failure, actual.InnerExceptions);
        Assert.Equal(2, fixture.Service.Rows.Count);
        Assert.True(File.Exists(Path.Combine(fixture.Directory, "acceptance-ownership.json")));
    }

    /// <summary>預先取消不得建立ownership或入場SDK；借入連線不能被產品自行Dispose。</summary>
    [Fact]
    public void Run_AlreadyCancelled_PerformsNoSdkWrites()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", cancellation.Token));
        Assert.Equal(0, fixture.Service.Creates);
        Assert.Empty(fixture.Service.Rows);
    }

    /// <summary>若server意外接受第二筆，探測必須失敗並把三筆自身列全部清理，不能以Create成功當唯一性成功。</summary>
    [Fact]
    public void Run_DuplicateUnexpectedlyAccepted_FailsAndRemovesAllThreeRows()
    {
        using var fixture = new Fixture();fixture.Service.AllowDuplicate = true;
        Assert.Throws<InvalidOperationException>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default));
        Assert.Empty(fixture.Service.Rows);
        Assert.Equal(3, fixture.Service.Deletes);
    }

    /// <summary>同名但不同資料庫ID是合法記錄；即使名稱恰巧等於本次標記，也不得透過名稱刪除，僅清候選正式ID。</summary>
    [Fact]
    public void Run_LegitimateSameNameDifferentId_PreservesOtherRecord()
    {
        using var fixture = new Fixture();Entity? sameName = null;
        fixture.Service.AfterAccountCreate = () =>
        {
            sameName = new Entity("account", Guid.NewGuid()) { ["name"] = fixture.Service.Rows[0]["name"] };
            fixture.Service.Rows.Add(sameName);
        };
        SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default);
        Assert.Same(sameName, Assert.Single(fixture.Service.Rows));
        Assert.Equal(2, fixture.Service.Deletes);
    }

    /// <summary>SDK若回傳其他ID，不可發布假成功；保留已傳入且server實際建立的候選ID，用標記查回清理。</summary>
    [Fact]
    public void Run_ServerReturnsDifferentIdentity_FailsAndCleansCandidate()
    {
        using var fixture = new Fixture();fixture.Service.WrongReturnedId = true;
        Assert.Throws<InvalidDataException>(() => SchemaAcceptanceProbe.Run(fixture.Service, fixture.Directory, "synthetic-group", default));
        Assert.Empty(fixture.Service.Rows);
        Assert.Equal(1, fixture.Service.Deletes);
    }

    /// <summary>每案唯一暫存根與純記憶體SDK，finally不碰其他案目錄。</summary>
    private sealed class Fixture : IDisposable
    {
        /// <summary>本案唯一的檔案owner根，沒有共用路徑；所有證據stream關閉後才由Dispose清除。</summary>
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "p112-probe-" + Guid.NewGuid().ToString("N"));
        /// <summary>本案獨占SDK資料庫，不含真連線或credential；其資料列由探測本身清理並由測試斷言。</summary>
        internal Store Service { get; }
        /// <summary>先固定唯一證據路徑再建立替身，讓Create可獨立核對ownership已flush及關閉。</summary>
        internal Fixture() { Service = new Store(Directory); }
        /// <summary>只刪本案GUID目錄，產品若保留檔案handle會令此清理失敗；不操作任何實機資料。</summary>
        public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
    }

    /// <summary>只允許固定account／localizedtext的Create、精確ID查回及Delete，獨立模擬三欄資料庫唯一性。</summary>
    private sealed class Store(string directory) : IOrganizationService
    {
        /// <summary>本案純記憶體正式ID集合，可含非本次同名列以驗不誤刪；每個Fixture獨占，不供並行案共享。</summary>
        internal List<Entity> Rows { get; } = new();
        /// <summary>記錄實際入場Create次數，包括預期重複鍵拒絕；取消前未入場不得遞增。</summary>
        internal int Creates { get; private set; }
        /// <summary>只計成功精確ID刪除，用於斷言finally真正清理，不把查無列當成Delete成功。</summary>
        internal int Deletes { get; private set; }
        /// <summary>由三欄唯一性比較獨立判定的衝突次數，不從產品結果反推。</summary>
        internal int Duplicates { get; private set; }
        /// <summary>第二筆相同鍵的故障注入，非官方錯誤碼須原例外保真且整案失敗。</summary>
        internal Exception? DuplicateFailure { get; set; }
        /// <summary>注入精確刪除失敗，產品必須聚合並保存ownership，不得假稱清理歸零。</summary>
        internal Exception? DeleteFailure { get; set; }
        /// <summary>在account已提交但回應前注入取消／逾時，保護不確定寫入也能依已記錄ID清理。</summary>
        internal Action? AfterAccountCreate { get; set; }
        /// <summary>故意接受相同三欄鍵以確認產品絕不宣稱唯一性通過。</summary>
        internal bool AllowDuplicate { get; set; }
        /// <summary>合成server回傳錯誤正式ID，保護身份核對及不確定結果清理。</summary>
        internal bool WrongReturnedId { get; set; }
        /// <summary>僅用於legacy通用SQL錯誤的控制實驗，注入控制寫入／集合不符／已提交重複，不能讓錯誤碼本身成為成功oracle。</summary>
        internal string LegacyFault { get; set; } = "";
        /// <summary>實際嘗試不同文化控制列的次數，正例必須證明I/O入場，不能只靠既有第一列通過。</summary>
        internal int ControlAttempts { get; private set; }
        /// <summary>控制列提交與回應之間注入caller取消，驗新增的第四候選也由同一finally drain與清理。</summary>
        internal Action? AfterControlCreate { get; set; }
        /// <summary>固定型別與正式非空ID；每個Create都必須已有flush並關閉的ownership，重複三欄key只拋正式錯誤。</summary>
        public Guid Create(Entity entity)
        {
            Assert.True(entity.LogicalName is "account" or "new_localizedtext");Assert.NotEqual(Guid.Empty, entity.Id);
            using (var file = File.Open(Path.Combine(directory, "acceptance-ownership.json"), FileMode.Open, FileAccess.Read, FileShare.None)) Assert.True(file.Length > 0);
            Creates++;
            if (entity.LogicalName == "new_localizedtext" && entity.GetAttributeValue<string>("new_culture") == "zh-TW")
            {
                ControlAttempts++;
                if (LegacyFault == "control-fault") throw new InvalidOperationException("synthetic-control-failure");
                if (LegacyFault == "control-response-lost") { Rows.Add(entity); throw new TimeoutException("synthetic-control-committed"); }
            }
            if (!AllowDuplicate && entity.LogicalName == "new_localizedtext" && Rows.Any(row => row.LogicalName == entity.LogicalName
                && row.GetAttributeValue<EntityReference>("new_accountid").Id == entity.GetAttributeValue<EntityReference>("new_accountid").Id
                && row.GetAttributeValue<string>("new_key") == entity.GetAttributeValue<string>("new_key")
                && row.GetAttributeValue<string>("new_culture") == entity.GetAttributeValue<string>("new_culture")))
            {
                Duplicates++;
                if (LegacyFault == "duplicate-committed") Rows.Add(entity);
                if (DuplicateFailure is { } failure) throw failure;
                throw new FaultException<OrganizationServiceFault>(new OrganizationServiceFault { ErrorCode = unchecked((int)0x80060892) });
            }
            Rows.Add(entity);
            if (entity.LogicalName == "account") AfterAccountCreate?.Invoke();
            if (entity.GetAttributeValue<string>("new_culture") == "zh-TW") AfterControlCreate?.Invoke();
            return WrongReturnedId ? Guid.NewGuid() : entity.Id;
        }
        /// <summary>僅接收一個非空ID的等值條件，拒絕掃描或按名稱清除；回傳區域快照，不與caller共用集合。</summary>
        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = Assert.IsType<QueryExpression>(query);Assert.Equal(2, expression.TopCount);
            if (expression.Criteria.Conditions.Count == 3)
            {
                Assert.Equal("new_localizedtext", expression.EntityName);
                Assert.Equal("new_accountid,new_culture,new_key", string.Join(",", expression.Criteria.Conditions.Select(c => c.AttributeName).Order(StringComparer.Ordinal)));
                Assert.All(expression.Criteria.Conditions, c => Assert.Equal(ConditionOperator.Equal, c.Operator));
                var matches = Rows.Where(row => row.LogicalName == "new_localizedtext" && expression.Criteria.Conditions.All(c =>
                    Equals(row[c.AttributeName] is EntityReference lookup ? lookup.Id : row[c.AttributeName], Assert.Single(c.Values)))).ToList();
                return new EntityCollection(LegacyFault == "wrong-tuple" ? new List<Entity>() : matches);
            }
            var condition = Assert.Single(expression.Criteria.Conditions);Assert.Equal(expression.EntityName + "id", condition.AttributeName);
            Assert.Equal(ConditionOperator.Equal, condition.Operator);var id = Assert.IsType<Guid>(Assert.Single(condition.Values));
            return new EntityCollection(Rows.Where(row => row.LogicalName == expression.EntityName && row.Id == id).ToList());
        }
        /// <summary>精確刪自身已核對ID，注入清理故障時保留原例外。</summary>
        public void Delete(string entityName, Guid id) { if (DeleteFailure is { } failure) throw failure;var row = Assert.Single(Rows.Where(row => row.LogicalName == entityName && row.Id == id));Rows.Remove(row);Deletes++; }
        /// <summary>本受限探測不允許更新舊列。</summary>
        public void Update(Entity entity) => throw new NotSupportedException();
        /// <summary>所有查回必須走明確ID與有界集合，以測試缺失及多列拒絕。</summary>
        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet) => throw new NotSupportedException();
        /// <summary>探測不執行metadata或其他任意SDK訊息。</summary>
        public OrganizationResponse Execute(OrganizationRequest request) => throw new NotSupportedException();
        /// <summary>不建立正式資料間的額外關聯。</summary>
        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
        /// <summary>不解除既有關聯。</summary>
        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities) => throw new NotSupportedException();
    }
}

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Moq;
using ToolUtilityNameSpace.EntityOperations;
using ToolUtilityNameSpace.OwnerOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 保護純 SDK 寫入的識別、故障與引用生命週期；故障在記憶體注入，不實際修改 D365 或帳號權限。
    /// 各案例使用獨立服務；正常業務授權與跨程序唯一鍵仍是 host／資料庫責任，不能以測試假裝已整合。
    /// </summary>
    public sealed class MutationOperationsSafetyTests
    {
        /// <summary>保留兩服務的舊 null 建構拒絕；不因移除 logger 欄位就放寬錯誤組態。</summary>
        /// <param name="owner">是否測負責人服務。</param>
        /// <param name="missingService">是否缺 SDK，否則缺 logger。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Constructor_MissingDependency_Rejects(bool owner, bool missingService)
        {
            var sdk = missingService ? null : new Mock<IOrganizationService>().Object;
            var logger = missingService ? new object() : null;
            Assert.Throws<ArgumentNullException>(() =>
                owner ? (object)new OwnerManagementService(logger!, sdk!) : new EntityCrudService(logger!, sdk!));
        }

        /// <summary>無合法目標的操作必須在 I/O 前拒絕；Strict SDK 證明沒有送出任何部分寫入。</summary>
        /// <param name="operation">要拒絕的合成錯誤。</param>
        [Theory]
        [InlineData("create-null")]
        [InlineData("create-name")]
        [InlineData("update-null")]
        [InlineData("update-id")]
        [InlineData("update-name")]
        [InlineData("delete-name")]
        [InlineData("delete-empty")]
        [InlineData("delete-id")]
        [InlineData("assign-null")]
        [InlineData("assign-id")]
        [InlineData("assign-owner")]
        [InlineData("assign-name")]
        [InlineData("assign-empty")]
        [InlineData("assign-mismatch")]
        public void InvalidIdentity_RejectsBeforeSdk(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var crud = new EntityCrudService(new object(), sdk.Object);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            var entity = new Entity("contact", Guid.NewGuid());
            Action action = operation switch
            {
                "create-null" => () => crud.CreateEntity(null!),
                "create-name" => () => crud.CreateEntity(new Entity(" ")),
                "update-null" => () => crud.UpdateEntity(null!),
                "update-id" => () => crud.UpdateEntity(new Entity("contact")),
                "update-name" => () => crud.UpdateEntity(new Entity(" ", entity.Id)),
                "delete-name" => () => crud.DeleteEntity(" ", entity.Id),
                "delete-empty" => () => crud.DeleteEntity("", entity.Id),
                "delete-id" => () => crud.DeleteEntity("contact", Guid.Empty),
                "assign-null" => () => owner.AssignOwner("contact", null!, Guid.NewGuid()),
                "assign-id" => () => owner.AssignOwner("contact", new Entity("contact"), Guid.NewGuid()),
                "assign-owner" => () => owner.AssignOwner("contact", entity, Guid.Empty),
                "assign-name" => () => owner.AssignOwner(" ", entity, Guid.NewGuid()),
                "assign-empty" => () => owner.AssignOwner("", entity, Guid.NewGuid()),
                "assign-mismatch" => () => owner.AssignOwner("account", entity, Guid.NewGuid()),
                _ => throw new ArgumentException("Unknown test scenario.", nameof(operation))
            };
            if (operation == "delete-empty" || operation == "assign-empty")
                Assert.Throws<ArgumentNullException>(action);
            else
                Assert.ThrowsAny<ArgumentException>(action);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 空字串保留舊版 ArgumentNullException；純空白名稱則新增 fail-closed 拒絕。
        /// 嚴格 SDK 替身在本機注入兩種輸入，並以精確例外型別及零呼叫保護相容差異。
        /// </summary>
        [Fact]
        public void Delete_NameValidation_PreservesEmptyExceptionAndRejectsWhitespace()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var crud = new EntityCrudService(new object(), sdk.Object);

            var emptyName = Assert.Throws<ArgumentNullException>(() =>
                crud.DeleteEntity(string.Empty, Guid.NewGuid()));
            Assert.Equal("entityName", emptyName.ParamName);
            Assert.Throws<ArgumentException>(() => crud.DeleteEntity(" ", Guid.NewGuid()));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 真的故障必須把同一 Exception 傳回共用 host 邊界，不能吞掉、重試重複寫入或包成含個資訊息。</summary>
        /// <param name="operation">四個會寫入 SDK 的入口。</param>
        [Theory]
        [InlineData("create")]
        [InlineData("update")]
        [InlineData("delete")]
        [InlineData("assign")]
        public void SdkFault_PreservesOriginalExceptionAndDoesNotRetry(string operation)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var borrowed = sdk.As<IDisposable>();
            var fault = new TimeoutException("synthetic-private-fault");
            var entity = new Entity("contact", Guid.NewGuid());
            sdk.Setup(s => s.Create(entity)).Throws(fault);
            sdk.Setup(s => s.Update(entity)).Throws(fault);
            sdk.Setup(s => s.Delete("contact", entity.Id)).Throws(fault);
            sdk.Setup(s => s.Execute(It.IsAny<AssignRequest>())).Throws(fault);
            var crud = new EntityCrudService(new object(), sdk.Object);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            Action action = operation switch
            {
                "create" => () => crud.CreateEntity(entity),
                "update" => () => crud.UpdateEntity(entity),
                "delete" => () => crud.DeleteEntity("contact", entity.Id),
                "assign" => () => owner.AssignOwner("contact", entity, Guid.NewGuid()),
                _ => throw new ArgumentException("Unknown test scenario.", nameof(operation))
            };
            Assert.Same(fault, Record.Exception(action));
            Assert.Single(sdk.Invocations);
            borrowed.Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>空 Create 結果不是成功資料庫身分；不得把它交給 caller 當已完成發布或自行重試。</summary>
        [Fact]
        public void Create_EmptySdkIdentity_RejectsOnce()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(Guid.Empty);
            Assert.Throws<InvalidOperationException>(() =>
                new EntityCrudService(new object(), sdk.Object).CreateEntity(new Entity("contact")));
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 支援的伺服器替代索引鍵更新仍合法，不能為 GUID 守門破壞既有 alternate-key API。</summary>
        [Fact]
        public void Update_ServerAlternateKey_PreservesSdkContract()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var entity = new Entity("contact", "new_serverkey", "synthetic-event");
            sdk.Setup(s => s.Update(entity));
            new EntityCrudService(new object(), sdk.Object).UpdateEntity(entity);
            sdk.Verify(s => s.Update(entity), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>唯讀 getter 的 null 是輸入拒絕，不應包成含實體文字的功能故障或呼叫 SDK。</summary>
        [Fact]
        public void Owner_NullEntity_RejectsWithoutSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var owner = new OwnerManagementService(new object(), sdk.Object);
            Assert.Throws<ArgumentNullException>(() => owner.GetOwnerId(null!));
            Assert.Throws<ArgumentNullException>(() => owner.GetOwnerName(null!));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>未用的 legacy logger 不可被任一服務延長保存；無內嵌／no-inline 工廠排除測試區域變數根參照。</summary>
        /// <param name="owner">是否測試負責人服務。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LegacyLogger_IsNotRetained(bool owner)
        {
            var sdk = new Mock<IOrganizationService>().Object;
            var holder = CreateService(owner, sdk, out var logger);
            for (var count = 0; count < 3 && logger.IsAlive; count++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(logger.IsAlive);
            GC.KeepAlive(holder);
        }

        /// <summary>
        /// 兩個 scope 一千次並行寫入同名但不同 ID；每次都驗正確 SDK、完整參數與結果。
        /// Task 全部 await 後計數固定1000，借入 SDK Dispose 為0；服務不保存 entity 或產生內部工作。
        /// </summary>
        /// <returns>全部外層有界測試工作完成後的 Task。</returns>
        [Fact]
        public async Task ConcurrentScopes_SameDisplayNames_NoCrossTalkAndDrain()
        {
            var count = 0;
            var assignments = 0;
            var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            var services = new EntityCrudService[2];
            var owners = new OwnerManagementService[2];
            for (var index = 0; index < clients.Length; index++)
            {
                var id = ids[index];
                clients[index].As<IDisposable>();
                clients[index].Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity entity) =>
                {
                    Assert.Equal(id, entity.Id);
                    Assert.Equal("合成同名", entity.GetAttributeValue<string>("fullname"));
                    Interlocked.Increment(ref count);
                    return id;
                });
                clients[index].Setup(s => s.Execute(It.IsAny<AssignRequest>())).Returns((OrganizationRequest request) =>
                {
                    var assign = Assert.IsType<AssignRequest>(request);
                    Assert.Equal(id, assign.Target.Id);
                    Assert.Equal(id, assign.Assignee.Id);
                    Assert.Equal("contact", assign.Target.LogicalName);
                    Assert.Equal("systemuser", assign.Assignee.LogicalName);
                    Interlocked.Increment(ref assignments);
                    return new AssignResponse();
                });
                services[index] = new EntityCrudService(new object(), clients[index].Object);
                owners[index] = new OwnerManagementService(new object(), clients[index].Object);
            }
            var tasks = new Task[1000];
            for (var index = 0; index < tasks.Length; index++)
            {
                var scope = index % 2;
                tasks[index] = Task.Run(() =>
                {
                    var entity = new Entity("contact", ids[scope]) { ["fullname"] = "合成同名" };
                    Assert.Equal(ids[scope], services[scope].CreateEntity(entity));
                    owners[scope].AssignOwner("contact", entity, ids[scope]);
                });
            }
            await Task.WhenAll(tasks);
            Assert.All(tasks, task => Assert.True(task.IsCompleted));
            Assert.Equal(1000, Volatile.Read(ref count));
            Assert.Equal(1000, Volatile.Read(ref assignments));
            foreach (var client in clients) client.As<IDisposable>().Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>只把弱引用交回測試，服務可存活但無理由保留的 logger 必須可回收；不建立外部資源。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object CreateService(bool owner, IOrganizationService sdk, out WeakReference logger)
        {
            var candidate = new object();
            logger = new WeakReference(candidate);
            return owner ? (object)new OwnerManagementService(candidate, sdk) : new EntityCrudService(candidate, sdk);
        }
    }
}

/*
 * 檔案整體說明
 *
 * 目的與定位：附件固定錯誤、正式識別／parent、容量與生命週期的安全契約。 所有資料為合成值；只替代 SDK I/O，不讀組織容量或假稱這是 D365 實機／授權證據。
 *
 * 主要功能與流程：
 * 本檔宣告 AttachmentSafetyTests。
 * - SDK自身參數例外或逾時均保存同一InnerException，固定外層不回顯名稱、ID、時間或原訊息。
 * - 無法確認輸入scope或內容的請求在SDK前拒絕，沿用舊操作包裝但保存真正參數例外。
 * - 只在完整頁的正式annotation ID與parent都有效時發布；衝突不裁剪、不補值或生成ID。
 * - 寫入狀態不確定的空Create ID必須拒絕，不盲目重試或回報void成功。
 * 測試案例入口包含 SdkFailure_PreservesSameInnerAndFixedMessage、InvalidInput_RejectsWithoutSdk、
 * Download_InvalidCandidate_RejectsWholePage、Upload_EmptySdkIdentity_RejectsWithoutRetry、
 * DefaultUploadLimit_RejectsBeforeSdk、ExplicitLimits_EnforceAllBoundaries、
 * Limits_RejectInvalidConfigurationAndCannotMutate、Download_Base64PaddingAndRawLength_AreValidated；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.AttachmentOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 附件固定錯誤、正式識別／parent、容量與生命週期的安全契約。
    /// 所有資料為合成值；只替代 SDK I/O，不讀組織容量或假稱這是 D365 實機／授權證據。
    /// </summary>
    public sealed class AttachmentSafetyTests
    {
        private static readonly Guid ParentId = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid NoteId = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid OtherId = new Guid("33333333-3333-3333-3333-333333333333");

        /// <summary>SDK自身參數例外或逾時均保存同一InnerException，固定外層不回顯名稱、ID、時間或原訊息。</summary>
        /// <param name="upload">是否測上傳。</param>
        /// <param name="argument">是否注入SDK自身參數例外。</param>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void SdkFailure_PreservesSameInnerAndFixedMessage(bool upload, bool argument)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Exception original = argument ? new ArgumentException("synthetic_private_fault") : new TimeoutException("synthetic_private_fault");
            if (upload) sdk.Setup(s => s.Create(It.IsAny<Entity>())).Throws(original);
            else sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(original);
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() => Invoke(service, ref borrowed, upload));
            Assert.Equal(upload ? "The attachment could not be uploaded." : "The attachments could not be downloaded.", error.Message);
            Assert.Same(original, error.InnerException);
            Assert.Same(sdk.Object, borrowed);
            Assert.Single(sdk.Invocations);
        }

        /// <summary>無法確認輸入scope或內容的請求在SDK前拒絕，沿用舊操作包裝但保存真正參數例外。</summary>
        /// <param name="shape">明列的新安全邊界。</param>
        [Theory]
        [InlineData("download-sdk")]
        [InlineData("upload-sdk")]
        [InlineData("download-id")]
        [InlineData("upload-id")]
        [InlineData("upload-name")]
        [InlineData("upload-body")]
        public void InvalidInput_RejectsWithoutSdk(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = shape.EndsWith("sdk", StringComparison.Ordinal) ? null! : sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() =>
            {
                if (shape.StartsWith("download", StringComparison.Ordinal))
                    service.DownloadAttachment(ref borrowed, shape.EndsWith("id", StringComparison.Ordinal) ? Guid.Empty : ParentId);
                else
                    service.UploadAttachment(ref borrowed, shape == "upload-name" ? " " : "contact", "合成", "", "", "",
                        shape == "upload-body" ? null! : Array.Empty<byte>(), shape == "upload-id" ? Guid.Empty : ParentId);
            });
            Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>只在完整頁的正式annotation ID與parent都有效時發布；衝突不裁剪、不補值或生成ID。</summary>
        /// <param name="shape">合成SDK損壞回應。</param>
        [Theory]
        [InlineData("null")]
        [InlineData("empty-id")]
        [InlineData("duplicate")]
        [InlineData("wrong-table")]
        [InlineData("missing-parent")]
        [InlineData("wrong-parent")]
        [InlineData("wrong-parent-type")]
        [InlineData("wrong-body-type")]
        public void Download_InvalidCandidate_RejectsWholePage(string shape)
        {
            var page = new EntityCollection(new[] { Row(NoteId), Row(OtherId) });
            var bad = page.Entities[1];
            if (shape == "empty-id") bad.Id = Guid.Empty;
            else if (shape == "duplicate") bad.Id = NoteId;
            else if (shape == "wrong-table") bad.LogicalName = "contact";
            else if (shape == "missing-parent") bad.Attributes.Remove("objectid");
            else if (shape == "wrong-parent") bad["objectid"] = new EntityReference("contact", OtherId);
            else if (shape == "wrong-parent-type") bad["objectid"] = ParentId;
            else if (shape == "wrong-body-type") bad["documentbody"] = new byte[] { 1 };
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(shape == "null" ? null! : page);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() =>
                new AttachmentService(new object(), sdk.Object).DownloadAttachment(ref borrowed, ParentId));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Equal(2, page.Entities.Count);
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>寫入狀態不確定的空Create ID必須拒絕，不盲目重試或回報void成功。</summary>
        [Fact]
        public void Upload_EmptySdkIdentity_RejectsWithoutRetry()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(Guid.Empty);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() => Invoke(new AttachmentService(new object(), sdk.Object), ref borrowed, true));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>舊建構式也必須套用預設5MiB界線；在Base64與SDK前拒絕，不能保留無界後門。</summary>
        [Fact]
        public void DefaultUploadLimit_RejectsBeforeSdk()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = sdk.Object;
            var error = Assert.Throws<InvalidOperationException>(() => service.UploadAttachment(ref borrowed,
                "contact", "", "", "", "", new byte[5 * 1024 * 1024 + 1], ParentId));
            Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 以極小限制實際執行有界API：單檔／單頁本文／列數超界皆整體拒絕，
        /// 恰好在邊界的合法檔案則成功；容量設定不可變，不代表組織實際上限。
        /// </summary>
        /// <param name="shape">容量場景。</param>
        [Theory]
        [InlineData("upload-over")]
        [InlineData("upload-exact")]
        [InlineData("page-total")]
        [InlineData("page-rows")]
        [InlineData("page-single")]
        [InlineData("page-exact")]
        public void ExplicitLimits_EnforceAllBoundaries(string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = CreateWithLimits(sdk.Object, 4, shape == "page-exact" ? 16 : 8, 2);
            var borrowed = sdk.Object;
            if (shape.StartsWith("upload", StringComparison.Ordinal))
            {
                sdk.Setup(s => s.Create(It.IsAny<Entity>())).Returns(NoteId);
                if (shape == "upload-over")
                {
                    var error = Assert.Throws<InvalidOperationException>(() => service.UploadAttachment(ref borrowed,
                        "contact", "", "", "", "", new byte[5], ParentId));
                    Assert.IsType<ArgumentOutOfRangeException>(error.InnerException);
                    sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Never);
                }
                else
                {
                    service.UploadAttachment(ref borrowed, "contact", "", "", "", "", new byte[4], ParentId);
                    sdk.Verify(s => s.Create(It.Is<Entity>(e => (string)e["documentbody"] == "AAAAAA==")), Times.Once);
                }
            }
            else
            {
                var page = new EntityCollection(new[] { Row(NoteId, "AAAAAA=="), Row(OtherId, "AAAAAA==") });
                if (shape == "page-rows") page.Entities.Add(Row(ParentId));
                if (shape == "page-single") page.Entities[0]["documentbody"] = "AAAAAAAAAAAA";
                sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
                if (shape == "page-exact") Assert.Same(page, service.DownloadAttachment(ref borrowed, ParentId));
                else Assert.IsType<InvalidOperationException>(Assert.Throws<InvalidOperationException>(() =>
                    service.DownloadAttachment(ref borrowed, ParentId)).InnerException);
                Assert.Equal(shape == "page-rows" ? 3 : 2, page.Entities.Count);
                sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            }
        }

        /// <summary>容量物件只接受正值，getter不得有setter；null設定不能悄悄降級成無界模式。</summary>
        [Fact]
        public void Limits_RejectInvalidConfigurationAndCannotMutate()
        {
            var type = LimitsType();
            foreach (var property in type.GetProperties()) Assert.Null(property.SetMethod);
            foreach (var values in new[] { new[] { 0, 8, 2 }, new[] { 4, 0, 2 }, new[] { 4, 8, 0 } })
            {
                var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => Activator.CreateInstance(type, values.Cast<object>().ToArray()));
                Assert.IsType<ArgumentOutOfRangeException>(failure.InnerException);
            }
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var constructor = typeof(AttachmentService).GetConstructor(new[] { typeof(object), typeof(IOrganizationService), type });
            Assert.NotNull(constructor);
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => constructor!.Invoke(new object?[] { new object(), sdk.Object, null }));
            Assert.IsType<ArgumentNullException>(error.InnerException);
        }

        /// <summary>
        /// Base64 字元數不能代替解碼長度：四、五、六位元組都可能佔八字元。
        /// 以四位元組限制測試 padding、非法字元與 .NET 接受的空白；只掃描，不配置解碼副本。
        /// </summary>
        /// <param name="body">合成 Base64 或損壞內容。</param>
        /// <param name="accepted">是否應保留完整原字串並發布。</param>
        [Theory]
        [InlineData("AAAAAA==", true)]
        [InlineData("AAAAAAA=", false)]
        [InlineData("AAAAAAAA", false)]
        [InlineData("AA \t\r\nEC", true)]
        [InlineData("", true)]
        [InlineData(" \t\r\n", true)]
        [InlineData("abc", false)]
        [InlineData("A===", false)]
        [InlineData("AA=A", false)]
        [InlineData("tCo==", false)]
        [InlineData("AA?=", false)]
        [InlineData("=AAA", false)]
        [InlineData("AA\u00a0==", false)]
        public void Download_Base64PaddingAndRawLength_AreValidated(string body, bool accepted)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var page = new EntityCollection(new[] { Row(NoteId, body) });
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
            var service = CreateWithLimits(sdk.Object, 4, 64, 2);
            var borrowed = sdk.Object;
            if (accepted)
            {
                Assert.Same(page, service.DownloadAttachment(ref borrowed, ParentId));
                Assert.Equal(body, Assert.Single(page.Entities)["documentbody"]);
            }
            else
            {
                var failure = Assert.Throws<InvalidOperationException>(() => service.DownloadAttachment(ref borrowed, ParentId));
                Assert.IsType<InvalidOperationException>(failure.InnerException);
                Assert.Equal(body, Assert.Single(page.Entities)["documentbody"]);
            }
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// null 本文仍是合法文字附註；反之 parent 名稱缺失不能視為有效關聯。
        /// 防止新增 parent 防護誤刪合法附註或只驗 GUID 而接受無表名關聯。
        /// </summary>
        /// <param name="validParent">是否提供完整 parent。</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Download_NullBodyAndParentName_RespectPublicationContract(bool validParent)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var row = Row(NoteId);
            row["documentbody"] = null;
            if (!validParent) row["objectid"] = new EntityReference(" ", ParentId);
            var page = new EntityCollection(new[] { row });
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
            var borrowed = sdk.Object;
            var service = new AttachmentService(new object(), sdk.Object);
            if (validParent) Assert.Same(page, service.DownloadAttachment(ref borrowed, ParentId));
            else Assert.IsType<InvalidOperationException>(Assert.Throws<InvalidOperationException>(() =>
                service.DownloadAttachment(ref borrowed, ParentId)).InnerException);
        }

        /// <summary>
        /// 以固定種子產生512組合法本文、插入標準空白及損壞padding共1536例，
        /// 合法候選實際以BCL解碼比對原bytes，產品則不得為驗證再配置解碼副本。
        /// Framework的BCL會接受部分多餘padding（例如tCo==），net10不接受；產品兩目標都依標準拒絕，
        /// 不能把單一Framework的寬鬆實作當作共同格式規格。錯算4bytes界線或接受損壞候選即失敗。
        /// </summary>
        [Fact]
        public void Download_Base64Acceptance_MatchesStandardAcrossFrameworkDecoders()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var row = Row(NoteId);
            var page = new EntityCollection(new[] { row });
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(page);
            var service = CreateWithLimits(sdk.Object, 4, 128, 2);
            var borrowed = sdk.Object;
            var random = new Random(731);
            for (var index = 0; index < 512; index++)
            {
                var bytes = new byte[index % 9];
                random.NextBytes(bytes);
                var encoded = Convert.ToBase64String(bytes);
                var candidates = new[] { encoded, " \t" + encoded.Insert(encoded.Length / 2, "\r\n") + " ", encoded + "=" };
                for (var variant = 0; variant < candidates.Length; variant++)
                {
                    var body = candidates[variant];
                    // 前兩種是合法編碼，其BCL解碼必須還原原bytes；第三種故意多一個padding，一律拒絕。
                    if (variant < 2) Assert.Equal(bytes, Convert.FromBase64String(body));
                    var expected = variant < 2 && bytes.Length <= 4;
                    row["documentbody"] = body;
                    if (expected)
                    {
                        EntityCollection? result = null;
                        var failure = Record.Exception(() => result = service.DownloadAttachment(ref borrowed, ParentId));
                        Assert.True(failure == null, $"synthetic index={index}; UTF16={string.Join(",", body.Select(c => (int)c))}; {failure?.GetType().Name}");
                        Assert.Same(page, result);
                    }
                    else Assert.IsType<InvalidOperationException>(Assert.Throws<InvalidOperationException>(() =>
                        service.DownloadAttachment(ref borrowed, ParentId)).InnerException);
                    Assert.Equal(body, row["documentbody"]);
                }
            }
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(1536));
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 保留舊七欄null metadata與零bytes語義；新nullable註記不能悄悄變更寫入值或省略欄位。
        /// 同時拒絕兩個舊ctor的null輸入，沒有SDK I/O或額外處置。
        /// </summary>
        [Fact]
        public void Upload_NullMetadataRemainsSevenFields_AndConstructorRejectsNulls()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            Assert.Equal("logger", Assert.Throws<ArgumentNullException>(() => new AttachmentService(null!, sdk.Object)).ParamName);
            Assert.Equal("organizationService", Assert.Throws<ArgumentNullException>(() => new AttachmentService(new object(), null!)).ParamName);
            sdk.Setup(s => s.Create(It.IsAny<Entity>())).Callback<Entity>(row =>
            {
                Assert.Equal(7, row.Attributes.Count);
                foreach (var key in new[] { "subject", "notetext", "filename", "mimetype" })
                {
                    Assert.True(row.Contains(key));
                    Assert.Null(row[key]);
                }
                Assert.Equal("", row["documentbody"]);
            }).Returns(NoteId);
            var borrowed = sdk.Object;
            new AttachmentService(new object(), sdk.Object).UploadAttachment(ref borrowed,
                "contact", null!, null!, null!, null!, Array.Empty<byte>(), ParentId);
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Once);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>
        /// 共用無狀態服務但每次借入已授權A/B SDK，1000次操作驗證回應／候選不串資料。
        /// 所有有限Task全await，活動計數回零，ref不改、借入SDK不Dispose；不建立或保存背景工作。
        /// </summary>
        /// <returns>測試擁有且完整排空的有限工作。</returns>
        [Fact]
        public async Task ConcurrentRefScopes_NoCrossTalkAndDrain()
        {
            var active = 0;
            var completed = 0;
            var ctorSdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new AttachmentService(new object(), ctorSdk.Object);
            var clients = new[] { new Mock<IOrganizationService>(MockBehavior.Strict), new Mock<IOrganizationService>(MockBehavior.Strict) };
            for (var scope = 0; scope < 2; scope++)
            {
                var parent = scope == 0 ? ParentId : OtherId;
                clients[scope].As<IDisposable>();
                clients[scope].Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase input) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        var query = Assert.IsType<QueryExpression>(input);
                        Assert.Equal(parent.ToString(), Assert.Single(Assert.Single(query.Criteria.Conditions).Values));
                        var row = Row(NoteId);
                        row["objectid"] = new EntityReference("contact", parent);
                        return new EntityCollection(new[] { row });
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
                clients[scope].Setup(s => s.Create(It.IsAny<Entity>())).Returns((Entity entity) =>
                {
                    Interlocked.Increment(ref active);
                    try
                    {
                        Assert.Equal(parent, Assert.IsType<EntityReference>(entity["objectid"]).Id);
                        Assert.Equal(parent.ToString(), entity["subject"]);
                        Assert.Equal("AQID", entity["documentbody"]);
                        return NoteId;
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
            }
            // Moq 的 Object 首次建立不是受測並行契約；先在單執行緒完成proxy初始化，避免fixture自己競態。
            var sdkObjects = clients.Select(client => client.Object).ToArray();
            var tasks = Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
            {
                var scope = index % 2;
                var parent = scope == 0 ? ParentId : OtherId;
                var borrowed = sdkObjects[scope];
                var row = Assert.Single(service.DownloadAttachment(ref borrowed, parent).Entities);
                Assert.Equal(parent, Assert.IsType<EntityReference>(row["objectid"]).Id);
                row["subject"] = "caller-mutated";
                service.UploadAttachment(ref borrowed, "contact", parent.ToString(), "", "", "", new byte[] { 1, 2, 3 }, parent);
                Assert.Same(sdkObjects[scope], borrowed);
                Interlocked.Increment(ref completed);
            })).ToArray();
            await Task.WhenAll(tasks);
            Assert.Equal(1000, Volatile.Read(ref completed));
            Assert.Equal(0, Volatile.Read(ref active));
            ctorSdk.VerifyNoOtherCalls();
            foreach (var client in clients) client.As<IDisposable>().Verify(d => d.Dispose(), Times.Never);
        }

        /// <summary>
        /// 原SDK故障或頁面組裝部分成功後的錯parent都不得發布；caller明確下一次呼叫才重試。
        /// 成功後caller可修改自己的頁面，第三次SDK結果不受影響；所有借入SDK仍由原owner處置。
        /// </summary>
        [Fact]
        public void FailureRetryAndCallerMutation_DoNotPublishPartialOrRetainResults()
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var disposable = sdk.As<IDisposable>();
            var failure = new TimeoutException("synthetic_private_transport");
            sdk.SetupSequence(s => s.Create(It.IsAny<Entity>())).Throws(failure).Returns(NoteId);
            var partial = new EntityCollection(new[] { Row(NoteId), Row(OtherId) });
            partial.Entities[1]["objectid"] = new EntityReference("contact", OtherId);
            sdk.SetupSequence(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns(partial)
                .Returns(new EntityCollection(new[] { Row(NoteId) })).Returns(new EntityCollection(new[] { Row(NoteId) }));
            var service = new AttachmentService(new object(), sdk.Object);
            var borrowed = sdk.Object;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => Invoke(service, ref borrowed, true)).InnerException);
            Invoke(service, ref borrowed, true);
            Assert.Throws<InvalidOperationException>(() => service.DownloadAttachment(ref borrowed, ParentId));
            Assert.Equal(2, partial.Entities.Count);
            var first = service.DownloadAttachment(ref borrowed, ParentId);
            Assert.Single(first.Entities)["subject"] = "caller-mutated";
            var next = service.DownloadAttachment(ref borrowed, ParentId);
            Assert.Equal("合成同名", Assert.Single(next.Entities)["subject"]);
            Assert.NotSame(first, next);
            sdk.Verify(s => s.Create(It.IsAny<Entity>()), Times.Exactly(2));
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(3));
            disposable.Verify(s => s.Dispose(), Times.Never);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>ctor的logger與未使用SDK不得延長生命；保持產品服務存活時，兩個原輸入都應可回收。</summary>
        [Fact]
        public void Constructor_DoesNotRetainUnusedInputs()
        {
            var service = CreateWithWeakInputs(out var logger, out var sdk);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(logger.IsAlive);
            Assert.False(sdk.IsAlive);
            GC.KeepAlive(service);
        }

        /// <summary>產生單次合成附註；沒有本文仍是合法附註，不需要補造空檔案。</summary>
        private static Entity Row(Guid id, string? body = null)
        {
            var row = new Entity("annotation", id) { ["objectid"] = new EntityReference("contact", ParentId), ["subject"] = "合成同名" };
            if (body != null) row["documentbody"] = body;
            return row;
        }

        /// <summary>呼叫真實上傳／下載入口，不在fixture模擬錯誤或容量判斷。</summary>
        private static void Invoke(AttachmentService service, ref IOrganizationService sdk, bool upload)
        {
            if (upload) service.UploadAttachment(ref sdk, "contact", "合成", "", "", "", new byte[] { 1 }, ParentId);
            else service.DownloadAttachment(ref sdk, ParentId);
        }

        /// <summary>反射讓新增API不存在時也能執行紅測，而不是把編譯錯誤當作驗收證據。</summary>
        private static Type LimitsType()
        {
            var type = typeof(AttachmentService).Assembly.GetType("ToolUtilityNameSpace.AttachmentOperations.AttachmentTransferLimits");
            Assert.NotNull(type);
            return type!;
        }

        /// <summary>只經公開constructor建立產品，不在fixture覆寫限制或提供測試專用產品入口。</summary>
        private static AttachmentService CreateWithLimits(IOrganizationService sdk, int bytes, int characters, int rows)
        {
            var type = LimitsType();
            var limits = Activator.CreateInstance(type, bytes, characters, rows);
            var constructor = typeof(AttachmentService).GetConstructor(new[] { typeof(object), typeof(IOrganizationService), type });
            Assert.NotNull(constructor);
            return Assert.IsType<AttachmentService>(constructor!.Invoke(new[] { new object(), sdk, limits }));
        }

        /// <summary>隔離JIT區域及Moq局部變數；沒有方法呼叫紀錄會保存SDK。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static AttachmentService CreateWithWeakInputs(out WeakReference logger, out WeakReference sdk)
        {
            var log = new object();
            var client = new Mock<IOrganizationService>(MockBehavior.Strict).Object;
            var service = new AttachmentService(log, client);
            logger = new WeakReference(log);
            sdk = new WeakReference(client);
            return service;
        }
    }
}

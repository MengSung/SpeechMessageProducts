using System.Collections;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using ToolUtilityNameSpace.ActivityOperations;
using Xunit;

namespace ToolUtility.Tests.ActivityOperations
{
    /// <summary>
    /// 以原 host 重現跨表識別覆寫與兩清單部分發布，再保護完整候選、故障重試及有界讀取。
    /// 只替代 SDK，所有識別及故障內容均為合成值；不代表 D365 實機或伺服器授權驗收。
    /// </summary>
    public sealed class ActivitySafetyTests
    {
        private static readonly Guid First = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Second = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Prefix = new Guid("33333333-3333-3333-3333-333333333333");

        /// <summary>不同資料表的同 GUID 必須各保留正確記錄，不能被最後一張表覆寫；同名亦不可合併。</summary>
        [Fact]
        public void CrossTableSameGuid_PreservesBothDatabaseIdentities()
        {
            var sdk = Reader(query => new EntityCollection(new[]
                { new Entity(query.EntityName, First) { ["name"] = "合成同名" } }));
            var values = new ArrayList();
            var types = new ArrayList();
            new ActivityService(new object(), sdk.Object).GetActivityPartyList(
                Activity(Party("contact", First), Party("systemuser", First)), "to", values, types);
            Assert.Equal(new[] { "contact", "systemuser" }, types.Cast<string>());
            Assert.Equal(new[] { "contact", "systemuser" }, values.Cast<Entity>().Select(row => row.LogicalName));
            Assert.All(values.Cast<Entity>(), row => Assert.Equal(First, row.Id));
            Assert.NotSame(values[0], values[1]);
        }

        /// <summary>
        /// 第二組 I/O 失敗時不能先 append 型別或第一組資料；明確再次呼叫才重試，
        /// 固定訊息不含 SDK 內容且 InnerException 必須仍是同一實例。
        /// </summary>
        [Fact]
        public void LaterGroupFailure_PreservesPrefixAndExplicitRetryPublishesCompletePair()
        {
            var fail = true;
            var original = new TimeoutException("synthetic_private_fault");
            var sdk = Reader(query =>
            {
                if (query.EntityName == "systemuser" && fail) throw original;
                return new EntityCollection(new[] { new Entity(query.EntityName, First) });
            });
            var prefix = new Entity("contact", Prefix);
            var values = new ArrayList { prefix };
            var types = new ArrayList { "contact" };
            var service = new ActivityService(new object(), sdk.Object);
            var input = Activity(Party("contact", First), Party("systemuser", First));
            var error = Assert.Throws<InvalidOperationException>(() => service.GetActivityPartyList(input, "to", values, types));
            Assert.Equal("The activity parties could not be retrieved.", error.Message);
            Assert.Same(original, error.InnerException);
            Assert.Equal(new[] { prefix }, values.Cast<Entity>());
            Assert.Equal(new[] { "contact" }, types.Cast<string>());
            fail = false;
            service.GetActivityPartyList(input, "to", values, types);
            Assert.Equal(3, values.Count);
            Assert.Equal(new[] { "contact", "contact", "systemuser" }, types.Cast<string>());
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Exactly(4));
        }

        /// <summary>解析後段非法參照、重複正式識別或既有前綴衝突時，兩個入口皆須在任何 I/O／append 前拒絕。</summary>
        /// <param name="ids">是否測純 ID 入口。</param>
        /// <param name="shape">注入的不合法來源或前綴。</param>
        [Theory]
        [InlineData(false, "wrong-party")]
        [InlineData(true, "wrong-party")]
        [InlineData(false, "null-party")]
        [InlineData(true, "null-party")]
        [InlineData(false, "empty-id")]
        [InlineData(true, "empty-id")]
        [InlineData(false, "invalid-name")]
        [InlineData(true, "invalid-name")]
        [InlineData(false, "duplicate")]
        [InlineData(true, "duplicate")]
        [InlineData(false, "prefix-conflict")]
        [InlineData(true, "prefix-conflict")]
        [InlineData(false, "prefix-misaligned")]
        [InlineData(true, "prefix-misaligned")]
        [InlineData(false, "prefix-wrong-type")]
        [InlineData(true, "prefix-wrong-type")]
        [InlineData(false, "prefix-empty-id")]
        [InlineData(true, "prefix-empty-id")]
        [InlineData(false, "prefix-duplicate")]
        [InlineData(true, "prefix-duplicate")]
        public void InvalidInput_RejectsWithoutPartialPublicationOrSdk(bool ids, string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var service = new ActivityService(new object(), sdk.Object);
            var next = Party("contact", Second);
            if (shape == "wrong-party") next["partyid"] = "synthetic_invalid_reference";
            if (shape == "null-party") next["partyid"] = null;
            if (shape == "empty-id") next["partyid"] = new EntityReference("contact", Guid.Empty);
            if (shape == "invalid-name") next["partyid"] = new EntityReference("contact<", Second);
            if (shape == "duplicate") next["partyid"] = new EntityReference("contact", First);
            object prefix = ids ? (object)Prefix : new Entity("contact", Prefix);
            if (shape == "prefix-conflict") prefix = ids ? (object)First : new Entity("contact", First);
            if (shape == "prefix-empty-id") prefix = ids ? (object)Guid.Empty : new Entity("contact", Guid.Empty);
            var values = new ArrayList { prefix };
            var types = new ArrayList { shape == "prefix-wrong-type" ? "systemuser" : "contact" };
            // 純 ID 沒有 Entity 型別可交叉比對；以非字串型別值注入同一種前綴錯誤。
            if (shape == "prefix-wrong-type" && ids) types[0] = new object();
            if (shape == "prefix-misaligned") types.Add("contact");
            if (shape == "prefix-duplicate") { values.Add(prefix); types.Add("contact"); }
            var oldValues = values.ToArray();
            var oldTypes = types.ToArray();
            Assert.Throws<InvalidOperationException>(() => Invoke(service,
                Activity(Party("contact", First), next), values, types, ids));
            Assert.Equal(oldValues, values.ToArray());
            Assert.Equal(oldTypes, types.ToArray());
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>SDK 的損壞頁、未請求記錄、遺失記錄、重複與錯表皆拒絕整個候選，不能只略過缺失列。</summary>
        /// <param name="shape">模擬 SDK 回應。</param>
        [Theory]
        [InlineData("null")]
        [InlineData("missing")]
        [InlineData("unrequested")]
        [InlineData("empty-id")]
        [InlineData("duplicate")]
        [InlineData("wrong-table")]
        [InlineData("wrong-page-table")]
        [InlineData("empty-more")]
        [InlineData("all-with-more")]
        public void InvalidSdkCandidate_PreservesBothOutputPrefixes(string shape)
        {
            var page = new EntityCollection(new[] { new Entity("contact", First), new Entity("contact", Second) });
            if (shape == "missing") page.Entities.RemoveAt(1);
            if (shape == "unrequested") page.Entities[1].Id = Guid.NewGuid();
            if (shape == "empty-id") page.Entities[1].Id = Guid.Empty;
            if (shape == "duplicate") page.Entities[1].Id = First;
            if (shape == "wrong-table") page.Entities[1].LogicalName = "systemuser";
            if (shape == "wrong-page-table") page.EntityName = "systemuser";
            if (shape == "empty-more") page.Entities.Clear();
            if (shape.EndsWith("more", StringComparison.Ordinal)) page.MoreRecords = true;
            var sdk = Reader(_ => shape == "null" ? null! : page);
            var prefix = new Entity("contact", Prefix);
            var values = new ArrayList { prefix };
            var types = new ArrayList { "contact" };
            Assert.Throws<InvalidOperationException>(() => new ActivityService(new object(), sdk.Object)
                .GetActivityPartyList(Activity(Party("contact", First), Party("contact", Second)), "to", values, types));
            Assert.Equal(new[] { prefix }, values.Cast<Entity>());
            Assert.Equal(new[] { "contact" }, types.Cast<string>());
            sdk.Verify(s => s.RetrieveMultiple(It.IsAny<QueryBase>()), Times.Once);
        }

        /// <summary>
        /// 舊單頁查詢若發現 MoreRecords，丟棄探測頁並以正式主鍵排序重新完整讀取；
        /// cookie 原樣帶入、頁碼增加，只有全部記錄讀完才按原參與者順序發布。
        /// </summary>
        /// <param name="cookie">SDK 是否提供分頁 cookie；未提供時仍以有界簡單分頁讀取。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MoreRecords_RestartsWithDeterministicOrderAndPublishesOnlyCompleteResult(bool cookie)
        {
            var calls = 0;
            var sdk = Reader(query =>
            {
                calls++;
                Assert.True(query.ColumnSet.AllColumns);
                Assert.Null(query.TopCount);
                Assert.Equal(500, query.PageInfo.Count);
                if (calls == 1)
                {
                    Assert.Empty(query.Orders);
                    return new EntityCollection(new[] { new Entity("contact", First) })
                        { MoreRecords = true, PagingCookie = "discarded-probe" };
                }
                var order = Assert.Single(query.Orders);
                Assert.Equal("contactid", order.AttributeName);
                Assert.Equal(OrderType.Ascending, order.OrderType);
                Assert.Equal(calls - 1, query.PageInfo.PageNumber);
                Assert.Equal(calls == 3 && cookie ? "synthetic-cookie" : null, query.PageInfo.PagingCookie);
                return new EntityCollection(new[] { new Entity("contact", calls == 2 ? Second : First) })
                    { MoreRecords = calls == 2, PagingCookie = calls == 2 && cookie ? "synthetic-cookie" : null };
            });
            var values = new ArrayList();
            var types = new ArrayList();
            new ActivityService(new object(), sdk.Object).GetActivityPartyList(
                Activity(Party("contact", First), Party("contact", Second)), "to", values, types);
            Assert.Equal(new[] { First, Second }, values.Cast<Entity>().Select(row => row.Id));
            Assert.Equal(3, calls);
        }

        /// <summary>重啟後跨頁重複、空進度或第二頁故障不能發布探測頁／前半頁，且讀取次數有界。</summary>
        /// <param name="fault">第二個正式頁的故障形狀。</param>
        [Theory]
        [InlineData("duplicate")]
        [InlineData("empty")]
        [InlineData("timeout")]
        public void PagingFailure_RejectsWithoutPublishingProbeOrFirstPage(string fault)
        {
            var calls = 0;
            var sdk = Reader(_ =>
            {
                calls++;
                if (calls == 3 && fault == "timeout") throw new TimeoutException("synthetic_private_fault");
                return new EntityCollection(calls == 3 && fault == "empty" ? Array.Empty<Entity>()
                    : new[] { new Entity("contact", First) }) { MoreRecords = calls < 3 || fault == "empty" };
            });
            var values = new ArrayList();
            var types = new ArrayList();
            Assert.Throws<InvalidOperationException>(() => new ActivityService(new object(), sdk.Object)
                .GetActivityPartyList(Activity(Party("contact", First), Party("contact", Second)), "to", values, types));
            Assert.Empty(values);
            Assert.Empty(types);
            Assert.Equal(3, calls);
        }

        /// <summary>5001 個來源容器即拒絕，包含無正式收件者的外部地址；不能用略過列突破本地預算。</summary>
        /// <param name="ids">是否測純 ID 入口。</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SourceBudget_RejectsBeforeSdk(bool ids)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            var values = new ArrayList();
            var types = new ArrayList();
            Assert.Throws<InvalidOperationException>(() => Invoke(new ActivityService(new object(), sdk.Object),
                Activity(Enumerable.Range(0, 5001).Select(_ => new Entity("activityparty")).ToArray()), values, types, ids));
            Assert.Empty(values);
            Assert.Empty(types);
            sdk.VerifyNoOtherCalls();
        }

        /// <summary>1001 個正式參照必須分成 500／500／1 三批；每批完整取得，不能 N+1 或截斷結果。</summary>
        [Fact]
        public void BatchBudget_ChunksWithoutDroppingAnyIdentity()
        {
            var batchSizes = new List<int>();
            var sdk = Reader(query =>
            {
                var condition = Assert.Single(query.Criteria.Conditions);
                batchSizes.Add(condition.Values.Count);
                Assert.Equal(ConditionOperator.In, condition.Operator);
                return new EntityCollection(condition.Values.Cast<Guid>()
                    .Reverse().Select(id => new Entity(query.EntityName, id)).ToArray());
            });
            var parties = Enumerable.Range(0, 1001).Select(_ => Party("contact", Guid.NewGuid())).ToArray();
            var values = new ArrayList();
            var types = new ArrayList();
            new ActivityService(new object(), sdk.Object).GetActivityPartyList(Activity(parties), "to", values, types);
            Assert.Equal(new[] { 500, 500, 1 }, batchSizes);
            Assert.Equal(parties.Select(row => ((EntityReference)row["partyid"]).Id), values.Cast<Entity>().Select(row => row.Id));
            Assert.Equal(1001, types.Count);
        }

        /// <summary>讀取及兩種狀態變更的 SDK timeout／自身參數例外均固定包裝且保留同一原始例外，不重試寫入。</summary>
        /// <param name="operation">讀取、完成或排程入口。</param>
        /// <param name="argument">是否注入 SDK 自身的 ArgumentException。</param>
        [Theory]
        [InlineData("read", false)]
        [InlineData("read", true)]
        [InlineData("complete", false)]
        [InlineData("complete", true)]
        [InlineData("schedule", false)]
        [InlineData("schedule", true)]
        public void SdkFailure_KeepsOriginalExceptionAndFixedWrapper(string operation, bool argument)
        {
            Exception original = argument ? new ArgumentException("synthetic_private_fault") : new TimeoutException("synthetic_private_fault");
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Throws(original);
            sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Throws(original);
            var service = new ActivityService(new object(), sdk.Object);
            var error = Assert.Throws<InvalidOperationException>(() =>
            {
                if (operation == "read") service.GetActivityPartyList(Activity(Party("contact", First)), "to", new ArrayList(), new ArrayList());
                else if (operation == "complete") service.SetActivityStatusToCompleted("email", First);
                else service.SetAppointmentStatusToScheduled(First);
            });
            Assert.Same(original, error.InnerException);
            Assert.Equal(operation == "read" ? "The activity parties could not be retrieved."
                : operation == "complete" ? "The activity could not be completed." : "The appointment could not be scheduled.", error.Message);
            Assert.Single(sdk.Invocations);
        }

        /// <summary>狀態變更沒有正式 ID 時不可送出；SDK 回傳 null／其他訊息不能當作成功，外部 SDK 不得混用。</summary>
        /// <param name="appointment">是否測約會排程。</param>
        /// <param name="shape">空 ID、null 或錯誤回應。</param>
        [Theory]
        [InlineData(false, "empty-id")]
        [InlineData(true, "empty-id")]
        [InlineData(false, "null")]
        [InlineData(true, "null")]
        [InlineData(false, "wrong-response")]
        [InlineData(true, "wrong-response")]
        public void SetState_InvalidIdentityOrResponse_CannotReportSuccess(bool appointment, string shape)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            if (shape != "empty-id")
                sdk.Setup(s => s.Execute(It.IsAny<OrganizationRequest>())).Returns(shape == "null" ? null! : new OrganizationResponse());
            var service = new ActivityService(new object(), sdk.Object);
            Action action = () =>
            {
                if (appointment) service.SetAppointmentStatusToScheduled(shape == "empty-id" ? Guid.Empty : First);
                else service.SetActivityStatusToCompleted("email", shape == "empty-id" ? Guid.Empty : First);
            };
            if (shape == "empty-id") { Assert.Throws<ArgumentException>(action); sdk.VerifyNoOtherCalls(); }
            else
            {
                var error = Assert.Throws<InvalidOperationException>(action);
                Assert.IsType<InvalidOperationException>(error.InnerException);
                Assert.Single(sdk.Invocations);
            }
        }

        /// <summary>建立指定查詢結果的嚴格 SDK；只允許 RetrieveMultiple，防止測試掩蓋多餘的呼叫。</summary>
        private static Mock<IOrganizationService> Reader(Func<QueryExpression, EntityCollection> read)
        {
            var sdk = new Mock<IOrganizationService>(MockBehavior.Strict);
            sdk.Setup(s => s.RetrieveMultiple(It.IsAny<QueryBase>())).Returns((QueryBase query) => read(Assert.IsType<QueryExpression>(query)));
            return sdk;
        }

        /// <summary>合成參與者容器不補造正式列 ID；唯一正式識別來自 partyid。</summary>
        private static Entity Party(string name, Guid id) => new Entity("activityparty") { ["partyid"] = new EntityReference(name, id) };

        /// <summary>本次操作擁有的合成活動；不保存於服務或共用狀態。</summary>
        private static Entity Activity(params Entity[] parties) => new Entity("email", Guid.NewGuid()) { ["to"] = new EntityCollection(parties) };

        /// <summary>以相同輸入測實體及純 ID 入口，不繞過產品的完整解析／發布路徑。</summary>
        private static void Invoke(ActivityService service, Entity activity, ArrayList values, ArrayList types, bool ids)
        {
            if (ids) service.GetActivityPartyIdList(activity, "to", values, types);
            else service.GetActivityPartyList(activity, "to", values, types);
        }
    }
}

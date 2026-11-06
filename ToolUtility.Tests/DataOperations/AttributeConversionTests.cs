/*
 * 檔案整體說明
 *
 * 目的與定位：無 I/O 的屬性操作以固定值鎖特性，沒有可送 SDK 的 QueryExpression，故不虛構 XML 黃金檔。 每種型別驗證已存值、缺值 sentinel 及寫回；全部輸入是 caller 自有 
 * Entity，沒有共享快照或 cache。
 *
 * 主要功能與流程：
 * 本檔宣告 AttributeConversionTests、FaultingValue。
 * - 九個轉換器的人工固定樣本，避免以產品轉換器計算預期結果造成同源斷言。
 * - 同一公開 getter 對存在／缺少欄位維持原轉換與 sentinel，錯誤預設值或消失的欄位均可被攔下。
 * - 原 setter 的 add／replace 與型別包裝維持；Lookup 的正式 ID 及表名不被換成顯示名稱。
 * - 靜態 handler 保留正常 typed 讀寫、預設值及 null 字串替代，不能以重新導向而改成另一套 sentinel。
 * 測試案例入口包含 Values、OriginalGetters_PreserveStoredAndMissingValues、OriginalSetters_PreserveAddAndReplace、
 * OriginalStaticHandler_PreservesTypedValuesAndDefaults、Services_DoNotRetainLogger、
 * StaticHandler_PropagatesRealFormattingFailure、StaticHandler_RejectsWrongStoredType、
 * Lookup_ExplicitNullMeansClearedReference；其餘入口見下方成員契約。
 *
 * 使用邊界與維護：
 * 以本檔實際安排的輸入、替身及斷言判定契約；測試名稱只說明案例，不能代替執行結果。
 * 新增故障／隔離案例時，須明確說明注入點及決定性斷言；建立的檔案、串流、替身與背景工作由該案例負責清理。
 */
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xrm.Sdk;
using ToolUtilityNameSpace.AttributeOperations;
using ToolUtilityNameSpace.EntityOperations;
using Xunit;

namespace ToolUtility.Tests.DataOperations
{
    /// <summary>
    /// 無 I/O 的屬性操作以固定值鎖特性，沒有可送 SDK 的 QueryExpression，故不虛構 XML 黃金檔。
    /// 每種型別驗證已存值、缺值 sentinel 及寫回；全部輸入是 caller 自有 Entity，沒有共享快照或 cache。
    /// </summary>
    public sealed class AttributeConversionTests
    {
        /// <summary>九個轉換器的人工固定樣本，避免以產品轉換器計算預期結果造成同源斷言。</summary>
        public static IEnumerable<object[]> Values()
        {
            yield return new object[] { typeof(BoolAttributeService), true, true, false };
            yield return new object[] { typeof(IntAttributeService), 12, 12, 0 };
            yield return new object[] { typeof(StringAttributeService), "synthetic", "synthetic", "" };
            yield return new object[] { typeof(DateTimeAttributeService), new DateTime(2026, 11, 1), new DateTime(2026, 11, 1), DateTime.MinValue };
            yield return new object[] { typeof(MoneyAttributeService), new Money(12.5m), new Money(12.5m), new Money(0m) };
            yield return new object[] { typeof(LookupAttributeService), new EntityReference("account", Identity), Identity, Guid.Empty };
            yield return new object[] { typeof(OptionSetAttributeService), new OptionSetValue(7), 7, -9999 };
            yield return new object[] { typeof(FloatAttributeService), 12.5f, 12.5f, -9999f };
            yield return new object[] { typeof(DoubleAttributeService), 12.5, 12.5, -9999.0 };
        }

        /// <summary>只有合成身份，不由 caller 文字或列位置推算。</summary>
        private static readonly Guid Identity = new Guid("11111111-1111-1111-1111-111111111111");

        /// <summary>同一公開 getter 對存在／缺少欄位維持原轉換與 sentinel，錯誤預設值或消失的欄位均可被攔下。</summary>
        [Theory]
        [MemberData(nameof(Values))]
        public void OriginalGetters_PreserveStoredAndMissingValues(Type type, object stored, object expected, object missing)
        {
            var service = Activator.CreateInstance(type, new object())!;
            var getter = type.GetMethod("GetAttribute")!;
            var entity = new Entity("contact") { ["value"] = stored };
            EqualValue(expected, getter.Invoke(service, new object[] { entity, "value" }));
            EqualValue(missing, getter.Invoke(service, new object[] { entity, "missing" }));
        }

        /// <summary>原 setter 的 add／replace 與型別包裝維持；Lookup 的正式 ID 及表名不被換成顯示名稱。</summary>
        [Theory]
        [MemberData(nameof(Values))]
        public void OriginalSetters_PreserveAddAndReplace(Type type, object stored, object expected, object missing)
        {
            _ = missing;
            var service = Activator.CreateInstance(type, new object())!;
            var entity = new Entity("contact");
            object[] args = type == typeof(LookupAttributeService)
                ? new object[] { entity, "value", "account", Identity }
                : new object[] { entity, "value", type == typeof(OptionSetAttributeService) ? expected : stored };
            var setter = type.GetMethods().Single(method => method.Name == "SetAttribute"
                && method.GetParameters().Length == args.Length
                && (type != typeof(LookupAttributeService) || method.GetParameters().Length == 4));
            setter.Invoke(service, args);
            setter.Invoke(service, args);
            Assert.Single(entity.Attributes);
            if (type == typeof(LookupAttributeService))
            {
                var reference = Assert.IsType<EntityReference>(entity["value"]);
                Assert.Equal("account", reference.LogicalName);
                Assert.Equal(Identity, reference.Id);
            }
            else EqualValue(stored, entity["value"]);
        }

        /// <summary>靜態 handler 保留正常 typed 讀寫、預設值及 null 字串替代，不能以重新導向而改成另一套 sentinel。</summary>
        [Fact]
        public void OriginalStaticHandler_PreservesTypedValuesAndDefaults()
        {
            var entity = new Entity("contact");
            EntityAttributeHandler.SetStringAttribute(ref entity, "text", null!);
            EntityAttributeHandler.SetOptionSetAttribute(ref entity, "option", 4);
            EntityAttributeHandler.SetMoneyAttribute(ref entity, "money", new Money(5.5m));
            EntityAttributeHandler.SetLookupAttribute(ref entity, "lookup", "account", Identity);
            EntityAttributeHandler.SetDateTimeAttribute(ref entity, "date", new DateTime(2026, 11, 1));
            EntityAttributeHandler.SetBooleanAttribute(ref entity, "bool", true);
            Assert.Equal("", EntityAttributeHandler.GetStringAttribute(entity, "text"));
            Assert.Equal(4, EntityAttributeHandler.GetOptionSetAttribute(entity, "option"));
            Assert.Equal(5.5m, EntityAttributeHandler.GetMoneyAttribute(entity, "money").Value);
            Assert.Equal(Identity, EntityAttributeHandler.GetLookupAttribute(entity, "lookup"));
            Assert.Equal(new DateTime(2026, 11, 1), EntityAttributeHandler.GetDateTimeAttribute(entity, "date"));
            Assert.True(EntityAttributeHandler.GetBooleanAttribute(entity, "bool"));
            Assert.Equal(-1, EntityAttributeHandler.GetOptionSetAttribute(entity, "missing"));
            Assert.Equal(DateTime.MinValue, EntityAttributeHandler.GetDateTimeAttribute(entity, "missing"));
            Assert.Equal(Guid.Empty, EntityAttributeHandler.GetOwnerId(entity));
        }

        /// <summary>轉換器所有權不能包含 logger／request；保持服務存活仍應回收建構時僅供相容的物件。</summary>
        [Theory]
        [InlineData(typeof(BoolAttributeService))]
        [InlineData(typeof(IntAttributeService))]
        [InlineData(typeof(StringAttributeService))]
        [InlineData(typeof(DateTimeAttributeService))]
        [InlineData(typeof(MoneyAttributeService))]
        [InlineData(typeof(LookupAttributeService))]
        [InlineData(typeof(OptionSetAttributeService))]
        [InlineData(typeof(FloatAttributeService))]
        [InlineData(typeof(DoubleAttributeService))]
        [InlineData(typeof(AttributeServiceComposite))]
        public void Services_DoNotRetainLogger(Type type)
        {
            var pair = Create(type);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Assert.False(pair.Reference.IsAlive);
            GC.KeepAlive(pair.Service);
        }

        /// <summary>真正 ToString 故障保留同一例外，不能被靜態 handler 偽裝為正常空字串。</summary>
        [Fact]
        public void StaticHandler_PropagatesRealFormattingFailure()
        {
            var fault = new FormatException("synthetic-private-value");
            var entity = new Entity("contact") { ["value"] = new FaultingValue(fault) };
            Assert.Same(fault, Assert.Throws<FormatException>(() => EntityAttributeHandler.GetStringAttribute(entity, "value")));
        }

        /// <summary>非 DateTime／bool 的錯型不能被吞成 sentinel，讓最終 host 有機會接收真實故障。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StaticHandler_RejectsWrongStoredType(bool boolean)
        {
            var entity = new Entity("contact") { ["value"] = new object() };
            Assert.Throws<InvalidCastException>(() =>
            {
                if (boolean) EntityAttributeHandler.GetBooleanAttribute(entity, "value");
                else EntityAttributeHandler.GetDateTimeAttribute(entity, "value");
            });
        }

        /// <summary>Lookup 明確 null 是 caller 清空欄位，不應因 ToString 產生 NullReferenceException。</summary>
        [Fact]
        public void Lookup_ExplicitNullMeansClearedReference()
        {
            var entity = new Entity("contact") { ["value"] = null };
            Assert.Equal(Guid.Empty, new LookupAttributeService(new object()).GetAttribute(entity, "value"));
        }

        /// <summary>反射真實組件，阻止只搬介面卻留下實作／反射 host 相依；Framework 另由目標測試確認實載 net462。</summary>
        [Theory]
        [InlineData(typeof(BoolAttributeService))]
        [InlineData(typeof(IntAttributeService))]
        [InlineData(typeof(StringAttributeService))]
        [InlineData(typeof(DateTimeAttributeService))]
        [InlineData(typeof(MoneyAttributeService))]
        [InlineData(typeof(LookupAttributeService))]
        [InlineData(typeof(OptionSetAttributeService))]
        [InlineData(typeof(FloatAttributeService))]
        [InlineData(typeof(DoubleAttributeService))]
        [InlineData(typeof(AttributeServiceComposite))]
        [InlineData(typeof(IAttributeService))]
        [InlineData(typeof(EntityAttributeHandler))]
        [InlineData(typeof(ToolUtilityNameSpace.Core.IToolUtilityCore))]
        public void PureContractsAndImplementations_AreLoadedFromCore(Type type)
            => Assert.Equal("ToolUtility.Core", type.Assembly.GetName().Name);

        /// <summary>隔離 JIT 局部生命週期；服務仍為強參照，只有 logger 必須變成不可達。</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static (object Service, WeakReference Reference) Create(Type type)
        {
            var logger = new object();
            return (Activator.CreateInstance(type, logger)!, new WeakReference(logger));
        }

        /// <summary>比較 SDK Money 的值而非參照；其他型別直接比較人工預期，不呼叫產品 helper。</summary>
        private static void EqualValue(object expected, object? actual)
        {
            if (expected is Money money) Assert.Equal(money.Value, Assert.IsType<Money>(actual).Value);
            else Assert.Equal(expected, actual);
        }

        /// <summary>只在測試 scope 保存一個合成故障；ToString 失敗用來驗證真實例外路徑，無資源或背景工作。</summary>
        private sealed class FaultingValue
        {
            private readonly Exception _failure;
            /// <summary>保存本案例例外，不傳給外部 logger 或模型。</summary>
            internal FaultingValue(Exception failure) { _failure = failure; }
            /// <summary>確定性故障注入；產品若吞掉此例外，decisive assertion 必須失敗。</summary>
            public override string ToString() => throw _failure;
        }
    }
}

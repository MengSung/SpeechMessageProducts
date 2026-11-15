/*
 * 檔案整體說明
 * 目的與定位：保護P5-07課程QR轉為對外待辦的雙宿主契約，閱讀舊
 * DiscipleLessonsPluInClass.cs:285-297及QrCodeUtility.cs:399-418取得31／32張的期望。
 * 主要流程：以操作專用合成store驗包含15堂簽到／簽退、報名與活動限定問卷的整組命令，
 * 再驗同事件重送、內容版本與教會分區。錯誤ID、版本及預取消必須零I/O；
 * 儲存故障必須保留原例外，不以部分結果冒充整組成功。
 * 使用邊界與維護：本檔不產圖、不連D365、LINE或檔案分享；store集合只屬於測試，
 * finally清空全部命令及正式ID。沒有背景工作、計時器、SDK或需GC補救的資源，
 * 字典只模擬儲存契約，不宣稱已證明資料庫跨程序唯一性或實機影子驗收。
 */
using Newtonsoft.Json.Linq;
using SpeechMessageProducts.GoodShepherd.Core.Lessons;
using System.Threading;
using ToolUtilityNameSpace.Outbound;
using Xunit;

namespace SpeechMessageProducts.GoodShepherd.Core.Tests;

/// <summary>課程QR入列的正常與故障邊界；期望數量及欄位由舊碼閱讀取得。</summary>
public sealed class LessonLinkRuleTests
{
    /// <summary>核定名稱；驗所有圖片的純值命令、重送防重與A／B／A隔離，不用產品結果重生期望。</summary>
    [Fact]
    public void Lessons_Link_QrImage_EnqueuesTaskNotDrawing()
    {
        var church = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var other = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var lesson = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var store = new Store();
        try
        {
            var first = LessonLinkRule.Enqueue(store, church, lesson, 1, includePoll: false);
            Assert.Single(first); Assert.Single(store.Rows); Assert.DoesNotContain(Guid.Empty, first);
            var commands = store.Rows.Values.Select(x => x.Command).ToArray();
            Assert.All(commands, command =>
            {
                Assert.Equal(OutboundTaskType.QrImageGenerate, command.Type);
                Assert.Equal(church, command.ChurchId); Assert.Null(command.ContactId);
                var payload = JObject.Parse(command.PayloadJson);
                Assert.Equal(1, (int)payload["schema"]!);
                Assert.Equal(lesson, Guid.Parse((string)payload["lessonId"]!));
                Assert.Equal(1, (int)payload["contentVersion"]!);
                Assert.Equal(31, (int)payload["imageCount"]!);
                Assert.Equal(0, (int)payload["includePoll"]!);
                Assert.Equal(5, payload.Count);
            });
            Assert.Equal(first, LessonLinkRule.Enqueue(store, church, lesson, 1, includePoll: false));
            Assert.Single(store.Rows);
            var activity = LessonLinkRule.Enqueue(store, church, lesson, 1, includePoll: true);
            Assert.Single(activity); Assert.Empty(first.Intersect(activity)); Assert.Equal(2, store.Rows.Count);
            var poll = JObject.Parse(store.Rows.Values.Single(x => x.Id == activity[0]).Command.PayloadJson);
            Assert.Equal(32, (int)poll["imageCount"]!); Assert.Equal(1, (int)poll["includePoll"]!);
            Assert.Single(LessonLinkRule.Enqueue(store, other, lesson, 1, false));
            Assert.Equal(3, store.Rows.Count);
            Assert.Equal(first, LessonLinkRule.Enqueue(store, church, lesson, 1, false));
            var versioned = LessonLinkRule.Enqueue(store, church, lesson, 2, false);
            Assert.Empty(first.Intersect(versioned)); Assert.Equal(4, store.Rows.Count);
            store.Rows.Clear(); store.Calls = 0;
            Assert.Throws<ArgumentException>(() => LessonLinkRule.Enqueue(store, Guid.Empty, lesson, 1, false));
            Assert.Throws<ArgumentException>(() => LessonLinkRule.Enqueue(store, church, Guid.Empty, 1, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => LessonLinkRule.Enqueue(store, church, lesson, 0, false));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => LessonLinkRule.Enqueue(store, church, lesson, 1, false, cancellation.Token));
            Assert.Equal(0, store.Calls); Assert.Empty(store.Rows);
            var failure = new InvalidOperationException("Synthetic store failure.");
            store.Failure = failure;
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => LessonLinkRule.Enqueue(store, church, lesson, 1, false)));
            Assert.Equal(1, store.Calls); Assert.Empty(store.Rows);
        }
        finally { store.Rows.Clear(); store.Failure = null; }
    }

    /// <summary>單次合成store；同鍵驗不可變內容，正式ID由store另產生，不冒用業務事件ID。</summary>
    private sealed class Store : IOutboundEnqueueStore
    {
        /// <summary>操作專用已儲存命令及正式ID，唯一owner於測試finally清空。</summary>
        internal Dictionary<string, Stored> Rows { get; } = new(StringComparer.Ordinal);
        /// <summary>同步呼叫次數，用於確認先驗失敗零I/O。</summary>
        internal int Calls { get; set; }
        /// <summary>只供本案故障注入的原始例外，finally移除參考。</summary>
        internal Exception? Failure { get; set; }
        /// <summary>不排背景工作；同鍵衝突拒絕，不覆寫已儲存payload。</summary>
        public Guid CreateOrGet(OutboundTaskCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++;
            if (Failure is not null) throw Failure;
            if (Rows.TryGetValue(command.IdempotencyKey, out var old))
            {
                Assert.Equal(old.Command.PayloadJson, command.PayloadJson);
                Assert.Equal(old.Command.ChurchId, command.ChurchId);
                return old.Id;
            }
            var stored = new Stored(Guid.NewGuid(), command); Rows.Add(command.IdempotencyKey, stored);
            return stored.Id;
        }
    }
    /// <summary>由單次store擁有的純值結果，沒有SDK、連線或其他資源。</summary>
    private sealed record Stored(Guid Id, OutboundTaskCommand Command);
}

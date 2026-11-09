/*
檔案整體說明
目的與定位：為 P3-04 離線故障測試提供窄 DbConnection 接縫，確定性控制 Open 與每條 SQL 命令的取消／原例外，不模擬 SQL 唯一性成功。
主要功能與流程：probe factory 為每次操作建立新連線，callback 可等待手動時鐘取消或拋固定例外；命令保存參數型別供測試驗證，另可在 COMMIT 已成功回傳後注入清理故障，所有連線與命令的建立／釋放計數必須平衡。
使用邊界與維護：只持有限案例的計數與合成命令，不讀任何真連線。fixture 擁有 provider 與共享 diagnostics，手動 clock 的 timer 在停止後必歸零。資料庫真實結果另由 SqlIntegration 案例證明，本替身只驗失敗傳播、資源所有權與參數邊界。
*/
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using LineMessagingProcessor.AspNetCore.Webhooks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ToolUtilityNameSpace.Diagnostics;
using Xunit;

namespace LineMessagingProcessor.AspNetCore.Tests;

/// <summary>單案例 probe 與 DI owner；最多由該案例明確呼叫數建立連線，不保存 HTTP 或事件。</summary>
internal sealed class WebhookSqlProbe : IAsyncDisposable
{
    private readonly ExceptionDiagnostics diagnostics;
    /// <summary>正式服務從此容器解析，讓缺少組合 hook 也會產生功能紅燈。</summary>
    internal ServiceProvider Provider { get; }
    /// <summary>可推進時間與 timer 基線，不靠五秒 sleep 測期限。</summary>
    internal WebhookQueueClock Clock { get; } = new();
    /// <summary>產品每次 Open 會調此委派，故障由測試明確注入並等待 drain。</summary>
    internal Func<CancellationToken, Task> Open { get; set; } = _ => Task.CompletedTask;
    /// <summary>正式產品命令到達此接縫後才注入故障；只有測試自己規定的合成回應。</summary>
    internal Func<DbCommand, CancellationToken, Task<int>> Execute { get; set; } = (_, _) => Task.FromResult(1);
    /// <summary>只在 INSERT 命令實際釋放後注入故障，驗證 finally 不覆蓋主要 SQL 錯誤。</summary>
    internal Exception? InsertCommandDisposeFailure { get; set; }
    /// <summary>在已回傳成功 COMMIT 結果後注入清理故障，驗證不能撤回已確認的 queue 交接。</summary>
    internal Exception? CommitCommandDisposeFailure { get; set; }
    /// <summary>實際建立與釋放的 owner 計數，測試須比較而非只看最後回應。</summary>
    internal int Created, Released, Commands, ReleasedCommands;
    /// <summary>正式 dedupe 不存在時立即精確失敗，不能由測試補記憶體後備。</summary>
    internal IWebhookEventDeduplicator Dedupe => Assert.IsAssignableFrom<IWebhookEventDeduplicator>(Provider.GetService<IWebhookEventDeduplicator>());
    /// <summary>只讀目前案例的去識別日誌，不印出 exception.Message。</summary>
    internal string LogPath { get; }

    /// <summary>把窄 factory 交可信 DI 組合根；組態只留合成字串並於註冊後釋放。</summary>
    internal WebhookSqlProbe()
    {
        LogPath = Path.Combine(WebhookSqlTestDatabase.Root, ".trellis", "tasks", "11-09-p3-04-sql-dedupe", "evidence", "probe", Guid.NewGuid().ToString("N"));
        diagnostics = new ExceptionDiagnostics(LogPath, outputOptions: new ExceptionOutputOptions(true, false));
        using var config = new ConfigurationManager(); config["ConnectionStrings:Platform"] = "Server=synthetic;Database=synthetic;Integrated Security=true";
        var services = new ServiceCollection(); services.AddSingleton(diagnostics); services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<Func<DbConnection>>(() => new Connection(this)); services.AddLineWebhook(config);
        Provider = services.BuildServiceProvider();
    }
    /// <summary>相同原例外不能再次被記錄，驗證安全入口保留真實身分。</summary>
    internal bool ReportAgain(Exception error) => diagnostics.Report(error, "WebhookSqlTests.Probe");
    /// <summary>斷言每個命令與連線恰好釋放及 CTS timer 全解除，不能用 GC 補漏。</summary>
    internal void AssertBaseline()
    { Assert.Equal(Created, Released); Assert.Equal(Commands, ReleasedCommands); Assert.Equal(0, Clock.TimerCount); }
    /// <summary>容器先排空後 diagnostics 才釋放，維持失敗診斷在清理期間可用。</summary>
    public async ValueTask DisposeAsync()
    { try { await Provider.DisposeAsync(); } finally { await diagnostics.DisposeAsync(); } }

    /// <summary>沒有真正網路資源的單操作連線，只追蹤產品是否正確交接及釋放 owner。</summary>
    private sealed class Connection : DbConnection
    {
        private readonly WebhookSqlProbe owner;
        private ConnectionState state;
        private int disposed;
        /// <summary>建立當下即計入 owner，Open 失敗也必須釋放。</summary>
        internal Connection(WebhookSqlProbe owner) { this.owner = owner; Interlocked.Increment(ref owner.Created); }
        /// <summary>抽象契約用合成字串，不存任何部署秘密。</summary>
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        /// <summary>固定合成識別，不連 SQL。</summary>
        public override string Database => "synthetic";
        /// <summary>固定合成來源，不查 DNS。</summary>
        public override string DataSource => "synthetic";
        /// <summary>不是實際 SQL 版本。</summary>
        public override string ServerVersion => "synthetic";
        /// <summary>Open callback 完成後才顯示 Open，禁止測試跳過開啟故障。</summary>
        public override ConnectionState State => state;
        /// <summary>不支援切換資料庫，避免擴大替身範圍。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>只關閉目前狀態，owner 仍須 Dispose。</summary>
        public override void Close() => state = ConnectionState.Closed;
        /// <summary>產品只能非同步開啟，否則測試立即拒絕。</summary>
        public override void Open() => throw new NotSupportedException();
        /// <summary>先執行受控 callback，故障時保留原例外及 Closed 狀態。</summary>
        public override async Task OpenAsync(CancellationToken cancellationToken)
        { await owner.Open(cancellationToken); state = ConnectionState.Open; }
        /// <summary>產品用可取消 T-SQL 交易命令；不允許轉用 DbTransaction 同步後備而繞過期限。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        /// <summary>每條命令歸操作局部 owner，成功或故障皆須釋放。</summary>
        protected override DbCommand CreateDbCommand() => new Command(this, owner);
        /// <summary>精確記錄一次釋放，重複釋放視為雙 owner 缺陷。</summary>
        protected override void Dispose(bool disposing)
        { if (disposing) { Assert.Equal(0, Interlocked.Exchange(ref disposed, 1)); Close(); Interlocked.Increment(ref owner.Released); } base.Dispose(disposing); }
    }

    /// <summary>命令參數使用實際 SqlParameter 集合，但不傳輸；只把執行交給目前案例 callback。</summary>
    private sealed class Command : DbCommand
    {
        private readonly WebhookSqlProbe owner;
        private readonly SqlCommand parameterOwner = new();
        private int disposed;
        /// <summary>建立時接收唯一 connection，不跨操作重用。</summary>
        internal Command(DbConnection connection, WebhookSqlProbe owner)
        { DbConnection = connection; this.owner = owner; Interlocked.Increment(ref owner.Commands); }
        /// <summary>保存目前固定 SQL 供 callback 辨認階段。</summary>
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        /// <summary>產品必須明確設五秒，測試在 callback 驗此設定。</summary>
        public override int CommandTimeout { get; set; }
        /// <summary>正式產品僅使用 Text，無 stored procedure 隱藏路徑。</summary>
        public override CommandType CommandType { get; set; }
        /// <summary>不使用設計工具行為。</summary>
        public override bool DesignTimeVisible { get; set; }
        /// <summary>不使用 DataAdapter 更新路徑。</summary>
        public override UpdateRowSource UpdatedRowSource { get; set; }
        /// <summary>僅目前局部連線參照，Dispose 後清空。</summary>
        protected override DbConnection? DbConnection { get; set; }
        /// <summary>只由命令自身擁有參數集合，不共用 mutable 參數。</summary>
        protected override DbParameterCollection DbParameterCollection => parameterOwner.Parameters;
        /// <summary>不使用同步 DbTransaction 後備。</summary>
        protected override DbTransaction? DbTransaction { get; set; }
        /// <summary>取消由 callback token 合作，沒有額外背景任務。</summary>
        public override void Cancel() { }
        /// <summary>同步 SQL 一律拒絕，以確保期限不被同步後備架空。</summary>
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        /// <summary>只允許非同步純量轉接。</summary>
        public override object ExecuteScalar() => throw new NotSupportedException();
        /// <summary>不允許未用到的 prepare 路徑。</summary>
        public override void Prepare() => throw new NotSupportedException();
        /// <summary>建立真實 SqlParameter 供型別、長度與毫秒精度斷言。</summary>
        protected override DbParameter CreateDbParameter() => new SqlParameter();
        /// <summary>不允許未用到的 reader 路徑。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        /// <summary>執行前驗固定五秒，仍把原 token 傳 callback 觀察 deadline。</summary>
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        { Assert.Equal(5, CommandTimeout); return owner.Execute(this, cancellationToken); }
        /// <summary>純量亦使用同一受控 callback，無額外擁有權。</summary>
        public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken) => await ExecuteNonQueryAsync(cancellationToken);
        /// <summary>先釋放參數 owner 再清空連線，重複 Dispose 直接測試失敗。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            { Assert.Equal(0, Interlocked.Exchange(ref disposed, 1)); parameterOwner.Dispose(); DbConnection = null; Interlocked.Increment(ref owner.ReleasedCommands); }
            base.Dispose(disposing);
            if (disposing && CommandText.StartsWith("INSERT", StringComparison.Ordinal) && owner.InsertCommandDisposeFailure is { } failure) throw failure;
            if (disposing && CommandText.StartsWith("COMMIT", StringComparison.Ordinal) && owner.CommitCommandDisposeFailure is { } commitFailure) throw commitFailure;
        }
    }
}

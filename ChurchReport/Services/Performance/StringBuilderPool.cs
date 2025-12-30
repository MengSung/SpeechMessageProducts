using System;
using Microsoft.Extensions.ObjectPool;
using System.Text;

namespace ChurchReport.Services.Performance
{
    /// <summary>
    /// StringBuilder 物件池策略
    /// Phase 5.4: 減少字串處理時的記憶體分配
    /// </summary>
    public class StringBuilderPooledObjectPolicy : PooledObjectPolicy<StringBuilder>
    {
        /// <summary>
        /// 預設初始容量
        /// </summary>
        public int InitialCapacity { get; set; } = 256;

        /// <summary>
        /// 最大保留容量（超過此容量的 StringBuilder 不會被回收）
        /// </summary>
        public int MaximumRetainedCapacity { get; set; } = 4096;

        /// <summary>
        /// 建立新的 StringBuilder
        /// </summary>
        public override StringBuilder Create()
        {
            return new StringBuilder(InitialCapacity);
        }

        /// <summary>
        /// 回收 StringBuilder
        /// </summary>
        public override bool Return(StringBuilder obj)
        {
            // 如果容量太大，不回收（避免佔用過多記憶體）
            if (obj.Capacity > MaximumRetainedCapacity)
            {
                return false;
            }

            obj.Clear();
            return true;
        }
    }

    /// <summary>
    /// StringBuilder 物件池服務
    /// </summary>
    public interface IStringBuilderPool
    {
        /// <summary>
        /// 從池中取得 StringBuilder
        /// </summary>
        StringBuilder Get();

        /// <summary>
        /// 將 StringBuilder 歸還池中
        /// </summary>
        void Return(StringBuilder builder);

        /// <summary>
        /// 使用 StringBuilder 執行操作並自動歸還
        /// </summary>
        string UseAndReturn(Action<StringBuilder> action);
    }

    /// <summary>
    /// StringBuilder 物件池實作
    /// </summary>
    public class StringBuilderPool : IStringBuilderPool
    {
        private readonly ObjectPool<StringBuilder> _pool;

        public StringBuilderPool()
        {
            var policy = new StringBuilderPooledObjectPolicy
            {
                InitialCapacity = 256,
                MaximumRetainedCapacity = 4096
            };
            _pool = new DefaultObjectPool<StringBuilder>(policy, 50);
        }

        public StringBuilder Get() => _pool.Get();

        public void Return(StringBuilder builder) => _pool.Return(builder);

        public string UseAndReturn(Action<StringBuilder> action)
        {
            var sb = Get();
            try
            {
                action(sb);
                return sb.ToString();
            }
            finally
            {
                Return(sb);
            }
        }
    }
}

// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Services/Performance/StringBuilderPool.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案位於服務或工具層，註解重點在說明共用責任、外部依賴、錯誤傳遞與呼叫端應遵守的前置條件。
// 主要型別：class StringBuilderPooledObjectPolicy、interface IStringBuilderPool、class StringBuilderPool
// 主要成員：Create、Return、Get、UseAndReturn、InitialCapacity、MaximumRetainedCapacity
// 引用命名空間：System、Microsoft.Extensions.ObjectPool、System.Text
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
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

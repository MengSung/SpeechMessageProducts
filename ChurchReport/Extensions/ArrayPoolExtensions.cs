using System;
using System.Buffers;
using System.Collections.Generic;

namespace ChurchReport.Extensions
{
    /// <summary>
    /// ArrayPool 擴充方法
    /// Phase 5.3: 減少 LINQ 和陣列操作的記憶體分配
    /// </summary>
    public static class ArrayPoolExtensions
    {
        /// <summary>
        /// 使用 ArrayPool 執行操作，自動處理租借和歸還
        /// </summary>
        /// <typeparam name="T">陣列元素類型</typeparam>
        /// <param name="minimumLength">最小長度</param>
        /// <param name="action">要執行的操作</param>
        public static void UseRentedArray<T>(int minimumLength, Action<T[]> action)
        {
            var pool = ArrayPool<T>.Shared;
            var array = pool.Rent(minimumLength);
            try
            {
                action(array);
            }
            finally
            {
                pool.Return(array, clearArray: true);
            }
        }

        /// <summary>
        /// 使用 ArrayPool 執行操作並返回結果
        /// </summary>
        /// <typeparam name="T">陣列元素類型</typeparam>
        /// <typeparam name="TResult">返回結果類型</typeparam>
        /// <param name="minimumLength">最小長度</param>
        /// <param name="func">要執行的操作</param>
        /// <returns>操作結果</returns>
        public static TResult UseRentedArray<T, TResult>(int minimumLength, Func<T[], TResult> func)
        {
            var pool = ArrayPool<T>.Shared;
            var array = pool.Rent(minimumLength);
            try
            {
                return func(array);
            }
            finally
            {
                pool.Return(array, clearArray: true);
            }
        }

        /// <summary>
        /// 將 IEnumerable 轉換為 List，預先配置容量以減少重新分配
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="estimatedCount">預估數量</param>
        /// <returns>List</returns>
        public static List<T> ToListWithCapacity<T>(this IEnumerable<T> source, int estimatedCount)
        {
            var list = new List<T>(estimatedCount);
            list.AddRange(source);
            return list;
        }

        /// <summary>
        /// 篩選集合，預先配置容量
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="predicate">篩選條件</param>
        /// <param name="estimatedResultCount">預估結果數量</param>
        /// <returns>篩選後的 List</returns>
        public static List<T> WhereToList<T>(
            this IList<T> source,
            Func<T, bool> predicate,
            int? estimatedResultCount = null)
        {
            var capacity = estimatedResultCount ?? source.Count;
            var result = new List<T>(capacity);

            for (int i = 0; i < source.Count; i++)
            {
                if (predicate(source[i]))
                {
                    result.Add(source[i]);
                }
            }

            return result;
        }

        /// <summary>
        /// 選擇並轉換集合元素，預先配置容量
        /// </summary>
        /// <typeparam name="TSource">來源元素類型</typeparam>
        /// <typeparam name="TResult">結果元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="selector">轉換函數</param>
        /// <returns>轉換後的 List</returns>
        public static List<TResult> SelectToList<TSource, TResult>(
            this IList<TSource> source,
            Func<TSource, TResult> selector)
        {
            var result = new List<TResult>(source.Count);

            for (int i = 0; i < source.Count; i++)
            {
                result.Add(selector(source[i]));
            }

            return result;
        }
    }
}

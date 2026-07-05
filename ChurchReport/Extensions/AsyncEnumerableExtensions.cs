// ============================================================================
// AI-繁體中文檔案註解
// 檔案路徑：ChurchReport/Extensions/AsyncEnumerableExtensions.cs
// 所屬區塊：ChurchReport 主網站與後台應用程式，承載控制器、模型、CRM 整合、付款流程、LINE 通知與產品層商業規則。
// 檔案責任：此檔案提供 AsyncEnumerableExtensions 相關功能，註解重點在說明檔案責任、上游/下游依賴與維護時不可破壞的行為假設。
// 主要型別：class AsyncEnumerableExtensions
// 主要成員：未偵測到公開/受保護成員；維護時請以檔案內的常數、欄位、private helper 或屬性初始化邏輯為主要閱讀入口。
// 引用命名空間：System、System.Collections.Generic、System.Runtime.CompilerServices、System.Threading、System.Threading.Tasks
// 閱讀路徑：閱讀此檔案時應先從公開型別、建構式注入、主要方法與例外處理路徑掌握資料流，再進行維護。
// 維護重點：後續修改時應先理解既有呼叫端與外部系統契約，避免把註解整理誤變成行為重構。
// 行為保護：本註解僅補充設計意圖與維護脈絡，不應改變任何執行流程、資料格式、序列化結果或外部 API 契約。
// 編碼要求：本檔案需維持 UTF-8 without BOM 與 CRLF，以符合專案 .editorconfig 與 Windows/Visual Studio 工作流。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ChurchReport.Extensions
{
    /// <summary>
    /// IAsyncEnumerable 擴充方法
    /// Phase 6.3: 支援大量資料串流處理，減少記憶體使用
    /// </summary>
    public static class AsyncEnumerableExtensions
    {
        /// <summary>
        /// 將 IEnumerable 轉換為 IAsyncEnumerable
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>非同步可列舉</returns>
        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
            this IEnumerable<T> source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var item in source)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                // 讓出執行緒，避免長時間阻塞
                await Task.Yield();
            }
        }

        /// <summary>
        /// 將 IAsyncEnumerable 轉換為 List
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>List</returns>
        public static async Task<List<T>> ToListAsync<T>(
            this IAsyncEnumerable<T> source,
            CancellationToken cancellationToken = default)
        {
            var list = new List<T>();
            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                list.Add(item);
            }
            return list;
        }

        /// <summary>
        /// 篩選非同步可列舉
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="predicate">篩選條件</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>篩選後的非同步可列舉</returns>
        public static async IAsyncEnumerable<T> WhereAsync<T>(
            this IAsyncEnumerable<T> source,
            Func<T, bool> predicate,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                if (predicate(item))
                {
                    yield return item;
                }
            }
        }

        /// <summary>
        /// 轉換非同步可列舉元素
        /// </summary>
        /// <typeparam name="TSource">來源元素類型</typeparam>
        /// <typeparam name="TResult">結果元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="selector">轉換函數</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>轉換後的非同步可列舉</returns>
        public static async IAsyncEnumerable<TResult> SelectAsync<TSource, TResult>(
            this IAsyncEnumerable<TSource> source,
            Func<TSource, TResult> selector,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                yield return selector(item);
            }
        }

        /// <summary>
        /// 分批處理非同步可列舉
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="batchSize">批次大小</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>批次的非同步可列舉</returns>
        public static async IAsyncEnumerable<List<T>> BatchAsync<T>(
            this IAsyncEnumerable<T> source,
            int batchSize,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var batch = new List<T>(batchSize);

            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                batch.Add(item);

                if (batch.Count >= batchSize)
                {
                    yield return batch;
                    batch = new List<T>(batchSize);
                }
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        /// <summary>
        /// 取得前 N 個元素
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="count">數量</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>前 N 個元素的非同步可列舉</returns>
        public static async IAsyncEnumerable<T> TakeAsync<T>(
            this IAsyncEnumerable<T> source,
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var taken = 0;
            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                if (taken >= count)
                    yield break;

                yield return item;
                taken++;
            }
        }

        /// <summary>
        /// 跳過前 N 個元素
        /// </summary>
        /// <typeparam name="T">元素類型</typeparam>
        /// <param name="source">來源集合</param>
        /// <param name="count">數量</param>
        /// <param name="cancellationToken">取消權杖</param>
        /// <returns>跳過後的非同步可列舉</returns>
        public static async IAsyncEnumerable<T> SkipAsync<T>(
            this IAsyncEnumerable<T> source,
            int count,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var skipped = 0;
            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                if (skipped < count)
                {
                    skipped++;
                    continue;
                }

                yield return item;
            }
        }
    }
}

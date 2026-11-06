/*
檔案整體說明

目的與定位：為目前頁面的集合載入管理單一有效請求與generation，取消過期transport並丟棄晚到回應；dispose清除callback與DOM參照，避免舊日期資料覆寫新畫面。

主要功能與流程：
操作入口包含createStaleGenerationError、abortTransport、createCollectionLoadCoordinator、flushPendingRefresh、completeIfCurrent、rejectBeforeLoad、settleCurrent、resolveCurrent、rejectCurrent、mount；呼叫參數與輸出形狀以各入口宣告為準。

使用邊界與維護：修改前先核對引用此檔的專案、頁面、腳本或CI步驟，保留既有參數、輸出及失敗結束碼。
涉及寫入、外部命令或暫存資源時，依本檔實際實作的owner及清理路徑處理；說明不能取代執行前的範圍驗證。
*/
// ============================================================================
// 檔案路徑：SpeechMessageProducts.ChurchReport/wwwroot/js/collection-load-coordinator.js
// 檔案責任：協調單一 Grid／表格元件的非同步載入世代，阻止慢網路、代理重送、重複 refresh
//           或回應亂序讓舊 callback 覆蓋最新畫面；同時將 refresh 意圖限制為最多一個。
// 身份邊界：本檔案不判斷資料列是否重複，也不保存姓名、PresentRecordId、Session、token、
//           credential 或 response data；資料列唯一性仍由伺服器端資料庫 ID guard 負責。
// 資源生命週期：每個 coordinator 只有一個 owner、一個 active transport 與一個 pending callback。
//               duplicate mount 先 dispose 舊 owner；dispose 使 generation 失效、abort transport、
//               清除 callback 與元件 closure。WeakMap 不會強引用已移除的 DOM owner。
// 編碼要求：本檔案必須以 UTF-8 without BOM、CRLF、final CRLF 儲存。
// ============================================================================
(function exposeCollectionLoadCoordinator(root, factory) {
    'use strict';

    var api = factory();
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
    } else {
        root.CollectionLoadCoordinator = api;
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function createApi() {
    'use strict';

    // WeakMap 的 key 是 DOM owner 或測試 owner；它只提供「同一元件最多一個 coordinator」語意，
    // 不保存使用者資料。owner 不再被頁面參考時，瀏覽器可一併回收 registry entry。
    var ownerRegistry = typeof WeakMap === 'function' ? new WeakMap() : null;

    /**
     * 建立代表過期世代的內部錯誤。錯誤不含 response、URL、Session 或個資，避免診斷物件
     * 延長保存大型回應；adapter 可用 isStaleGeneration 判斷這不是目前世代的服務錯誤。
     */
    function createStaleGenerationError() {
        var error = new Error('資料載入世代已過期，拒絕發布舊回應。');
        error.name = 'StaleGenerationError';
        error.isStaleGeneration = true;
        error.canceled = true;
        return error;
    }

    /**
     * 安全呼叫 transport.abort。取消只用於縮短網路資源生命週期；即使 abort 不存在、失敗或
     * 回應仍晚到，runLoad 的 generation 檢查仍會拒絕舊 callback，因此不把取消當正確性邊界。
     */
    function abortTransport(transport) {
        if (!transport || typeof transport.abort !== 'function') {
            return;
        }

        try {
            transport.abort();
        } catch (error) {
            // 某些第三方 transport 在完成後 abort 會擲例外。資源已不可再控制時只清除本地參考；
            // generation 已先失效，所以忽略 abort 例外不會讓舊資料取得發布權。
        }
    }

    /**
     * 建立只屬於一個 UI component instance 的載入協調器。
     * @param {Function=} disposeOwnedComponent 釋放此 coordinator 所擁有之 Grid 的函式；不得捕獲 Session 資料。
     * @returns {Object} 具有 runLoad、requestRefresh、dispose 與有界診斷計數的 owner。
     */
    function createCollectionLoadCoordinator(disposeOwnedComponent) {
        var generation = 0;
        var activeRequest = null;
        var pendingRefresh = null;
        var refreshScheduled = false;
        var disposed = false;
        var ownedComponentDisposer = typeof disposeOwnedComponent === 'function'
            ? disposeOwnedComponent
            : null;

        /**
         * 在目前 microtask 執行唯一 pending refresh。callback 執行前先從 owner 清除參考，
         * 即使 refresh 再同步要求 refresh，也只會建立下一個有界意圖，不會遞迴累積 queue。
         */
        function flushPendingRefresh() {
            refreshScheduled = false;
            if (disposed || activeRequest || !pendingRefresh) {
                return;
            }

            var refresh = pendingRefresh;
            pendingRefresh = null;
            refresh();
        }

        /**
         * 當目前世代完成時釋放 transport 參考，並排程最多一次 refresh。
         * 舊世代完成不得清掉新世代 transport，也不得觸發屬於新世代的 pending callback。
         */
        function completeIfCurrent(requestGeneration) {
            if (!activeRequest || activeRequest.generation !== requestGeneration) {
                return;
            }

            activeRequest = null;
            if (!disposed && pendingRefresh && !refreshScheduled) {
                refreshScheduled = true;
                Promise.resolve().then(flushPendingRefresh);
            }
        }

        return {
            /**
             * 包裝既有 DevExtreme store.load 回傳的 thenable；本方法不建立第二條 HTTP 管線。
             * 每次呼叫先使前一世代失效並嘗試 abort，只有最新世代的 success/error 可完成結果。
             * Grid 可注入既有 jQuery Deferred factory，保留 done/fail 與 data、totalCount 等多個
             * 完成參數；未注入時仍回傳原生 Promise，供日期切換使用。不得用 Promise.resolve
             * 同化 Deferred，否則第二個 metadata 參數會遺失。所有參考只存活於本次載入。
             * @param {Function} loadFactory 既有 transport 的建立函式。
             * @param {Function=} deferredFactory Grid 使用的 Deferred factory，不存入 coordinator。
             */
            runLoad: function runLoad(loadFactory, deferredFactory) {
                var deferred = typeof deferredFactory === 'function' ? deferredFactory() : null;
                var resolveNative;
                var rejectNative;
                var result = deferred ? deferred.promise() : new Promise(function captureCompletion(resolve, reject) {
                    resolveNative = resolve;
                    rejectNative = reject;
                });

                /**
                 * 同步驗證失敗也使用呼叫端選定的回傳型別，不把 Deferred 呼叫意外轉成原生 Promise。
                 */
                function rejectBeforeLoad(error) {
                    if (deferred) {
                        deferred.reject(error);
                    } else {
                        rejectNative(error);
                    }
                    return result;
                }

                if (disposed) {
                    return rejectBeforeLoad(createStaleGenerationError());
                }
                if (typeof loadFactory !== 'function') {
                    return rejectBeforeLoad(new TypeError('loadFactory 必須是函式。'));
                }

                generation += 1;
                if (activeRequest) {
                    var previousTransport = activeRequest.transport;
                    activeRequest = null;
                    abortTransport(previousTransport);
                }

                var requestGeneration = generation;
                var transport;
                try {
                    transport = loadFactory();
                } catch (error) {
                    return rejectBeforeLoad(error);
                }

                activeRequest = {
                    generation: requestGeneration,
                    transport: transport
                };

                /**
                 * 在呼叫消費者前先歸還 active transport；jQuery 2.x callback 可同步擲出例外，
                 * 因此不能把清理排在 resolve/reject 後面。context 與 arguments 不寫入共用欄位。
                 */
                function settleCurrent(succeeded, context, args) {
                    var stale = disposed || requestGeneration !== generation;
                    completeIfCurrent(requestGeneration);
                    if (stale) {
                        rejectBeforeLoad(createStaleGenerationError());
                    } else if (deferred) {
                        deferred[succeeded ? 'resolveWith' : 'rejectWith'](context, args);
                    } else if (succeeded) {
                        resolveNative(args[0]);
                    } else {
                        rejectNative(args[0]);
                    }
                }

                function resolveCurrent() {
                    settleCurrent(true, this, arguments);
                }

                function rejectCurrent() {
                    settleCurrent(false, this, arguments);
                }

                if (deferred && transport && typeof transport.done === 'function' &&
                    typeof transport.fail === 'function') {
                    // 直接觀察原 Deferred 才能保留多參數；回傳的是新的受世代保護結果，不能直接
                    // 返回原 transport，否則晚到資料會繞過 generation 驗證。
                    transport.done(resolveCurrent).fail(rejectCurrent);
                } else {
                    Promise.resolve(transport).then(resolveCurrent, rejectCurrent);
                }
                return result;
            },

            /**
             * 要求元件 refresh。active load 期間反覆呼叫只覆蓋同一個 callback 欄位；沒有 active
             * load 時也只排一個 microtask，避免慢網路或多個 UI callback 建立無界 timer queue。
             */
            requestRefresh: function requestRefresh(refreshAction) {
                if (disposed || typeof refreshAction !== 'function') {
                    return;
                }

                pendingRefresh = refreshAction;
                if (!activeRequest && !refreshScheduled) {
                    refreshScheduled = true;
                    Promise.resolve().then(flushPendingRefresh);
                }
            },

            /**
             * 釋放此 owner。先標記 disposed 與遞增 generation，使任何同步／晚到 callback 失效；
             * 再 abort transport、清除 pending callback，最後釋放元件。重複 dispose 是安全 no-op。
             */
            dispose: function dispose() {
                if (disposed) {
                    return;
                }

                disposed = true;
                generation += 1;
                if (activeRequest) {
                    abortTransport(activeRequest.transport);
                    activeRequest = null;
                }
                pendingRefresh = null;
                refreshScheduled = false;

                var disposer = ownedComponentDisposer;
                ownedComponentDisposer = null;
                if (disposer) {
                    disposer();
                }
            },

            /**
             * 回傳純量診斷值供測試／監控確認 drain；不回傳 transport、callback 或 response 參考。
             */
            getDiagnostics: function getDiagnostics() {
                return {
                    disposed: disposed,
                    activeRequestCount: activeRequest ? 1 : 0,
                    pendingRefreshCount: pendingRefresh ? 1 : 0
                };
            }
        };
    }

    /**
     * 為同一 DOM owner 掛載唯一 coordinator；若已存在舊世代，先確定 dispose 再發布新 owner。
     */
    function mount(owner, disposeOwnedComponent) {
        if (!owner || (typeof owner !== 'object' && typeof owner !== 'function')) {
            throw new TypeError('CollectionLoadCoordinator owner 必須是物件。');
        }
        if (!ownerRegistry) {
            throw new Error('目前瀏覽器不支援 WeakMap，拒絕建立可能無界保留 DOM 的 owner registry。');
        }

        var previous = ownerRegistry.get(owner);
        if (previous) {
            previous.dispose();
        }

        var coordinator = createCollectionLoadCoordinator(disposeOwnedComponent);
        ownerRegistry.set(owner, coordinator);
        return coordinator;
    }

    /**
     * 釋放 owner 目前登記的 coordinator，並立即刪除 WeakMap entry，避免保留 transport closure。
     */
    function disposeOwner(owner) {
        if (!ownerRegistry || !owner) {
            return;
        }

        var coordinator = ownerRegistry.get(owner);
        if (coordinator) {
            ownerRegistry.delete(owner);
            coordinator.dispose();
        }
    }

    return {
        createCollectionLoadCoordinator: createCollectionLoadCoordinator,
        mount: mount,
        disposeOwner: disposeOwner
    };
}));

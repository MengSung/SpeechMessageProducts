/*
 * 檔案整體說明
 * 目的與定位：本檔提供 GoodShepherd2 共通層 F6 的 LIFF 純路由及首頁入口，
 * UMD 匯出供 node:test 直接驗證，瀏覽器只在 router 頁面自動啟動。
 * 主要功能與流程：首頁以固定同源 config 取得部署 LIFF ID，等待 SDK init 成功，
 * 才讀 liff.state 查詢參數並以精確鍵派送本機白名單；純 bootstrapRouter 只呼叫
 * onRoute，真正導頁由首頁提供的 callback 負責。內建 whoami 不可覆寫，F12 可
 * 登錄 enroll 等固定 /liff/ 單層 HTML 頁面，拒絕原型鍵及任何需正規化的路徑。
 * 信任與隔離邊界：設定與 state 都不是授權，沒有 ID 顯示尚未設定，不產生替代值。
 * 本檔不讀取身分或 token、不持有使用者快取；頁面模組共用的 helper 不保存每頁
 * 狀態。GET 禁止 cookie、快取、轉址與 referrer，身分頁自行提供單次 Bearer。
 * 資源與失敗：每次頁面啟動唯一 scope 擁有 AbortController、離頁 listener 及
 * 最多一個進行中操作的十秒 timer。操作完成清 timer／abort listener，pagehide
 * 取消請求並執行清理；SDK init 沒有取消 API，因此只停止等待且禁止後續讀取或
 * 導頁，SDK 自身網路由官方 SDK 管理。任何失敗固定訊息，不顯示例外或設定內容。
 * 維護與驗證：node --test 執行產品 js 契約測試；必須檢查 init 前零參數讀寫、
 * 白名單拒絕、離頁排空及 UTF-8 無 BOM、CRLF、末尾 CRLF。離線測試不代表手機驗收。
 */
(function (root, factory) {
    'use strict';
    if (typeof module === 'object' && module.exports) {
        module.exports = factory();
    } else {
        root.LiffRouter = factory();
        if (root.document && root.document.body.dataset.liffPage === 'router') {
            void root.LiffRouter.startRouterPage(root);
        }
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';
    const WHOAMI = '/liff/whoami.html';
    const OWN = Object.prototype.hasOwnProperty;
    const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

    /** GUID 僅驗證本機輸入格式，不能代表 actor、教會選擇或授權。空 GUID 無效。 */
    function isGuid(value) {
        return typeof value === 'string' && GUID.test(value) &&
            value !== '00000000-0000-0000-0000-000000000000';
    }

    /**
     * 等待唯一必要 SDK 成員 init 成功後才呼叫 readState；不讀任何瀏覽器全域。
     * state 不解碼、不裁切、不補正，目標必須是已登錄的單層本機 HTML 路徑。
     * callback 及 SDK 的失敗原樣拒絕，呼叫端不得以預設路由掩蓋 init 失敗。
     */
    async function bootstrapRouter(liff, { liffId, readState, routes = {}, onRoute }) {
        if (typeof liffId !== 'string' || !liffId.trim() || liffId !== liffId.trim() ||
            liffId.length > 128 || typeof readState !== 'function' || typeof onRoute !== 'function') {
            throw new Error('Invalid router configuration');
        }
        await liff.init({ liffId });
        const state = readState();
        const key = state === null || state === undefined ? 'whoami' : state;
        if (typeof key !== 'string' || !/^[a-z][a-z0-9-]{0,31}$/.test(key) ||
            key === 'constructor' || key === 'prototype') {
            throw new Error('Unknown route');
        }
        let target;
        if (key === 'whoami') {
            if (OWN.call(routes, key) && routes[key] !== WHOAMI) throw new Error('Invalid route');
            target = WHOAMI;
        } else if (OWN.call(routes, key)) {
            target = routes[key];
        }
        if (typeof target !== 'string' || !/^\/liff\/[a-z][a-z0-9-]{0,63}\.html$/.test(target)) {
            throw new Error('Invalid route');
        }
        await onRoute(target);
    }

    /**
     * 建立每頁獨立且冪等的資源 owner。清理只存 callback、不存身分或請求 headers；
     * pagehide 先中止所有等待，再移除 DOM listener 與清除頁面資料，禁止離頁後發布。
     */
    function createPageScope(win) {
        const controller = new win.AbortController();
        const cleanups = [];
        let closed = false;
        const scope = {
            signal: controller.signal,
            get closed() { return closed; },
            assertActive() {
                if (closed || controller.signal.aborted) throw new Error('Page operation stopped');
            },
            addCleanup(cleanup) {
                if (closed) cleanup(); else cleanups.push(cleanup);
            },
            listen(target, name, callback) {
                target.addEventListener(name, callback);
                scope.addCleanup(() => target.removeEventListener(name, callback));
            },
            dispose() {
                if (closed) return;
                closed = true;
                controller.abort();
                for (const cleanup of cleanups.splice(0)) cleanup();
            },
            /** 操作最多十秒；finally 等價的 settle 清除 timer 與取消註冊，不建立重試。 */
            waitFor(work) {
                return new Promise((resolve, reject) => {
                    let settled = false;
                    let timer;
                    const settle = (callback, value) => {
                        if (settled) return;
                        settled = true;
                        if (timer !== undefined) win.clearTimeout(timer);
                        controller.signal.removeEventListener('abort', abort);
                        callback(value);
                    };
                    const abort = () => settle(reject, new Error('Page operation stopped'));
                    Promise.resolve(work).then(value => settle(resolve, value), error => settle(reject, error));
                    if (controller.signal.aborted || closed) { abort(); return; }
                    controller.signal.addEventListener('abort', abort, { once: true });
                    timer = win.setTimeout(() => controller.abort(), 10000);
                });
            }
        };
        scope.listen(win, 'pagehide', () => scope.dispose());
        return scope;
    }

    /**
     * 固定同源 GET helper：同一 scope.signal 同時取消 fetch 與讀取回應，十秒包含
     * JSON 消費。失敗回應取消 body，禁止自動跟隨轉址將 Bearer 帶往其他位置。
     * 呼叫端只可傳固定產品路徑；本 helper 不保存 headers 或回應到模組狀態。
     */
    async function requestJson(win, scope, url, headers = {}) {
        scope.assertActive();
        return scope.waitFor((async () => {
            const response = await win.fetch(url, { method: 'GET', headers,
                cache: 'no-store', credentials: 'omit', redirect: 'error',
                referrerPolicy: 'no-referrer', signal: scope.signal });
            scope.assertActive();
            if (!response.ok) {
                if (response.body) await response.body.cancel();
                return { ok: false, status: response.status };
            }
            const data = await response.json();
            scope.assertActive();
            return { ok: true, status: response.status, data };
        })());
    }

    /** 只接受部署回應的真實 ID；空白／缺少固定回傳 null，其他設定故障拒絕。 */
    async function loadConfig(win, scope) {
        const response = await requestJson(win, scope, '/api/liff/config');
        if (!response.ok || !response.data || typeof response.data !== 'object') {
            throw new Error('Configuration unavailable');
        }
        const config = response.data;
        if (config.liffId === undefined || config.liffId === null ||
            (typeof config.liffId === 'string' && !config.liffId.trim())) return null;
        if (typeof config.liffId !== 'string' || config.liffId !== config.liffId.trim() ||
            !config.liffId.trim() || config.liffId.length > 128) throw new Error('Invalid configuration');
        return { liffId: config.liffId, isDevelopment: config.isDevelopment === true };
    }

    /**
     * 首頁唯一導頁入口：config 不讀 location；bootstrap 內的 init 經 scope 有界等待。
     * 只在 init 成功後讀取一次查詢，多重 state 拒絕，缺省才用 whoami。
     * 真正 replace 前再次檢查離頁；錯誤固定顯示且不嘗試任何備援導頁。
     */
    async function startRouterPage(win) {
        const scope = createPageScope(win);
        const status = win.document.getElementById('page-status');
        try {
            const config = await loadConfig(win, scope);
            scope.assertActive();
            if (!config) { status.textContent = '尚未設定'; scope.dispose(); return scope; }
            await bootstrapRouter({ init: options => scope.waitFor(win.liff.init(options)) }, {
                liffId: config.liffId,
                readState() {
                    scope.assertActive();
                    const values = new win.URLSearchParams(win.location.search).getAll('liff.state');
                    if (values.length > 1) throw new Error('Ambiguous route');
                    return values.length === 0 ? null : values[0];
                },
                onRoute(target) {
                    scope.assertActive();
                    win.location.replace(target);
                }
            });
        } catch (_error) {
            if (!scope.closed) status.textContent = '無法開啟頁面，請稍後再試。';
        }
        scope.dispose();
        return scope;
    }

    return Object.freeze({ bootstrapRouter, startRouterPage, createPageScope, requestJson, loadConfig, isGuid });
}));

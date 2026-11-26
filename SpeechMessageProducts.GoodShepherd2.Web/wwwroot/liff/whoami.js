/*
 * 檔案整體說明
 * 目的與定位：本檔是 GoodShepherd2 共通層 F6 的 LIFF 身分頁控制器，以
 * liff-router 共用頁面資源 owner，依伺服器確認的教會與角色顯示純文字結果。
 * 主要功能與流程：先取同源部署 config，再 await init 成功，才在單次請求函式
 * 取得 ID Token，以 Bearer 呼叫固定 whoami；完整驗證回應形狀後一次發布。
 * 401 顯示重新登入按鈕且 login 不帶 redirect；403 只提示請選擇教會，本包
 * 不實作選擇 UI。開發設定必須明確 true 且 probe 是唯一合法 GUID 才有測試按鈕。
 * 信任與隔離：probe 只組合 ping 目標，不選 actor、不授權、不改教會快照。
 * 身分依伺服器回應，不解碼 token 或信任使用者參數；只寫 textContent，不顯示
 * token／userId、不記 console、不寫 storage、Session 或 cookie。token 僅保留
 * 在單次 authenticatedRequest 的區域變數及 fetch headers，完成即失去本檔參照。
 * 資源與失敗：每頁 scope 擁有有界十秒操作、listener 與 AbortController，probe
 * 點擊互斥以限制並行工作，離頁中止請求、移除 listener 並清除可見身分。SDK init
 * 無取消 API，離頁僅停止等待及後續頁面動作。例外以固定訊息呈現，不回顯機密。
 * 維護與驗證：node --test 以合成 SDK／DOM／fetch 驗證次序、錯誤、A/B 隔離、
 * 取消與清理；不代表 LINE／手機實測。UTF-8 無 BOM、CRLF、末尾 CRLF 必須檢查。
 */
(function (root, factory) {
    'use strict';
    if (typeof module === 'object' && module.exports) {
        module.exports = factory(require('./liff-router.js'));
    } else {
        root.LiffWhoami = factory(root.LiffRouter);
        if (root.document && root.document.body.dataset.liffPage === 'whoami') {
            void root.LiffWhoami.startWhoamiPage(root);
        }
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function (router) {
    'use strict';

    /**
     * token 的唯一應用程式 owner 是本次函式；每次 probe 重新向 SDK 取得，不將
     * token 捕捉在 DOM listener 或共享狀態。空 token 等價 401，不發 HTTP。
     */
    async function authenticatedRequest(win, scope, url) {
        scope.assertActive();
        const token = win.liff.getIDToken();
        if (typeof token !== 'string' || token.length === 0) return { ok: false, status: 401 };
        return await router.requestJson(win, scope, url, { Authorization: `Bearer ${token}` });
    }

    /**
     * 在 detached candidate 驗證完整回應；清單只作顯示，不推斷權限。固定大小限制
     * 防止非預期回應無界保留；沒有任何 userId 或額外欄位會被複製到頁面狀態。
     */
    function identityCandidate(data) {
        if (!data || !router.isGuid(data.tenantId) || typeof data.tenantName !== 'string' ||
            data.tenantName.length > 256 || !Array.isArray(data.roles) || data.roles.length > 100 ||
            data.roles.some(role => typeof role !== 'string' || role.length > 128)) {
            throw new Error('Invalid identity response');
        }
        return { tenantId: data.tenantId, tenantName: data.tenantName, roles: data.roles.join('、') };
    }

    /**
     * 每次建立獨立頁面，所有可變狀態只活到 pagehide。先完整載入 whoami 才安裝
     * probe 行為，未啟用的 probe 按鈕沒有 listener，不能藉點擊選擇 actor 或越過環境開關。
     */
    async function startWhoamiPage(win) {
        const scope = router.createPageScope(win);
        const element = id => win.document.getElementById(id);
        const status = element('page-status');
        const identity = element('identity');
        const login = element('login-button');
        const probe = element('probe-button');
        const result = element('probe-result');

        /** 清空所有可見身分資料與控制項；唯一 owner 在離頁時確定呼叫。 */
        function clearIdentity() {
            identity.hidden = true;
            for (const id of ['tenant-id', 'tenant-name', 'roles']) element(id).textContent = '';
            login.hidden = true;
            probe.hidden = true;
            result.textContent = '';
        }
        clearIdentity();
        scope.addCleanup(clearIdentity);

        /** 預期 401／403 與功能失敗分別呈現固定訊息，不顯示伺服器錯誤本文。 */
        function showFailure(code) {
            clearIdentity();
            status.textContent = code === 401 ? '請重新登入' :
                code === 403 ? '請選擇教會' : '無法取得教會資料，請稍後再試。';
            login.hidden = code !== 401;
        }

        /** 使用者主動重新登入，不接受 redirect 或其他查詢參數控制登入流程。 */
        scope.listen(login, 'click', async () => {
            if (login.hidden || login.disabled || scope.closed) return;
            login.disabled = true;
            try {
                scope.assertActive();
                await scope.waitFor(Promise.resolve(win.liff.login()));
            } catch (_error) {
                if (!scope.closed) status.textContent = '無法重新登入，請稍後再試。';
            } finally {
                if (!scope.closed) login.disabled = false;
            }
        });

        try {
            const config = await router.loadConfig(win, scope);
            scope.assertActive();
            if (!config) { status.textContent = '尚未設定'; scope.dispose(); return scope; }
            await scope.waitFor(win.liff.init({ liffId: config.liffId }));
            scope.assertActive();
            const response = await authenticatedRequest(win, scope, '/api/liff/whoami');
            scope.assertActive();
            if (!response.ok) { showFailure(response.status); return scope; }
            const candidate = identityCandidate(response.data);
            element('tenant-id').textContent = candidate.tenantId;
            element('tenant-name').textContent = candidate.tenantName;
            element('roles').textContent = candidate.roles || '尚無角色';
            identity.hidden = false;
            status.textContent = '已確認您的教會';

            if (config.isDevelopment) {
                const values = new win.URLSearchParams(win.location.search).getAll('probe');
                if (values.length === 1 && router.isGuid(values[0])) {
                    const target = `/api/liff/tenant/${values[0]}/ping`;
                    probe.hidden = false;
                    /** 單一進行中的 probe，離頁取消後不得再發布結果或重啟請求。 */
                    scope.listen(probe, 'click', async () => {
                        if (probe.hidden || probe.disabled || scope.closed) return;
                        probe.disabled = true;
                        result.textContent = '測試中…';
                        try {
                            const ping = await authenticatedRequest(win, scope, target);
                            scope.assertActive();
                            if (ping.status === 401) { showFailure(401); return; }
                            result.textContent = ping.ok ? '測試完成：允許存取。' :
                                ping.status === 403 ? '測試完成：已拒絕存取。' : '無法完成測試，請稍後再試。';
                        } catch (_error) {
                            if (!scope.closed) result.textContent = '無法完成測試，請稍後再試。';
                        } finally {
                            if (!scope.closed) probe.disabled = false;
                        }
                    });
                }
            }
        } catch (_error) {
            if (!scope.closed) showFailure(500);
        }
        return scope;
    }

    return Object.freeze({ startWhoamiPage });
}));

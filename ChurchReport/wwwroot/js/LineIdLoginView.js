// ========================================
// ✅ P0: 電腦版 LINE 登入方案（Server-side OAuth 2.0）
// ========================================
// 策略：偵測環境，根據環境選擇登入方式
// 1. 手機版 LINE / 外部瀏覽器 → 使用原有的 LIFF SDK
// 2. 電腦版 LINE 內建瀏覽器 → 使用 Server-side LINE Login OAuth 2.0

// Scripts moved from LineIdLoginView.cshtml:
// <script src="~/lib/jquery/dist/jquery.js"></script>
// <script src="~/lib/bootstrap/dist/js/bootstrap.js"></script>
// <!-- DevExtreme Globalization -->
// <script src="~/js/devextreme/cldr.js"></script>
// <script src="~/js/devextreme/cldr/event.js"></script>
// <script src="~/js/devextreme/cldr/supplemental.js"></script>
// <script src="~/js/devextreme/cldr/unresolved.js"></script>
// <script src="~/js/devextreme/globalize.js"></script>
// <script src="~/js/devextreme/globalize/message.js"></script>
// <script src="~/js/devextreme/globalize/number.js"></script>
// <script src="~/js/devextreme/globalize/currency.js"></script>
// <script src="~/js/devextreme/globalize/date.js"></script>
// <!-- DevExtreme Core -->
// <script src="~/js/devextreme/dx.all.js"></script>
// <script src="~/js/devextreme/vectormap-data/world.js"></script>
// <script src="~/lib/devextreme-aspnet-data/js/dx.aspnet.data.js"></script>
// <script src="~/js/devextreme/aspnet/dx.aspnet.mvc.js"></script>
// <script src="~/lib/jquery-ajax-unobtrusive/jquery.unobtrusive-ajax.js"></script>
// <script src="https://static.line-scdn.net/liff/edge/2/sdk.js"></script>
// <script src="~/js/LineIdLoginView.js"></script>

function getLoadPanelInstance() { return $("#loadPanel").dxLoadPanel("instance"); }
function loadPanel_show() { getLoadPanelInstance().show(); }
function loadPanel_hide() { getLoadPanelInstance().hide(); }
function Binding() { window.location.href = "/Authentication/LineLiffView/1653819697-YkPyPkr6"; }
function Login() { window.location.href = "/Authentication/Login"; }

var urlParams = new URLSearchParams(window.location.search);
var cleanUrl = window.location.href.split('?')[0];

window.onload = function () {
    // 優先使用 Server-side OAuth（更可靠，支援所有環境）
    // 如果用戶更偏好 LIFF，可以在這裡做環境判斷

    document.getElementById('displaynamefield').innerHTML = '正在準備 LINE 登入...';

    // ✅ 方案 A：一律使用 Server-side OAuth（推薦，最穩定）
    //useServerSideOAuth();

    // ✅ 方案 B：根據環境選擇（可選）
    detectEnvironmentAndChooseMethod();
};

// ========================================
// 方案 A：使用 Server-side OAuth（推薦）
// ========================================
function useServerSideOAuth() {
    console.log('[LINE Login] 使用 Server-side OAuth 2.0');
    document.getElementById('displaynamefield').innerHTML = '正在導向 LINE 登入...';
    ShowToast('正在導向 LINE 登入...', 'info', 2000);

    setTimeout(function() {
        // 直接導向後端的 LINE Login 起點
        window.location.href = '/Authentication/LineLoginStart';
    }, 500);
}

// ========================================
// 方案 B：根據環境選擇登入方式（可選）
// ========================================
function detectEnvironmentAndChooseMethod() {
    liff.init({ liffId: '@TempData["Proponent"]' })
        .then(async () => {
            var os = '';
            var isInClient = false;

            try { os = liff.getOS(); } catch (e) { }
            try { isInClient = liff.isInClient(); } catch (e) { }

            console.log('[LIFF Detection]', { os: os, isInClient: isInClient });

            // 如果是電腦版 LINE（os === 'web' && isInClient）或無法判斷，使用 Server-side OAuth
            if (os === 'web' || !isInClient) {
                console.log('[LINE Login] 偵測到電腦版環境，使用 Server-side OAuth');
                useServerSideOAuth();
            } else {
                // 手機版 LINE，使用 LIFF SDK
                console.log('[LINE Login] 偵測到手機版環境，使用 LIFF SDK');
                useLiffSdk();
            }
        })
        .catch(function(error) {
            console.error('[LIFF Init Error]', error);
            // LIFF 初始化失敗，使用 Server-side OAuth 作為後備
            useServerSideOAuth();
        });
}

// ========================================
// LIFF SDK 流程（手機版 LINE 用）
// ========================================
function useLiffSdk() {
    var isLoggedIn = false;
    var token = null;

    try { isLoggedIn = liff.isLoggedIn(); } catch (e) { }
    try { token = liff.getAccessToken(); } catch (e) { token = null; }

    if (isLoggedIn || token) {
        ensureProfilePermissionAndRun();
        return;
    }

    // 未登入，導向 LINE Login
    document.getElementById('displaynamefield').innerHTML = '您尚未登入 LINE';
    ShowToast('您尚未登入 LINE，將為您導向登入…', 'warning', 2500);

    setTimeout(function () {
        try {
            liff.login({ redirectUri: cleanUrl });
        } catch (e) {
            // LIFF login 失敗，改用 Server-side OAuth
            console.error('[LIFF Login Error]', e);
            useServerSideOAuth();
        }
    }, 600);
}

async function ensureProfilePermissionAndRun() {
    try {
        var permissionStatus = await liff.permission.query('profile');
        if (permissionStatus && permissionStatus.state === 'granted') {
            initializeApp();
            return;
        }
        if (permissionStatus && permissionStatus.state === 'prompt') {
            document.getElementById('displaynamefield').innerHTML = '請授權取得基本資料';
            try { liff.permission.requestAll(); } catch (e) { }
            setTimeout(function () { initializeApp(); }, 800);
            return;
        }
    } catch (e) {
        // 權限 API 不可用，直接嘗試
    }

    initializeApp();
}

var UserId = ""; var GroupId = ""; var RoomId = ""; var DisplayName = ""; var ViewType = "";

async function initializeApp() {
    try {
        const profile = await liff.getProfile();

        document.getElementById('displaynamefield').innerHTML =
            "歡迎 " + profile.displayName + " 登入<br/>" +
            "願神永遠祝福 " + profile.displayName + "<br/>" +
            "約需10~15秒，感謝您的耐心等候!";

        loadPanel_show();

        DisplayName = profile.displayName;
        UserId = profile.userId;
        GroupId = profile.aGroupId || "";
        RoomId = profile.aRoomId || "";
        ViewType = profile.aViewType || "";

        console.log('[LINE Profile]', { DisplayName, UserId, GroupId, RoomId, ViewType });

        UpdateLineUserId(UserId, GroupId, RoomId, ViewType);
    } catch (error) {
        console.error('[Get Profile Error]', error);
        document.getElementById('displaynamefield').innerHTML = "取得個人資料錯誤: " + error.message;
        ShowToast("取得個人資料失敗", "error", 3000);
        loadPanel_hide();
    }
}

function UpdateLineUserId(aUserLineId, aGroupId, aRoomId, aViewType) {
    console.log('[UpdateLineUserId] 開始更新 LINE ID', {
        UserLineId: aUserLineId,
        GroupId: aGroupId,
        RoomId: aRoomId,
        ViewType: aViewType
    });

    $.ajax({
        // ✅ 使用正確的 Controller 路徑
        url: '@Url.Action("SaveUserLineId", "Authentication")',
        data: {
            UserLineId: aUserLineId,
            GroupId: aGroupId,
            RoomId: aRoomId,
            ViewType: aViewType
        },
        type: 'POST',
        dataType: 'json',
        timeout: 30000, // 30 秒超時
        success: function (data) {
            console.log('[AJAX Success]', data);

            // ✅ 重要修正: 使用 PascalCase (因為 Startup.cs 使用 DefaultContractResolver)
            // C# 回傳: DisplayViewType, ActiveListId (大寫開頭)
            // JavaScript 存取: data.DisplayViewType, data.ActiveListId (大寫開頭)

            if (data.message != "尚未綁定") {
                ShowToast(data.message, "success", 1600);

                // ✅ 使用 PascalCase 存取屬性
                if (data.DisplayViewType == "MultiGroupView") {
                    console.log('[導向] MultiGroupView:', data.ActiveListId);
                    window.location.href = "/SmallGroup/MultiGroupView/" + data.ActiveListId;
                } else if (data.DisplayViewType == "IntegrateView") {
                    console.log('[導向] IntegrateView:', data.ActiveListId);
                    window.location.href = "/SmallGroup/IntegrateView/" + data.ActiveListId;
                } else if (data.DisplayViewType == "HappyGroupView") {
                    console.log('[導向] HappyGroupView');
                    window.location.href = "/SmallGroup/HappyGroup";
                } else {
                    console.error('[導向錯誤] 未知的視圖類型:', data.DisplayViewType);
                    ShowToast("登入錯誤: 未知的視圖類型", "error", 3000);
                    loadPanel_hide();
                    document.getElementById('displaynamefield').innerHTML = "登入錯誤: 未知的視圖類型<br/><small>DisplayViewType=" + data.DisplayViewType + "</small>";
                }
            } else {
                console.warn('[未綁定]', data.message);
                ShowToast(data.message, "warning", 2200);
                loadPanel_hide();
                document.getElementById('displaynamefield').innerHTML = "尚未綁定帳號<br/>請先完成綁定程序";

                // 3 秒後導向綁定頁面
                setTimeout(function() {
                    window.location.href = "/Authentication/LineLiffView/1653819697-YkPyPkr6";
                }, 3000);
            }
        },
        error: function (xhr, status, error) {
            console.error('[AJAX Error]', {
                status: status,
                error: error,
                statusCode: xhr.status,
                responseText: xhr.responseText,
                readyState: xhr.readyState
            });

            loadPanel_hide();

            var errorMessage = "登入失敗";

            if (xhr.status === 0) {
                errorMessage = "網路連線失敗，請檢查網路設定";
            } else if (xhr.status === 404) {
                errorMessage = "找不到登入頁面 (404)";
            } else if (xhr.status === 500) {
                errorMessage = "伺服器錯誤 (500)";
            } else if (status === 'timeout') {
                errorMessage = "連線逾時，請稍後再試";
            } else if (xhr.responseText) {
                try {
                    var errorData = JSON.parse(xhr.responseText);
                    errorMessage = errorData.message || errorMessage;
                } catch (e) {
                    errorMessage = "伺服器回應錯誤";
                }
            }

            ShowToast(errorMessage, "error", 4000);
            document.getElementById('displaynamefield').innerHTML =
                errorMessage + "<br/><small>錯誤代碼: " + xhr.status + "</small>";

            // 5 秒後導向登入頁
            setTimeout(function() {
                window.location.href = "/Authentication/Login";
            }, 5000);
        }
    });
}

function ShowLoadPanel(LocalLoadPanelMessage) {
    var lp = $("#loadPanel").dxLoadPanel("instance");
    lp.option('message', LocalLoadPanelMessage);
    lp.show();
}

function ShowToast(LocalToastMessage, Type, DisplayTime) {
    var ToastInstance = $("#Toast").dxToast("instance");
    ToastInstance.option('message', LocalToastMessage);
    ToastInstance.option('type', Type);
    ToastInstance.option('displayTime', DisplayTime);
    ToastInstance.option('closeOnClick', true);
    ToastInstance.show();
}
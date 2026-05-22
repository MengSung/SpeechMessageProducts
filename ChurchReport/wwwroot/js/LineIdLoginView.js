// LineIdLoginView.js
//
// Safe LIFF-only LINE ID login flow.
// Expected optional config from the page:
//   window.LINE_LOGIN_CFG = {
//     liffId: "...",
//     bindingLiffId: "...",
//     bindingLiffUrl: "https://liff.line.me/...",
//     saveUserLineIdUrl: "/Authentication/SaveUserLineId"
//   };

(function () {
    var cfg = window.LINE_LOGIN_CFG || {};
    var userId = "";
    var groupId = "";
    var roomId = "";
    var displayName = "";
    var viewType = "";

    function byId(id) {
        return document.getElementById(id);
    }

    function setStatus(html) {
        var target = byId("displaynamefield");
        if (target) {
            target.innerHTML = html;
        }
    }

    function getLoadPanelInstance() {
        if (!window.$) return null;
        var target = $("#loadPanel");
        return target.length ? target.dxLoadPanel("instance") : null;
    }

    window.loadPanel_show = function () {
        var panel = getLoadPanelInstance();
        if (panel) panel.show();
    };

    window.loadPanel_hide = function () {
        var panel = getLoadPanelInstance();
        if (panel) panel.hide();
    };

    window.ShowLoadPanel = function (message) {
        var panel = getLoadPanelInstance();
        if (!panel) return;
        panel.option("message", message);
        panel.show();
    };

    window.ShowToast = function (message, type, displayTime) {
        if (!window.$) return;
        var target = $("#Toast");
        if (!target.length) return;

        var toast = target.dxToast("instance");
        if (!toast) return;

        toast.option("message", message);
        toast.option("type", type);
        toast.option("displayTime", displayTime);
        toast.option("closeOnClick", true);
        toast.show();
    };

    function getBindingPageUrl() {
        if (cfg.bindingLiffUrl) {
            return cfg.bindingLiffUrl;
        }

        var bindingLiffId = cfg.bindingLiffId || "1653819697-YkPyPkr6";
        return "https://liff.line.me/" + encodeURIComponent(bindingLiffId);
    }

    window.Binding = function () {
        window.location.href = getBindingPageUrl();
    };

    window.Login = function () {
        window.location.href = "/Authentication/Login";
    };

    window.onload = function () {
        setStatus("Preparing LINE login...");

        if (!cfg.liffId) {
            setStatus("LIFF ID is missing.");
            window.ShowToast("LIFF ID is missing", "error", 4000);
            return;
        }

        if (!window.liff) {
            setStatus("LIFF SDK failed to load.");
            window.ShowToast("LIFF SDK failed to load", "error", 4000);
            return;
        }

        liff.init({ liffId: cfg.liffId })
            .then(function () {
                useLiffSdk();
            })
            .catch(function (error) {
                console.error("[LIFF Init Error]", error);
                setStatus("LINE initialization failed: " + error);
                window.ShowToast("LINE initialization failed", "error", 4000);
            });
    };

    function useLiffSdk() {
        var isLoggedIn = false;
        var token = null;

        try { isLoggedIn = liff.isLoggedIn(); } catch (e) { }
        try { token = liff.getAccessToken(); } catch (e) { token = null; }

        if (isLoggedIn || token) {
            ensureProfilePermissionAndRun();
            return;
        }

        setStatus("You are not logged in to LINE.");
        window.ShowToast("Redirecting to LINE login", "warning", 2500);

        setTimeout(function () {
            try {
                // Do not pass redirectUri manually. LINE will use the LIFF Endpoint URL.
                liff.login();
            } catch (e) {
                console.error("[LIFF Login Error]", e);
                setStatus("LINE login failed: " + e);
                window.ShowToast("LINE login failed", "error", 4000);
            }
        }, 600);
    }

    async function ensureProfilePermissionAndRun() {
        try {
            var status = await liff.permission.query("profile");

            if (status && status.state === "granted") {
                initializeApp();
                return;
            }

            if (status && status.state === "prompt") {
                setStatus("Please grant profile permission.");
                try { liff.permission.requestAll(); } catch (e) { }
                setTimeout(function () { initializeApp(); }, 800);
                return;
            }
        } catch (e) {
            // Some LIFF environments do not support the permission API.
        }

        initializeApp();
    }

    async function initializeApp() {
        try {
            var profile = await liff.getProfile();

            displayName = profile.displayName;
            userId = profile.userId;
            groupId = profile.aGroupId || "";
            roomId = profile.aRoomId || "";
            viewType = profile.aViewType || "";

            setStatus(
                "Welcome " + displayName + "<br/>" +
                "Signing in with LINE. Please wait..."
            );

            window.loadPanel_show();
            updateLineUserId(userId, groupId, roomId, viewType);
        } catch (error) {
            console.error("[Get Profile Error]", error);
            setStatus("Failed to get LINE profile: " + error.message);
            window.ShowToast("Failed to get LINE profile", "error", 3000);
            window.loadPanel_hide();
        }
    }

    function updateLineUserId(lineUserId, currentGroupId, currentRoomId, currentViewType) {
        if (!window.$) {
            setStatus("jQuery is not loaded.");
            return;
        }

        $.ajax({
            url: cfg.saveUserLineIdUrl || "/Authentication/SaveUserLineId",
            data: {
                UserLineId: lineUserId,
                GroupId: currentGroupId,
                RoomId: currentRoomId,
                ViewType: currentViewType
            },
            type: "POST",
            dataType: "json",
            timeout: 30000,
            success: function (data) {
                if (data.message !== "尚未綁定") {
                    window.ShowToast(data.message, "success", 1600);

                    if (data.DisplayViewType === "MultiGroupView") {
                        window.location.href = "/SmallGroup/MultiGroupView/" + data.ActiveListId;
                    } else if (data.DisplayViewType === "IntegrateView") {
                        window.location.href = "/SmallGroup/IntegrateView/" + data.ActiveListId;
                    } else if (data.DisplayViewType === "HappyGroupView") {
                        window.location.href = "/SmallGroup/HappyGroup";
                    } else {
                        window.loadPanel_hide();
                        setStatus("Unknown DisplayViewType: " + data.DisplayViewType);
                        window.ShowToast("Unknown DisplayViewType", "error", 3000);
                    }
                    return;
                }

                window.ShowToast(data.message, "warning", 2200);
                window.loadPanel_hide();
                setStatus("LINE account is not bound.<br/>Please complete binding first.");

                setTimeout(function () {
                    window.location.href = getBindingPageUrl();
                }, 3000);
            },
            error: function (xhr, status) {
                window.loadPanel_hide();

                var errorMessage = "Login failed";
                if (xhr.status === 0) {
                    errorMessage = "Network connection failed";
                } else if (xhr.status === 404) {
                    errorMessage = "Login endpoint not found (404)";
                } else if (xhr.status === 500) {
                    errorMessage = "Server error (500)";
                } else if (status === "timeout") {
                    errorMessage = "Connection timed out";
                }

                window.ShowToast(errorMessage, "error", 4000);
                setStatus(errorMessage + "<br/><small>Status: " + xhr.status + "</small>");

                setTimeout(function () {
                    window.location.href = "/Authentication/Login";
                }, 5000);
            }
        });
    }
})();

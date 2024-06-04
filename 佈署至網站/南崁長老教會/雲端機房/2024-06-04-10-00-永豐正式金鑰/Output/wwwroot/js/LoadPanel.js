function getLoadPanelInstance() {
    return $("#loadPanel").dxLoadPanel("instance");
}

function button_click() {
    getLoadPanelInstance().show();
}

function loadPanel_shown(e) {
    //    setTimeout(function () {
    //        e.component.hide();
    //    }, 3000);
}

function loadPanel_hidden() {
}

function SaveSmallGroup(arg) {

    //DevExpress.ui.notify(Model.members[0].PrayItem, "success", 2000);

    getLoadPanelInstance().show();
}

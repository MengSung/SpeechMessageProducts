function syncTreeViewSelection(treeView, value) {
    debugger;

    //alert("syncTreeViewSelection");

    //if (!value) {
    //    treeView.unselectAll();
    //    return;
    //}
    //
    //value.forEach(function (key) {
    //    treeView.selectItem(key);
    //});

    var DropDownValue = $("#DropDownBox").dxDropDownBox("instance").value;
    //alert("DropDown = " + DropDownValue);

    value = "耶和華";
}

function treeBox_valueChanged(e) {
    debugger;

    //alert("treeBox_valueChanged");

    var $treeView = e.component.content().find(".dx-treeview");

    if ($treeView.length) {
        syncTreeViewSelection($treeView.dxTreeView("instance"), e.value);
    }
}

function gridBox_valueChanged(e) {
    var $dataGrid = $("#embedded-datagrid");

    if ($dataGrid.length) {
        var dataGrid = $dataGrid.dxDataGrid("instance");
        dataGrid.selectRows(e.value, false);
    }
}

function OnItemSelectionChanged(args) {
    //alert("AAA");
    var value = args.component.getSelectedNodesKeys();
    //alert("value =" + value);
    //alert("args.component = " + args.component);
    component.option("value", value);
}

function OptionChanged(args) {
    //alert("BBB");
}


function ContentReady(args) {
    //debugger;
    //alert("ContentReady 001");
    var Option = component.option("value");
    //alert("Option = " + Option);
    syncTreeViewSelection(args.component, component.option("value"));
}

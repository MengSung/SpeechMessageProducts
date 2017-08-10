function onBegin() {
    debugger;

            //Request.arguments = "Hello";
            //request.data = "Hello";
            //data: "Hello";
            //aResult: "ZZZZ";
            Request.data = "{ aResult: Hello }";

            //$.ajax({
            //    url: '@Url.Action("SaveSmallGroup", "Home")',
            //    data: { aResult: "Hello" }
            //    type: 'POST', //POST if you want to save, GET if you want to fetch data from server
            //
            //
            //
            //});

            //var members = $("#gridContainer").dxDataGrid("instance").getDataSource().items();
            //var result = "";
            //$.each(members, function (index, member) {
            //    for (var prop in member) {
            //        result += "&Members[" + index + "]." + prop + "=" + member[prop];
            //        alert("Result = " + result)
            //    }
            //})
            ////request.data += result;
            //
            ////request.data = "HELLO";
            //
            //data.aResult = "Hello";
        }

        function onSuccess(data) {
            //alert("Success");

            getLoadPanelInstance().hide();

            if (data.status == 1) {
                DevExpress.ui.notify(data.message, "success", 2000);
                //window.location.href = "/Jesus/Login";
                //window.location.href = "/Home/NewPerson";
            }
            else {
                //window.location.href = "/Home/NewPerson";
                //window.location.href = "/Home/InputReport/" + data.status;
                DevExpress.ui.notify(data.message, "error", 5000);
            }
        }

        function onComplete(data) {
            //alert("結束!");
            if (data.status == 1) {
                //window.location.href = "/Jesus/Login";
            }
            else {
                //window.location.href = "/Home/Index";

            }

            //$("#divLoading").html("");
        }


        function GetData() {
            debugger;

            //Request.arguments = "Hello";
            //request.data = "Hello";
            //data: "Hello";
            //aResult: "ZZZZ";
            //return "耶和華";

            //$.ajax({
            //    url: '@Url.Action("SaveSmallGroup", "Home")',
            //    data: { aResult: "Hello" }
            //    type: 'POST', //POST if you want to save, GET if you want to fetch data from server
            //
            //
            //
            //});

            var Section = $("#gridContainer").dxDataGrid("instance").getDataSource().items();

            $.each( Section, function (index, aSection) {

                var SmallGroup = aSection.items;

                $.each(SmallGroup, function (index1, aSmallGroupElement) {
                    var SmallGroupMember = aSmallGroupElement.items;

                    $.each(SmallGroupMember, function (index2, aMember) {

                        for (var prop in aMember) {

                           //result += "&Members[" + index + "]." + prop + "=" + member[prop];
                           // alert("prop = " + prop + " Value = " + aMember[prop]);
                       }

                    });

                });

                //for (var prop in member) {
                //    result += "&Members[" + index + "]." + prop + "=" + member[prop];
                //    alert("Result = " + result)
                //}
            });

            //var result = "";
            //$.each(members, function (index, member) {
            //    for (var prop in member) {
            //        result += "&Members[" + index + "]." + prop + "=" + member[prop];
            //        alert("Result = " + result)
            //    }
            //})
            ////request.data += result;
            //
            ////request.data = "HELLO";
            //
            //data.aResult = "Hello";

            return "耶和華";

        }


$(function () {
    const $employeeDescription = $("#txtEmployeeDescription");
    const $employeeCode = $("#txtEmployeeCode");
    const $searchUrl = $employeeDescription.data("search-url");

    if ($searchUrl) {
        $employeeDescription.autocomplete({
            source: function (request, response) {
                $.ajax({
                    url: $searchUrl,
                    type: "GET",
                    dataType: "json",
                    data: { term: request.term },
                    success: function (data) {
                        response($.map(data, function (item) {
                            return { label: item.label, value: item.desc, id: item.value };
                        }));
                    }
                });
            },
            select: function (event, ui) {
                $employeeDescription.val(ui.item.value);
                $employeeCode.val(ui.item.id);
                return false;
            },
            minLength: 1
        });
    }

    $employeeDescription.on("keyup", function () {
        if (!$(this).val()) {
            $employeeCode.val("");
        }
    });

    $("#chkheader").on("click", function () {
        $(".chkvid").prop("checked", $(this).is(":checked"));
    });

    const toggleGrid = () => {
        const isAuto = $("#cbAutoAdjustment").is(":checked");
        $("#gridRowContainer").toggle(!isAuto);
        $("#btnshow").toggle(!isAuto);
    };

    $("#cbAutoAdjustment").on("change", toggleGrid);
    toggleGrid();
});

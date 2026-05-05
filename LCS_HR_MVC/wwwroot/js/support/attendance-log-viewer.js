(function ($) {
    function buildMonthOptions($monthSelect, workingYear, workingMonth) {
        var selectedYear = parseInt($("#ddlYear").val(), 10);
        var selectedMonth = parseInt($monthSelect.val(), 10);
        var maxMonth = selectedYear < workingYear ? 12 : workingMonth;

        $monthSelect.empty();

        for (var month = 1; month <= maxMonth; month += 1) {
            var monthName = new Date(2000, month - 1, 1).toLocaleString("en-US", { month: "long" });
            var option = $("<option></option>").val(month).text(monthName);
            if (month === selectedMonth || (isNaN(selectedMonth) && month === maxMonth)) {
                option.prop("selected", true);
            }

            $monthSelect.append(option);
        }

        if ($monthSelect.prop("selectedIndex") === -1 && maxMonth > 0) {
            $monthSelect.val(maxMonth.toString());
        }
    }

    $(function () {
        var $container = $("#attendance-log-viewer");
        if ($container.length === 0) {
            return;
        }

        var searchUrl = $container.data("search-url");
        var workingYear = parseInt($container.data("working-year"), 10);
        var workingMonth = parseInt($container.data("working-month"), 10);
        var $employeeDescription = $("#txtEmployeeDescription");
        var $employeeCode = $("#txtEmployeeCode");
        var $monthSelect = $("#ddlMonth");

        buildMonthOptions($monthSelect, workingYear, workingMonth);

        $("#ddlYear").on("change", function () {
            buildMonthOptions($monthSelect, workingYear, workingMonth);
        });

        $employeeDescription.autocomplete({
            source: function (request, response) {
                $.ajax({
                    url: searchUrl,
                    type: "GET",
                    dataType: "json",
                    data: { term: request.term },
                    success: function (data) {
                        response($.map(data, function (item) {
                            return {
                                label: item.label,
                                value: item.desc,
                                id: item.value
                            };
                        }));
                    }
                });
            },
            minLength: 1,
            select: function (event, ui) {
                $employeeDescription.val(ui.item.value);
                $employeeCode.val(ui.item.id);
                return false;
            }
        });

        $employeeDescription.on("input", function () {
            if (!$(this).val()) {
                $employeeCode.val("");
            }
        });
    });
})(jQuery);

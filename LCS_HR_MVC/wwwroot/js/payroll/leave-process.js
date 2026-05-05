document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("leaveProcessForm");
    if (!form) {
        return;
    }

    const zone = document.getElementById("ddlZone");
    const city = document.getElementById("ddlCity");
    const division = document.getElementById("ddlBU");
    const department = document.getElementById("ddlDepartment");
    const subDepartment = document.getElementById("ddlSubDepartments");
    const employeeDescription = document.getElementById("txtEmployeeDescription");
    const employeeCode = document.getElementById("txtEmployeeCode");
    const modeInputs = form.querySelectorAll("input[name='Mode']");

    const rows = {
        zone: document.getElementById("rowZone"),
        city: document.getElementById("rowCity"),
        division: document.getElementById("rowDiv"),
        department: document.getElementById("rowDept"),
        subDepartment: document.getElementById("rowSubDept"),
        employee: document.getElementById("rowEmp")
    };

    const toggleMode = () => {
        const isEmployeeMode = form.querySelector("input[name='Mode']:checked")?.value === "Employee";
        rows.zone.style.display = isEmployeeMode ? "none" : "table-row";
        rows.city.style.display = isEmployeeMode ? "none" : "table-row";
        rows.division.style.display = isEmployeeMode ? "none" : "table-row";
        rows.department.style.display = isEmployeeMode ? "none" : "table-row";
        rows.subDepartment.style.display = isEmployeeMode ? "none" : "table-row";
        rows.employee.style.display = isEmployeeMode ? "table-row" : "none";
    };

    const populateSelect = (select, items, placeholderText, placeholderValue) => {
        select.innerHTML = "";
        const placeholder = document.createElement("option");
        placeholder.value = placeholderValue;
        placeholder.textContent = placeholderText;
        select.appendChild(placeholder);

        items.forEach((item) => {
            const option = document.createElement("option");
            option.value = item.value;
            option.textContent = item.text;
            select.appendChild(option);
        });
    };

    const fetchOptions = async (url, queryName, queryValue) => {
        const response = await fetch(`${url}?${queryName}=${encodeURIComponent(queryValue)}`, {
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            return [];
        }

        return await response.json();
    };

    if (zone && city) {
        zone.addEventListener("change", async () => {
            if (!form.dataset.citiesUrl) {
                return;
            }

            const items = await fetchOptions(form.dataset.citiesUrl, "zoneId", zone.value);
            populateSelect(city, items, "All Cities", "00");
        });
    }

    if (division && department) {
        division.addEventListener("change", async () => {
            if (!form.dataset.departmentsUrl) {
                return;
            }

            if (!division.value || division.value === "0") {
                populateSelect(department, [], "Please select Department", "0");
                populateSelect(subDepartment, [], "All", "0");
                return;
            }

            const items = await fetchOptions(form.dataset.departmentsUrl, "buId", division.value);
            populateSelect(department, items, "Please select Department", "0");
            populateSelect(subDepartment, [], "All", "0");
        });
    }

    if (department && subDepartment) {
        department.addEventListener("change", async () => {
            if (!form.dataset.subdepartmentsUrl) {
                return;
            }

            if (!department.value || department.value === "0") {
                populateSelect(subDepartment, [], "All", "0");
                return;
            }

            const items = await fetchOptions(form.dataset.subdepartmentsUrl, "departmentId", department.value);
            populateSelect(subDepartment, items, "All", "0");
        });
    }

    modeInputs.forEach((input) => input.addEventListener("change", toggleMode));
    toggleMode();

    if (window.jQuery && employeeDescription && employeeCode) {
        const $employeeDescription = window.jQuery(employeeDescription);
        const $employeeCode = window.jQuery(employeeCode);
        const searchUrl = $employeeDescription.data("search-url");

        if (searchUrl) {
            $employeeDescription.autocomplete({
                source: function (request, response) {
                    window.jQuery.ajax({
                        url: searchUrl,
                        type: "GET",
                        dataType: "json",
                        data: { term: request.term },
                        success: function (data) {
                            response(window.jQuery.map(data, function (item) {
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

            $employeeDescription.on("keyup", function () {
                if (!window.jQuery(this).val()) {
                    $employeeCode.val("");
                }
            });
        }
    }

    form.addEventListener("submit", (event) => {
        const submitter = event.submitter;
        if (!submitter || submitter.value !== "Process") {
            return;
        }

        const isEmployeeMode = form.querySelector("input[name='Mode']:checked")?.value === "Employee";
        if (isEmployeeMode && (!employeeCode.value || !employeeCode.value.trim())) {
            event.preventDefault();
            alert("Employee is required.");
        }
    });
});

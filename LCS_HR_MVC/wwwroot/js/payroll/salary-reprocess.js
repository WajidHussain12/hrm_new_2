document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("salaryReprocessForm");
    if (!form) {
        return;
    }

    const zone = document.getElementById("ddlZone");
    const city = document.getElementById("ddlCity");
    const subDepartment = document.getElementById("ddlSubDepartment");
    const employeeList = document.getElementById("employeeList");
    const headerAll = document.getElementById("chkHeaderAll");

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

    const renderEmployees = (items) => {
        employeeList.innerHTML = "";
        headerAll.checked = false;

        if (!items.length) {
            employeeList.textContent = "No employees found.";
            return;
        }

        items.forEach((item) => {
            const label = document.createElement("label");
            label.style.display = "block";

            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.name = "SelectedEmployeeIds";
            checkbox.value = item.empNo;

            label.appendChild(checkbox);
            label.appendChild(document.createTextNode(` ${item.displayName}`));
            employeeList.appendChild(label);
        });
    };

    const fetchJson = async (url, params) => {
        const query = new URLSearchParams(params);
        const response = await fetch(`${url}?${query.toString()}`, {
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            return [];
        }

        return await response.json();
    };

    zone.addEventListener("change", async () => {
        const items = await fetchJson(form.dataset.citiesUrl, { zoneId: zone.value });
        populateSelect(city, items, "Please Select", "0");
        populateSelect(subDepartment, [], "Please Select", "0");
        renderEmployees([]);
    });

    city.addEventListener("change", async () => {
        if (!city.value || city.value === "0") {
            populateSelect(subDepartment, [], "Please Select", "0");
            renderEmployees([]);
            return;
        }

        const items = await fetchJson(form.dataset.subdepartmentsUrl, { cityCode: city.value });
        populateSelect(subDepartment, items, "Please Select", "0");
        renderEmployees([]);
    });

    subDepartment.addEventListener("change", async () => {
        if (!city.value || city.value === "0" || !subDepartment.value || subDepartment.value === "0") {
            renderEmployees([]);
            return;
        }

        const items = await fetchJson(form.dataset.employeesUrl, {
            cityCode: city.value,
            subDepartmentId: subDepartment.value
        });

        renderEmployees(items);
    });

    headerAll.addEventListener("change", () => {
        employeeList.querySelectorAll("input[type='checkbox']").forEach((checkbox) => {
            checkbox.checked = headerAll.checked;
        });
    });
});

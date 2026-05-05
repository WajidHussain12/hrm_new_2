document.addEventListener("DOMContentLoaded", () => {
    const form = document.getElementById("codCommissionForm");
    if (!form) {
        return;
    }

    const zone = document.getElementById("ddlZone");
    const city = document.getElementById("ddlCity");

    const populateCitySelect = (items) => {
        city.innerHTML = "";

        const placeholder = document.createElement("option");
        placeholder.value = "0";
        placeholder.textContent = "Please Select";
        city.appendChild(placeholder);

        items.forEach((item) => {
            const option = document.createElement("option");
            option.value = item.value;
            option.textContent = item.text;
            city.appendChild(option);
        });
    };

    zone.addEventListener("change", async () => {
        const query = new URLSearchParams({ zoneId: zone.value });
        const response = await fetch(`${form.dataset.citiesUrl}?${query.toString()}`, {
            headers: { "X-Requested-With": "XMLHttpRequest" }
        });

        if (!response.ok) {
            populateCitySelect([]);
            return;
        }

        const items = await response.json();
        populateCitySelect(items);
    });
});

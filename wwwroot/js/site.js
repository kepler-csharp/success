const scanForm = document.getElementById("scanForm");
const scanInput = document.getElementById("scanInput");
const resultCard = document.getElementById("resultCard");
const ticketDetails = document.getElementById("ticketDetails");
const verdictMark = document.getElementById("verdictMark");
const apiStatus = document.getElementById("apiStatus");

const currentDate = document.getElementById("currentDate");
const currentTime = document.getElementById("currentTime");

scanForm.addEventListener("submit", async function (event) {
    event.preventDefault();

    const code = scanInput.value.trim();
    if (code === "") {
        showResult({
            success: false,
            title: "Code required",
            message: "Scan the QR or type the ticket code.",
            type: "error"
        });
        return;
    }

    await validateTicket(code);
});

document.getElementById("clearButton").addEventListener("click", function () {
    scanInput.value = "";
    scanInput.focus();
});

async function validateTicket(code) {
    setLoading();

    try {
        // Token antiforgery generado por Razor en el formulario.
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

        // El backend valida el ticket y devuelve un resultado listo para pintar.
        const response = await fetch(window.ticketUrls.validate, {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": token
            },
            body: JSON.stringify({ scanCode: code })
        });

        const data = await response.json();
        showResult(data);
        markApiStatus(true);
        scanInput.value = "";
        scanInput.focus();
    } catch {
        markApiStatus(false);
        showResult({
            success: false,
            title: "Could not check ticket",
            message: "Check the connection and try again.",
            type: "error"
        });
    }
}

function setLoading() {
    resultCard.className = "panel result-panel loading";
    verdictMark.className = "verdict-mark loading";
    verdictMark.innerHTML = '<i class="fas fa-spinner fa-spin"></i>';
    document.getElementById("resultTitle").textContent = "Checking...";
    document.getElementById("resultMessage").textContent = "Please wait a moment.";
    ticketDetails.classList.add("hidden");
}

function showResult(data) {
    // Cambia el estado visual segun la respuesta del servidor.
    const resultType = data.success ? "success" : (data.type || "error");
    resultCard.className = `panel result-panel ${resultType}`;
    verdictMark.className = `verdict-mark ${data.success ? "success" : "error"}`;
    verdictMark.innerHTML = data.success ? '<i class="fas fa-check"></i>' : '<i class="fas fa-xmark"></i>';
    document.getElementById("resultTitle").textContent = toUserText(data.title || "Result");
    document.getElementById("resultMessage").textContent = toUserText(data.message || "");

    if (!data.ticket) {
        ticketDetails.classList.add("hidden");
        return;
    }

    const ticket = data.ticket;
    const contact = [ticket.email, ticket.phoneNumber].filter(Boolean).join(" - ");
    const seat = [ticket.row, ticket.seatNumber].filter(Boolean).join("-");
    const typeStatus = [ticket.ticketType || ticket.entryMode, ticket.status].filter(Boolean).join(" / ");

    document.getElementById("clientName").textContent = ticket.clientName || "No name";
    document.getElementById("clientContact").textContent = contact || "No contact";
    document.getElementById("eventName").textContent = ticket.eventName || "--";
    document.getElementById("venueName").textContent = ticket.venueName || "--";
    document.getElementById("showtimeStart").textContent = formatDateTime(ticket.showtimeStart);
    document.getElementById("seatNumber").textContent = seat || "No seat";
    document.getElementById("ticketType").textContent = typeStatus || "--";
    document.getElementById("ticketCode").textContent = ticket.code || "--";
    document.getElementById("scanTime").textContent = formatDateTime(ticket.scanTime);

    const photo = document.getElementById("clientPhoto");
    if (ticket.photoUrl) {
        photo.src = ticket.photoUrl;
        photo.classList.add("has-photo");
    } else {
        photo.removeAttribute("src");
        photo.classList.remove("has-photo");
    }

    ticketDetails.classList.remove("hidden");
}

function markApiStatus(isOnline) {
    apiStatus.className = `status ${isOnline ? "online" : "offline"}`;
    apiStatus.textContent = isOnline ? "Ready to check" : "No connection";
}

function toUserText(value) {
    return String(value || "")
        .replace(/\bapi\b/gi, "system")
        .replace(/\bjson\b/gi, "response")
        .replace(/\btoken\b/gi, "session");
}

function formatDateTime(value) {
    if (!value) {
        return "--";
    }

    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
        return "--";
    }

    return date.toLocaleString("en-US", {
        day: "2-digit",
        month: "short",
        hour: "2-digit",
        minute: "2-digit"
    });
}

function updateClock() {
    const now = new Date();

    currentDate.textContent = now.toLocaleDateString("en-US", {
        weekday: "long",
        day: "2-digit",
        month: "short"
    });

    currentTime.textContent = now.toLocaleTimeString("en-US", {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
    });
}

updateClock();
setInterval(updateClock, 1000);

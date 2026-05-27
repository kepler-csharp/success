const scanForm = document.getElementById("scanForm");
const scanInput = document.getElementById("scanInput");
const resultCard = document.getElementById("resultCard");
const ticketDetails = document.getElementById("ticketDetails");
const verdictMark = document.getElementById("verdictMark");
const apiStatus = document.getElementById("apiStatus");
const qrReader = document.getElementById("qrReader");
const cameraStatus = document.getElementById("cameraStatus");
const startCameraButton = document.getElementById("startCameraButton");
const stopCameraButton = document.getElementById("stopCameraButton");
const scanNextButton = document.getElementById("scanNextButton");

const currentDate = document.getElementById("currentDate");
const currentTime = document.getElementById("currentTime");

let qrScanner = null;
let isScannerRunning = false;
let isScanLocked = false;

// Mantiene el flujo manual para lectores fisicos o digitacion.
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

if (startCameraButton && stopCameraButton && scanNextButton) {
    // La camara se inicia por click para que el navegador permita pedir permisos.
    startCameraButton.addEventListener("click", startCameraScanner);
    stopCameraButton.addEventListener("click", stopCameraScanner);
    scanNextButton.addEventListener("click", resumeCameraScanner);
}

async function validateTicket(code, options = {}) {
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
        if (!options.fromScanner) {
            scanInput.focus();
        }
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

async function startCameraScanner() {
    if (isScannerRunning) {
        return;
    }

    // En celulares la camara solo funciona en HTTPS o localhost.
    if (!window.isSecureContext && !["localhost", "127.0.0.1"].includes(window.location.hostname)) {
        setCameraStatus("Camera requires HTTPS", false);
        return;
    }

    if (typeof Html5Qrcode === "undefined") {
        setCameraStatus("QR scanner is not available", false);
        return;
    }

    try {
        setCameraStatus("Requesting permission...", true);
        // Html5Qrcode abre la camara y decodifica QR en el navegador.
        const scannerOptions = typeof Html5QrcodeSupportedFormats === "undefined"
            ? {}
            : { formatsToSupport: [Html5QrcodeSupportedFormats.QR_CODE] };

        qrScanner = qrScanner || new Html5Qrcode(qrReader.id, scannerOptions);

        await qrScanner.start(
            { facingMode: "environment" },
            {
                fps: 10,
                qrbox: getQrBox,
                aspectRatio: 1.333334,
                disableFlip: false
            },
            handleQrScan
        );

        isScannerRunning = true;
        isScanLocked = false;
        startCameraButton.classList.add("hidden");
        stopCameraButton.classList.remove("hidden");
        scanNextButton.classList.add("hidden");
        setCameraStatus("Point at a QR", true);
    } catch {
        isScannerRunning = false;
        setCameraStatus("Camera could not start", false);
        startCameraButton.classList.remove("hidden");
        stopCameraButton.classList.add("hidden");
        scanNextButton.classList.add("hidden");
    }
}

async function stopCameraScanner() {
    if (!qrScanner || !isScannerRunning) {
        return;
    }

    try {
        await qrScanner.stop();
        qrScanner.clear();
    } catch {
        // The camera may already be stopped by the browser.
    }

    isScannerRunning = false;
    isScanLocked = false;
    qrReader.innerHTML = "";
    startCameraButton.classList.remove("hidden");
    stopCameraButton.classList.add("hidden");
    scanNextButton.classList.add("hidden");
    setCameraStatus("Camera off", true);
    scanInput.focus();
}

function resumeCameraScanner() {
    if (!qrScanner || !isScannerRunning) {
        return;
    }

    try {
        qrScanner.resume();
        isScanLocked = false;
        scanNextButton.classList.add("hidden");
        setCameraStatus("Point at a QR", true);
    } catch {
        setCameraStatus("Restart camera", false);
        startCameraButton.classList.remove("hidden");
        scanNextButton.classList.add("hidden");
    }
}

async function handleQrScan(decodedText) {
    const code = String(decodedText || "").trim();

    if (isScanLocked || code === "") {
        return;
    }

    isScanLocked = true;
    scanInput.value = code;
    setCameraStatus("QR read. Verifying...", true);

    if (navigator.vibrate) {
        navigator.vibrate(80);
    }

    try {
        // Se pausa para evitar validar varias veces el mismo QR.
        qrScanner.pause(true);
    } catch {
        // Some browsers may not support pausing with the current camera state.
    }

    // Reutiliza la misma validacion que usa el formulario manual.
    await validateTicket(code, { fromScanner: true });

    if (isScannerRunning) {
        scanNextButton.classList.remove("hidden");
        setCameraStatus("Verified. Ready for next scan.", true);
    }
}

function getQrBox(viewfinderWidth, viewfinderHeight) {
    // Mantiene el cuadro de lectura proporcional en desktop y celular.
    const minEdge = Math.min(viewfinderWidth, viewfinderHeight);
    const size = Math.floor(Math.min(minEdge * 0.72, 280));
    return {
        width: size,
        height: size
    };
}

function setCameraStatus(message, isOk) {
    cameraStatus.textContent = message;
    cameraStatus.className = `camera-status ${isOk ? "online" : "offline"}`;
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

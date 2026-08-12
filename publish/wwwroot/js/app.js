/**
 * ESD FLOOR CLEANING SYSTEM - ANSI/ESD S20.20-2021
 * Frontend Controller Application
 */

document.addEventListener('DOMContentLoaded', () => {
    // App State
    let currentMaps = [];
    let currentSelectedArea = null;
    let editorSelectedArea = null; // Independent state for Ajustes Editor
    let selectedCoords = null; // { xPercent, yPercent }
    let uploadedEvidence = null; // { fileName, fileType, base64 }
    let currentRequests = [];
    let webcamStream = null;

    // DOM Elements
    const areaSelect = document.getElementById('area-select');
    const editorAreaSelect = document.getElementById('editor-area-select');
    const floorMapImg = document.getElementById('floor-map-img');
    const mapWrapper = document.getElementById('map-wrapper');
    const mapOverlay = document.getElementById('map-overlay');
    const coordText = document.getElementById('coord-text');
    const priorityPreviewContent = document.getElementById('priority-preview-content');
    
    const evidenceFileInput = document.getElementById('evidence-file-input');
    const evidencePreviewContainer = document.getElementById('evidence-preview-container');
    const btnOpenCamera = document.getElementById('btn-open-camera');
    
    const modalWebcam = document.getElementById('modal-webcam');
    const webcamVideo = document.getElementById('webcam-video');
    const webcamCanvas = document.getElementById('webcam-canvas');
    const btnCapturePhoto = document.getElementById('btn-capture-photo');
    const btnCloseWebcam = document.getElementById('btn-close-webcam');
    
    const formCleaningRequest = document.getElementById('form-cleaning-request');
    const requestsTbody = document.getElementById('requests-tbody');
    const requestBadge = document.getElementById('request-badge');

    // Initialize Application
    initApp();

    async function initApp() {
        setupNavigation();
        setupEventListeners();
        await loadMaps();
        await loadRequests();
        await loadSettings();
    }

    // Navigation Tabs Handler
    function setupNavigation() {
        const navBtns = document.querySelectorAll('.nav-btn');
        const tabContents = document.querySelectorAll('.tab-content');

        navBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const targetTab = btn.getAttribute('data-tab');
                navBtns.forEach(b => b.classList.remove('active'));
                tabContents.forEach(t => t.classList.remove('active'));

                btn.classList.add('active');
                document.getElementById(`tab-${targetTab}`).classList.add('active');

                if (targetTab === 'ajustes') {
                    renderEditorMap();
                    renderAreasManagementTable();
                }
            });
        });
    }

    function setupEventListeners() {
        areaSelect.addEventListener('change', (e) => {
            selectArea(e.target.value);
        });

        if (editorAreaSelect) {
            editorAreaSelect.addEventListener('change', (e) => {
                selectEditorArea(e.target.value);
            });
        }

        document.getElementById('btn-refresh-requests').addEventListener('click', loadRequests);
        document.getElementById('btn-close-detail').addEventListener('click', () => {
            document.getElementById('modal-detail').classList.remove('active');
        });
    }

    // Fetch Maps from C# API
    async function loadMaps() {
        try {
            const res = await fetch('/api/cleaning/maps');
            if (res.ok) {
                currentMaps = await res.json();
                populateAreaDropdowns();
            }
        } catch (err) {
            console.error('Error fetching maps:', err);
        }
    }

    function populateAreaDropdowns() {
        areaSelect.innerHTML = '';
        if (editorAreaSelect) editorAreaSelect.innerHTML = '';

        currentMaps.forEach(map => {
            const opt1 = document.createElement('option');
            opt1.value = map.areaId;
            opt1.textContent = `${map.areaId} - ${map.areaName}`;
            areaSelect.appendChild(opt1);

            if (editorAreaSelect) {
                const opt2 = document.createElement('option');
                opt2.value = map.areaId;
                opt2.textContent = `${map.areaId} - ${map.areaName} (${map.criticality})`;
                editorAreaSelect.appendChild(opt2);
            }
        });

        if (currentMaps.length > 0) {
            selectArea(currentMaps[0].areaId);
            
            const currentEditorValue = editorAreaSelect ? editorAreaSelect.value : null;
            if (currentEditorValue && currentMaps.some(m => m.areaId === currentEditorValue)) {
                selectEditorArea(currentEditorValue);
            } else {
                selectEditorArea(currentMaps[0].areaId);
            }
        }

        renderAreasManagementTable();
    }

    function selectArea(areaId) {
        currentSelectedArea = currentMaps.find(m => m.areaId === areaId);
        if (!currentSelectedArea) return;

        floorMapImg.src = currentSelectedArea.imageUrl;
        
        // Update Info Banner
        document.getElementById('lbl-criticality').textContent = currentSelectedArea.criticality;
        
        const days = calculateDays(currentSelectedArea.lastCleaningDate);
        const lastCleanDateStr = currentSelectedArea.lastCleaningDate 
            ? new Date(currentSelectedArea.lastCleaningDate).toLocaleDateString('es-MX') 
            : 'Sin registro';
        
        let nextCleanDate = currentSelectedArea.nextCleaningDate;
        if (!nextCleanDate && currentSelectedArea.lastCleaningDate) {
            nextCleanDate = new Date(new Date(currentSelectedArea.lastCleaningDate).getTime() + 90 * 24 * 60 * 60 * 1000);
        }
        const nextCleanStr = nextCleanDate ? new Date(nextCleanDate).toLocaleDateString('es-MX') : 'Por definir';

        document.getElementById('lbl-last-cleaning').innerHTML = `
            <strong>Última Limpieza:</strong> ${lastCleanDateStr} (${days} días transcurridos) | <strong>Próxima Limpieza:</strong> ${nextCleanStr}
        `;

        // Clear reticle
        selectedCoords = null;
        coordText.textContent = "Ninguna ubicación elegida (Haz clic en el mapa)";
        
        renderMapOverlay();
        updatePriorityPreview();
    }

    function selectEditorArea(areaId) {
        editorSelectedArea = currentMaps.find(m => m.areaId === areaId);
        if (editorAreaSelect && editorSelectedArea) {
            editorAreaSelect.value = areaId;
        }
        renderEditorMap();
    }

    function renderMapOverlay() {
        mapOverlay.innerHTML = '';

        if (!currentSelectedArea) return;

        // Render Measurement Nodes
        if (currentSelectedArea.points) {
            currentSelectedArea.points.forEach(pt => {
                const pin = document.createElement('div');
                pin.className = 'map-pin';
                if (pt.lastResistanceOhms > 1e8) {
                    pin.classList.add('high-resistance');
                }
                pin.style.left = `${pt.xPercent}%`;
                pin.style.top = `${pt.yPercent}%`;

                const resSci = pt.lastResistanceOhms.toExponential(1).toUpperCase();
                pin.innerHTML = `
                    <div class="pin-head"><i class="fa-solid fa-bolt"></i></div>
                    <div class="pin-label">${pt.code}: ${resSci} &Omega;</div>
                `;
                mapOverlay.appendChild(pin);
            });
        }

        // Render Reticle if selected
        if (selectedCoords) {
            const reticle = document.createElement('div');
            reticle.className = 'reticle-marker';
            reticle.style.left = `${selectedCoords.xPercent}%`;
            reticle.style.top = `${selectedCoords.yPercent}%`;
            mapOverlay.appendChild(reticle);
        }
    }

    // Map Click Handler (Target Selection)
    mapWrapper.addEventListener('click', (e) => {
        if (!currentSelectedArea) return;

        const rect = mapWrapper.getBoundingClientRect();
        const xPixels = e.clientX - rect.left;
        const yPixels = e.clientY - rect.top;

        const xPercent = parseFloat(((xPixels / rect.width) * 100).toFixed(2));
        const yPercent = parseFloat(((yPixels / rect.height) * 100).toFixed(2));

        selectedCoords = { xPercent, yPercent };
        coordText.textContent = `Coordenadas: X=${xPercent}%, Y=${yPercent}% (Área: ${currentSelectedArea.areaName})`;

        renderMapOverlay();
        updatePriorityPreview();
    });

    // Priority Live Preview Engine
    function updatePriorityPreview() {
        if (!currentSelectedArea || !selectedCoords) {
            priorityPreviewContent.innerHTML = `<span style="color: var(--text-dim);">Haz clic sobre el plano para evaluar las condiciones de resistencia ESD y regla de 3 meses.</span>`;
            return;
        }

        const days = calculateDays(currentSelectedArea.lastCleaningDate);
        
        // Find nearest measurement point
        let nearestPt = null;
        let minDistance = Infinity;

        if (currentSelectedArea.points && currentSelectedArea.points.length > 0) {
            currentSelectedArea.points.forEach(pt => {
                const dist = Math.hypot(pt.xPercent - selectedCoords.xPercent, pt.yPercent - selectedCoords.yPercent);
                if (dist < minDistance) {
                    minDistance = dist;
                    nearestPt = pt;
                }
            });
        }

        const resistance = nearestPt ? nearestPt.lastResistanceOhms : 4.5e7;
        const isHighRes = resistance > 1e8;
        const meets3Months = days >= 90;

        let badgePriority = '';
        let badgeStatus = '';
        let textExplanation = '';

        if (isHighRes) {
            badgePriority = `<span class="badge badge-alta">PRIORIDAD ALTA</span>`;
            badgeStatus = `<span class="badge badge-autorizada">AUTORIZADA</span>`;
            textExplanation = `¡Atención! La medición ESD en ${nearestPt ? nearestPt.code : 'zona'} es de <strong>${resistance.toExponential(1).toUpperCase()} &Omega; (> 1.0e8 &Omega;)</strong>. Excede la resistencia máxima permitida. Según ANSI/ESD S20.20-2021 se autoriza renovación inmediata de cera sin esperar 3 meses.`;
        } else if (!meets3Months) {
            badgePriority = `<span class="badge badge-baja">PRIORIDAD BAJA</span>`;
            badgeStatus = `<span class="badge badge-denegada">DENEGADA (PERIODICIDAD)</span>`;
            textExplanation = `Solo han transcurrido <strong>${days} días</strong> de la última limpieza (Requerido: ≥ 90 días). La resistencia actual (${resistance.toExponential(1).toUpperCase()} &Omega;) es conforme (&le; 1e8 &Omega;).`;
        } else {
            const isHighCrit = currentSelectedArea.criticality === 'ALTA';
            badgePriority = isHighCrit ? `<span class="badge badge-alta">PRIORIDAD ALTA</span>` : `<span class="badge badge-media">PRIORIDAD MEDIA</span>`;
            badgeStatus = `<span class="badge badge-autorizada">AUTORIZADA</span>`;
            textExplanation = `Han transcurrido <strong>${days} días</strong> (&ge; 3 meses). Área de criticidad ${currentSelectedArea.criticality}. Ciclado regular cumplido.`;
        }

        priorityPreviewContent.innerHTML = `
            <div style="display: flex; gap: 0.5rem; margin-bottom: 0.5rem;">${badgeStatus} ${badgePriority}</div>
            <p style="font-size: 0.85rem; color: var(--text-main);">${textExplanation}</p>
        `;
    }

    // Evidence Upload & Webcam
    evidenceFileInput.addEventListener('change', (e) => {
        const file = e.target.files[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (evt) => {
            const base64 = evt.target.result;
            const isPdf = file.type === 'application/pdf';

            uploadedEvidence = {
                fileName: file.name,
                fileType: isPdf ? 'PDF' : 'IMAGE',
                base64: base64
            };

            renderEvidencePreview();
        };
        reader.readAsDataURL(file);
    });

    btnOpenCamera.addEventListener('click', async () => {
        try {
            webcamStream = await navigator.mediaDevices.getUserMedia({ video: true });
            webcamVideo.srcObject = webcamStream;
            modalWebcam.classList.add('active');
        } catch (err) {
            alert('No se pudo acceder a la cámara del dispositivo: ' + err.message);
        }
    });

    btnCloseWebcam.addEventListener('click', closeWebcamModal);

    function closeWebcamModal() {
        if (webcamStream) {
            webcamStream.getTracks().forEach(track => track.stop());
            webcamStream = null;
        }
        modalWebcam.classList.remove('active');
    }

    btnCapturePhoto.addEventListener('click', () => {
        const context = webcamCanvas.getContext('2d');
        webcamCanvas.width = webcamVideo.videoWidth || 640;
        webcamCanvas.height = webcamVideo.videoHeight || 480;
        context.drawImage(webcamVideo, 0, 0, webcamCanvas.width, webcamCanvas.height);
        
        const base64 = webcamCanvas.toDataURL('image/png');
        uploadedEvidence = {
            fileName: `foto_evidencia_${Date.now()}.png`,
            fileType: 'WEBCAM_PHOTO',
            base64: base64
        };

        renderEvidencePreview();
        closeWebcamModal();
    });

    function renderEvidencePreview() {
        if (!uploadedEvidence) {
            evidencePreviewContainer.className = 'evidence-preview-box empty';
            evidencePreviewContainer.innerHTML = `<div class="empty-state"><i class="fa-solid fa-file-image"></i><span>Sin evidencia cargada.</span></div>`;
            return;
        }

        evidencePreviewContainer.className = 'evidence-preview-box';
        if (uploadedEvidence.fileType === 'PDF') {
            evidencePreviewContainer.innerHTML = `
                <div style="text-align:center; color: #ef4444;">
                    <i class="fa-solid fa-file-pdf" style="font-size: 3rem; margin-bottom: 0.4rem;"></i>
                    <div>Documento PDF Cargado: <strong>${uploadedEvidence.fileName}</strong></div>
                </div>
            `;
        } else {
            evidencePreviewContainer.innerHTML = `
                <div style="text-align:center;">
                    <img src="${uploadedEvidence.base64}" alt="Evidencia" />
                    <div style="font-size: 0.78rem; color: var(--text-muted); margin-top: 0.4rem;">${uploadedEvidence.fileName} (${uploadedEvidence.fileType})</div>
                </div>
            `;
        }
    }

    // Submit Request
    formCleaningRequest.addEventListener('submit', async (e) => {
        e.preventDefault();

        if (!currentSelectedArea || !selectedCoords) {
            alert('Debe hacer clic sobre el mapa para indicar la ubicación exacta de la limpieza.');
            return;
        }

        if (!uploadedEvidence) {
            alert('Es obligatorio adjuntar la evidencia (imagen, documento PDF o captura de fotografía).');
            return;
        }

        const dto = {
            areaId: currentSelectedArea.areaId,
            coordXPercent: selectedCoords.xPercent,
            coordYPercent: selectedCoords.yPercent,
            reason: document.getElementById('req-reason').value,
            detailedNotes: document.getElementById('req-notes').value,
            requestedBy: document.getElementById('req-user').value,
            evidenceFileName: uploadedEvidence.fileName,
            evidenceFileType: uploadedEvidence.fileType,
            evidenceBase64: uploadedEvidence.base64
        };

        try {
            const res = await fetch('/api/cleaning/requests', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });

            if (res.ok) {
                const data = await res.json();
                alert(`Solicitud ${data.request.id} creada.\nEstado: ${data.request.status}\nPrioridad: ${data.request.priority}`);
                
                // Reset form
                formCleaningRequest.reset();
                uploadedEvidence = null;
                renderEvidencePreview();
                selectedCoords = null;
                renderMapOverlay();
                updatePriorityPreview();

                await loadRequests();
                
                // Switch to History Tab
                document.querySelector('[data-tab="historial"]').click();
            } else {
                const errData = await res.json();
                alert('Error al enviar solicitud: ' + (errData.message || 'Error del servidor'));
            }
        } catch (err) {
            alert('Error de conexión con el servidor C#: ' + err.message);
        }
    });

    // Load Requests List
    async function loadRequests() {
        try {
            const res = await fetch('/api/cleaning/requests');
            if (res.ok) {
                currentRequests = await res.json();
                renderRequestsTable();
                requestBadge.textContent = currentRequests.length;
            }
        } catch (err) {
            console.error('Error fetching requests:', err);
        }
    }

    function getStatusBadgeHtml(status) {
        const st = (status || 'AUTORIZADA').toUpperCase();
        switch (st) {
            case 'LIMPIEZA_COMPLETADA':
            case 'REALIZADA':
                return `<span class="badge badge-limpieza_completada"><i class="fa-solid fa-check-double"></i> COMPLETADA</span>`;
            case 'EN_PROCESO':
                return `<span class="badge badge-en_proceso"><i class="fa-solid fa-spinner"></i> EN PROCESO</span>`;
            case 'CANCELADA':
                return `<span class="badge badge-cancelada"><i class="fa-solid fa-ban"></i> CANCELADA</span>`;
            case 'AUTORIZADA':
                return `<span class="badge badge-autorizada"><i class="fa-solid fa-circle-check"></i> AUTORIZADA</span>`;
            default:
                return `<span class="badge badge-denegada">${st}</span>`;
        }
    }

    function renderRequestsTable() {
        requestsTbody.innerHTML = '';

        if (currentRequests.length === 0) {
            requestsTbody.innerHTML = `<tr><td colspan="10" style="text-align:center; color:var(--text-dim);">No hay solicitudes registradas.</td></tr>`;
            return;
        }

        currentRequests.forEach(req => {
            const tr = document.createElement('tr');
            const dateStr = new Date(req.requestDate).toLocaleString('es-MX');
            const resStr = req.lastEsdResistanceOhms ? req.lastEsdResistanceOhms.toExponential(1).toUpperCase() + ' Ω' : 'N/A';

            let prioBadge = `<span class="badge badge-${req.priority.toLowerCase()}">${req.priority}</span>`;
            let statusBadge = getStatusBadgeHtml(req.status || req.authorizationStatus);

            tr.innerHTML = `
                <td><strong>${req.id}</strong></td>
                <td>${dateStr}</td>
                <td>${req.areaName}</td>
                <td>${req.reason}</td>
                <td>${req.daysSinceLastCleaning} días</td>
                <td>${resStr}</td>
                <td>${prioBadge}</td>
                <td>${statusBadge}</td>
                <td><a href="${req.evidenceUrl}" target="_blank" style="color:var(--primary); text-decoration:none;"><i class="fa-solid fa-file"></i> Ver (${req.evidenceFileType})</a></td>
                <td><button class="btn-secondary btn-sm-view" data-id="${req.id}"><i class="fa-solid fa-map-location-dot"></i> Ver en Mapa</button></td>
            `;

            requestsTbody.appendChild(tr);
        });

        // Add Click event for Detail Modal
        document.querySelectorAll('.btn-sm-view').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const reqId = btn.getAttribute('data-id');
                const req = currentRequests.find(r => r.id === reqId);
                if (req) showDetailModal(req);
            });
        });
    }

    // Detail Modal with Map Viewport & Status Action Buttons
    function showDetailModal(req) {
        const modal = document.getElementById('modal-detail');
        const body = document.getElementById('detail-modal-body');

        // Find floor map config for this area
        const mapConfig = currentMaps.find(m => m.areaId === req.areaId);
        const mapImageUrl = mapConfig ? mapConfig.imageUrl : '/uploads/smt_floor_plan.svg';

        const statusBadge = getStatusBadgeHtml(req.status || req.authorizationStatus);
        const completedInfo = req.completedDate 
            ? `<p style="color:#00f2fe; margin-top:0.3rem;"><i class="fa-solid fa-circle-check"></i> Limpiado el ${new Date(req.completedDate).toLocaleString('es-MX')} por ${req.cleanedBy || 'Personal ESD'}</p>`
            : '';

        body.innerHTML = `
            <!-- Floor Map Viewport for Cleaning Staff -->
            <div style="margin-bottom: 1.2rem;">
                <h4 style="color:var(--primary); margin-bottom: 0.5rem;">
                    <i class="fa-solid fa-location-dot"></i> Ubicación Exacta a Limpiar en Piso (${req.areaName}):
                </h4>
                <div class="modal-map-viewport">
                    <div class="modal-map-wrapper">
                        <img src="${mapImageUrl}" alt="Mapa de Área" />
                        <div class="modal-map-overlay">
                            <div class="reticle-marker" style="left: ${req.coordXPercent}%; top: ${req.coordYPercent}%;"></div>
                        </div>
                    </div>
                </div>
                <div style="font-size:0.82rem; color:var(--text-muted); text-align:center;">
                    <i class="fa-solid fa-crosshairs"></i> Coordenadas: X=${req.coordXPercent}%, Y=${req.coordYPercent}% | Punto Referencia: ${req.nearestPointId || 'P-01'}
                </div>
            </div>

            <div style="display:grid; grid-template-columns: 1fr 1fr; gap: 1.5rem;">
                <div>
                    <h4 style="color:var(--primary); margin-bottom: 0.5rem;">Identificación de la Solicitud</h4>
                    <p><strong>Folio:</strong> ${req.id}</p>
                    <p><strong>Estatus Actual:</strong> ${statusBadge}</p>
                    <p><strong>Área:</strong> ${req.areaName} (${req.areaId})</p>
                    <p><strong>Solicitante:</strong> ${req.requestedBy}</p>
                    <p><strong>Motivo:</strong> ${req.reason}</p>
                    <p><strong>Notas Solicitante:</strong> ${req.detailedNotes || 'Sin observaciones'}</p>
                    ${completedInfo}

                    <h4 style="color:var(--primary); margin-top: 1rem; margin-bottom: 0.5rem;">Criterios ANSI/ESD S20.20-2021</h4>
                    <p><strong>Criticidad Zona:</strong> ${req.areaCriticality}</p>
                    <p><strong>Días desde última limpieza:</strong> ${req.daysSinceLastCleaning} días</p>
                    <p><strong>Resistencia Medida:</strong> ${req.lastEsdResistanceOhms ? req.lastEsdResistanceOhms.toExponential(1).toUpperCase() + ' &Omega;' : 'N/A'}</p>
                </div>
                <div>
                    <h4 style="color:var(--primary); margin-bottom: 0.5rem;">Evidencia Adjunta</h4>
                    <div style="background:#000; padding:10px; border-radius:8px; text-align:center;">
                        ${req.evidenceFileType === 'PDF' 
                            ? `<iframe src="${req.evidenceUrl}" style="width:100%; height:220px;"></iframe>`
                            : `<img src="${req.evidenceUrl}" style="max-width:100%; max-height:220px; border-radius:6px;" />`}
                    </div>
                </div>
            </div>

            <div style="margin-top: 1rem; background: rgba(0, 242, 254, 0.08); padding: 0.8rem; border-radius: 8px; border: 1px solid var(--border-color);">
                <strong style="color: var(--primary);">Dictamen del Sistema:</strong>
                <p style="margin-top: 0.2rem; font-size: 0.85rem;">${req.evaluationSummary}</p>
            </div>

            <!-- Action Buttons Bar for Cleaning Personnel -->
            <div class="modal-actions-bar">
                <button type="button" class="btn-status-completed btn-action-status" data-status="LIMPIEZA_COMPLETADA">
                    <i class="fa-solid fa-circle-check"></i> Confirmar Limpieza Realizada
                </button>
                <button type="button" class="btn-status-in-progress btn-action-status" data-status="EN_PROCESO">
                    <i class="fa-solid fa-spinner"></i> Marcar En Proceso
                </button>
                <button type="button" class="btn-status-cancel btn-action-status" data-status="CANCELADA">
                    <i class="fa-solid fa-ban"></i> Cancelar Solicitud
                </button>
            </div>
        `;

        // Attach Click Listeners to Action Buttons
        body.querySelectorAll('.btn-action-status').forEach(btn => {
            btn.addEventListener('click', async () => {
                const newStatus = btn.getAttribute('data-status');
                let notes = '';
                let technician = 'Personal de Limpieza ESD';

                if (newStatus === 'CANCELADA') {
                    notes = prompt('Por favor ingrese el motivo de cancelación:', 'Limpieza duplicada o no requerida');
                    if (notes === null) return; // User cancelled prompt
                } else if (newStatus === 'LIMPIEZA_COMPLETADA') {
                    technician = prompt('Nombre del técnico que realizó la limpieza:', 'Técnico de Limpieza ESD') || 'Técnico ESD';
                    notes = prompt('Notas de finalización de limpieza (opcional):', 'Cera antiestática aplicada correctamente') || '';
                }

                await updateRequestStatus(req.id, newStatus, notes, technician);
            });
        });

        modal.classList.add('active');
    }

    async function updateRequestStatus(requestId, newStatus, notes, performedBy) {
        try {
            const dto = {
                newStatus: newStatus,
                notes: notes || '',
                performedBy: performedBy || 'Personal de Limpieza'
            };

            const res = await fetch(`/api/cleaning/requests/${requestId}/status`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });

            if (res.ok) {
                const data = await res.json();
                alert(`Estado de la solicitud actualizado a: ${data.request.status}`);
                
                document.getElementById('modal-detail').classList.remove('active');
                await loadRequests();
                await loadMaps(); // Refresh area clean dates
            } else {
                const err = await res.json();
                alert('Error al actualizar estado: ' + err.message);
            }
        } catch (err) {
            alert('Error de conexión: ' + err.message);
        }
    }

    // TAB 3: Ajustes & Settings
    async function loadSettings() {
        try {
            const res = await fetch('/api/cleaning/settings');
            if (res.ok) {
                const settings = await res.json();
                document.getElementById('cfg-sp-url').value = settings.url;
                document.getElementById('cfg-sp-key').value = settings.anonKey;
                document.getElementById('cfg-sp-table').value = settings.tableName;
                document.getElementById('cfg-min-days').value = settings.minimumCleaningIntervalDays;
            }
        } catch (err) {
            console.error('Error fetching settings:', err);
        }
    }

    document.getElementById('form-supabase-config').addEventListener('submit', async (e) => {
        e.preventDefault();
        const settings = {
            url: document.getElementById('cfg-sp-url').value,
            anonKey: document.getElementById('cfg-sp-key').value,
            tableName: document.getElementById('cfg-sp-table').value,
            minimumCleaningIntervalDays: parseInt(document.getElementById('cfg-min-days').value, 10),
            useMockFallback: false
        };

        try {
            const res = await fetch('/api/cleaning/settings', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(settings)
            });

            if (res.ok) {
                alert('Ajustes de Supabase y periodicidad guardados.');
            }
        } catch (err) {
            alert('Error guardando ajustes: ' + err.message);
        }
    });

    // Supabase Debug Button & Modal Listener
    const btnDebugSupabase = document.getElementById('btn-debug-supabase');
    if (btnDebugSupabase) {
        btnDebugSupabase.addEventListener('click', async () => {
            const areaId = editorSelectedArea ? editorSelectedArea.areaId : 'Cuarto 1';
            const modal = document.getElementById('modal-debug-supabase');
            const body = document.getElementById('debug-modal-body');

            body.innerHTML = `<div style="text-align:center; padding: 2rem;"><i class="fa-solid fa-spinner fa-spin" style="font-size: 2rem; color: var(--primary);"></i><p>Consultando Supabase tabla validacion_piso para '${areaId}'...</p></div>`;
            modal.classList.add('active');

            try {
                const res = await fetch(`/api/cleaning/debug/supabase?areaId=${encodeURIComponent(areaId)}`);
                if (res.ok) {
                    const data = await res.json();
                    
                    let rowsHtml = '';
                    let rawItems = [];
                    try {
                        if (data.rawJsonResponse && data.rawJsonResponse.startsWith('[')) {
                            rawItems = JSON.parse(data.rawJsonResponse);
                        }
                    } catch (e) {}

                    const itemsToRender = (rawItems.length > 0) ? rawItems : (data.measurements || []);

                    if (itemsToRender.length > 0) {
                        itemsToRender.forEach((m, idx) => {
                            const areaId = m.cuarto || m.area_id || m.areaId || 'Cuarto 1';
                            const pointId = m.punto !== undefined ? m.punto : (m.point_id || m.pointId || (idx + 1));
                            const pointName = m.point_name || m.pointName || `Punto ${pointId}`;
                            
                            let ohms = m.medicion_ohms !== undefined ? m.medicion_ohms : (m.resistance_ohms !== undefined ? m.resistance_ohms : m.resistanceOhms);
                            if (ohms !== null && ohms !== undefined) ohms = Number(ohms);
                            
                            const resStr = (ohms !== null && ohms !== undefined && !isNaN(ohms)) ? ohms.toExponential(2).toUpperCase() + ' Ω (' + ohms.toLocaleString() + ' Ω)' : 'N/A';
                            const isHigh = ohms > 1e8;
                            
                            const dateVal = m.fecha_medicion || m.fecha || m.created_at || m.measurement_date || m.measurementDate;
                            const dateStr = (dateVal && !isNaN(new Date(dateVal).getTime())) ? new Date(dateVal).toLocaleString('es-MX') : 'Reciente';
                            const auditor = m.auditor || m.inspector || 'Aldo Orozco';

                            rowsHtml += `
                                <tr>
                                    <td>${idx + 1}</td>
                                    <td><strong>${areaId}</strong></td>
                                    <td><span style="color:#00f2fe; font-weight:bold;">${pointId}</span></td>
                                    <td>${pointName}</td>
                                    <td style="color:${isHigh ? '#ef4444' : '#10b981'}; font-weight:bold;">${resStr}</td>
                                    <td>${auditor}</td>
                                    <td>${dateStr}</td>
                                </tr>
                            `;
                        });
                    } else {
                        rowsHtml = `<tr><td colspan="7" style="text-align:center; color: var(--text-muted);">No se encontraron filas filtradas para '${areaId}'.</td></tr>`;
                    }

                    const isRlsBlocked = data.rawJsonResponse === '[]';
                    const rlsNotice = isRlsBlocked ? `
                        <div style="background: rgba(239, 68, 68, 0.15); border: 1px solid #ef4444; padding: 0.8rem; border-radius: 8px; margin-bottom: 1rem; font-size: 0.85rem; color: #fca5a5;">
                            <strong><i class="fa-solid fa-triangle-exclamation"></i> Alerta de Seguridad RLS en Supabase:</strong><br/>
                            La API de Supabase devolvió HTTP ${data.httpStatusCode} con respuesta vacía <code>[]</code>.<br/>
                            Esto indica que **Row Level Security (RLS)** está habilitado en la tabla <code>${data.tableName}</code> sin una política de lectura pública.<br/><br/>
                            <strong>Solución en Supabase Dashboard:</strong><br/>
                            1. Ve a tu tabla <code>validacion_piso</code> en Supabase.<br/>
                            2. Haz clic arriba a la derecha en <strong>"+ Add RLS policy"</strong>.<br/>
                            3. Selecciona <em>"Enable read access for all users"</em> (o desactiva RLS temporalmente para esa tabla).
                        </div>
                    ` : '';

                    body.innerHTML = `
                        ${rlsNotice}
                        <div style="background: rgba(0,0,0,0.4); padding: 1rem; border-radius: 8px; margin-bottom: 1rem; font-size: 0.88rem;">
                            <p><strong>URL Supabase:</strong> ${data.supabaseUrl}</p>
                            <p><strong>Tabla Consultada:</strong> <code>${data.tableName}</code> (HTTP Status: ${data.httpStatusCode})</p>
                            <p><strong>Área Filtrada (cuarto):</strong> <span style="color:var(--primary); font-weight:bold;">${data.targetArea}</span></p>
                            <p><strong>Respuesta JSON Cruda de Supabase:</strong> <code>${data.rawJsonResponse}</code></p>
                            <p><strong>Total Registros Procesados:</strong> ${data.totalRowsFetched}</p>
                        </div>

                        <h4 style="color:var(--primary); margin-bottom:0.5rem;">Registros Leídos de <code>${data.tableName}</code>:</h4>
                        <table class="custom-table" style="font-size:0.85rem;">
                            <thead>
                                <tr>
                                    <th>#</th>
                                    <th>cuarto (Área)</th>
                                    <th>punto (Código)</th>
                                    <th>Nombre Punto</th>
                                    <th>medicion_ohms</th>
                                    <th>Auditor</th>
                                    <th>Fecha</th>
                                </tr>
                            </thead>
                            <tbody>${rowsHtml}</tbody>
                        </table>
                    `;
                } else {
                    body.innerHTML = `<div style="color:#ef4444; padding:1rem;">Error consultando endpoint de diagnóstico.</div>`;
                }
            } catch (err) {
                body.innerHTML = `<div style="color:#ef4444; padding:1rem;">Error de conexión: ${err.message}</div>`;
            }
        });
    }

    const btnCloseDebugModal = document.getElementById('btn-close-debug-modal');
    if (btnCloseDebugModal) {
        btnCloseDebugModal.addEventListener('click', () => {
            document.getElementById('modal-debug-supabase').classList.remove('active');
        });
    }

    // Upload New Area Map (Ajustes)
    document.getElementById('form-upload-map').addEventListener('submit', (e) => {
        e.preventDefault();
        const areaId = document.getElementById('cfg-area-id').value.trim();
        const areaName = document.getElementById('cfg-area-name').value.trim();
        const criticality = document.getElementById('cfg-area-criticality').value;
        const fileInput = document.getElementById('cfg-map-file');
        
        if (!fileInput.files[0]) return;

        const reader = new FileReader();
        reader.onload = async (evt) => {
            const dto = {
                areaId: areaId,
                areaName: areaName,
                criticality: criticality,
                fileName: fileInput.files[0].name,
                imageBase64: evt.target.result
            };

            const res = await fetch('/api/cleaning/maps/upload', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });

            if (res.ok) {
                alert(`Plano cargado correctamente para el área ${areaId}.`);
                document.getElementById('form-upload-map').reset();
                await loadMaps();
                selectEditorArea(areaId);
            }
        };
        reader.readAsDataURL(fileInput.files[0]);
    });

    // Render Areas Management Table (Ajustes Card 4)
    function renderAreasManagementTable() {
        const tbody = document.getElementById('areas-management-tbody');
        if (!tbody) return;

        tbody.innerHTML = '';

        if (!currentMaps || currentMaps.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align:center; color:var(--text-dim);">No hay áreas registradas.</td></tr>`;
            return;
        }

        currentMaps.forEach(map => {
            if (!map) return;
            const tr = document.createElement('tr');
            const areaId = map.areaId || 'N/A';
            const areaName = map.areaName || areaId;
            const criticalityStr = map.criticality || 'MEDIA';
            const pointsCount = (map.points && Array.isArray(map.points)) ? map.points.length : 0;
            const imgUrl = map.imageUrl || '/uploads/smt_floor_plan.svg';
            const critBadge = `<span class="badge badge-${criticalityStr.toLowerCase()}">${criticalityStr}</span>`;

            tr.innerHTML = `
                <td><strong>${areaId}</strong></td>
                <td>${areaName}</td>
                <td>${critBadge}</td>
                <td><span style="color:#00f2fe; font-weight:bold;">${pointsCount}</span> puntos</td>
                <td><img src="${imgUrl}" alt="${areaId}" style="width:60px; height:40px; object-fit:cover; border-radius:4px; border:1px solid var(--border-color);" /></td>
                <td>
                    <button type="button" class="btn-secondary btn-sm-edit-area" data-id="${areaId}"><i class="fa-solid fa-pen"></i> Editar</button>
                    <button type="button" class="btn-secondary btn-sm-del-area" data-id="${areaId}" style="border-color:#ef4444; color:#ef4444;"><i class="fa-solid fa-trash"></i> Eliminar</button>
                </td>
            `;

            tbody.appendChild(tr);
        });

        // Edit Area Listener
        tbody.querySelectorAll('.btn-sm-edit-area').forEach(btn => {
            btn.addEventListener('click', () => {
                const areaId = btn.getAttribute('data-id');
                const map = currentMaps.find(m => m.areaId === areaId);
                if (map) showEditAreaModal(map);
            });
        });

        // Delete Area Listener
        tbody.querySelectorAll('.btn-sm-del-area').forEach(btn => {
            btn.addEventListener('click', async () => {
                const areaId = btn.getAttribute('data-id');
                if (confirm(`¿Está seguro de eliminar el área '${areaId}' y su configuración?`)) {
                    await deleteArea(areaId);
                }
            });
        });
    }

    function showEditAreaModal(map) {
        document.getElementById('edit-area-id-hidden').value = map.areaId;
        document.getElementById('edit-area-id').value = map.areaId;
        document.getElementById('edit-area-name').value = map.areaName;
        document.getElementById('edit-area-criticality').value = map.criticality;
        document.getElementById('modal-edit-area').classList.add('active');
    }

    const btnCloseEditArea = document.getElementById('btn-close-edit-area');
    if (btnCloseEditArea) {
        btnCloseEditArea.addEventListener('click', () => {
            document.getElementById('modal-edit-area').classList.remove('active');
        });
    }

    const formEditArea = document.getElementById('form-edit-area');
    if (formEditArea) {
        formEditArea.addEventListener('submit', async (e) => {
            e.preventDefault();
            const areaId = document.getElementById('edit-area-id-hidden').value;
            const areaName = document.getElementById('edit-area-name').value.trim();
            const criticality = document.getElementById('edit-area-criticality').value;
            const fileInput = document.getElementById('edit-area-file');

            const processSave = async (imageBase64 = '') => {
                const dto = {
                    areaId: areaId,
                    areaName: areaName,
                    criticality: criticality,
                    imageBase64: imageBase64,
                    fileName: fileInput.files[0] ? fileInput.files[0].name : ''
                };

                const res = await fetch(`/api/cleaning/maps/${areaId}`, {
                    method: 'PUT',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(dto)
                });

                if (res.ok) {
                    alert(`Área '${areaId}' actualizada correctamente.`);
                    document.getElementById('modal-edit-area').classList.remove('active');
                    await loadMaps();
                } else {
                    alert('Error al actualizar el área.');
                }
            };

            if (fileInput.files[0]) {
                const reader = new FileReader();
                reader.onload = (evt) => processSave(evt.target.result);
                reader.readAsDataURL(fileInput.files[0]);
            } else {
                await processSave();
            }
        });
    }

    async function deleteArea(areaId) {
        try {
            const res = await fetch(`/api/cleaning/maps/${areaId}`, {
                method: 'DELETE'
            });

            if (res.ok) {
                alert(`Área '${areaId}' eliminada correctamente.`);
                await loadMaps();
            } else {
                alert(`Error al eliminar el área '${areaId}'.`);
            }
        } catch (err) {
            alert('Error de conexión: ' + err.message);
        }
    }

    // Editor Map Coordinates (Dedicated State for Selected Area)
    const editorMapImg = document.getElementById('editor-map-img');
    const editorMapOverlay = document.getElementById('editor-map-overlay');
    const pointsTbody = document.getElementById('points-tbody');

    function renderEditorMap() {
        if (!editorSelectedArea) {
            if (currentMaps.length > 0) editorSelectedArea = currentMaps[0];
            else return;
        }
        editorMapImg.src = editorSelectedArea.imageUrl;
        renderEditorPoints();
    }

    function renderEditorPoints() {
        editorMapOverlay.innerHTML = '';
        pointsTbody.innerHTML = '';

        if (!editorSelectedArea || !editorSelectedArea.points) return;

        editorSelectedArea.points.forEach((pt, index) => {
            // Render Pin on Editor
            const pin = document.createElement('div');
            pin.className = 'map-pin';
            pin.style.left = `${pt.xPercent}%`;
            pin.style.top = `${pt.yPercent}%`;
            pin.innerHTML = `<div class="pin-head" style="background:#00f2fe;">${index + 1}</div>`;
            editorMapOverlay.appendChild(pin);

            // Render Table Row
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td><input type="text" class="custom-input pt-code" value="${pt.code}" /></td>
                <td><input type="text" class="custom-input pt-label" value="${pt.label}" /></td>
                <td>${pt.xPercent}%</td>
                <td>${pt.yPercent}%</td>
                <td><input type="number" step="1e7" class="custom-input pt-res" value="${pt.lastResistanceOhms}" /></td>
                <td><button type="button" class="btn-secondary btn-del-pt" data-index="${index}"><i class="fa-solid fa-trash"></i></button></td>
            `;
            pointsTbody.appendChild(tr);
        });

        document.querySelectorAll('.btn-del-pt').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const idx = parseInt(btn.getAttribute('data-index'), 10);
                editorSelectedArea.points.splice(idx, 1);
                renderEditorPoints();
            });
        });
    }

    document.getElementById('editor-map-wrapper').addEventListener('click', (e) => {
        if (!editorSelectedArea) return;

        const rect = e.currentTarget.getBoundingClientRect();
        const xPercent = parseFloat((((e.clientX - rect.left) / rect.width) * 100).toFixed(2));
        const yPercent = parseFloat((((e.clientY - rect.top) / rect.height) * 100).toFixed(2));

        const count = (editorSelectedArea.points ? editorSelectedArea.points.length : 0) + 1;
        const newPt = {
            id: `${count}`,
            code: `${count}`,
            label: `Punto de Medición ${count}`,
            xPercent: xPercent,
            yPercent: yPercent,
            lastResistanceOhms: 4.5e7
        };

        if (!editorSelectedArea.points) editorSelectedArea.points = [];
        editorSelectedArea.points.push(newPt);

        renderEditorPoints();
    });

    document.getElementById('btn-save-points').addEventListener('click', async () => {
        if (!editorSelectedArea) return;

        // Read table inputs
        const rows = pointsTbody.querySelectorAll('tr');
        rows.forEach((tr, idx) => {
            if (editorSelectedArea.points[idx]) {
                editorSelectedArea.points[idx].code = tr.querySelector('.pt-code').value;
                editorSelectedArea.points[idx].label = tr.querySelector('.pt-label').value;
                editorSelectedArea.points[idx].lastResistanceOhms = parseFloat(tr.querySelector('.pt-res').value);
            }
        });

        const dto = {
            areaId: editorSelectedArea.areaId,
            criticality: editorSelectedArea.criticality,
            points: editorSelectedArea.points
        };

        const res = await fetch('/api/cleaning/maps/points', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });

        if (res.ok) {
            alert(`Coordenadas de puntos ESD para el área '${editorSelectedArea.areaId}' guardadas y sincronizadas con Supabase.`);
            await loadMaps();
        }
    });

    // Helper: calculate days difference
    function calculateDays(dateStr) {
        if (!dateStr) return 100;
        const diffMs = new Date() - new Date(dateStr);
        return Math.max(0, Math.floor(diffMs / (1000 * 60 * 60 * 24)));
    }
});

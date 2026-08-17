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

    // User Authentication Session State
    let currentUserSession = JSON.parse(localStorage.getItem('esd_user_session')) || {
        role: 'GUEST',
        username: 'Invitado',
        displayName: 'Usuario no registrado'
    };

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

    async function initApp() {
        setupNavigation();
        setupEventListeners();
        setupAuthListeners();
        updateRoleBasedUI();
        await loadMaps();
        await loadRequests();
        await loadSettings();
    }

    function updateRoleBasedUI() {
        const unauthContainer = document.getElementById('unauth-header-container');
        const authContainer = document.getElementById('auth-header-container');
        const userDispName = document.getElementById('user-display-name');
        const userRoleBadge = document.getElementById('user-role-badge');

        const role = currentUserSession.role || 'GUEST';

        if (role === 'GUEST') {
            if (unauthContainer) unauthContainer.classList.remove('hidden');
            if (authContainer) authContainer.classList.add('hidden');
        } else {
            if (unauthContainer) unauthContainer.classList.add('hidden');
            if (authContainer) authContainer.classList.remove('hidden');
            if (userDispName) userDispName.textContent = currentUserSession.displayName || currentUserSession.username;
            if (userRoleBadge) {
                userRoleBadge.textContent = role === 'ADMIN' ? 'SUPERVISOR' : 'TÉCNICO';
                userRoleBadge.style.borderColor = role === 'ADMIN' ? '#00f2fe' : '#10b981';
                userRoleBadge.style.color = role === 'ADMIN' ? '#00f2fe' : '#6ee7b7';
            }
        }

        // Filter Sidebar Navigation Buttons
        const navBtns = document.querySelectorAll('.nav-btn');
        navBtns.forEach(btn => {
            const targetTab = btn.getAttribute('data-tab');

            let isAllowed = false;
            if (targetTab === 'solicitud') isAllowed = true;
            else if (targetTab === 'historial' || targetTab === 'limpieza') {
                isAllowed = (role === 'TECHNICIAN' || role === 'ADMIN');
            } else if (targetTab === 'ajustes') {
                isAllowed = (role === 'ADMIN');
            }

            btn.style.display = isAllowed ? 'flex' : 'none';
        });

        // If active tab is forbidden for current role, auto-switch to 'solicitud'
        const activeNavBtn = document.querySelector('.nav-btn.active');
        if (activeNavBtn && activeNavBtn.style.display === 'none') {
            const defaultBtn = document.querySelector('.nav-btn[data-tab="solicitud"]');
            if (defaultBtn) defaultBtn.click();
        }
    }

    // Navigation Tabs Handler
    function setupNavigation() {
        const navBtns = document.querySelectorAll('.nav-btn');
        const tabContents = document.querySelectorAll('.tab-content');

        navBtns.forEach(btn => {
            btn.addEventListener('click', (e) => {
                const navTarget = e.currentTarget || btn;
                const targetTab = navTarget.getAttribute('data-tab');
                if (!targetTab) return;

                navBtns.forEach(b => b.classList.remove('active'));
                tabContents.forEach(t => t.classList.remove('active'));

                navTarget.classList.add('active');
                const targetSection = document.getElementById(`tab-${targetTab}`);
                if (targetSection) targetSection.classList.add('active');

                if (targetTab === 'historial') {
                    loadRequests();
                    loadCleaningHistory();
                }

                if (targetTab === 'ajustes') {
                    renderEditorMap();
                    renderAreasManagementTable();
                    loadUserAccounts();
                }

                if (targetTab === 'limpieza') {
                    if (cleanerAreaSelect) {
                        const targetArea = cleanerAreaSelect.value || (currentMaps[0] ? currentMaps[0].areaId : '');
                        selectCleanerArea(targetArea);
                    }
                    renderAuthorizedRequestsQueue();
                }
            });
        });
    }

    function setupEventListeners() {
        // Subtab Handlers for Historial & Solicitudes
        const btnSubtabRequests = document.getElementById('btn-subtab-requests');
        const btnSubtabHistory = document.getElementById('btn-subtab-history');
        const viewSubtabRequests = document.getElementById('subtab-requests-view');
        const viewSubtabHistory = document.getElementById('subtab-history-view');

        if (btnSubtabRequests && btnSubtabHistory) {
            btnSubtabRequests.addEventListener('click', () => {
                btnSubtabRequests.className = 'btn-primary btn-sm active';
                btnSubtabHistory.className = 'btn-secondary btn-sm';
                if (viewSubtabRequests) viewSubtabRequests.classList.remove('hidden');
                if (viewSubtabHistory) viewSubtabHistory.classList.add('hidden');
            });

            btnSubtabHistory.addEventListener('click', () => {
                btnSubtabHistory.className = 'btn-primary btn-sm active';
                btnSubtabRequests.className = 'btn-secondary btn-sm';
                if (viewSubtabHistory) viewSubtabHistory.classList.remove('hidden');
                if (viewSubtabRequests) viewSubtabRequests.classList.add('hidden');
                loadCleaningHistory();
            });
        }

        // Filters Event Listeners for Requests
        const reqFilterIds = ['filter-req-status', 'filter-req-area', 'filter-req-date-from', 'filter-req-date-to'];
        reqFilterIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.addEventListener('change', renderRequestsTable);
        });

        const btnClearReqFilters = document.getElementById('btn-clear-req-filters');
        if (btnClearReqFilters) {
            btnClearReqFilters.addEventListener('click', () => {
                if (document.getElementById('filter-req-status')) document.getElementById('filter-req-status').value = 'TODOS';
                if (document.getElementById('filter-req-area')) document.getElementById('filter-req-area').value = 'TODOS';
                if (document.getElementById('filter-req-date-from')) document.getElementById('filter-req-date-from').value = '';
                if (document.getElementById('filter-req-date-to')) document.getElementById('filter-req-date-to').value = '';
                renderRequestsTable();
            });
        }

        // Filters Event Listeners for Cleaning History
        const histFilterIds = ['filter-hist-area', 'filter-hist-date-from', 'filter-hist-date-to'];
        histFilterIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.addEventListener('change', renderHistoryTable);
        });

        const btnClearHistFilters = document.getElementById('btn-clear-hist-filters');
        if (btnClearHistFilters) {
            btnClearHistFilters.addEventListener('click', () => {
                if (document.getElementById('filter-hist-area')) document.getElementById('filter-hist-area').value = 'TODOS';
                if (document.getElementById('filter-hist-date-from')) document.getElementById('filter-hist-date-from').value = '';
                if (document.getElementById('filter-hist-date-to')) document.getElementById('filter-hist-date-to').value = '';
                renderHistoryTable();
            });
        }

        areaSelect.addEventListener('change', (e) => {
            selectArea(e.target.value);
        });

        if (editorAreaSelect) {
            editorAreaSelect.addEventListener('change', (e) => {
                selectEditorArea(e.target.value);
            });
        }

        if (cleanerAreaSelect) {
            cleanerAreaSelect.addEventListener('change', (e) => {
                selectCleanerArea(e.target.value);
            });
        }

        if (btnClearDrawing) {
            btnClearDrawing.addEventListener('click', () => {
                drawnBoxCoords = null;
                if (cleanerBoxText) cleanerBoxText.innerHTML = '<i class="fa-solid fa-vector-square"></i> <span>Ningún recuadro dibujado (Haz clic y arrastra en el mapa)</span>';
                renderCleanerMapOverlay();
            });
        }

        document.getElementById('btn-refresh-requests').addEventListener('click', () => {
            loadRequests();
            loadCleaningHistory();
        });
        document.getElementById('btn-close-detail').addEventListener('click', () => {
            document.getElementById('modal-detail').classList.remove('active');
        });

        // Floor Condition Guide Cards Listener
        const cardStained = document.getElementById('card-guide-stained');
        const cardScratched = document.getElementById('card-guide-scratched');
        const selectReasonEl = document.getElementById('req-reason');

        function syncGuideCardHighlights(selectedVal) {
            if (cardStained) cardStained.classList.toggle('active-guide', selectedVal === 'Piso manchado');
            if (cardScratched) cardScratched.classList.toggle('active-guide', selectedVal === 'Piso con rayones');
        }

        if (cardStained) {
            cardStained.addEventListener('click', () => {
                if (selectReasonEl) {
                    selectReasonEl.value = 'Piso manchado';
                    syncGuideCardHighlights('Piso manchado');
                }
            });
        }

        if (cardScratched) {
            cardScratched.addEventListener('click', () => {
                if (selectReasonEl) {
                    selectReasonEl.value = 'Piso con rayones';
                    syncGuideCardHighlights('Piso con rayones');
                }
            });
        }

        if (selectReasonEl) {
            selectReasonEl.addEventListener('change', (e) => {
                syncGuideCardHighlights(e.target.value);
            });
        }
    }

    function setupAuthListeners() {
        const modalLogin = document.getElementById('modal-login');
        const btnOpenLogin = document.getElementById('btn-open-login');
        const btnCloseLogin = document.getElementById('btn-close-login');
        const btnLogout = document.getElementById('btn-logout');
        const formLogin = document.getElementById('form-login');
        const btnQuickTech = document.getElementById('btn-quick-tech');
        const btnQuickAdmin = document.getElementById('btn-quick-admin');

        if (btnOpenLogin && modalLogin) {
            btnOpenLogin.addEventListener('click', () => {
                modalLogin.classList.add('active');
            });
        }

        if (btnCloseLogin && modalLogin) {
            btnCloseLogin.addEventListener('click', () => {
                modalLogin.classList.remove('active');
            });
        }

        if (btnLogout) {
            btnLogout.addEventListener('click', () => {
                currentUserSession = {
                    role: 'GUEST',
                    username: 'Invitado',
                    displayName: 'Usuario no registrado'
                };
                localStorage.removeItem('esd_user_session');
                updateRoleBasedUI();
                alert('Has cerrado sesión correctamente.');
            });
        }

        function loginUser(role, username, displayName) {
            currentUserSession = { role, username, displayName };
            localStorage.setItem('esd_user_session', JSON.stringify(currentUserSession));
            if (modalLogin) modalLogin.classList.remove('active');
            updateRoleBasedUI();
            alert(`¡Bienvenido ${displayName}! Inició sesión con rol: ${role === 'ADMIN' ? 'SUPERVISOR ADMIN' : 'TÉCNICO ESD'}`);
        }

        if (formLogin) {
            formLogin.addEventListener('submit', async (e) => {
                e.preventDefault();
                const username = document.getElementById('input-username').value.trim();
                const password = document.getElementById('input-password').value.trim();

                try {
                    const res = await fetch('/api/auth/login', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ username, password })
                    });

                    if (res.ok) {
                        const data = await res.json();
                        loginUser(data.user.role, data.user.username, data.user.displayName);
                        formLogin.reset();
                    } else {
                        const err = await res.json();
                        alert('Error al iniciar sesión: ' + (err.message || 'Usuario o contraseña incorrectos.'));
                    }
                } catch (err) {
                    // Fallback login if backend offline
                    const uLower = username.toLowerCase();
                    if (uLower.includes('admin') || uLower.includes('super')) {
                        loginUser('ADMIN', username, `Ing. ${username} (Supervisor)`);
                    } else {
                        loginUser('TECHNICIAN', username, `Téc. ${username}`);
                    }
                }
            });
        }

        if (btnQuickTech) {
            btnQuickTech.addEventListener('click', () => {
                loginUser('TECHNICIAN', 'tecnico', 'Téc. Mantenimiento ESD');
            });
        }

        if (btnQuickAdmin) {
            btnQuickAdmin.addEventListener('click', () => {
                loginUser('ADMIN', 'admin', 'Ing. Aldo Orozco (Admin)');
            });
        }
    }

    // User Accounts Management (Tab 4 - Admin Only)
    let currentUserAccounts = [];

    async function loadUserAccounts() {
        if (currentUserSession.role !== 'ADMIN') return;
        try {
            const res = await fetch('/api/auth/users');
            if (res.ok) {
                currentUserAccounts = await res.json();
                renderUsersManagementTable();
            }
        } catch (err) {
            Console.error('Error fetching users:', err);
        }
    }

    function renderUsersManagementTable() {
        const tbody = document.getElementById('users-management-tbody');
        if (!tbody) return;

        tbody.innerHTML = '';

        if (!currentUserAccounts || currentUserAccounts.length === 0) {
            tbody.innerHTML = `<tr><td colspan="4" style="text-align:center; color:var(--text-dim);">No hay usuarios registrados.</td></tr>`;
            return;
        }

        currentUserAccounts.forEach(u => {
            const tr = document.createElement('tr');
            const roleBadge = u.role === 'ADMIN' 
                ? `<span class="badge badge-alta" style="border-color:#00f2fe; color:#00f2fe;">SUPERVISOR ADMIN</span>`
                : `<span class="badge badge-autorizada">TÉCNICO</span>`;

            const isSelf = u.username.toLowerCase() === currentUserSession.username.toLowerCase();

            tr.innerHTML = `
                <td><strong>${u.username}</strong></td>
                <td>${u.displayName}</td>
                <td>${roleBadge}</td>
                <td>
                    ${isSelf ? '<span style="font-size:0.75rem; color:var(--text-dim);">(Sesión Actual)</span>' : `<button type="button" class="btn-secondary btn-sm-del-user" data-username="${u.username}" style="border-color:#ef4444; color:#ef4444;"><i class="fa-solid fa-trash"></i> Eliminar</button>`}
                </td>
            `;

            tbody.appendChild(tr);
        });

        tbody.querySelectorAll('.btn-sm-del-user').forEach(btn => {
            btn.addEventListener('click', async () => {
                const uname = btn.getAttribute('data-username');
                if (confirm(`¿Está seguro de eliminar la cuenta de usuario '${uname}'?`)) {
                    await deleteUserAccount(uname);
                }
            });
        });
    }

    const formCreateUser = document.getElementById('form-create-user');
    if (formCreateUser) {
        formCreateUser.addEventListener('submit', async (e) => {
            e.preventDefault();
            const username = document.getElementById('new-user-username').value.trim();
            const displayName = document.getElementById('new-user-fullname').value.trim();
            const password = document.getElementById('new-user-password').value.trim();
            const role = document.getElementById('new-user-role').value;

            try {
                const res = await fetch('/api/auth/users', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ username, displayName, password, role })
                });

                if (res.ok) {
                    const data = await res.json();
                    alert(`¡Usuario '${username}' registrado con éxito en la Base de Datos!`);
                    formCreateUser.reset();
                    await loadUserAccounts();
                } else {
                    const err = await res.json();
                    alert('Error al registrar usuario: ' + (err.message || 'Error del servidor'));
                }
            } catch (err) {
                alert('Error de conexión: ' + err.message);
            }
        });
    }

    async function deleteUserAccount(username) {
        try {
            const res = await fetch(`/api/auth/users/${encodeURIComponent(username)}`, {
                method: 'DELETE'
            });

            if (res.ok) {
                alert(`Usuario '${username}' eliminado correctamente.`);
                await loadUserAccounts();
            } else {
                alert('Error al eliminar usuario.');
            }
        } catch (err) {
            alert('Error de conexión: ' + err.message);
        }
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
        areaSelect.innerHTML = '<option value="">-- Seleccionar Área de Planta --</option>';
        if (editorAreaSelect) editorAreaSelect.innerHTML = '';
        if (cleanerAreaSelect) cleanerAreaSelect.innerHTML = '';

        const filterReqArea = document.getElementById('filter-req-area');
        const filterHistArea = document.getElementById('filter-hist-area');
        if (filterReqArea) filterReqArea.innerHTML = '<option value="TODOS">-- Todas las Áreas --</option>';
        if (filterHistArea) filterHistArea.innerHTML = '<option value="TODOS">-- Todas las Áreas --</option>';

        currentMaps.forEach(map => {
            const opt1 = document.createElement('option');
            opt1.value = map.areaId;
            opt1.textContent = `${map.areaId} - ${map.areaName}`;
            areaSelect.appendChild(opt1);

            if (filterReqArea) {
                const optR = document.createElement('option');
                optR.value = map.areaId;
                optR.textContent = `${map.areaId} - ${map.areaName}`;
                filterReqArea.appendChild(optR);
            }

            if (filterHistArea) {
                const optH = document.createElement('option');
                optH.value = map.areaId;
                optH.textContent = `${map.areaId} - ${map.areaName}`;
                filterHistArea.appendChild(optH);
            }

            if (editorAreaSelect) {
                const opt2 = document.createElement('option');
                opt2.value = map.areaId;
                opt2.textContent = `${map.areaId} - ${map.areaName} (${map.criticality})`;
                editorAreaSelect.appendChild(opt2);
            }

            if (cleanerAreaSelect) {
                const opt3 = document.createElement('option');
                opt3.value = map.areaId;
                opt3.textContent = `${map.areaId} - ${map.areaName}`;
                cleanerAreaSelect.appendChild(opt3);
            }
        });

        if (currentMaps.length > 0) {
            const currentEditorValue = editorAreaSelect ? editorAreaSelect.value : null;
            if (currentEditorValue && currentMaps.some(m => m.areaId === currentEditorValue)) {
                selectEditorArea(currentEditorValue);
            } else {
                selectEditorArea(currentMaps[0].areaId);
            }

            if (cleanerAreaSelect && !cleanerSelectedArea) {
                selectCleanerArea(currentMaps[0].areaId);
            }
        }

        renderAreasManagementTable();
        updateStepFlow();
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
        updateStepFlow();
    }

    function selectEditorArea(areaId) {
        editorSelectedArea = currentMaps.find(m => m.areaId === areaId);
        if (editorAreaSelect && editorSelectedArea) {
            editorAreaSelect.value = areaId;
        }
        renderEditorMap();
    }

    function extractRectangleCoords(notes) {
        if (!notes) return null;
        const match = notes.match(/X[:=]\s*([0-9.]+)\s*%?,?\s*Y[:=]\s*([0-9.]+)\s*%?,?\s*(?:Ancho|W)[:=]\s*([0-9.]+)\s*%?,?\s*(?:Alto|H)[:=]\s*([0-9.]+)\s*%/i);
        if (match) {
            return {
                xPercent: parseFloat(match[1]),
                yPercent: parseFloat(match[2]),
                widthPercent: parseFloat(match[3]),
                heightPercent: parseFloat(match[4])
            };
        }
        return null;
    }

    async function renderMapOverlay() {
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

        // Fetch & Render ALL Cleaned Zones for this room as reference
        try {
            const res = await fetch(`/api/cleaning/zones/${currentSelectedArea.areaId}`);
            if (res.ok) {
                const zones = await res.json();
                if (zones && zones.length > 0) {
                    zones.forEach(zone => {
                        let x = zone.xPercent;
                        let y = zone.yPercent;
                        let w = zone.widthPercent;
                        let h = zone.heightPercent;

                        if ((!w || w <= 0) && zone.notes) {
                            const ext = extractRectangleCoords(zone.notes);
                            if (ext) {
                                x = ext.xPercent;
                                y = ext.yPercent;
                                w = ext.widthPercent;
                                h = ext.heightPercent;
                            }
                        }

                        if (w && w > 0) {
                            const rect = document.createElement('div');
                            rect.className = 'cleaned-zone-rect';
                            rect.style.left = `${x}%`;
                            rect.style.top = `${y}%`;
                            rect.style.width = `${w}%`;
                            rect.style.height = `${h}%`;
                            rect.style.borderColor = '#10b981';
                            rect.style.background = 'rgba(16, 185, 129, 0.18)';
                            rect.style.pointerEvents = 'none';

                            const dateStr = new Date(zone.cleanedDate).toLocaleDateString('es-MX');
                            rect.innerHTML = `
                                <div class="zone-label-badge" style="background:#10b981; color:#000;">
                                    <i class="fa-solid fa-broom"></i> Limpiado: ${dateStr}
                                </div>
                            `;
                            mapOverlay.appendChild(rect);
                        }
                    });
                }
            }
        } catch (err) {
            console.error('Error loading cleaned zones for map overlay:', err);
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
        updateStepFlow();
    });

    // Priority Live Preview Engine
    async function updatePriorityPreview() {
        if (!currentSelectedArea || !selectedCoords) {
            priorityPreviewContent.innerHTML = `<span style="color: var(--text-dim);">Haz clic sobre el plano para evaluar las condiciones de resistencia ESD y regla de 3 meses.</span>`;
            return;
        }

        // Fetch Cleaned Zones for current area to check against selectedCoords (with 2% margin)
        let matchingZone = null;
        try {
            const res = await fetch(`/api/cleaning/zones/${currentSelectedArea.areaId}`);
            if (res.ok) {
                const zones = await res.json();
                if (zones && zones.length > 0) {
                    const margin = 2.0; // 2% proximity margin
                    for (const z of zones) {
                        let x = z.xPercent;
                        let y = z.yPercent;
                        let w = z.widthPercent;
                        let h = z.heightPercent;

                        if ((!w || w <= 0) && z.notes) {
                            const ext = extractRectangleCoords(z.notes);
                            if (ext) {
                                x = ext.xPercent;
                                y = ext.yPercent;
                                w = ext.widthPercent;
                                h = ext.heightPercent;
                            }
                        }

                        if (w && w > 0) {
                            const minX = x - margin;
                            const maxX = x + w + margin;
                            const minY = y - margin;
                            const maxY = y + h + margin;

                            if (selectedCoords.xPercent >= minX && selectedCoords.xPercent <= maxX &&
                                selectedCoords.yPercent >= minY && selectedCoords.yPercent <= maxY) {
                                matchingZone = { ...z, xPercent: x, yPercent: y, widthPercent: w, heightPercent: h };
                                break;
                            }
                        }
                    }
                }
            }
        } catch (err) {
            console.error('Error checking matching zone:', err);
        }

        const lastCleanDate = matchingZone ? matchingZone.cleanedDate : currentSelectedArea.lastCleaningDate;
        const days = calculateDays(lastCleanDate);
        
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
        const isHighCrit = currentSelectedArea.criticality === 'ALTA';
        const meets3Months = days >= 90;
        const meets2MonthsHighCrit = isHighCrit && days > 60 && resistance > 1e6;

        let badgePriority = '';
        let badgeStatus = '';
        let textExplanation = '';
        let zoneMatchInfo = matchingZone 
            ? `<p style="font-size:0.83rem; color:#10b981; margin-bottom:0.4rem;"><i class="fa-solid fa-vector-square"></i> <strong>Recuadro Detectado en Coordenada:</strong> Limpiado el ${new Date(matchingZone.cleanedDate).toLocaleDateString('es-MX')} (${days} días transcurridos).</p>`
            : `<p style="font-size:0.83rem; color:var(--text-muted); margin-bottom:0.4rem;"><i class="fa-solid fa-circle-info"></i> <strong>Sin recuadro reciente en estas coordenadas.</strong> (Evaluado con fecha base del área: ${days} días).</p>`;

        if (isHighRes) {
            badgePriority = `<span class="badge badge-alta">PRIORIDAD ALTA</span>`;
            badgeStatus = `<span class="badge badge-autorizada">AUTORIZADA</span>`;
            textExplanation = `¡Atención! La resistencia ESD en ${nearestPt ? nearestPt.code : 'zona'} es de <strong>${resistance.toExponential(1).toUpperCase()} &Omega; (> 1.0e8 &Omega;)</strong>. Excede el límite máximo permitido por ANSI/ESD S20.20-2021. Se autoriza renovación inmediata de cera sin importar la fecha previa.`;
        } else if (meets2MonthsHighCrit) {
            badgePriority = `<span class="badge badge-alta">PRIORIDAD ALTA</span>`;
            badgeStatus = `<span class="badge badge-autorizada">AUTORIZADA</span>`;
            textExplanation = `Excepción por Criticidad ALTA: Han transcurrido <strong>${days} días (> 2 meses)</strong> en un recuadro de criticidad ALTA con resistencia (${resistance.toExponential(1).toUpperCase()} &Omega; > 1.0e6 &Omega;). Pre-aprobada para proteger componentes sensibles.`;
        } else if (matchingZone && !meets3Months) {
            badgePriority = `<span class="badge badge-baja">PRIORIDAD BAJA</span>`;
            badgeStatus = `<span class="badge badge-denegada">DENEGADA (PERIODICIDAD)</span>`;
            textExplanation = `El punto seleccionado está dentro/próximo de un recuadro limpiado hace solo <strong>${days} días (< 3 meses)</strong>. Resistencia conforme (${resistance.toExponential(1).toUpperCase()} &Omega;). Faltan ${90 - days} días para habilitar mantenimiento.`;
        } else if (!meets3Months) {
            badgePriority = `<span class="badge badge-baja">PRIORIDAD BAJA</span>`;
            badgeStatus = `<span class="badge badge-denegada">DENEGADA (PERIODICIDAD)</span>`;
            textExplanation = `Han transcurrido <strong>${days} días</strong> desde la última limpieza registrada (< 3 meses). Resistencia conforme. Faltan ${90 - days} días.`;
        } else {
            badgePriority = isHighCrit ? `<span class="badge badge-alta">PRIORIDAD ALTA</span>` : `<span class="badge badge-media">PRIORIDAD MEDIA</span>`;
            badgeStatus = `<span class="badge badge-autorizada">AUTORIZADA</span>`;
            textExplanation = `Han transcurrido <strong>${days} días (&ge; 3 meses)</strong>. Ciclado regular cumplido.`;
        }

        priorityPreviewContent.innerHTML = `
            ${zoneMatchInfo}
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
                updateStepFlow();

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

    // Step Unlock Manager
    function updateStepFlow() {
        const step2Overlay = document.getElementById('step2-lock-overlay');
        const step3Overlay = document.getElementById('step3-lock-overlay');
        const badgeStep1 = document.getElementById('badge-step1');
        const badgeStep3 = document.getElementById('badge-step3');

        // Step 1: Area Selection
        const hasArea = currentSelectedArea && areaSelect && areaSelect.value;
        if (hasArea) {
            if (step2Overlay) step2Overlay.classList.add('hidden');
            if (badgeStep1) {
                badgeStep1.className = 'step-header-badge step-completed';
                badgeStep1.innerHTML = '<i class="fa-solid fa-check"></i> Paso 1 OK';
            }
        } else {
            if (step2Overlay) step2Overlay.classList.remove('hidden');
            if (badgeStep1) {
                badgeStep1.className = 'step-header-badge step-active';
                badgeStep1.innerHTML = 'Paso 1';
            }
        }

        // Step 2: Map Point Clicked
        const hasPoint = hasArea && selectedCoords !== null;
        if (hasPoint) {
            if (step3Overlay) step3Overlay.classList.add('hidden');
            if (badgeStep3) {
                badgeStep3.className = 'step-header-badge step-active';
                badgeStep3.innerHTML = 'Paso 3';
            }
        } else {
            if (step3Overlay) step3Overlay.classList.remove('hidden');
            if (badgeStep3) {
                badgeStep3.className = 'step-header-badge step-locked';
                badgeStep3.innerHTML = 'Paso 3';
            }
        }
    }

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

    // Load Cleaning History (Realizadas)
    let currentHistory = [];

    async function loadCleaningHistory() {
        try {
            const res = await fetch('/api/cleaning/history');
            if (res.ok) {
                currentHistory = await res.json();
                renderHistoryTable();
            }
        } catch (err) {
            console.error('Error fetching cleaning history:', err);
        }
    }

    function renderHistoryTable() {
        const historyTbody = document.getElementById('history-tbody');
        if (!historyTbody) return;
        historyTbody.innerHTML = '';

        const filterArea = document.getElementById('filter-hist-area')?.value || 'TODOS';
        const filterDateFrom = document.getElementById('filter-hist-date-from')?.value;
        const filterDateTo = document.getElementById('filter-hist-date-to')?.value;

        let filtered = currentHistory.filter(item => {
            // Filter by area
            if (filterArea !== 'TODOS' && item.areaId !== filterArea) {
                return false;
            }

            // Filter by date range
            if (item.cleanedDate) {
                const cleanDate = new Date(item.cleanedDate);
                if (filterDateFrom) {
                    const fromDate = new Date(filterDateFrom);
                    fromDate.setHours(0, 0, 0, 0);
                    if (cleanDate < fromDate) return false;
                }
                if (filterDateTo) {
                    const toDate = new Date(filterDateTo);
                    toDate.setHours(23, 59, 59, 999);
                    if (cleanDate > toDate) return false;
                }
            }

            return true;
        });

        if (!filtered || filtered.length === 0) {
            historyTbody.innerHTML = `<tr><td colspan="9" style="text-align:center; color:var(--text-dim);">No hay registros de limpiezas realizadas que coincidan con los filtros.</td></tr>`;
            return;
        }

        filtered.forEach(item => {
            const tr = document.createElement('tr');
            const dateCleanStr = item.cleanedDate ? new Date(item.cleanedDate).toLocaleString('es-MX') : 'Reciente';
            const dateNextStr = item.nextCleaningDate ? new Date(item.nextCleaningDate).toLocaleDateString('es-MX') : 'Por definir';

            let reasonBadge = `<span class="badge badge-autorizada"><i class="fa-solid fa-broom"></i> ${item.reason}</span>`;
            if (item.reason && item.reason.includes('Solicitud')) {
                reasonBadge = `<span class="badge badge-alta"><i class="fa-solid fa-file-circle-check"></i> ${item.reason}</span>`;
            }

            tr.innerHTML = `
                <td><strong>${item.id}</strong></td>
                <td>${item.areaName} (${item.areaId})</td>
                <td>${reasonBadge}</td>
                <td>${dateCleanStr}</td>
                <td><strong style="color: var(--primary);">${dateNextStr}</strong></td>
                <td><span style="font-size:0.82rem; color:#00f2fe;"><i class="fa-solid fa-vector-square"></i> ${item.mapSection}</span></td>
                <td>${item.cleanedBy}</td>
                <td>${item.notes}</td>
                <td><button class="btn-secondary btn-sm-view-hist" data-id="${item.id}"><i class="fa-solid fa-map-location-dot"></i> Ver en Mapa</button></td>
            `;

            historyTbody.appendChild(tr);
        });

        // Add Click event for History Detail Modal
        document.querySelectorAll('.btn-sm-view-hist').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const histId = btn.getAttribute('data-id');
                const item = currentHistory.find(h => h.id === histId);
                if (item) showHistoryDetailModal(item);
            });
        });
    }

    function showHistoryDetailModal(item) {
        const modal = document.getElementById('modal-detail');
        const body = document.getElementById('detail-modal-body');

        const mapConfig = currentMaps.find(m => m.areaId === item.areaId);
        const mapImageUrl = mapConfig ? mapConfig.imageUrl : '/uploads/smt_floor_plan.svg';

        const dateCleanStr = item.cleanedDate ? new Date(item.cleanedDate).toLocaleString('es-MX') : 'Reciente';
        const dateNextStr = item.nextCleaningDate ? new Date(item.nextCleaningDate).toLocaleDateString('es-MX') : 'Por definir';

        let x = item.xPercent;
        let y = item.yPercent;
        let w = item.widthPercent;
        let h = item.heightPercent;

        if ((!w || w <= 0) && item.notes) {
            const ext = extractRectangleCoords(item.notes);
            if (ext) {
                x = ext.xPercent;
                y = ext.yPercent;
                w = ext.widthPercent;
                h = ext.heightPercent;
            }
        }

        let zoneOverlayHtml = '';
        if (w && w > 0) {
            zoneOverlayHtml = `
                <div class="cleaned-zone-rect" style="left:${x}%; top:${y}%; width:${w}%; height:${h}%; border-color:#10b981; background:rgba(16,185,129,0.25);">
                    <div class="zone-label-badge" style="background:#10b981; color:#000;">
                        <i class="fa-solid fa-broom"></i> Zona Limpiada (${dateCleanStr})
                    </div>
                </div>
            `;
        } else {
            zoneOverlayHtml = `
                <div class="reticle-marker" style="left: 50%; top: 50%;"></div>
            `;
        }

        body.innerHTML = `
            <div style="margin-bottom: 1.2rem;">
                <h4 style="color:var(--primary); margin-bottom: 0.5rem;">
                    <i class="fa-solid fa-location-dot"></i> Mapa de Área Limpiada (${item.areaName}):
                </h4>
                <div class="modal-map-viewport">
                    <div class="modal-map-wrapper">
                        <img src="${mapImageUrl}" alt="Mapa de Área" />
                        <div class="modal-map-overlay">
                            ${zoneOverlayHtml}
                        </div>
                    </div>
                </div>
                <div style="font-size:0.82rem; color:var(--text-muted); text-align:center;">
                    <i class="fa-solid fa-vector-square"></i> Sección: ${item.mapSection}
                </div>
            </div>

            <div style="display:grid; grid-template-columns: 1fr 1fr; gap: 1.5rem;">
                <div>
                    <h4 style="color:var(--primary); margin-bottom: 0.5rem;">Detalle del Registro de Limpieza</h4>
                    <p><strong>Folio / ID:</strong> ${item.id}</p>
                    <p><strong>Área:</strong> ${item.areaName} (${item.areaId})</p>
                    <p><strong>Origen / Motivo:</strong> ${item.reason}</p>
                    <p><strong>Fecha Realización:</strong> ${dateCleanStr}</p>
                    <p><strong>Próxima Limpieza Programada:</strong> ${dateNextStr}</p>
                </div>
                <div>
                    <h4 style="color:var(--primary); margin-bottom: 0.5rem;">Responsable & Observaciones</h4>
                    <p><strong>Realizado Por:</strong> ${item.cleanedBy}</p>
                    <p><strong>Observaciones:</strong> ${item.notes || 'Sin observaciones'}</p>
                </div>
            </div>
        `;

        modal.classList.add('active');
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
        if (!requestsTbody) return;
        requestsTbody.innerHTML = '';

        const filterStatus = document.getElementById('filter-req-status')?.value || 'TODOS';
        const filterArea = document.getElementById('filter-req-area')?.value || 'TODOS';
        const filterDateFrom = document.getElementById('filter-req-date-from')?.value;
        const filterDateTo = document.getElementById('filter-req-date-to')?.value;

        let filtered = currentRequests.filter(req => {
            // Filter by status
            const st = (req.status || req.authorizationStatus || 'AUTORIZADA').toUpperCase();
            if (filterStatus !== 'TODOS') {
                if (filterStatus === 'AUTORIZADA' && st !== 'AUTORIZADA') return false;
                if (filterStatus === 'EN_PROCESO' && st !== 'EN_PROCESO') return false;
                if (filterStatus === 'LIMPIEZA_COMPLETADA' && st !== 'LIMPIEZA_COMPLETADA' && st !== 'REALIZADA') return false;
                if (filterStatus === 'CANCELADA' && st !== 'CANCELADA') return false;
                if (filterStatus === 'DENEGADA' && !st.includes('DENEGADA')) return false;
            }

            // Filter by area
            if (filterArea !== 'TODOS' && req.areaId !== filterArea) {
                return false;
            }

            // Filter by date range
            if (req.requestDate) {
                const reqDate = new Date(req.requestDate);
                if (filterDateFrom) {
                    const fromDate = new Date(filterDateFrom);
                    fromDate.setHours(0, 0, 0, 0);
                    if (reqDate < fromDate) return false;
                }
                if (filterDateTo) {
                    const toDate = new Date(filterDateTo);
                    toDate.setHours(23, 59, 59, 999);
                    if (reqDate > toDate) return false;
                }
            }

            return true;
        });

        if (filtered.length === 0) {
            requestsTbody.innerHTML = `<tr><td colspan="10" style="text-align:center; color:var(--text-dim);">No hay solicitudes que coincidan con los filtros aplicados.</td></tr>`;
            return;
        }

        filtered.forEach(req => {
            const tr = document.createElement('tr');
            const dateStr = new Date(req.requestDate).toLocaleString('es-MX');
            const resStr = req.lastEsdResistanceOhms ? req.lastEsdResistanceOhms.toExponential(1).toUpperCase() + ' Ω' : 'N/A';

            let prioBadge = `<span class="badge badge-${req.priority ? req.priority.toLowerCase() : 'media'}">${req.priority || 'MEDIA'}</span>`;
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

    // ==========================================
    // CLEANER EXECUTION MODULE (TAB 4)
    // ==========================================
    const cleanerAreaSelect = document.getElementById('cleaner-area-select');
    const cleanerFloorMapImg = document.getElementById('cleaner-floor-map-img');
    const cleanerMapWrapper = document.getElementById('cleaner-map-wrapper');
    const cleanerMapOverlay = document.getElementById('cleaner-map-overlay');
    const drawingBoxPreview = document.getElementById('drawing-box-preview');
    const cleanerBoxText = document.getElementById('cleaner-box-text');
    const cleanerReqIdSelect = document.getElementById('cleaner-req-id');
    const cleanerDateInput = document.getElementById('cleaner-date');
    const cleanerByInput = document.getElementById('cleaner-by');
    const cleanerNotesInput = document.getElementById('cleaner-notes');
    const formCleanerZone = document.getElementById('form-cleaner-zone');
    const btnClearDrawing = document.getElementById('btn-clear-drawing');
    const authorizedQueueList = document.getElementById('authorized-queue-list');

    let cleanerSelectedArea = null;
    let drawnBoxCoords = null; // { xPercent, yPercent, widthPercent, heightPercent }
    let isDrawingBox = false;
    let drawStartPx = { x: 0, y: 0 };

    if (cleanerDateInput && !cleanerDateInput.value) {
        const now = new Date();
        now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
        cleanerDateInput.value = now.toISOString().slice(0, 16);
    }

    async function selectCleanerArea(areaId) {
        cleanerSelectedArea = currentMaps.find(m => m.areaId === areaId);
        if (!cleanerSelectedArea) return;

        if (cleanerAreaSelect) cleanerAreaSelect.value = areaId;
        if (cleanerFloorMapImg) cleanerFloorMapImg.src = cleanerSelectedArea.imageUrl;

        drawnBoxCoords = null;
        if (cleanerBoxText) cleanerBoxText.innerHTML = '<i class="fa-solid fa-vector-square"></i> <span>Ningún recuadro dibujado (Haz clic y arrastra en el mapa)</span>';

        await renderCleanerMapOverlay();
        renderAuthorizedRequestsQueue();
    }

    // Mouse Events for Box Drawing on Map Canvas (2-Click System)
    if (cleanerMapWrapper) {
        // Prevent default drag behavior on the map wrapper and its children (like the img)
        cleanerMapWrapper.addEventListener('dragstart', (e) => e.preventDefault());

        cleanerMapWrapper.addEventListener('click', (e) => {
            if (!cleanerSelectedArea) return;
            const rect = cleanerMapWrapper.getBoundingClientRect();

            if (!isDrawingBox) {
                // First Click: Start drawing
                isDrawingBox = true;
                drawStartPx = {
                    x: e.clientX - rect.left,
                    y: e.clientY - rect.top
                };
                if (drawingBoxPreview) {
                    drawingBoxPreview.style.left = `${drawStartPx.x}px`;
                    drawingBoxPreview.style.top = `${drawStartPx.y}px`;
                    drawingBoxPreview.style.width = '0px';
                    drawingBoxPreview.style.height = '0px';
                    drawingBoxPreview.classList.remove('hidden');
                }
                if (cleanerBoxText) {
                    cleanerBoxText.innerHTML = '<i class="fa-solid fa-crosshairs"></i> <span>Mueve el ratón y haz clic de nuevo para finalizar el recuadro.</span>';
                }
            } else {
                // Second Click: Finish drawing
                isDrawingBox = false;
                const currentX = Math.max(0, Math.min(e.clientX - rect.left, rect.width));
                const currentY = Math.max(0, Math.min(e.clientY - rect.top, rect.height));

                const minX = Math.min(drawStartPx.x, currentX);
                const minY = Math.min(drawStartPx.y, currentY);
                const widthPx = Math.abs(currentX - drawStartPx.x);
                const heightPx = Math.abs(currentY - drawStartPx.y);

                if (drawingBoxPreview) drawingBoxPreview.classList.add('hidden');

                if (widthPx < 10 || heightPx < 10) {
                    if (cleanerBoxText) cleanerBoxText.innerHTML = '<i class="fa-solid fa-triangle-exclamation"></i> <span>Recuadro muy pequeño. Haz clic para volver a trazar.</span>';
                    return;
                }

                const xPercent = parseFloat(((minX / rect.width) * 100).toFixed(2));
                const yPercent = parseFloat(((minY / rect.height) * 100).toFixed(2));
                const widthPercent = parseFloat(((widthPx / rect.width) * 100).toFixed(2));
                const heightPercent = parseFloat(((heightPx / rect.height) * 100).toFixed(2));

                drawnBoxCoords = { xPercent, yPercent, widthPercent, heightPercent };
                if (cleanerBoxText) {
                    cleanerBoxText.innerHTML = `<strong>Recuadro Listo:</strong> X=${xPercent}%, Y=${yPercent}%, Ancho=${widthPercent}%, Alto=${heightPercent}%`;
                }
                renderCleanerMapOverlay();
            }
        });

        cleanerMapWrapper.addEventListener('mousemove', (e) => {
            if (!isDrawingBox || !cleanerSelectedArea || !drawingBoxPreview) return;
            const rect = cleanerMapWrapper.getBoundingClientRect();
            const currentX = Math.max(0, Math.min(e.clientX - rect.left, rect.width));
            const currentY = Math.max(0, Math.min(e.clientY - rect.top, rect.height));

            const minX = Math.min(drawStartPx.x, currentX);
            const minY = Math.min(drawStartPx.y, currentY);
            const width = Math.abs(currentX - drawStartPx.x);
            const height = Math.abs(currentY - drawStartPx.y);

            drawingBoxPreview.style.left = `${minX}px`;
            drawingBoxPreview.style.top = `${minY}px`;
            drawingBoxPreview.style.width = `${width}px`;
            drawingBoxPreview.style.height = `${height}px`;
        });
    }

    async function renderCleanerMapOverlay() {
        if (!cleanerMapOverlay || !cleanerSelectedArea) return;
        cleanerMapOverlay.innerHTML = '';

        // Render measurement points as subtle pins
        if (cleanerSelectedArea.points) {
            cleanerSelectedArea.points.forEach(pt => {
                const pin = document.createElement('div');
                pin.className = 'map-pin';
                if (pt.lastResistanceOhms > 1e8) pin.classList.add('high-resistance');
                pin.style.left = `${pt.xPercent}%`;
                pin.style.top = `${pt.yPercent}%`;
                pin.style.opacity = '0.7';
                pin.innerHTML = `<div class="pin-head" style="width:12px;height:12px;"><i class="fa-solid fa-bolt" style="font-size:0.5rem;"></i></div>`;
                cleanerMapOverlay.appendChild(pin);
            });
        }

        // Fetch & Render Cleaned Zones
        try {
            const res = await fetch(`/api/cleaning/zones/${cleanerSelectedArea.areaId}`);
            if (res.ok) {
                const zones = await res.json();
                zones.forEach((z) => {
                    const rect = document.createElement('div');
                    rect.className = 'cleaned-zone-rect';
                    rect.style.left = `${z.xPercent}%`;
                    rect.style.top = `${z.yPercent}%`;
                    rect.style.width = `${z.widthPercent}%`;
                    rect.style.height = `${z.heightPercent}%`;

                    const dateStr = new Date(z.cleanedDate).toLocaleDateString('es-MX');
                    rect.innerHTML = `
                        <div class="zone-label-badge">
                            <i class="fa-solid fa-broom"></i> Limpiado: ${dateStr} (${z.cleanedBy || 'Personal'})
                        </div>
                    `;
                    cleanerMapOverlay.appendChild(rect);
                });
            }
        } catch (err) {
            console.error('Error loading cleaned zones:', err);
        }

        // Render Active Drawn Box if currently selected
        if (drawnBoxCoords) {
            const activeRect = document.createElement('div');
            activeRect.className = 'cleaned-zone-rect';
            activeRect.style.left = `${drawnBoxCoords.xPercent}%`;
            activeRect.style.top = `${drawnBoxCoords.yPercent}%`;
            activeRect.style.width = `${drawnBoxCoords.widthPercent}%`;
            activeRect.style.height = `${drawnBoxCoords.heightPercent}%`;
            activeRect.style.borderColor = '#00f2fe';
            activeRect.style.boxShadow = '0 0 20px #00f2fe';
            activeRect.innerHTML = `<div class="zone-label-badge" style="color:#00f2fe; border-color:#00f2fe;"><i class="fa-solid fa-pencil"></i> Nuevo Recuadro Trazado</div>`;
            cleanerMapOverlay.appendChild(activeRect);
        }
    }

    function renderAuthorizedRequestsQueue() {
        if (!authorizedQueueList || !cleanerReqIdSelect) return;

        authorizedQueueList.innerHTML = '';
        cleanerReqIdSelect.innerHTML = '<option value="">-- Sin Solicitud (Limpieza Directa de Rutina) --</option>';

        const authorizedReqs = currentRequests.filter(r => r.status === 'AUTORIZADA' || r.authorizationStatus === 'AUTORIZADA');

        if (authorizedReqs.length === 0) {
            authorizedQueueList.innerHTML = '<div style="color:var(--text-dim); font-size:0.82rem; text-align:center; padding:1rem;"><i class="fa-solid fa-circle-check"></i> No hay solicitudes autorizadas pendientes.</div>';
            return;
        }

        authorizedReqs.forEach(req => {
            const opt = document.createElement('option');
            opt.value = req.id;
            opt.textContent = `${req.id} - ${req.areaName} (${req.reason})`;
            cleanerReqIdSelect.appendChild(opt);

            const card = document.createElement('div');
            card.className = 'authorized-req-card';
            if (cleanerReqIdSelect.value === req.id) card.classList.add('selected-for-clean');

            const reqDateStr = new Date(req.requestDate).toLocaleDateString('es-MX');

            card.innerHTML = `
                <div class="req-card-info">
                    <strong>${req.id} • ${req.areaName}</strong>
                    <span><i class="fa-solid fa-triangle-exclamation"></i> ${req.reason} | Fecha: ${reqDateStr}</span>
                </div>
                <button type="button" class="btn-select-req" data-req-id="${req.id}" data-area-id="${req.areaId}" data-x="${req.coordXPercent}" data-y="${req.coordYPercent}">
                    <i class="fa-solid fa-draw-polygon"></i> Trazar Limpieza
                </button>
            `;

            authorizedQueueList.appendChild(card);
        });

        // Add Click event to Trazar Limpieza buttons
        authorizedQueueList.querySelectorAll('.btn-select-req').forEach(btn => {
            btn.addEventListener('click', () => {
                const reqId = btn.getAttribute('data-req-id');
                const areaId = btn.getAttribute('data-area-id');
                const x = parseFloat(btn.getAttribute('data-x'));
                const y = parseFloat(btn.getAttribute('data-y'));

                if (!cleanerSelectedArea || cleanerSelectedArea.areaId !== areaId) {
                    selectCleanerArea(areaId);
                }

                cleanerReqIdSelect.value = reqId;
                
                // Pre-draw suggested box around request point (20% x 20%)
                const w = 20;
                const h = 20;
                const minX = Math.max(0, x - w / 2);
                const minY = Math.max(0, y - h / 2);

                drawnBoxCoords = {
                    xPercent: parseFloat(minX.toFixed(2)),
                    yPercent: parseFloat(minY.toFixed(2)),
                    widthPercent: w,
                    heightPercent: h
                };

                cleanerBoxText.innerHTML = `<strong>Recuadro Sugerido por Solicitud ${reqId}:</strong> X=${drawnBoxCoords.xPercent}%, Y=${drawnBoxCoords.yPercent}%, W=${w}%, H=${h}%`;
                
                const targetReq = currentRequests.find(r => r.id === reqId);
                if (targetReq && cleanerNotesInput) {
                    cleanerNotesInput.value = `Limpieza realizada en atención a solicitud ${reqId}: ${targetReq.reason}`;
                }

                renderCleanerMapOverlay();
                renderAuthorizedRequestsQueue();
            });
        });
    }

    if (formCleanerZone) {
        formCleanerZone.addEventListener('submit', async (e) => {
            e.preventDefault();

            if (!cleanerSelectedArea) {
                alert('Debe seleccionar un área de planta.');
                return;
            }

            if (!drawnBoxCoords) {
                alert('Debe dibujar un recuadro sobre el mapa de piso haciendo clic y arrastrando el ratón para definir la zona limpiada.');
                return;
            }

            const dto = {
                areaId: cleanerSelectedArea.areaId,
                requestId: cleanerReqIdSelect.value || '',
                xPercent: drawnBoxCoords.xPercent,
                yPercent: drawnBoxCoords.yPercent,
                widthPercent: drawnBoxCoords.widthPercent,
                heightPercent: drawnBoxCoords.heightPercent,
                cleanedDate: cleanerDateInput.value ? new Date(cleanerDateInput.value).toISOString() : new Date().toISOString(),
                cleanedBy: cleanerByInput.value || 'Personal de Limpieza ESD',
                notes: cleanerNotesInput.value || 'Limpieza realizada con recuadro en mapa'
            };

            try {
                const res = await fetch('/api/cleaning/zones', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(dto)
                });

                if (res.ok) {
                    const data = await res.json();
                    alert('¡Recuadro de área limpiada registrado con éxito!\n' + (dto.requestId ? `Estado de la solicitud ${dto.requestId} cambiado a LIMPIEZA_REALIZADA.` : ''));

                    formCleanerZone.reset();
                    if (cleanerDateInput) {
                        const now = new Date();
                        now.setMinutes(now.getMinutes() - now.getTimezoneOffset());
                        cleanerDateInput.value = now.toISOString().slice(0, 16);
                    }
                    drawnBoxCoords = null;
                    isDrawingBox = false;
                    if (drawingBoxPreview) {
                        drawingBoxPreview.style.width = '0px';
                        drawingBoxPreview.style.height = '0px';
                        drawingBoxPreview.classList.add('hidden');
                    }
                    if (cleanerBoxText) {
                        cleanerBoxText.innerHTML = '<i class="fa-solid fa-vector-square"></i> <span>Ningún recuadro dibujado (Haz clic en el mapa para trazar)</span>';
                    }

                    await loadMaps();
                    await loadRequests();
                    await selectCleanerArea(dto.areaId);
                } else {
                    const err = await res.json();
                    alert('Error al registrar zona limpiada: ' + (err.message || 'Error del servidor'));
                }
            } catch (err) {
                alert('Error de conexión: ' + err.message);
            }
        });
    }

    // Initialize Application after all variables are declared
    initApp();
});

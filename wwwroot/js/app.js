/**
 * J.A.R.V.I.S. Main Application Controller
 * Coordinates SignalR, Safe Authentication Gateway, 3D WebGL Hologram,
 * Canvas 2D Dynamic Renderer, Voice Studio, and Advanced Settings.
 */

class JarvisApp {
    constructor() {
        this.hub = null;
        this.hologram3D = null;
        this.hudCanvas = null;
        this.voiceStudio = null;
        this.settingsModal = null;
        this.sfx = new JarvisAudioSfx();
        window.sfx = this.sfx;

        this.authToken = localStorage.getItem('jarvis_auth_token') || null;
        this.activeAgent = null;
        this.currentStreamingCard = null;
        this.streamingTextBuffer = '';

        // Contacts State
        this.contacts = [];
        this.activeContactsFilter = 'ALL';
        this.contactsSearchQuery = '';
        this.activeDossierContact = null;

        this.init();
    }

    escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    async init() {
        // 1. Initialize Visual Engines
        this.hologram3D = new JarvisHologram3D('webgl-canvas');
        this.hudCanvas = new JarvisHudCanvas('hud-canvas');

        // 2. Initialize Voice & Audio Studio
        this.voiceStudio = new JarvisVoiceStudio({
            onTranscript: (text) => this.handleVoiceTranscript(text),
            onInterim: (text) => this.handleVoiceInterim(text),
            onWakeWord: () => this.handleWakeWordTrigger(),
            onAudioFft: (fftArray, rms) => {
                if (this.hudCanvas) this.hudCanvas.setAudioFft(fftArray);
                if (this.hologram3D) this.hologram3D.setAudioRms(rms);
            },
            onSpeakingChange: (isSpeaking) => {
                const badge = document.getElementById('speaking-indicator');
                if (badge) badge.style.display = isSpeaking ? 'inline-block' : 'none';
            },
            onListeningChange: (isListening) => {
                const micBtn = document.getElementById('mic-btn');
                if (micBtn) {
                    if (isListening) micBtn.classList.add('recording');
                    else micBtn.classList.remove('recording');
                }
            }
        });

        // 3. Initialize Settings Modal & Projects Studio
        this.settingsModal = new JarvisSettingsModal(this);
        if (typeof JarvisProjectsStudio !== 'undefined') {
            this.projectsStudio = new JarvisProjectsStudio(this);
        }
        window.jarvisApp = this;

        // 4. Setup Safe Authentication Gateway
        this.initAuthGateway();

        // 5. Setup UI Event Listeners
        this.bindUiEvents();

        // 6. Connect to .NET 10 SignalR Hub
        await this.connectSignalR();

        // 7. Initialize Stark Radio Station & Media Harvester
        this.initStarkRadio();

        // 8. Initialize Stark Cinema & YouTube Video Player
        this.initStarkCinema();

        // 9. Pre-load contacts cache
        fetch('/api/contacts', {
            headers: this.authToken ? { 'Authorization': `Bearer ${this.authToken}` } : {}
        }).then(r => r.json()).then(data => {
            if (Array.isArray(data) && data.length > 0) {
                this.contacts = data;
                this.renderContactsList(this.contacts);
            }
        }).catch(() => {});
    }

    // ==================== AUTHENTICATION GATEWAY ====================
    initAuthGateway() {
        const overlay = document.getElementById('login-overlay');
        const loginBtn = document.getElementById('btn-stark-login');
        const passInput = document.getElementById('login-passcode');
        const reactorGate = document.getElementById('reactor-gate-trigger');

        const doLogin = async () => {
            const passcode = passInput.value.trim();
            const errorEl = document.getElementById('login-error-msg');
            errorEl.textContent = '';

            try {
                const res = await fetch('/api/auth/login', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ passcode })
                });

                const data = await res.json();
                if (data.success && data.token) {
                    this.authToken = data.token;
                    localStorage.setItem('jarvis_auth_token', data.token);
                    this.sfx.playAccessGrantedChime();

                    overlay.classList.add('hidden');
                    this.appendAssistantMessage(data.message || "STARK CLEARANCE LEVEL 10 GRANTED: Welcome back, Sir.");
                } else {
                    this.sfx.playAccessDeniedAlarm();
                    errorEl.textContent = data.message || "ACCESS DENIED: Invalid Clearance Passcode.";
                }
            } catch (err) {
                errorEl.textContent = "Authentication subsystem error: " + err.message;
            }
        };

        loginBtn?.addEventListener('click', doLogin);
        reactorGate?.addEventListener('click', doLogin);
        passInput?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') doLogin();
        });

        // Check if already authenticated
        if (this.authToken) {
            fetch('/api/auth/check', {
                headers: { 'Authorization': `Bearer ${this.authToken}` }
            }).then(r => r.json()).then(data => {
                if (data.isAuthenticated) {
                    overlay.classList.add('hidden');
                }
            }).catch(() => {});
        }

        // Logout button
        document.getElementById('btn-logout')?.addEventListener('click', async () => {
            if (confirm("Lock Stark Terminal and Log Out?")) {
                await fetch('/api/auth/logout', {
                    method: 'POST',
                    headers: { 'Authorization': `Bearer ${this.authToken}` }
                });
                localStorage.removeItem('jarvis_auth_token');
                this.authToken = null;
                overlay.classList.remove('hidden');
                this.sfx.playHudBeep(300);
            }
        });
    }

    // ==================== SIGNALR REALTIME PIPELINE ====================
    async connectSignalR() {
        try {
            this.hub = new signalR.HubConnectionBuilder()
                .withUrl('/hub/jarvis', {
                    accessTokenFactory: () => this.authToken
                })
                .withAutomaticReconnect([0, 2000, 5000, 10000])
                .build();

            // Hub Event Listeners
            this.hub.on('OnInitState', (state) => {
                this.activeAgent = state.agent;
                this.updateActiveAgentBadge(state.agent);
                this.updateTelemetryUi(state.telemetry);
                if (state.config) this.applyConfig(state.config);
                if (state.contacts) {
                    this.contacts = state.contacts;
                    this.renderContactsList(this.contacts);
                }
                if (state.projects && this.settingsModal) {
                    this.settingsModal.projectsList = state.projects;
                    this.settingsModal.renderSettingsProjects(state.projects);
                    this.settingsModal.renderDrawerProjects(state.projects);
                    this.settingsModal.populateTargetProjectSelect(state.projects);
                }
            });

            this.hub.on('OnSensorTelemetry', (telemetry) => {
                this.updateTelemetryUi(telemetry);
            });

            this.hub.on('OnTokenReceived', (token) => {
                this.handleIncomingToken(token);
            });

            this.hub.on('OnStreamComplete', (message) => {
                this.handleStreamComplete(message);
            });

            this.hub.on('OnStreamError', (error) => {
                this.appendAssistantMessage(`[Alert]: ${error}`);
            });

            this.hub.on('OnClearScreen', () => {
                const stream = document.getElementById('chat-stream');
                if (stream) stream.innerHTML = '';
            });

            this.hub.on('OnProtocolAlert', (protocol) => {
                this.sfx.playProtocolPulse();
                this.appendAssistantMessage(`[DEFENSE PROTOCOL DEPLOYED]: ${protocol} matrix active.`);
            });

            // Tactical Calendar & Appointment Reminders
            this.hub.on('OnAppointmentReminder', (alert) => {
                this.handleAppointmentReminder(alert);
            });

            this.hub.on('OnAppointmentDismissed', (apptId) => {
                this.hideAppointmentReminder(apptId);
                if (this.settingsModal && !this.settingsModal.modalEl.classList.contains('hidden')) {
                    this.settingsModal.loadCalendarData();
                }
            });

            this.hub.on('OnAppointmentCancelled', (apptId) => {
                this.hideAppointmentReminder(apptId);
                if (this.settingsModal && !this.settingsModal.modalEl.classList.contains('hidden')) {
                    this.settingsModal.loadCalendarData();
                }
            });

            this.hub.on('OnCalendarUpdated', (appt) => {
                if (this.settingsModal && !this.settingsModal.modalEl.classList.contains('hidden')) {
                    this.settingsModal.loadCalendarData();
                }
            });

            // Stark Secure Contacts Directory
            this.hub.on('OnContactUpdated', (contact) => {
                const idx = this.contacts.findIndex(c => c.id === contact.id);
                if (idx >= 0) this.contacts[idx] = contact;
                else this.contacts.push(contact);
                this.renderContactsList(this.contacts);
                if (this.settingsModal && !this.settingsModal.modalEl.classList.contains('hidden')) {
                    this.settingsModal.loadContactsData();
                }
            });

            this.hub.on('OnContactDeleted', (contactId) => {
                this.contacts = this.contacts.filter(c => c.id !== contactId);
                this.renderContactsList(this.contacts);
                if (this.settingsModal && !this.settingsModal.modalEl.classList.contains('hidden')) {
                    this.settingsModal.loadContactsData();
                }
            });

            // Holographic Biometric Dossier Auto-Popup
            this.hub.on('OnOpenContactDossier', (contact) => {
                if (contact) {
                    this.openContactDossier(contact);
                }
            });

            // Strategic Projects & Autonomous Agents
            this.hub.on('OnProjectUpdated', (project) => {
                if (this.settingsModal) {
                    this.settingsModal.loadProjectsData();
                }
            });

            this.hub.on('OnOpenProjectStudio', (projectId) => {
                if (this.projectsStudio) {
                    this.projectsStudio.open(projectId);
                }
            });

            this.hub.on('OnProjectDeleted', (projectId) => {
                if (this.settingsModal) {
                    this.settingsModal.loadProjectsData();
                }
            });

            // Autonomous Tasks & Scheduler
            this.hub.on('OnScheduledTaskUpdated', (task) => {
                if (this.settingsModal && !this.settingsModal.modalEl.classList.contains('hidden')) {
                    this.settingsModal.loadCalendarData();
                }
            });

            this.hub.on('OnAgentTaskCompleted', (data) => {
                this.handleAgentTaskCompleted(data);
            });

            // Storage Telemetry & Ghost Drives
            this.hub.on('OnLowSpaceAlert', (alert) => {
                this.handleLowSpaceAlert(alert);
            });

            this.hub.on('OnGhostDrivesUpdated', (drives) => {
                if (this.settingsModal && !this.settingsModal.modalEl.classList.contains('hidden')) {
                    this.settingsModal.loadStorageData();
                }
            });

            // ReAct Autonomous Reasoning & Execution Step
            this.hub.on('OnReActStep', (step) => {
                if (this.currentStreamingCard) {
                    const contentEl = this.currentStreamingCard.querySelector('#stream-content');
                    if (contentEl) {
                        const pulse = document.createElement('div');
                        pulse.className = 'react-step-pulse';
                        pulse.style.cssText = 'font-size:0.75rem;color:var(--accent-color);font-family:var(--font-mono);margin:4px 0;padding:2px 6px;background:rgba(0,255,170,0.08);border-left:2px solid var(--accent-color);';
                        pulse.textContent = `⚡ [ReAct ${step.step || 'Step'}]: ${step.status || 'Active'}`;
                        contentEl.parentNode.insertBefore(pulse, contentEl);
                    }
                }
            });

            // Voice Barge-in Interrupt
            this.hub.on('OnSpeechInterrupted', () => {
                if (this.voiceStudio) this.voiceStudio.stopSpeaking();
            });

            // Smart Home Mesh Updated
            this.hub.on('OnSmartHomeUpdated', (devices) => {
                console.log("Smart home mesh state synchronized:", devices);
            });

            // Snapshots Updated
            this.hub.on('OnSnapshotsUpdated', (snapshots) => {
                console.log("Stark Snapshots synchronized:", snapshots);
            });

            // Terminal Sentinel Security Challenge
            this.hub.on('OnSecurityChallengeRequired', (challenge) => {
                const pass = prompt(`[TERMINAL SENTINEL HAZARD DETECTED]\n${challenge.reason}\nToken: ${challenge.challengeToken}\n\nEnter Stark Master Passcode to authorize:`);
                if (pass) {
                    this.hub.invoke('AuthorizeSecurityChallenge', challenge.challengeToken, pass).then(approved => {
                        if (approved) alert("Clearance Approved. Command executing.");
                        else alert("Clearance Denied. Operation aborted.");
                    });
                }
            });

            // Stark Radio Station SignalR Events
            this.hub.on('OnRadioStateChanged', (state) => this.updateRadioPlayerState(state));
            this.hub.on('OnRadioDownloadProgress', (p) => this.updateRadioDownloadProgress(p));
            this.hub.on('OnRadioTrackReady', (track) => this.handleRadioTrackReady(track));
            this.hub.on('OnRadioCorruptedFixed', (d) => this.handleRadioCorruptedFixed(d));
            this.hub.on('OnRadioQueueUpdated', (q) => this.updateRadioQueueUi(q));

            // Stark Cinema Video Player & Movie Vault SignalR Events
            this.hub.on('OnVideoFeedUpdated', () => this.refreshCinemaFeeds());
            this.hub.on('OnVideoDownloadProgress', (p) => this.updateVideoDownloadProgress(p));
            this.hub.on('OnVideoReady', (v) => this.handleVideoReady(v));
            this.hub.on('OnVideoWatchedChanged', (d) => this.handleVideoWatchedChanged(d));
            this.hub.on('OnMoviesUpdated', () => this.loadMovieVault());
            this.hub.on('OnMovieWatchedChanged', (d) => this.handleMovieWatchedChanged(d));

            await this.hub.start();
            console.log("SignalR Duplex Pipeline connected to J.A.R.V.I.S. Core.");
        } catch (err) {
            console.warn("SignalR connection attempt:", err);
            setTimeout(() => this.connectSignalR(), 3000);
        }
    }

    // ==================== TELEMETRY & UI UPDATES ====================
    updateTelemetryUi(t) {
        if (!t) return;

        // Header pills
        const arcPowerEl = document.getElementById('hud-arc-power');
        if (arcPowerEl) arcPowerEl.textContent = `${t.arcReactorOutputGigawatts.toFixed(2)} GW`;

        const coreTempEl = document.getElementById('hud-core-temp');
        if (coreTempEl) coreTempEl.textContent = `${t.coreTemperatureCelsius.toFixed(1)}°C`;

        // Left Panel Telemetry Gauges
        const cpuEl = document.getElementById('tel-cpu-val');
        const cpuBar = document.getElementById('tel-cpu-bar');
        if (cpuEl) cpuEl.textContent = `${t.cpuUsagePercent.toFixed(1)}%`;
        if (cpuBar) cpuBar.style.width = `${Math.min(100, t.cpuUsagePercent)}%`;

        const memEl = document.getElementById('tel-mem-val');
        const memBar = document.getElementById('tel-mem-bar');
        if (memEl) memEl.textContent = `${t.memoryUsagePercent.toFixed(1)}% (${t.memoryUsedMb} MB)`;
        if (memBar) memBar.style.width = `${Math.min(100, t.memoryUsagePercent)}%`;

        const netEl = document.getElementById('tel-net-val');
        if (netEl) netEl.textContent = `${t.networkBandwidthKbps.toFixed(1)} Kbps`;

        const statusEl = document.getElementById('tel-core-status');
        if (statusEl) statusEl.textContent = t.coreStatus;

        const containmentEl = document.getElementById('tel-containment-val');
        if (containmentEl) containmentEl.textContent = `${t.magneticContainmentField.toFixed(1)}%`;

        const noiseEl = document.getElementById('tel-noise-val');
        if (noiseEl) noiseEl.textContent = `${t.ambientNoiseDb.toFixed(1)} dB`;
    }

    updateActiveAgentBadge(agent) {
        if (!agent) return;
        this.activeAgent = agent;
        const nameEl = document.getElementById('active-agent-name');
        const roleEl = document.getElementById('active-agent-role');
        const dotEl = document.getElementById('active-agent-dot');

        if (nameEl) nameEl.textContent = agent.name;
        if (roleEl) roleEl.textContent = agent.role;
        if (dotEl && agent.colorTheme) {
            dotEl.style.backgroundColor = agent.colorTheme;
            dotEl.style.boxShadow = `0 0 8px ${agent.colorTheme}`;
        }
    }

    updateHeaderAgentPicker(agents) {
        const picker = document.getElementById('header-agent-select');
        if (!picker) return;
        picker.innerHTML = '';
        agents.forEach(a => {
            const opt = document.createElement('option');
            opt.value = a.id;
            opt.textContent = `${a.name}`;
            if (a.isActive) opt.selected = true;
            picker.appendChild(opt);
        });

        picker.onchange = async () => {
            await this.settingsModal.activateAgent(picker.value);
        };
    }

    applyConfig(cfg) {
        if (!cfg) return;
        if (cfg.display && cfg.display.theme) {
            this.applyTheme(cfg.display.theme);
        }
        if (cfg.voice && this.voiceStudio) {
            this.voiceStudio.setVoiceByName(cfg.voice.browserVoiceName);
            this.voiceStudio.setVoiceParams(cfg.voice.browserPitch, cfg.voice.browserRate, cfg.voice.browserVolume);
            this.voiceStudio.wakeWord = cfg.voice.wakeWord || 'jarvis';
            this.voiceStudio.wakeWordEnabled = cfg.voice.wakeWordEnabled !== false;
            this.voiceStudio.setTtsSanitization(cfg.voice.enableTtsSanitization, cfg.voice.ttsExclusionSymbols);
            this.voiceStudio.setSttExclusions(cfg.voice.sttExclusionPhrases);
        }
    }

    applyTheme(themeName) {
        document.body.className = '';
        if (themeName === 'ArcOrange') document.body.classList.add('theme-arc-orange');
        else if (themeName === 'MarkGold') document.body.classList.add('theme-mark-gold');
        else if (themeName === 'UltronCrimson') document.body.classList.add('theme-ultron-crimson');
        else if (themeName === 'StealthGreen') document.body.classList.add('theme-stealth-green');

        if (this.hologram3D) this.hologram3D.setTheme(themeName);
    }

    // ==================== CHAT STREAMING & MESSAGING ====================
    async sendUserMessage(text) {
        if (!text || !text.trim()) return;
        const msg = text.trim();
        const lower = msg.toLowerCase();

        // 1. Instant client-side clear screen
        if (lower === 'clear' || lower === 'cls' || lower === 'clear screen' || lower === 'clean screen' || lower === 'clear chat' || lower === 'wipe screen') {
            const stream = document.getElementById('chat-stream');
            if (stream) stream.innerHTML = '';
            if (this.voiceStudio) this.voiceStudio.stopSpeaking();
            this.sfx.playHudBeep(900, 0.08);
            if (this.hub && this.hub.state === signalR.HubConnectionState.Connected) {
                try { await this.hub.invoke('SendMessage', { message: msg, speakResponse: false }); } catch {}
            }
            return;
        }

        // 2. Instant client-side theme switch
        if (lower.startsWith('theme ') || lower.startsWith('color ')) {
            const t = lower.replace('theme ', '').replace('color ', '').trim();
            let mapped = 'Default';
            if (t.includes('orange') || t.includes('arc')) mapped = 'ArcOrange';
            else if (t.includes('gold') || t.includes('mark')) mapped = 'MarkGold';
            else if (t.includes('crimson') || t.includes('red') || t.includes('ultron')) mapped = 'UltronCrimson';
            else if (t.includes('green') || t.includes('stealth')) mapped = 'StealthGreen';
            this.applyTheme(mapped);
            this.sfx.playHudBeep(1100, 0.06);
            this.appendUserMessage(msg);
            this.appendAssistantMessage(`Holographic neural core and HUD color scheme switched to **${mapped}**.`);
            return;
        }

        // 3. Instant client-side audio mute / silence
        if (lower === 'mute' || lower === 'silence' || lower === 'stop speaking' || lower === 'stop' || lower === 'quiet') {
            if (this.voiceStudio) this.voiceStudio.stopSpeaking();
            this.appendUserMessage(msg);
            this.appendAssistantMessage('Voice audio synthesis silenced.');
            return;
        }

        // 4. Instant client-side settings trigger
        if (lower === 'settings' || lower === 'open settings' || lower === 'config') {
            this.settingsModal.open();
            this.appendUserMessage(msg);
            this.appendAssistantMessage('Stark Advanced Configuration Studio opened.');
            return;
        }

        this.sfx.playHudBeep(700, 0.05);

        // Add User Card to stream
        this.appendUserMessage(msg);

        // Prepare Assistant Streaming Card
        this.currentStreamingCard = this.createAssistantStreamingCard();
        this.streamingTextBuffer = '';
        this.streamTtsCursor = 0;
        this.hasStreamTtsSpoken = false;
        if (this.voiceStudio) this.voiceStudio.clearSpeechQueue();

        // Send over SignalR
        if (this.hub && this.hub.state === signalR.HubConnectionState.Connected) {
            try {
                await this.hub.invoke('SendMessage', {
                    message: msg,
                    speakResponse: true
                });
            } catch (err) {
                console.error("SendMessage error", err);
                this.appendAssistantMessage(`[Communication Failure]: ${err.message}`);
            }
        } else {
            this.appendAssistantMessage("[Offline Mode]: SignalR connection disconnected. Retrying...");
        }
    }

    appendUserMessage(text) {
        const stream = document.getElementById('chat-stream');
        if (!stream) return;

        const card = document.createElement('div');
        card.className = 'chat-card user-card interactive';
        card.innerHTML = `
            <div class="chat-card-header">
                <span>TONY STARK</span>
                <span>${new Date().toLocaleTimeString()}</span>
            </div>
            <div class="chat-card-content">${this.escape(text)}</div>
        `;
        stream.appendChild(card);
        stream.scrollTop = stream.scrollHeight;
    }

    createAssistantStreamingCard() {
        const stream = document.getElementById('chat-stream');
        if (!stream) return null;

        const card = document.createElement('div');
        card.className = 'chat-card assistant-card interactive';
        card.innerHTML = `
            <div class="chat-card-header">
                <span id="stream-bot-name">${this.activeAgent ? this.activeAgent.name : 'J.A.R.V.I.S.'}</span>
                <span style="color:var(--accent-color);font-size:0.65rem;">SYNTHESIZING...</span>
            </div>
            <div class="chat-card-content" id="stream-content"><span class="blinking-cursor">▋</span></div>
        `;
        stream.appendChild(card);
        stream.scrollTop = stream.scrollHeight;
        return card;
    }

    handleIncomingToken(token) {
        this.streamingTextBuffer += token;
        if (this.currentStreamingCard) {
            const contentEl = this.currentStreamingCard.querySelector('#stream-content');
            if (contentEl) {
                contentEl.innerHTML = this.formatMarkdown(this.streamingTextBuffer) + '<span class="blinking-cursor">▋</span>';
            }
            const stream = document.getElementById('chat-stream');
            if (stream) stream.scrollTop = stream.scrollHeight;
        }

        // =========================================================================
        // J.A.R.V.I.S. EARLY STREAMING TTS PIPELINE:
        // Begin reading answers aloud as sentences complete, eliminating the long wait
        // =========================================================================
        this.processStreamingTts();
    }

    processStreamingTts() {
        if (!this.voiceStudio) return;

        const unread = this.streamingTextBuffer.substring(this.streamTtsCursor || 0);
        if (!unread || unread.length < 8) return;

        // Guard against speaking while inside open markdown code blocks (```)
        const fullBefore = this.streamingTextBuffer.substring(0, this.streamTtsCursor || 0);
        const codeFences = ((fullBefore.match(/```/g) || []).length) + ((unread.match(/```/g) || []).length);
        if (codeFences % 2 !== 0) return;

        // Match sentence boundary: period, question mark, or exclamation point followed by whitespace or line break
        const match = unread.match(/^([\s\S]*?[.!?])(?:\s+|\n+)/);
        if (match && match[1]) {
            const sentence = match[1].trim();
            const advance = match[0].length;
            if (sentence.length >= 8) {
                this.streamTtsCursor = (this.streamTtsCursor || 0) + advance;
                this.hasStreamTtsSpoken = true;
                this.voiceStudio.enqueueSentence(sentence);

                // Recursively check if multiple sentences arrived in a burst token
                this.processStreamingTts();
            }
        }
    }

    handleStreamComplete(assistantMsg) {
        if (this.currentStreamingCard) {
            const contentEl = this.currentStreamingCard.querySelector('#stream-content');
            if (contentEl) {
                contentEl.innerHTML = this.formatMarkdown(assistantMsg.content || this.streamingTextBuffer);
            }
            const headerStatus = this.currentStreamingCard.querySelector('.chat-card-header span:last-child');
            if (headerStatus) {
                headerStatus.textContent = new Date().toLocaleTimeString();
                headerStatus.style.color = 'var(--primary-color)';
            }

            // Audio Player if server-generated audio returned
            if (assistantMsg.audioUrl) {
                if (this.voiceStudio) this.voiceStudio.clearSpeechQueue();
                const player = document.createElement('audio');
                player.controls = true;
                player.src = assistantMsg.audioUrl;
                player.autoplay = true;
                player.className = 'card-audio-player';
                this.currentStreamingCard.appendChild(player);
            } else if (this.voiceStudio) {
                // If early streaming TTS was actively reading sentences, queue the tail chunk
                if (this.hasStreamTtsSpoken) {
                    const tail = this.streamingTextBuffer.substring(this.streamTtsCursor || 0).trim();
                    if (tail && tail.length >= 2) {
                        this.voiceStudio.enqueueSentence(tail);
                    }
                } else {
                    // Fallback for short / non-streamed responses: Speak full text
                    this.voiceStudio.speak(assistantMsg.content || this.streamingTextBuffer);
                }
            }
        }

        this.currentStreamingCard = null;
        this.streamingTextBuffer = '';
        this.streamTtsCursor = 0;
        this.hasStreamTtsSpoken = false;
    }

    appendAssistantMessage(text) {
        const stream = document.getElementById('chat-stream');
        if (!stream) return;

        const card = document.createElement('div');
        card.className = 'chat-card assistant-card interactive';
        card.innerHTML = `
            <div class="chat-card-header">
                <span>${this.activeAgent ? this.activeAgent.name : 'J.A.R.V.I.S.'}</span>
                <span>${new Date().toLocaleTimeString()}</span>
            </div>
            <div class="chat-card-content">${this.formatMarkdown(text)}</div>
        `;
        stream.appendChild(card);
        stream.scrollTop = stream.scrollHeight;
    }

    // =========================================================================
    // J.A.R.V.I.S. 1000x DIRECT VOICE INTENT ENGINE FOR EXTERNAL APPLICATIONS
    // Zero-latency local recognition and execution of commands for:
    // ArborNet, AgentLab (AutoLab Bench Pro), and AxiomCODAR Roslyn Studio
    // =========================================================================
    async handleExternalAppsVoiceCommand(text) {
        const lower = text.toLowerCase();
        const isExternalApp = 
            lower.includes('arbornet') || lower.includes('tensor') || lower.includes('nedk') ||
            lower.includes('agentlab') || lower.includes('autolab') || lower.includes('agent lab') ||
            lower.includes('axiom') || lower.includes('codar') || lower.includes('roslyn') ||
            lower.includes('external app') || lower.includes('ecosystem') || 
            (lower.includes('all apps') && (lower.includes('launch') || lower.includes('close') || lower.includes('build') || lower.includes('status')));

        if (!isExternalApp) return false;

        this.appendUserMessage(text);
        if (this.sfx) this.sfx.playProtocolPulse();

        // 1. Instant verbal acknowledgement via TTS
        let instantSpeech = "Processing external application directive, Sir.";
        if (lower.includes('arbornet') || lower.includes('tensor')) {
            if (lower.includes('benchmark') || lower.includes('speed')) instantSpeech = "Running ArborNet tensor benchmark now, Sir.";
            else if (lower.includes('build')) instantSpeech = "Compiling ArborNet tensor framework, Sir.";
            else if (lower.includes('test')) instantSpeech = "Running ArborNet unit test suite, Sir.";
            else if (lower.includes('model')) instantSpeech = "Checking ArborNet model weights, Sir.";
            else if (lower.includes('nedk') || lower.includes('faiss')) instantSpeech = "Querying NedK-Prime cognitive FAISS engine, Sir.";
            else instantSpeech = "Querying ArborNet telemetry, Sir.";
        } else if (lower.includes('agentlab') || lower.includes('autolab') || lower.includes('agent lab')) {
            if (lower.includes('launch') || lower.includes('open') || lower.includes('start') || lower.includes('run')) instantSpeech = "Launching AutoLab Bench Pro on host desktop, Sir.";
            else if (lower.includes('terminate') || lower.includes('close') || lower.includes('kill') || lower.includes('stop')) instantSpeech = "Closing AutoLab Bench Pro processes, Sir.";
            else if (lower.includes('build')) instantSpeech = "Building AutoLab Bench Pro project, Sir.";
            else if (lower.includes('test')) instantSpeech = "Running AutoLab test runner, Sir.";
            else if (lower.includes('workspace') || lower.includes('memory') || lower.includes('data')) instantSpeech = "Reading AutoLab critical mind workspaces, Sir.";
            else if (lower.includes('log')) instantSpeech = "Retrieving latest AutoLab execution logs, Sir.";
            else instantSpeech = "Synchronizing AutoLab telemetry, Sir.";
        } else if (lower.includes('axiom') || lower.includes('codar') || lower.includes('roslyn')) {
            if (lower.includes('launch') || lower.includes('open') || lower.includes('start')) instantSpeech = "Opening AxiomCODAR Roslyn AST Studio, Sir.";
            else if (lower.includes('terminate') || lower.includes('close') || lower.includes('kill') || lower.includes('stop')) instantSpeech = "Closing AxiomCODAR studio, Sir.";
            else if (lower.includes('build')) instantSpeech = "Compiling AxiomCODAR WPF solution, Sir.";
            else if (lower.includes('create snapshot') || lower.includes('take snapshot')) instantSpeech = "Creating a state snapshot of AxiomCODAR, Sir.";
            else if (lower.includes('snapshot')) instantSpeech = "Listing AxiomCODAR state snapshots, Sir.";
            else if (lower.includes('ast') || lower.includes('code analysis') || lower.includes('inspect')) instantSpeech = "Inspecting Roslyn AST syntax trees, Sir.";
            else instantSpeech = "Retrieving AxiomCODAR telemetry, Sir.";
        } else {
            if (lower.includes('launch') || lower.includes('open')) instantSpeech = "Launching entire external AI ecosystem, Sir.";
            else if (lower.includes('terminate') || lower.includes('close') || lower.includes('kill')) instantSpeech = "Closing all external AI applications, Sir.";
            else if (lower.includes('build')) instantSpeech = "Compiling all external AI solutions, Sir.";
            else instantSpeech = "Synchronizing full external AI ecosystem telemetry, Sir.";
        }

        if (this.voiceStudio) this.voiceStudio.speak(instantSpeech);

        try {
            const res = await fetch('/api/external-apps/voice-directive', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ directive: text })
            });
            const data = await res.json();
            if (data.success) {
                this.appendAssistantMessage(data.telemetryMarkdown || data.spokenResponse);
                if (this.sfx) this.sfx.playAccessGrantedChime();
                if (this.voiceStudio) {
                    setTimeout(() => {
                        this.voiceStudio.speak(data.spokenResponse);
                    }, 600);
                }
            } else {
                this.appendAssistantMessage(`⚠️ [Voice Directive]: ${data.telemetryMarkdown || data.spokenResponse}`);
            }
        } catch (err) {
            this.appendAssistantMessage(`[Voice Directive Error]: ${err.message}`);
        }
        return true;
    }

    // ==================== VOICE & WAKE WORD ====================
    async handleVoiceTranscript(text) {
        if (!text || !text.trim()) return;
        const clean = text.trim();
        const input = document.getElementById('chat-input');
        if (input) {
            input.value = '';
            input.placeholder = `[Transmitted: "${clean}"]`;
            setTimeout(() => {
                input.placeholder = "Speak 'Hey Jarvis' or type directives...";
            }, 3000);
        }

        // Try direct Stark Radio Station voice commands first!
        const handledRadio = await this.handleRadioVoiceCommand(clean);
        if (handledRadio) return;

        // Try direct 1000x Voice Directives Intent Engine for external apps!
        const handledDirectly = await this.handleExternalAppsVoiceCommand(clean);
        if (handledDirectly) return;

        // Otherwise pass to full LLM ReAct agent
        this.sendUserMessage(clean);
    }

    handleVoiceInterim(interimText) {
        const input = document.getElementById('chat-input');
        if (input && !input.value) {
            input.placeholder = `🎙️ "${interimText}..."`;
        }
    }

    handleWakeWordTrigger() {
        this.sfx.playProtocolPulse();
        const badge = document.getElementById('wake-word-badge');
        if (badge) {
            badge.style.display = 'inline-block';
            badge.textContent = 'WAKE CALL DETECTED: LISTENING...';
            setTimeout(() => { 
                badge.style.display = 'none'; 
                badge.textContent = 'WAKE WORD DETECTED';
            }, 5000);
        }
        const input = document.getElementById('chat-input');
        if (input && !input.value) {
            input.placeholder = "🎙️ [WAKENED: Speak your directive...]";
            input.focus();
        }
    }

    // ==================== UI EVENTS & PROTOCOLS ====================
    bindUiEvents() {
        // Send button
        const sendBtn = document.getElementById('btn-send');
        const chatInput = document.getElementById('chat-input');

        const submit = () => {
            const val = chatInput.value;
            chatInput.value = '';
            this.sendUserMessage(val);
        };

        sendBtn?.addEventListener('click', submit);
        chatInput?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') submit();
        });

        // Mic toggle
        const micBtn = document.getElementById('mic-btn');
        micBtn?.addEventListener('click', () => {
            if (this.voiceStudio) this.voiceStudio.toggleListening();
        });

        // Settings gear button
        document.getElementById('btn-open-settings')?.addEventListener('click', () => {
            this.settingsModal.open();
        });

        // Quick Protocol Chips
        document.getElementById('chip-clean-slate')?.addEventListener('click', async () => {
            await this.executeSuitProtocol('CleanSlate');
        });
        document.getElementById('chip-house-party')?.addEventListener('click', async () => {
            await this.executeSuitProtocol('HouseParty');
        });
        document.getElementById('chip-sentry-mode')?.addEventListener('click', async () => {
            await this.executeSuitProtocol('SentryMode');
        });
        document.getElementById('chip-rea-open')?.addEventListener('click', () => {
            this.settingsModal.open();
            this.settingsModal.activateTab('rea');
        });

        // ==================== SUB-HUD MENU & 3D DROPDOWN CONTROLS ====================
        const subHudMenu = document.getElementById('sub-hud-menu');
        const subHudToggle = document.getElementById('btn-sub-hud-toggle');
        const holoDropdown = document.getElementById('holo-mode-dropdown');
        const holoToggle = document.getElementById('btn-holo-dropdown-toggle');
        const holoActiveLabel = document.getElementById('active-holo-mode-label');

        subHudToggle?.addEventListener('click', (e) => {
            e.stopPropagation();
            subHudMenu?.classList.toggle('hidden');
            if (this.sfx) this.sfx.playHudBeep(750, 0.04);
        });

        document.getElementById('btn-close-sub-hud')?.addEventListener('click', () => {
            subHudMenu?.classList.add('hidden');
        });

        holoToggle?.addEventListener('click', (e) => {
            e.stopPropagation();
            holoDropdown?.classList.toggle('hidden');
            if (this.sfx) this.sfx.playHudBeep(650, 0.04);
        });

        document.querySelectorAll('.holo-dropdown-item').forEach(item => {
            item.addEventListener('click', () => {
                const mode = item.getAttribute('data-mode');
                document.querySelectorAll('.holo-dropdown-item').forEach(i => i.classList.remove('active'));
                item.classList.add('active');
                if (holoActiveLabel) holoActiveLabel.textContent = item.textContent.trim();
                holoDropdown?.classList.add('hidden');

                if (mode === 'CAD_MODEL') {
                    this.promptCadModelUpload();
                } else if (this.hologram3D) {
                    this.hologram3D.setHologramMode(mode);
                }
            });
        });

        // ==================== CENTRALIZED CLICK-OFF (OUTSIDE CLICK) AUTO-CLOSE ====================
        document.addEventListener('pointerdown', (e) => {
            // 1. Auto-close Sub-HUD Menu when clicking outside
            if (subHudMenu && !subHudMenu.classList.contains('hidden')) {
                if (!subHudMenu.contains(e.target) && !subHudToggle?.contains(e.target)) {
                    subHudMenu.classList.add('hidden');
                }
            }

            // 2. Auto-close 3D View Dropdown when clicking outside
            if (holoDropdown && !holoDropdown.classList.contains('hidden')) {
                if (!holoDropdown.contains(e.target) && !holoToggle?.contains(e.target)) {
                    holoDropdown.classList.add('hidden');
                }
            }

            // 3. Auto-close Contacts Drawer when clicking outside
            const contactsDrawer = document.getElementById('contacts-drawer');
            if (contactsDrawer && !contactsDrawer.classList.contains('hidden')) {
                const chipBtn = document.getElementById('chip-contacts-open');
                const subhudContacts = document.getElementById('subhud-item-contacts');
                const isTrigger = (chipBtn && chipBtn.contains(e.target)) || (subhudContacts && subhudContacts.contains(e.target));
                const isInsideDossier = e.target.closest('#contact-dossier-modal') || e.target.closest('#contact-edit-modal');
                if (!contactsDrawer.contains(e.target) && !isTrigger && !isInsideDossier) {
                    this.closeContactsDrawer();
                }
            }

            // 4. Auto-close In-HUD Terminal Drawer when clicking outside
            const termDrawer = document.getElementById('in-hud-terminal-drawer');
            const termTrigger = document.getElementById('subhud-item-terminal');
            if (termDrawer && !termDrawer.classList.contains('hidden')) {
                if (!termDrawer.contains(e.target) && !termTrigger?.contains(e.target)) {
                    termDrawer.classList.add('hidden');
                }
            }
        });

        // ==================== SUB-HUD ITEMS ROUTING ====================
        const closeSubHud = () => subHudMenu?.classList.add('hidden');

        document.getElementById('subhud-item-briefing')?.addEventListener('click', () => {
            closeSubHud();
            this.sendUserMessage('morning briefing');
        });

        document.getElementById('subhud-item-screenscan')?.addEventListener('click', () => {
            closeSubHud();
            this.captureAndPerceiveScreen();
        });

        document.getElementById('subhud-item-projects')?.addEventListener('click', () => {
            closeSubHud();
            this.openProjectsDrawer();
        });

        document.getElementById('subhud-item-contacts')?.addEventListener('click', (e) => {
            e.stopPropagation();
            closeSubHud();
            this.openContactsDrawer();
        });

        document.getElementById('subhud-item-dock')?.addEventListener('click', () => {
            closeSubHud();
            window.open('overlay.html', 'JarvisDock', 'width=520,height=340,toolbar=no,menubar=no');
        });

        document.getElementById('subhud-item-detach')?.addEventListener('click', () => {
            closeSubHud();
            this.openDetachedHudWindow();
        });

        // Stark Radio Deck triggers
        document.getElementById('btn-header-radio')?.addEventListener('click', () => {
            this.openRadioDeck();
        });

        document.getElementById('subhud-item-radio')?.addEventListener('click', () => {
            closeSubHud();
            this.openRadioDeck();
        });

        document.getElementById('subhud-item-terminal')?.addEventListener('click', (e) => {
            e.stopPropagation();
            closeSubHud();
            const termDrawer = document.getElementById('in-hud-terminal-drawer');
            termDrawer?.classList.toggle('hidden');
            (document.getElementById('terminal-input-field') || document.getElementById('terminal-cmd-input'))?.focus();
        });

        document.getElementById('subhud-item-scratchpad')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerClipboardDiagnosis();
        });

        document.getElementById('subhud-item-browser')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerBrowserAgent();
        });

        document.getElementById('subhud-item-swarm')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerAgentSwarm();
        });

        document.getElementById('subhud-item-meeting')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerMeetingEar();
        });

        document.getElementById('subhud-item-codebase')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerCodebaseSearch();
        });

        document.getElementById('subhud-item-media')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerMediaSummarizer();
        });

        document.getElementById('subhud-item-rea')?.addEventListener('click', () => {
            closeSubHud();
            this.settingsModal.open();
            this.settingsModal.activateTab('rea');
        });

        document.getElementById('subhud-item-docker')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerDockerInspection();
        });

        document.getElementById('subhud-item-database')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerDatabaseQuery();
        });

        document.getElementById('subhud-item-radar')?.addEventListener('click', () => {
            closeSubHud();
            this.triggerNetworkRadar();
        });

        document.getElementById('subhud-item-vault')?.addEventListener('click', () => {
            closeSubHud();
            this.settingsModal.open();
            this.settingsModal.activateTab('vault');
        });

        document.getElementById('subhud-item-snapshot')?.addEventListener('click', async () => {
            closeSubHud();
            if (this.hub) {
                const snap = await this.hub.invoke('CreateBackupSnapshot', 'Sub-HUD Manual Snapshot');
                this.appendAssistantMessage(`📦 [SNAPSHOT RECOVERY ENGINE]: Snapshot created successfully: **${snap.fileName}** (${Math.round(snap.sizeBytes / 1024)} KB). ID: \`${snap.id}\``);
                this.sfx.playAccessGrantedChime();
            }
        });

        document.getElementById('subhud-item-achievements')?.addEventListener('click', () => {
            closeSubHud();
            this.showAchievementsCard();
        });

        // =========================================================================
        // J.A.R.V.I.S. INTEGRATION NOTE: External Ecosystems Sub-HUD Handlers
        // Dispatches non-invasive inspection and management calls for external apps
        // =========================================================================
        document.getElementById('subhud-item-arbornet')?.addEventListener('click', async () => {
            closeSubHud();
            this.appendAssistantMessage('🌲 **[JARVIS INTEGRATION - ARBORNET]**: Querying ArborNet & NedK-Prime engine status...');
            try {
                const res = await fetch('/api/external-apps/arbornet', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action: 'inspect' })
                });
                const data = await res.json();
                this.appendAssistantMessage(`🌲 **[ARBORNET & NEDK-PRIME TELEMETRY]**:\n\`\`\`json\n${JSON.stringify(data, null, 2)}\n\`\`\``);
                this.sfx.playAccessGrantedChime();
            } catch (err) {
                this.appendAssistantMessage(`❌ [ArborNet Bridge Error]: ${err.message}`);
            }
        });

        document.getElementById('subhud-item-agentlab')?.addEventListener('click', async () => {
            closeSubHud();
            this.appendAssistantMessage('🧪 **[JARVIS INTEGRATION - AGENTLAB]**: Inspecting AutoLab Bench Pro workspace & telemetry...');
            try {
                const res = await fetch('/api/external-apps/agentlab', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action: 'inspect' })
                });
                const data = await res.json();
                this.appendAssistantMessage(`🧪 **[AUTOLAB BENCH PRO TELEMETRY]**:\n\`\`\`json\n${JSON.stringify(data, null, 2)}\n\`\`\``);
                this.sfx.playAccessGrantedChime();
            } catch (err) {
                this.appendAssistantMessage(`❌ [AgentLab Bridge Error]: ${err.message}`);
            }
        });

        document.getElementById('subhud-item-axiom')?.addEventListener('click', async () => {
            closeSubHud();
            this.appendAssistantMessage('📐 **[JARVIS INTEGRATION - AXIOMCODAR]**: Reading Roslyn AST Studio state & workspace snapshot...');
            try {
                const res = await fetch('/api/external-apps/axiom-codar', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action: 'inspect' })
                });
                const data = await res.json();
                this.appendAssistantMessage(`📐 **[AXIOMCODAR AST STUDIO TELEMETRY]**:\n\`\`\`json\n${JSON.stringify(data, null, 2)}\n\`\`\``);
                this.sfx.playAccessGrantedChime();
            } catch (err) {
                this.appendAssistantMessage(`❌ [AxiomCODAR Bridge Error]: ${err.message}`);
            }
        });

        document.getElementById('subhud-item-ecosystem')?.addEventListener('click', async () => {
            closeSubHud();
            this.appendAssistantMessage('🌐 **[JARVIS INTEGRATION - AI ECOSYSTEM ORCHESTRATOR]**: Querying unified status across ArborNet, AgentLab, and AxiomCODAR...');
            try {
                const res = await fetch('/api/external-apps/ecosystem', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ action: 'status' })
                });
                const data = await res.json();
                this.appendAssistantMessage(data.output || 'Ecosystem status synchronized.');
                this.sfx.playAccessGrantedChime();
            } catch (err) {
                this.appendAssistantMessage(`❌ [Ecosystem Orchestrator Error]: ${err.message}`);
            }
        });

        document.getElementById('subhud-item-voice-matrix')?.addEventListener('click', () => {
            closeSubHud();
            const voiceHelp = `
### 🎙️ J.A.R.V.I.S. 1000x EXTERNAL APPS VOICE DIRECTIVES MATRIX
*Speak any of these natural directives to Jarvis at any time for zero-latency execution:*

#### 🌲 **ArborNet & NedK-Prime**
- *"Hey Jarvis, run ArborNet benchmarks"* *(measures tensor speeds)*
- *"Hey Jarvis, compile ArborNet"* *(runs dotnet build on tensor engine)*
- *"Hey Jarvis, run ArborNet tests"* *(executes unit test suite)*
- *"Hey Jarvis, check NedK-Prime status"* *(inspects FAISS vector indexing specs)*
- *"Hey Jarvis, check ArborNet model checkpoints"* *(lists .bin & .onnx weights)*
- *"Hey Jarvis, ArborNet status"* *(summarizes solution architecture)*

#### 🧪 **AgentLab (AutoLab Bench Pro)**
- *"Hey Jarvis, launch AgentLab"* *(opens AutoLab Bench Pro WPF window)*
- *"Hey Jarvis, close AutoLab"* *(safely terminates AutoLab processes)*
- *"Hey Jarvis, build AgentLab"* *(compiles AutoLab solution)*
- *"Hey Jarvis, test AgentLab"* *(runs AutoLab test harness)*
- *"Hey Jarvis, inspect AutoLab workspaces"* *(lists cognitive data folders & memory files)*
- *"Hey Jarvis, check AutoLab logs"* *(reads recent execution logs)*

#### 📐 **AxiomCODAR Roslyn Studio**
- *"Hey Jarvis, launch Axiom CODAR"* *(opens AxiomCODAR WPF window)*
- *"Hey Jarvis, close Axiom"* *(terminates AxiomCODAR process)*
- *"Hey Jarvis, compile Axiom CODAR"* *(runs dotnet build on WPF project)*
- *"Hey Jarvis, create Axiom snapshot"* *(archives current state into a new zip)*
- *"Hey Jarvis, list Axiom snapshots"* *(displays existing snapshot archives)*
- *"Hey Jarvis, inspect Roslyn AST"* *(computes C# syntax metrics & compilation readiness)*

#### 🌐 **Unified Ecosystem Suite**
- *"Hey Jarvis, launch all external apps"* *(starts AgentLab & AxiomCODAR concurrently)*
- *"Hey Jarvis, close all external apps"* *(terminates external processes)*
- *"Hey Jarvis, compile entire external suite"* *(builds ArborNet, AgentLab, and AxiomCODAR)*
- *"Hey Jarvis, ecosystem status"* *(displays full multi-app health dashboard)*
`;
            this.appendAssistantMessage(voiceHelp);
            if (this.voiceStudio) this.voiceStudio.speak("Sir, here is the complete 1000x voice directives matrix for your external applications.");
            this.sfx.playAccessGrantedChime();
        });

        // ==================== IN-HUD TERMINAL HANDLERS ====================
        const termInput = document.getElementById('terminal-input-field') || document.getElementById('terminal-cmd-input');
        const termOutput = document.getElementById('terminal-output-window');
        const termDrawer = document.getElementById('in-hud-terminal-drawer');

        const execTerminalCmd = async () => {
            if (!termInput) return;
            const cmd = termInput.value.trim();
            if (!cmd) return;
            termInput.value = '';
            if (termOutput) {
                termOutput.textContent += `\nPS STARK> ${cmd}\n`;
                termOutput.scrollTop = termOutput.scrollHeight;
            }
            try {
                const res = await this.hub.invoke('ExecuteInteractiveTerminal', cmd);
                if (termOutput) {
                    if (res.standardOutput) termOutput.textContent += res.standardOutput;
                    if (res.standardError) termOutput.textContent += `[Error]: ${res.standardError}\n`;
                    termOutput.scrollTop = termOutput.scrollHeight;
                }
                if (this.sfx) this.sfx.playHudBeep(880, 0.04);
            } catch (err) {
                if (termOutput) termOutput.textContent += `[Execution Failure]: ${err.message}\n`;
            }
        };

        const clearTerminal = () => {
            if (termOutput) termOutput.textContent = '[STARK TACTICAL TERMINAL READY - POWERSHELL SUBSYSTEM ENGAGED]\nType any PowerShell, git, dotnet, or system command and press Enter.\n';
        };

        const closeTerminal = () => {
            termDrawer?.classList.add('hidden');
        };

        document.getElementById('btn-clear-terminal')?.addEventListener('click', clearTerminal);
        document.getElementById('btn-term-clear')?.addEventListener('click', clearTerminal);
        document.getElementById('btn-close-terminal-drawer')?.addEventListener('click', closeTerminal);
        document.getElementById('btn-term-close')?.addEventListener('click', closeTerminal);
        document.getElementById('btn-exec-terminal')?.addEventListener('click', execTerminalCmd);

        termInput?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                execTerminalCmd();
            }
        });

        // Global Desktop Hotkey (Alt+Space or Ctrl+Shift+J)
        window.addEventListener('keydown', (e) => {
            if ((e.altKey && e.code === 'Space') || (e.ctrlKey && e.shiftKey && e.code === 'KeyJ')) {
                e.preventDefault();
                const chatInput = document.getElementById('chat-input');
                if (chatInput) chatInput.focus();
                if (this.sfx) this.sfx.playHudBeep(980, 0.05);
            }
        });

        // Live Biometric Camera Scanner Toggle
        document.getElementById('btn-toggle-cam')?.addEventListener('click', () => {
            this.toggleBiometricCamera();
        });

        // Ambient Soundscape Controls
        document.getElementById('btn-soundscape-arc')?.addEventListener('click', () => {
            if (this.sfx) this.sfx.startSoundscape('ArcReactor', (document.getElementById('soundscape-vol')?.value || 25) / 100);
        });
        document.getElementById('btn-soundscape-rain')?.addEventListener('click', () => {
            if (this.sfx) this.sfx.startSoundscape('CyberRain', (document.getElementById('soundscape-vol')?.value || 25) / 100);
        });
        document.getElementById('btn-soundscape-turbine')?.addEventListener('click', () => {
            if (this.sfx) this.sfx.startSoundscape('Turbine', (document.getElementById('soundscape-vol')?.value || 25) / 100);
        });
        document.getElementById('btn-soundscape-off')?.addEventListener('click', () => {
            if (this.sfx) this.sfx.stopSoundscape();
        });
        document.getElementById('soundscape-vol')?.addEventListener('input', (e) => {
            if (this.sfx) this.sfx.setSoundscapeVolume(e.target.value / 100);
        });

        // Legacy / Fallback Mode Selector Pills (if any remain)
        document.querySelectorAll('.holo-mode-pill').forEach(pill => {
            pill.addEventListener('click', () => {
                const mode = pill.getAttribute('data-mode');
                if (mode === 'CAD_MODEL') {
                    this.promptCadModelUpload();
                } else if (this.hologram3D) {
                    this.hologram3D.setHologramMode(mode);
                }
            });
        });

        // Contacts Drawer Event Listeners
        document.getElementById('btn-open-contacts')?.addEventListener('click', (e) => {
            e.stopPropagation();
            this.toggleContactsDrawer();
        });
        document.getElementById('chip-contacts-open')?.addEventListener('click', () => this.openContactsDrawer());
        document.getElementById('btn-close-contacts-drawer')?.addEventListener('click', () => this.closeContactsDrawer());
        document.getElementById('btn-add-contact-drawer')?.addEventListener('click', () => {
            this.settingsModal.openContactEditModal(null);
        });
        document.getElementById('contacts-search-input')?.addEventListener('input', (e) => {
            this.filterContacts(e.target.value);
        });
        document.querySelectorAll('#contacts-drawer-tag-filters .contact-filter-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('#contacts-drawer-tag-filters .contact-filter-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.filterContactsByTag(btn.getAttribute('data-filter'));
            });
        });

        // Strategic Projects Drawer & Modal Listeners
        document.getElementById('btn-open-projects')?.addEventListener('click', () => this.toggleProjectsDrawer());
        document.getElementById('btn-close-projects-drawer')?.addEventListener('click', () => this.closeProjectsDrawer());
        document.getElementById('btn-drawer-new-project')?.addEventListener('click', () => {
            this.closeProjectsDrawer();
            if (this.settingsModal) {
                this.settingsModal.open();
                this.settingsModal.activateTab('projects');
            }
        });
        document.getElementById('btn-drawer-quick-research')?.addEventListener('click', () => {
            if (this.settingsModal) {
                this.settingsModal.openResearchModal(null);
            }
        });

        // Contact Biometric Dossier Popup Modal Listeners
        document.getElementById('btn-close-dossier-modal')?.addEventListener('click', () => this.closeContactDossier());
        document.getElementById('btn-dossier-edit')?.addEventListener('click', () => {
            if (this.activeDossierContact && this.settingsModal) {
                const c = this.activeDossierContact;
                this.closeContactDossier();
                this.settingsModal.openContactEditModal(c);
            }
        });
        document.getElementById('btn-dossier-schedule')?.addEventListener('click', () => {
            if (this.activeDossierContact) {
                const c = this.activeDossierContact;
                this.closeContactDossier();
                this.scheduleAppointmentForContact(c);
            }
        });
        document.getElementById('btn-dossier-comms')?.addEventListener('click', () => {
            if (this.activeDossierContact) {
                this.transmitCommsToContact(this.activeDossierContact);
            }
        });

        // Appointment Reminder Modal Buttons
        document.getElementById('btn-appt-dismiss')?.addEventListener('click', async () => {
            if (this.activeReminderApptId) {
                if (this.hub && this.hub.state === signalR.HubConnectionState.Connected) {
                    try { await this.hub.invoke('DismissAppointment', this.activeReminderApptId); } catch {}
                } else {
                    await fetch(`/api/calendar/appointments/${this.activeReminderApptId}/dismiss`, { method: 'POST' });
                }
            }
            this.hideAppointmentReminder(this.activeReminderApptId);
            if (this.voiceStudio) this.voiceStudio.stopSpeaking();
            this.sfx.playAccessGrantedChime();
        });

        document.getElementById('btn-appt-cancel')?.addEventListener('click', async () => {
            if (this.activeReminderApptId) {
                if (this.hub && this.hub.state === signalR.HubConnectionState.Connected) {
                    try { await this.hub.invoke('CancelAppointment', this.activeReminderApptId); } catch {}
                } else {
                    await fetch(`/api/calendar/appointments/${this.activeReminderApptId}`, { method: 'DELETE' });
                }
            }
            this.hideAppointmentReminder(this.activeReminderApptId);
            if (this.voiceStudio) this.voiceStudio.stopSpeaking();
            this.sfx.playHudBeep(400, 0.1);
        });

        document.getElementById('btn-appt-snooze')?.addEventListener('click', () => {
            const modal = document.getElementById('appointment-reminder-modal');
            if (modal) modal.classList.add('hidden');
            if (this.voiceStudio) this.voiceStudio.stopSpeaking();
            this.sfx.playHudBeep(600, 0.05);
            // Re-show after 5 minutes if still active
            const currentId = this.activeReminderApptId;
            setTimeout(() => {
                if (this.activeReminderApptId === currentId) {
                    modal?.classList.remove('hidden');
                    this.sfx.playProtocolPulse();
                }
            }, 300000);
        });

        // Interactive Full-Screen Canvas target click
        window.addEventListener('click', (e) => {
            // If click is not inside a HUD interactive panel, spawn a holographic target reticle
            if (!e.target.closest('.interactive')) {
                this.hudCanvas.renderCustomHologramCard(e.clientX, e.clientY, 180, 80, 'TARGET ACQUIRED', [
                    `Coords: [${e.clientX}, ${e.clientY}]`,
                    `Grid: Sector 4-G`,
                    `Telemetry: Locked`
                ]);
                this.sfx.playHudBeep(1200, 0.04);
            }
        });
    }

    // ==================== REMINDERS & NOTIFICATION ALERTS ====================
    handleAppointmentReminder(alert) {
        if (!alert || !alert.appointment) return;
        const appt = alert.appointment;
        this.activeReminderApptId = appt.id;

        const modal = document.getElementById('appointment-reminder-modal');
        if (modal) {
            const titleEl = document.getElementById('appt-reminder-title');
            const timeEl = document.getElementById('appt-reminder-time');
            const attendeesEl = document.getElementById('appt-reminder-attendees');
            const descEl = document.getElementById('appt-reminder-desc');

            if (titleEl) titleEl.textContent = appt.title;
            if (attendeesEl) attendeesEl.textContent = appt.attendees || 'None';
            if (descEl) descEl.textContent = appt.description || 'Stark Tactical Agenda';
            if (timeEl) {
                const d = new Date(appt.startTime);
                timeEl.textContent = isNaN(d) ? appt.startTime : d.toLocaleString([], { weekday: 'short', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
            }

            modal.classList.remove('hidden');
        }

        // Sound alert
        this.sfx.playProtocolPulse();

        // Verbal reminder
        if (this.voiceStudio && alert.verbalAlertText) {
            this.voiceStudio.speak(alert.verbalAlertText);
        }

        // Stream notification card
        this.appendAssistantMessage(`⏰ **[TACTICAL APPOINTMENT ALARM]**: **${appt.title}**${appt.attendees ? ` with *${appt.attendees}*` : ''} is upcoming at **${new Date(appt.startTime).toLocaleTimeString()}**.`);
    }

    hideAppointmentReminder(apptId) {
        if (!apptId || this.activeReminderApptId === apptId) {
            const modal = document.getElementById('appointment-reminder-modal');
            if (modal) modal.classList.add('hidden');
            this.activeReminderApptId = null;
        }
    }

    handleAgentTaskCompleted(data) {
        if (!data || !data.task) return;
        const task = data.task;
        this.sfx.playAccessGrantedChime();

        const card = document.createElement('div');
        card.className = 'chat-card assistant-card interactive';
        card.innerHTML = `
            <div class="chat-card-header" style="border-bottom:1px solid rgba(0,212,255,0.3);padding-bottom:4px;margin-bottom:6px;">
                <span style="color:#00d4ff;font-weight:700;">🤖 AUTONOMOUS AGENT REPORT: ${this.escape(task.assignedAgentName || 'J.A.R.V.I.S.')}</span>
                <span style="color:#aaa;font-size:0.7rem;">${new Date().toLocaleTimeString()}</span>
            </div>
            <div style="font-size:0.8rem;color:#7eb2cc;margin-bottom:6px;">
                <strong>Directive:</strong> ${this.escape(task.name)} (${task.runsPerDay}x/day)
            </div>
            <div class="chat-card-content" style="max-height:220px;overflow-y:auto;background:rgba(0,10,25,0.4);padding:8px;border-radius:4px;">
                ${this.formatMarkdown(data.findings || 'Autonomous scan executed successfully.')}
            </div>
        `;

        const stream = document.getElementById('chat-stream');
        if (stream) {
            stream.appendChild(card);
            stream.scrollTop = stream.scrollHeight;
        }

        if (this.voiceStudio) {
            this.voiceStudio.speak(`Sir, agent ${task.assignedAgentName || 'Jarvis'} has completed autonomous research on ${task.name}. Results logged.`);
        }
    }

    handleLowSpaceAlert(alert) {
        if (!alert) return;
        this.sfx.playAccessDeniedAlarm();

        this.appendAssistantMessage(`⚠ **[STORAGE TELEMETRY WARNING]**: Drive **${alert.driveName}** (${alert.volumeLabel || 'Local'}) free space is critically low at **${alert.freeSpaceGb.toFixed(1)} GB** (${alert.percentFree.toFixed(1)}% free). Threshold: ${alert.thresholdGb} GB.`);

        if (this.voiceStudio) {
            this.voiceStudio.speak(`Sir, caution. Drive space on ${alert.driveName} is critically low.`);
        }
    }

    async executeSuitProtocol(protocol) {
        this.sfx.playProtocolPulse();
        try {
            const res = await fetch('/api/tools/execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    toolNameOrId: 'suit_protocol_control',
                    arguments: { protocol }
                })
            });
            const data = await res.json();
            this.appendAssistantMessage(`[Stark Protocol]: ${data.output}`);
        } catch (e) {
            console.error(e);
        }
    }

    // ==================== STARK SECURE CONTACTS DRAWER ====================
    async openContactsDrawer() {
        const drawer = document.getElementById('contacts-drawer');
        if (drawer) {
            drawer.classList.remove('hidden');
            if (!this.contacts || this.contacts.length === 0) {
                try {
                    const res = await fetch('/api/contacts', {
                        headers: this.authToken ? { 'Authorization': `Bearer ${this.authToken}` } : {}
                    });
                    if (res.ok) {
                        this.contacts = await res.json();
                    }
                } catch (e) {
                    console.warn("Failed to load contacts:", e);
                }
            }
            this.renderContactsList(this.contacts);
            this.sfx.playHudBeep(780, 0.05);
        }
    }

    closeContactsDrawer() {
        const drawer = document.getElementById('contacts-drawer');
        if (drawer) {
            drawer.classList.add('hidden');
            this.sfx.playHudBeep(420, 0.04);
        }
    }

    toggleContactsDrawer() {
        const drawer = document.getElementById('contacts-drawer');
        if (!drawer) return;
        if (drawer.classList.contains('hidden')) {
            this.openContactsDrawer();
        } else {
            this.closeContactsDrawer();
        }
    }

    filterContacts(query) {
        this.contactsSearchQuery = (query || '').toLowerCase().trim();
        this.renderContactsList(this.contacts);
    }

    getContactTags(c) {
        if (!c || !c.tags) return [];
        if (Array.isArray(c.tags)) return c.tags;
        return String(c.tags).split(',').map(t => t.trim()).filter(Boolean);
    }

    filterContactsByTag(tag) {
        this.activeContactsFilter = tag || 'ALL';
        this.renderContactsList(this.contacts);
    }

    renderContactsList(contacts) {
        const container = document.getElementById('contacts-cards-list');
        const countLabel = document.getElementById('contacts-count-label');
        if (!container) return;

        container.innerHTML = '';
        const filter = (this.activeContactsFilter || 'ALL').toUpperCase();
        const q = (this.contactsSearchQuery || '').toLowerCase();

        const list = (contacts || this.contacts || []).filter(c => {
            const tags = this.getContactTags(c).map(t => t.toUpperCase());
            if (filter !== 'ALL') {
                if (!tags.includes(filter)) return false;
            }
            if (q) {
                const name = c.fullName || c.name || '';
                const matchStr = `${name} ${c.callSign || ''} ${c.role || ''} ${c.organization || ''} ${c.primaryPhone || ''} ${c.secondaryPhone || ''} ${c.primaryEmail || ''} ${c.workEmail || ''} ${tags.join(' ')}`.toLowerCase();
                if (!matchStr.includes(q)) return false;
            }
            return true;
        });

        if (countLabel) {
            countLabel.textContent = `${list.length} CONTACTS ENCRYPTED`;
        }

        if (list.length === 0) {
            container.innerHTML = `
                <div style="text-align:center;padding:30px 10px;color:#6fa0bc;">
                    <div style="font-size:2rem;margin-bottom:8px;">🔍</div>
                    <div style="font-family:var(--font-hud);font-size:0.85rem;color:#fff;">NO CONTACTS FOUND</div>
                    <p style="font-size:0.75rem;margin-top:4px;">No contacts match current query or filter tags.</p>
                </div>
            `;
            return;
        }

        list.forEach(c => {
            const card = document.createElement('div');
            card.className = 'contact-card interactive';

            const name = c.fullName || c.name || 'Unnamed Contact';
            const initials = name.split(' ').map(n => n[0]).slice(0, 2).join('').toUpperCase();
            const color = c.avatarColor || '#00d4ff';

            const clrBadge = c.clearanceLevel >= 10 
                ? `<span class="clearance-badge clearance-lvl-10">LVL ${c.clearanceLevel} VIP</span>` 
                : (c.clearanceLevel >= 8 ? `<span class="clearance-badge clearance-lvl-high">LVL ${c.clearanceLevel} HIGH</span>` : `<span class="clearance-badge clearance-lvl-std">LVL ${c.clearanceLevel} STD</span>`);

            const phoneList = [];
            if (c.primaryPhone) phoneList.push(`
                <div class="comms-row">
                    <span class="comms-label">PRI TEL:</span>
                    <a href="tel:${encodeURIComponent(c.primaryPhone)}" class="comms-link" title="Call">📞 ${this.escape(c.primaryPhone)}</a>
                </div>
            `);
            if (c.secondaryPhone) phoneList.push(`
                <div class="comms-row">
                    <span class="comms-label">SEC TEL:</span>
                    <a href="tel:${encodeURIComponent(c.secondaryPhone)}" class="comms-link" title="Satellite Call">📡 ${this.escape(c.secondaryPhone)}</a>
                </div>
            `);

            const emailList = [];
            if (c.primaryEmail) emailList.push(`
                <div class="comms-row">
                    <span class="comms-label">EMAIL:</span>
                    <a href="mailto:${encodeURIComponent(c.primaryEmail)}" class="comms-link" title="Send Email">✉️ ${this.escape(c.primaryEmail)}</a>
                </div>
            `);
            if (c.workEmail) emailList.push(`
                <div class="comms-row">
                    <span class="comms-label">SECURE:</span>
                    <a href="mailto:${encodeURIComponent(c.workEmail)}" class="comms-link" title="Secure Email">💼 ${this.escape(c.workEmail)}</a>
                </div>
            `);

            const commsHtml = (phoneList.length > 0 || emailList.length > 0)
                ? `<div class="contact-comms-box">${phoneList.join('')}${emailList.join('')}</div>`
                : '';

            const tagBadges = this.getContactTags(c).map(t => `<span class="contact-tag-badge">${this.escape(t)}</span>`).join('');
            const tagsHtml = tagBadges ? `<div class="contact-tags-row">${tagBadges}</div>` : '';
            const notesHtml = c.notes ? `<div class="contact-notes-text">${this.escape(c.notes)}</div>` : '';

            const avatarHtml = c.avatarUrl 
                ? `<div class="contact-avatar-wrapper" style="width:48px;height:48px;cursor:pointer;" title="Click to inspect Biometric Dossier">
                     <img src="${this.escape(c.avatarUrl)}" class="contact-avatar-img" onerror="this.style.display='none';">
                     <div class="contact-face-reticle"></div>
                   </div>`
                : `<div class="contact-avatar" style="background:linear-gradient(135deg, ${color}33, ${color}99);border-color:${color};box-shadow:0 0 12px ${color}66;cursor:pointer;" title="Click to inspect Biometric Dossier">
                     ${initials}
                   </div>`;

            card.innerHTML = `
                <div class="contact-card-header" style="cursor:pointer;" title="Click to view Biometric Dossier">
                    ${avatarHtml}
                    <div class="contact-identity">
                        <div class="contact-name">${this.escape(name)}</div>
                        ${c.callSign ? `<div class="contact-callsign">"${this.escape(c.callSign)}"</div>` : ''}
                        <div class="contact-role-org">${this.escape(c.role || 'Operative')} • ${this.escape(c.organization || 'Stark Industries')}</div>
                    </div>
                    ${clrBadge}
                </div>
                ${commsHtml}
                ${tagsHtml}
                ${notesHtml}
                <div class="contact-actions-row">
                    <button class="hud-btn hud-btn-primary btn-card-dossier" style="flex:1;padding:4px 8px;font-size:0.75rem;font-weight:700;" title="Inspect Face & Dossier">👁️ DOSSIER</button>
                    <button class="hud-btn btn-card-schedule" style="padding:4px 8px;font-size:0.75rem;" title="Schedule Appointment">📅 SCHEDULE</button>
                    <button class="hud-btn btn-card-msg" style="padding:4px 8px;font-size:0.75rem;" title="Message / Directive via Jarvis">💬</button>
                    <button class="hud-btn btn-card-edit" style="padding:4px 8px;font-size:0.75rem;" title="Edit Contact">✏️</button>
                    <button class="hud-btn hud-btn-danger btn-card-delete" style="padding:4px 8px;font-size:0.75rem;" title="Delete Contact">🗑️</button>
                </div>
            `;

            // Open Dossier on Header or Avatar or Dossier Button click
            card.querySelector('.contact-card-header')?.addEventListener('click', (e) => {
                if (!e.target.closest('button') && !e.target.closest('a')) {
                    this.openContactDossier(c);
                }
            });

            card.querySelector('.btn-card-dossier')?.addEventListener('click', () => {
                this.openContactDossier(c);
            });

            card.querySelector('.btn-card-schedule')?.addEventListener('click', () => {
                this.scheduleAppointmentForContact(c);
            });

            card.querySelector('.btn-card-msg')?.addEventListener('click', () => {
                this.messageContact(c);
            });

            card.querySelector('.btn-card-edit')?.addEventListener('click', () => {
                if (this.settingsModal) this.settingsModal.openContactEditModal(c);
            });

            card.querySelector('.btn-card-delete')?.addEventListener('click', () => {
                if (this.settingsModal) this.settingsModal.deleteContact(c.id, c.name);
            });

            container.appendChild(card);
        });
    }

    scheduleAppointmentForContact(contact) {
        this.closeContactsDrawer();
        if (this.settingsModal) {
            this.settingsModal.open();
            const tabBtn = document.querySelector('.tab-btn[data-tab="calendar"]');
            if (tabBtn) tabBtn.click();

            const titleInput = document.getElementById('new-appt-title');
            const attendeesInput = document.getElementById('new-appt-attendees');
            if (titleInput) titleInput.value = `Briefing with ${contact.name}`;
            if (attendeesInput) attendeesInput.value = contact.name;
        }
        this.sfx.playAccessGrantedChime();
    }

    messageContact(contact) {
        this.closeContactsDrawer();
        const input = document.getElementById('chat-input');
        if (input) {
            input.value = `Schedule meeting with ${contact.name} to discuss `;
            input.focus();
        }
    }

    // ==================== BIOMETRIC DOSSIER & PROJECTS DRAWER ====================
    openContactDossier(c) {
        if (!c) return;
        this.activeDossierContact = c;

        const modal = document.getElementById('contact-dossier-modal');
        if (!modal) return;

        // Face Image
        const img = document.getElementById('dossier-face-img');
        if (img) {
            img.src = c.avatarUrl || 'https://images.unsplash.com/photo-1534528741775-53994a69daeb?w=300&h=300&fit=crop&crop=faces';
            img.style.display = 'block';
        }

        // Details
        const nameEl = document.getElementById('dossier-name');
        if (nameEl) nameEl.textContent = c.fullName || c.name || 'Operative';

        const callSignEl = document.getElementById('dossier-callsign');
        if (callSignEl) {
            callSignEl.textContent = c.callSign ? `"${c.callSign}"` : '';
            callSignEl.style.display = c.callSign ? 'block' : 'none';
        }

        const roleEl = document.getElementById('dossier-role');
        if (roleEl) roleEl.textContent = `${c.role || 'Operative'} • ${c.organization || 'Stark Industries'}`;

        const badgeEl = document.getElementById('dossier-clearance-badge');
        if (badgeEl) badgeEl.textContent = `LEVEL ${c.clearanceLevel || 1}`;

        const tagsRow = document.getElementById('dossier-tags-row');
        if (tagsRow) {
            tagsRow.innerHTML = this.getContactTags(c).map(t => `<span class="contact-tag-badge">${this.escape(t)}</span>`).join(' ');
        }

        const priPhoneEl = document.getElementById('dossier-phone-primary');
        if (priPhoneEl) priPhoneEl.textContent = c.primaryPhone || 'None';

        const secPhoneEl = document.getElementById('dossier-phone-secondary');
        if (secPhoneEl) secPhoneEl.textContent = c.secondaryPhone || 'None';

        const priEmailEl = document.getElementById('dossier-email-primary');
        if (priEmailEl) priEmailEl.textContent = c.primaryEmail || 'None';

        const notesEl = document.getElementById('dossier-notes');
        if (notesEl) notesEl.textContent = c.notes || 'No security notes recorded.';

        modal.classList.remove('hidden');
        this.sfx.playProtocolPulse();
    }

    closeContactDossier() {
        const modal = document.getElementById('contact-dossier-modal');
        if (modal) {
            modal.classList.add('hidden');
            this.sfx.playHudBeep(450, 0.04);
        }
        this.activeDossierContact = null;
    }

    openProjectsDrawer(projectId = null) {
        if (this.projectsStudio) {
            this.projectsStudio.open(projectId);
        } else {
            const modal = document.getElementById('projects-fullscreen-modal');
            if (modal) modal.classList.remove('hidden');
        }
    }

    closeProjectsDrawer() {
        if (this.projectsStudio) {
            this.projectsStudio.close();
        } else {
            const modal = document.getElementById('projects-fullscreen-modal');
            if (modal) modal.classList.add('hidden');
        }
    }

    toggleProjectsDrawer(projectId = null) {
        if (this.projectsStudio) {
            if (this.projectsStudio.modalEl && !this.projectsStudio.modalEl.classList.contains('hidden')) {
                this.projectsStudio.close();
            } else {
                this.projectsStudio.open(projectId);
            }
        } else {
            const modal = document.getElementById('projects-fullscreen-modal');
            if (modal) modal.classList.toggle('hidden');
        }
    }

    transmitCommsToContact(contact) {
        if (!contact) return;
        this.closeContactDossier();
        const input = document.getElementById('chat-input');
        if (input) {
            input.value = `Initiate secure comms uplink with ${contact.name}`;
            input.focus();
        }
        this.sfx.playHudBeep(1000, 0.08);
    }

    formatMarkdown(text) {
        if (!text) return '';
        // Bold
        let formatted = text.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
        // Inline code
        formatted = formatted.replace(/`([^`]+)`/g, '<code>$1</code>');
        // Fenced code blocks
        formatted = formatted.replace(/```([a-zA-Z]*)\n([\s\S]*?)```/g, '<pre><code>$2</code></pre>');
        // Lists
        formatted = formatted.replace(/^\* (.*$)/gim, '• $1');
        return formatted;
    }

    // ==================== ADVANCED EXTENSION SUITE (ITEMS 2, 3, 4, 15, 18) ====================
    async captureAndPerceiveScreen() {
        try {
            if (!navigator.mediaDevices || !navigator.mediaDevices.getDisplayMedia) {
                alert("Screen Capture API is not supported in this browser environment.");
                return;
            }

            this.sfx.playProtocolPulse();
            this.appendUserMessage("👁️ [MULTIMODAL PERCEPTION]: Initiating Desktop Screen Scan...");

            const stream = await navigator.mediaDevices.getDisplayMedia({
                video: { cursor: "always" },
                audio: false
            });

            const video = document.createElement('video');
            video.srcObject = stream;
            await video.play();

            const canvas = document.createElement('canvas');
            canvas.width = video.videoWidth || 1920;
            canvas.height = video.videoHeight || 1080;
            const ctx = canvas.getContext('2d');
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

            // Clean up screen capture video tracks immediately
            stream.getTracks().forEach(track => track.stop());

            const base64Jpeg = canvas.toDataURL('image/jpeg', 0.85);

            this.appendAssistantMessage("Optical telemetry acquired. Analyzing visual buffer via Vision Multimodal Pipeline...");

            const response = await fetch('/api/vision/screen', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(this.authToken ? { 'Authorization': `Bearer ${this.authToken}` } : {})
                },
                body: JSON.stringify({
                    prompt: "Perform a detailed tactical inspection of this screen workspace. Identify running code, errors, active tools, and recommend immediate optimization steps.",
                    imageBase64: base64Jpeg
                })
            });

            const result = await response.json();
            if (result.analysis) {
                this.appendAssistantMessage(result.analysis);
                if (this.voiceStudio) {
                    this.voiceStudio.speak("Optical analysis complete, Sir. Telemetry rendered to screen.");
                }
            } else {
                this.appendAssistantMessage("Visual processing completed with no anomalies detected.");
            }
        } catch (err) {
            console.warn("Screen scan cancelled or failed:", err);
            this.appendAssistantMessage(`Screen perception cancelled or denied: ${err.message}`);
        }
    }

    openDetachedHudWindow() {
        this.sfx.playAccessGrantedChime();
        const popup = window.open('overlay.html', 'Jarvis_Detached_HUD', 'width=580,height=750,menubar=no,toolbar=no,status=no,resizable=yes');
        if (!popup) {
            alert("Popup blocked by browser. Please allow popups for detached multi-monitor HUD.");
        }
    }

    async toggleBiometricCamera() {
        const container = document.getElementById('cam-container');
        const video = document.getElementById('biometric-video');
        const canvas = document.getElementById('biometric-canvas');
        const badge = document.getElementById('biometric-presence-badge');

        if (!container || !video || !canvas) return;

        if (this.isBiometricActive) {
            // Turn Off
            this.isBiometricActive = false;
            container.style.display = 'none';
            if (this.biometricStream) {
                this.biometricStream.getTracks().forEach(t => t.stop());
                this.biometricStream = null;
            }
            if (this.biometricAnimId) {
                cancelAnimationFrame(this.biometricAnimId);
                this.biometricAnimId = null;
            }
            if (badge) {
                badge.textContent = 'STANDBY';
                badge.style.color = '#79d6ff';
            }
            this.sfx.playHudBeep(440, 0.05);
        } else {
            // Turn On
            try {
                this.biometricStream = await navigator.mediaDevices.getUserMedia({
                    video: { width: 320, height: 240, facingMode: 'user' }
                });
                video.srcObject = this.biometricStream;
                await video.play();

                this.isBiometricActive = true;
                container.style.display = 'block';
                if (badge) {
                    badge.textContent = 'PRESENCE: VERIFIED';
                    badge.style.color = '#39ff14';
                }
                this.sfx.playAccessGrantedChime();

                // Holographic HUD Biometric Reticle Tracking Loop
                const ctx = canvas.getContext('2d');
                let scanY = 10;
                let scanSpeed = 2;

                const renderBiometricHud = () => {
                    if (!this.isBiometricActive) return;
                    canvas.width = video.videoWidth || 320;
                    canvas.height = video.videoHeight || 240;

                    ctx.clearRect(0, 0, canvas.width, canvas.height);

                    // Bounding Box
                    const cx = canvas.width / 2;
                    const cy = canvas.height / 2;
                    const boxW = canvas.width * 0.55;
                    const boxH = canvas.height * 0.65;
                    const x0 = cx - boxW / 2;
                    const y0 = cy - boxH / 2;

                    ctx.strokeStyle = '#39ff14';
                    ctx.lineWidth = 2;

                    // Corner brackets
                    const cLen = 14;
                    // Top-Left
                    ctx.beginPath();
                    ctx.moveTo(x0, y0 + cLen); ctx.lineTo(x0, y0); ctx.lineTo(x0 + cLen, y0);
                    // Top-Right
                    ctx.moveTo(x0 + boxW - cLen, y0); ctx.lineTo(x0 + boxW, y0); ctx.lineTo(x0 + boxW, y0 + cLen);
                    // Bottom-Left
                    ctx.moveTo(x0, y0 + boxH - cLen); ctx.lineTo(x0, y0 + boxH); ctx.lineTo(x0 + cLen, y0 + boxH);
                    // Bottom-Right
                    ctx.moveTo(x0 + boxW - cLen, y0 + boxH); ctx.lineTo(x0 + boxW, y0 + boxH); ctx.lineTo(x0 + boxW, y0 + boxH - cLen);
                    ctx.stroke();

                    // Laser scanline
                    scanY += scanSpeed;
                    if (scanY > y0 + boxH || scanY < y0) scanSpeed = -scanSpeed;
                    ctx.strokeStyle = 'rgba(0, 212, 255, 0.85)';
                    ctx.beginPath();
                    ctx.moveTo(x0, scanY);
                    ctx.lineTo(x0 + boxW, scanY);
                    ctx.stroke();

                    // Reticle HUD telemetry
                    ctx.fillStyle = '#39ff14';
                    ctx.font = '10px monospace';
                    ctx.fillText('TARGET: STARK, ANTHONY E.', x0, y0 - 6);
                    ctx.fillText('HEART RATE: 72 BPM // BIO MATCH: 99.4%', x0, y0 + boxH + 14);

                    this.biometricAnimId = requestAnimationFrame(renderBiometricHud);
                };

                renderBiometricHud();
            } catch (err) {
                console.warn("Biometric camera access denied or error:", err);
                alert("Optical Biometric Scanner could not access camera: " + err.message);
            }
        }
    }

    promptCadModelUpload() {
        if (!this.hologram3D) return;

        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.obj,.stl,.txt';
        input.style.display = 'none';

        input.onchange = (e) => {
            const file = e.target.files[0];
            if (!file) return;

            const reader = new FileReader();
            reader.onload = (readEvent) => {
                const text = readEvent.target.result;
                const geo = this.hologram3D.loadObjGeometry(text);
                this.hologram3D.displayCadMesh(geo, file.name);
                this.appendAssistantMessage(`📐 [3D CAD SCHEMATIC LOADED]: Rendering holographic wireframe model for **${file.name}**. Interactive rotation and inspection engaged.`);
                this.sfx.playProtocolPulse();
            };
            reader.readAsText(file);
        };

        // Switch to CAD mode immediately
        this.hologram3D.setHologramMode('CAD_MODEL');
        this.sfx.playHudBeep(720, 0.05);

        // Offer to load user's custom file
        if (confirm("Switch to 3D CAD Hologram Viewer?\\n\\nPress OK to browse for a custom .OBJ or .STL 3D mesh model, or Cancel to inspect the default Arc Reactor CAD schematic.")) {
            document.body.appendChild(input);
            input.click();
            setTimeout(() => input.remove(), 1000);
        }
    }

    appendAssistantCard(innerHtml) {
        const card = document.createElement('div');
        card.className = 'chat-card assistant-card interactive';
        card.innerHTML = innerHtml;
        const stream = document.getElementById('chat-stream');
        if (stream) {
            stream.appendChild(card);
            stream.scrollTop = stream.scrollHeight;
        }
    }

    // ==================== SUB-HUD AUTONOMOUS ACTIONS ====================
    async triggerClipboardDiagnosis() {
        this.sfx.playProtocolPulse();
        try {
            let text = '';
            if (navigator.clipboard && navigator.clipboard.readText) {
                try { text = await navigator.clipboard.readText(); } catch {}
            }
            if (!text) {
                text = prompt("Enter or paste code, stack-trace, or JSON to diagnose with Clipboard Sentinel:");
            }
            if (!text || !text.trim()) return;

            this.appendUserMessage(`📋 [CLIPBOARD SENTINEL]: Analyzing snippet (${text.length} chars)...`);
            const res = await fetch('/api/clipboard/analyze', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ text })
            });
            const data = await res.json();
            const cardHtml = `
                <div class="chat-card-header" style="border-bottom:1px solid rgba(0,212,255,0.25);padding-bottom:4px;margin-bottom:6px;">
                    <span style="color:#00d4ff;font-weight:700;">📋 CLIPBOARD SENTINEL DIAGNOSIS</span>
                    <span style="color:#ffd700;font-size:0.75rem;">${this.escape(data.snippetType || 'GENERIC')}</span>
                </div>
                <div style="font-size:0.8rem;color:#7eb2cc;margin:6px 0;">
                    <strong>Detected:</strong> ${data.isIssueDetected ? '<span style="color:#ff5252;">⚠ Issue Detected</span>' : '<span style="color:#39ff14;">✓ Clean Snippet</span>'}
                </div>
                <div class="chat-card-content" style="max-height:240px;overflow-y:auto;background:rgba(0,10,25,0.4);padding:8px;border-radius:4px;font-size:0.78rem;">
                    ${this.formatMarkdown(data.diagnosis || 'Analysis completed.')}
                    ${data.recommendedAction ? `<div style="margin-top:8px;color:#39ff14;"><strong>Action:</strong> ${this.escape(data.recommendedAction)}</div>` : ''}
                </div>
            `;
            this.appendAssistantCard(cardHtml);
            if (this.voiceStudio) this.voiceStudio.speak("Sir, clipboard analysis complete. Telemetry rendered.");
        } catch (e) {
            this.appendAssistantMessage(`[Clipboard Sentinel Error]: ${e.message}`);
        }
    }

    async triggerBrowserAgent() {
        this.sfx.playProtocolPulse();
        const url = prompt("Enter target URL or web search query for Autonomous Headless Browser Agent:", "https://news.ycombinator.com");
        if (!url || !url.trim()) return;

        this.appendUserMessage(`🌐 [AUTONOMOUS BROWSER AGENT]: Navigating to: ${url}`);
        try {
            const res = await fetch('/api/browser/navigate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ url: url.trim(), extractMarkdown: true })
            });
            const data = await res.json();
            const cardHtml = `
                <div class="chat-card-header" style="border-bottom:1px solid rgba(0,212,255,0.25);padding-bottom:4px;margin-bottom:6px;">
                    <span style="color:#00d4ff;font-weight:700;">🌐 BROWSER AGENT: ${this.escape(data.title || 'Page Extracted')}</span>
                    <span style="color:#39ff14;font-size:0.72rem;">HTTP ${data.statusCode}</span>
                </div>
                <div style="font-size:0.75rem;color:#7eb2cc;margin:4px 0;">
                    <strong>URL:</strong> <a href="${this.escape(data.url)}" target="_blank" style="color:#00d4ff;">${this.escape(data.url)}</a> (${data.contentLength} bytes)
                </div>
                <div class="chat-card-content" style="max-height:240px;overflow-y:auto;background:rgba(0,10,25,0.4);padding:8px;border-radius:4px;font-size:0.78rem;">
                    ${this.formatMarkdown(data.markdown || data.textSnippet || 'No content extracted.')}
                </div>
            `;
            this.appendAssistantCard(cardHtml);
            if (this.voiceStudio) this.voiceStudio.speak(`Sir, browser agent successfully navigated to ${data.title || 'the target page'}.`);
        } catch (e) {
            this.appendAssistantMessage(`[Browser Agent Error]: ${e.message}`);
        }
    }

    async triggerAgentSwarm() {
        this.sfx.playProtocolPulse();
        const directive = prompt("Enter multi-agent strategic directive for House Party Autonomous Swarm (Lead Architect, Senior Dev, Security Sentinel, QA Engineer):", "Design a resilient microservices architecture for distributed telemetry processing.");
        if (!directive || !directive.trim()) return;

        this.appendUserMessage(`👥 [HOUSE PARTY SWARM]: Deploying 4 concurrent autonomous agents for directive: "${directive}"`);
        try {
            const res = await fetch('/api/swarm/deploy', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ directive: directive.trim() })
            });
            const data = await res.json();
            let agentStepsHtml = '';
            if (data.agentContributions && data.agentContributions.length) {
                agentStepsHtml = data.agentContributions.map(c => `
                    <div style="margin-bottom:8px;border-left:2px solid #00d4ff;padding-left:8px;">
                        <div style="color:#ffd700;font-weight:700;font-size:0.75rem;">${this.escape(c.agentRole || c.agentName || 'Agent')}:</div>
                        <div style="font-size:0.76rem;color:#c2e7ff;">${this.formatMarkdown(c.output || c.findings || '')}</div>
                    </div>
                `).join('');
            }
            const cardHtml = `
                <div class="chat-card-header" style="border-bottom:1px solid rgba(0,212,255,0.25);padding-bottom:4px;margin-bottom:6px;">
                    <span style="color:#00d4ff;font-weight:700;">👥 HOUSE PARTY AUTONOMOUS SWARM SYNTHESIS</span>
                    <span style="color:#39ff14;font-size:0.72rem;">COMPLETE</span>
                </div>
                <div style="font-size:0.8rem;color:#7eb2cc;margin:6px 0;">
                    <strong>Directive:</strong> ${this.escape(directive)}
                </div>
                <div class="chat-card-content" style="max-height:280px;overflow-y:auto;background:rgba(0,10,25,0.4);padding:8px;border-radius:4px;">
                    ${agentStepsHtml}
                    <div style="margin-top:10px;padding-top:8px;border-top:1px solid rgba(0,212,255,0.2);">
                        <strong style="color:#39ff14;">Unified Swarm Executive Conclusion:</strong>
                        <div style="margin-top:4px;font-size:0.8rem;color:#fff;">${this.formatMarkdown(data.unifiedSynthesis || data.summary || 'Swarm successfully resolved directive.')}</div>
                    </div>
                </div>
            `;
            this.appendAssistantCard(cardHtml);
            if (this.voiceStudio) this.voiceStudio.speak("Sir, House Party multi-agent swarm has completed tactical deliberations.");
        } catch (e) {
            this.appendAssistantMessage(`[Agent Swarm Error]: ${e.message}`);
        }
    }

    async triggerMeetingEar() {
        this.sfx.playProtocolPulse();
        const transcript = prompt("Enter meeting notes or speech transcript to analyze with Live Meeting Ear:", "Team discussed migrating auth tokens to Stark Vault AES-256. Renee will finalize schema by Friday. Pepper will coordinate budget approvals.");
        if (!transcript || !transcript.trim()) return;

        this.appendUserMessage(`🎙️ [LIVE MEETING EAR]: Synthesizing meeting minutes and action items...`);
        try {
            const res = await fetch('/api/tools/execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    toolNameOrId: 'meeting_transcribe',
                    arguments: { transcript: transcript.trim() }
                })
            });
            const data = await res.json();
            this.appendAssistantMessage(`🎙️ **[MEETING EAR EXECUTIVE MINUTES]**:\n\n${data.output || 'Minutes generated.'}`);
            if (this.voiceStudio) this.voiceStudio.speak("Sir, meeting minutes and action items have been processed and recorded.");
        } catch (e) {
            this.appendAssistantMessage(`[Meeting Ear Error]: ${e.message}`);
        }
    }

    async triggerCodebaseSearch() {
        this.sfx.playProtocolPulse();
        const query = prompt("Enter symbol name, class, interface or function to search in Codebase AST Index:", "IBrowserAgentService");
        if (!query || !query.trim()) return;

        this.appendUserMessage(`🧠 [CODEBASE AST INDEXER]: Querying symbol graph for: "${query}"`);
        try {
            const res = await fetch('/api/tools/execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    toolNameOrId: 'codebase_search_symbols',
                    arguments: { query: query.trim() }
                })
            });
            const data = await res.json();
            this.appendAssistantMessage(`🧠 **[AST SYMBOL GRAPH RESULTS]**:\n\n${data.output || 'No symbols found.'}`);
        } catch (e) {
            this.appendAssistantMessage(`[Codebase Indexer Error]: ${e.message}`);
        }
    }

    async triggerMediaSummarizer() {
        this.sfx.playProtocolPulse();
        const mediaUrl = prompt("Enter YouTube video or podcast URL to transcribe and summarize:", "https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        if (!mediaUrl || !mediaUrl.trim()) return;

        this.appendUserMessage(`🎬 [MEDIA SUMMARIZER]: Synthesizing executive thesis for: ${mediaUrl}`);
        try {
            const res = await fetch('/api/tools/execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    toolNameOrId: 'media_summarize',
                    arguments: { url: mediaUrl.trim() }
                })
            });
            const data = await res.json();
            this.appendAssistantMessage(`🎬 **[MEDIA BRIEF & CHAPTERS]**:\n\n${data.output || 'Summary completed.'}`);
        } catch (e) {
            this.appendAssistantMessage(`[Media Summarizer Error]: ${e.message}`);
        }
    }

    async triggerDockerInspection() {
        this.sfx.playProtocolPulse();
        this.appendUserMessage("🐳 [DOCKER MANAGER]: Querying local Docker engine containers and health stats...");
        try {
            const res = await fetch('/api/docker/containers');
            const data = await res.json();
            const containers = data.containers || [];
            if (containers.length === 0) {
                this.appendAssistantMessage(`🐳 **[DOCKER MANAGER]**: ${data.message || 'No active containers running or Docker daemon is in standby mode.'}`);
                return;
            }
            const rows = containers.map(c => `| ${c.name} | ${c.image} | ${c.state || c.status} |`).join('\n');
            this.appendAssistantMessage(`🐳 **[DOCKER ACTIVE CONTAINERS]**:\n\n| Container Name | Image | Status |\n|---|---|---|\n${rows}`);
        } catch (e) {
            this.appendAssistantMessage(`[Docker Manager Error]: ${e.message}`);
        }
    }

    async triggerDatabaseQuery() {
        this.sfx.playProtocolPulse();
        const sql = prompt("Enter read-only SQL query to execute against SQLite Knowledge Database (or leave empty to view schema):", "SELECT name, type FROM sqlite_master WHERE type='table';");
        if (sql === null) return;

        if (!sql.trim()) {
            this.appendUserMessage("🗄️ [DATABASE EXPLORER]: Inspecting database schema tables...");
            try {
                const res = await fetch('/api/database/schema');
                const data = await res.json();
                const tables = data.tables || [];
                const list = tables.map(t => `- **${t.tableName}** (${t.columnCount || (t.columns && t.columns.length) || 0} cols)`).join('\n');
                this.appendAssistantMessage(`🗄️ **[DATABASE SCHEMA INTROSPECTION]**:\n\n${list || 'No tables detected.'}`);
            } catch (e) {
                this.appendAssistantMessage(`[Database Schema Error]: ${e.message}`);
            }
        } else {
            this.appendUserMessage(`🗄️ [DATABASE QUERY]: \`${sql.trim()}\``);
            try {
                const res = await fetch('/api/database/query', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ sqlQuery: sql.trim() })
                });
                const data = await res.json();
                this.appendAssistantMessage(`🗄️ **[QUERY RESULTS]**:\n\n${this.formatMarkdown(data.formattedMarkdown || data.message || 'Query executed.')}`);
            } catch (e) {
                this.appendAssistantMessage(`[Database Query Error]: ${e.message}`);
            }
        }
    }

    async triggerNetworkRadar() {
        this.sfx.playProtocolPulse();
        this.appendUserMessage("📡 [NETWORK RADAR SENTINEL]: Scanning local subnet perimeter for active hosts and open ports...");
        try {
            const res = await fetch('/api/radar/scan', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ subnetCidr: '192.168.1.0/24' })
            });
            const data = await res.json();
            const devices = data.discoveredDevices || [];
            if (devices.length === 0) {
                this.appendAssistantMessage("📡 **[NETWORK RADAR SCAN COMPLETE]**: Perimeter quiet. No unauthorized devices detected.");
            } else {
                const rows = devices.map(d => `| ${d.ipAddress} | ${d.macAddress || 'N/A'} | ${d.hostName || 'Host'} | ${d.openPorts ? d.openPorts.join(',') : 'None'} |`).join('\n');
                this.appendAssistantMessage(`📡 **[NETWORK RADAR PERIMETER SCAN]**:\n\n| IP Address | MAC | Host | Open Ports |\n|---|---|---|---|\n${rows}`);
            }
        } catch (e) {
            this.appendAssistantMessage(`[Network Radar Error]: ${e.message}`);
        }
    }

    async showAchievementsCard() {
        this.sfx.playAccessGrantedChime();
        this.appendUserMessage("🏆 [STARK ACHIEVEMENTS]: Querying Iron Man milestone badges and gamified score...");
        try {
            const res = await fetch('/api/achievements');
            const data = await res.json();
            const list = data.achievements || [];
            const itemsHtml = list.map(a => `
                <div style="display:flex;align-items:center;gap:10px;padding:8px;background:${a.isUnlocked ? 'rgba(0,212,255,0.12)' : 'rgba(255,255,255,0.03)'};border:1px solid ${a.isUnlocked ? '#00d4ff' : 'rgba(255,255,255,0.1)'};border-radius:6px;margin-bottom:6px;">
                    <span style="font-size:1.4rem;">${a.badgeIcon || '🎖️'}</span>
                    <div style="flex:1;">
                        <div style="font-family:var(--font-hud);font-size:0.8rem;color:${a.isUnlocked ? '#ffd700' : '#888'};font-weight:700;">
                            ${this.escape(a.name)} ${a.isUnlocked ? '✓ UNLOCKED' : '🔒 LOCKED'}
                        </div>
                        <div style="font-size:0.72rem;color:#7eb2cc;">${this.escape(a.description)}</div>
                    </div>
                    <span style="font-family:var(--font-mono);font-size:0.75rem;color:#39ff14;">+${a.points} PTS</span>
                </div>
            `).join('');

            const cardHtml = `
                <div class="chat-card-header" style="border-bottom:1px solid rgba(0,212,255,0.25);padding-bottom:4px;margin-bottom:6px;">
                    <span style="color:#00d4ff;font-weight:700;">🏆 STARK INDUSTRIES ACHIEVEMENTS MATRIX</span>
                    <span style="color:#ffd700;font-size:0.75rem;">${data.totalScore || 0} TOTAL SCORE</span>
                </div>
                <div class="chat-card-content" style="max-height:280px;overflow-y:auto;padding:8px;border-radius:4px;">
                    ${itemsHtml || '<p>No achievements logged yet.</p>'}
                </div>
            `;
            this.appendAssistantCard(cardHtml);
            if (this.voiceStudio) this.voiceStudio.speak(`Sir, you have accumulated ${data.totalScore || 0} Stark achievement points.`);
        } catch (e) {
            this.appendAssistantMessage(`[Achievements Error]: ${e.message}`);
        }
    }

    // =========================================================================
    // STARK RADIO STATION & MEDIA HARVESTER ARCHITECTURE
    // Thread-Safe Non-Blocking Background Harvester & Radio DJ Engine
    // =========================================================================
    initStarkRadio() {
        this.radioAudio = document.getElementById('stark-radio-player');
        this.radioCurrentTrack = null;
        this.radioLibrary = [];
        this.isRadioPlaying = false;
        this.selectedGenre = null;

        if (!this.radioAudio) return;

        // Audio element events - Keep authoritative play state synchronized
        this.radioAudio.addEventListener('play', () => {
            this.isRadioPlaying = true;
            this.syncRadioPlayIcons(true);
        });
        this.radioAudio.addEventListener('pause', () => {
            this.isRadioPlaying = false;
            this.syncRadioPlayIcons(false);
        });
        this.radioAudio.addEventListener('timeupdate', () => this.syncRadioSeekProgress());
        this.radioAudio.addEventListener('ended', async () => {
            // Auto skip to next queued song
            await fetch('/api/radio/control', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Action: 'skip' })
            });
        });

        this.radioAudio.addEventListener('error', (e) => {
            console.warn("Stark Radio Audio Stream Error:", e);
        });

        // Mini-player buttons
        const miniPlayBtn = document.getElementById('btn-radio-play');
        const miniPrevBtn = document.getElementById('btn-radio-prev');
        const miniNextBtn = document.getElementById('btn-radio-next');
        const miniShuffleBtn = document.getElementById('btn-radio-shuffle-mini');
        const miniRepeatBtn = document.getElementById('btn-radio-repeat-mini');
        const miniVol = document.getElementById('radio-mini-volume');
        const miniToggle = document.getElementById('btn-radio-mini-toggle');
        const miniOpenDeck = document.getElementById('btn-radio-deck-open');
        const miniCorrupted = document.getElementById('btn-radio-corrupted-quick');
        const miniSeekBar = document.getElementById('radio-mini-seek-bar');

        miniPlayBtn?.addEventListener('click', () => this.toggleRadioPlayback());
        miniPrevBtn?.addEventListener('click', () => this.controlRadio('previous'));
        miniNextBtn?.addEventListener('click', () => this.controlRadio('skip'));
        miniShuffleBtn?.addEventListener('click', () => this.toggleRadioShuffle());
        miniRepeatBtn?.addEventListener('click', () => this.cycleRadioRepeat());

        miniVol?.addEventListener('input', (e) => {
            const v = e.target.value / 100.0;
            this.setRadioVolume(v);
        });

        miniToggle?.addEventListener('click', () => {
            const mini = document.getElementById('stark-radio-mini');
            mini?.classList.toggle('minimized');
            if (mini?.classList.contains('minimized')) {
                miniToggle.textContent = '□';
            } else {
                miniToggle.textContent = '—';
            }
        });

        miniOpenDeck?.addEventListener('click', () => this.openRadioDeck());
        miniCorrupted?.addEventListener('click', () => this.replaceCorruptedRadioTrack());

        miniSeekBar?.addEventListener('click', (e) => {
            if (!this.radioAudio || !this.radioAudio.duration) return;
            const rect = miniSeekBar.getBoundingClientRect();
            const pos = (e.clientX - rect.left) / rect.width;
            this.radioAudio.currentTime = pos * this.radioAudio.duration;
        });

        // Deck Modal Controls
        const deckPlayBtn = document.getElementById('btn-radio-deck-play');
        const deckPrevBtn = document.getElementById('btn-radio-deck-prev');
        const deckNextBtn = document.getElementById('btn-radio-deck-next');
        const deckShuffleBtn = document.getElementById('btn-radio-deck-shuffle');
        const deckRepeatBtn = document.getElementById('btn-radio-deck-repeat');
        const deckVol = document.getElementById('radio-deck-volume');
        const deckCorrupted = document.getElementById('btn-radio-corrupted-deck');
        const deckSeekBar = document.getElementById('radio-deck-seek-bar');
        const deckCloseBtn = document.getElementById('btn-close-radio-deck');
        const deckCloseFooter = document.getElementById('btn-close-radio-deck-footer');

        deckPlayBtn?.addEventListener('click', () => this.toggleRadioPlayback());
        deckPrevBtn?.addEventListener('click', () => this.controlRadio('previous'));
        deckNextBtn?.addEventListener('click', () => this.controlRadio('skip'));
        deckShuffleBtn?.addEventListener('click', () => this.toggleRadioShuffle());
        deckRepeatBtn?.addEventListener('click', () => this.cycleRadioRepeat());

        deckVol?.addEventListener('input', (e) => {
            const v = e.target.value / 100.0;
            this.setRadioVolume(v);
        });
        deckCorrupted?.addEventListener('click', () => this.replaceCorruptedRadioTrack());
        deckCloseBtn?.addEventListener('click', () => this.closeRadioDeck());
        deckCloseFooter?.addEventListener('click', () => this.closeRadioDeck());

        deckSeekBar?.addEventListener('click', (e) => {
            if (!this.radioAudio || !this.radioAudio.duration) return;
            const rect = deckSeekBar.getBoundingClientRect();
            const pos = (e.clientX - rect.left) / rect.width;
            this.radioAudio.currentTime = pos * this.radioAudio.duration;
        });

        // Tab Switching
        document.querySelectorAll('.radio-deck-tab').forEach(tab => {
            tab.addEventListener('click', () => {
                document.querySelectorAll('.radio-deck-tab').forEach(t => t.classList.remove('active'));
                tab.classList.add('active');
                const targetId = tab.getAttribute('data-tab');
                document.querySelectorAll('.radio-deck-tab-content').forEach(c => c.style.display = 'none');
                const content = document.getElementById(targetId);
                if (content) content.style.display = 'block';

                if (targetId === 'tab-radio-library') {
                    this.loadRadioLibrary(this.selectedGenre);
                }
            });
        });

        // Request Single Song & Harvest form
        document.getElementById('btn-radio-harvest-submit')?.addEventListener('click', () => {
            const q = document.getElementById('radio-harvest-query')?.value.trim();
            const genre = document.getElementById('radio-harvest-genre')?.value || '';
            const fmt = document.getElementById('radio-harvest-format')?.value || 'mp3';
            const autoPlay = document.getElementById('radio-harvest-autoplay')?.checked ?? true;
            if (q) {
                this.requestRadioSong(q, fmt, autoPlay, genre);
            }
        });

        document.getElementById('radio-harvest-query')?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                const q = e.target.value.trim();
                const genre = document.getElementById('radio-harvest-genre')?.value || '';
                const fmt = document.getElementById('radio-harvest-format')?.value || 'mp3';
                const autoPlay = document.getElementById('radio-harvest-autoplay')?.checked ?? true;
                if (q) this.requestRadioSong(q, fmt, autoPlay, genre);
            }
        });

        // Request Full Album / Discography Harvest Form
        document.getElementById('btn-radio-harvest-album-submit')?.addEventListener('click', () => {
            const artist = document.getElementById('radio-harvest-album-artist')?.value.trim();
            const album = document.getElementById('radio-harvest-album-name')?.value.trim();
            const count = parseInt(document.getElementById('radio-harvest-album-count')?.value || '12', 10);
            const genre = document.getElementById('radio-harvest-album-genre')?.value || '';
            const autoPlay = document.getElementById('radio-harvest-album-autoplay')?.checked ?? true;
            if (artist) {
                this.requestRadioAlbum(artist, album, count, genre, autoPlay);
            }
        });

        // Presets
        document.querySelectorAll('.radio-preset-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const q = btn.getAttribute('data-query');
                const genre = btn.getAttribute('data-genre') || '';
                if (q) {
                    const fmtInput = document.getElementById('radio-harvest-format');
                    const fmt = fmtInput ? fmtInput.value : 'mp3';
                    this.requestRadioSong(q, fmt, true, genre);
                }
            });
        });

        // Genre Filter Chips in Library
        document.querySelectorAll('.radio-genre-pill').forEach(pill => {
            pill.addEventListener('click', () => {
                document.querySelectorAll('.radio-genre-pill').forEach(p => p.classList.remove('active'));
                pill.classList.add('active');
                const genre = pill.getAttribute('data-genre');
                this.selectedGenre = genre && genre !== 'ALL' ? genre : null;
                this.loadRadioLibrary(this.selectedGenre);
            });
        });

        // Tune Genre Station Button
        document.getElementById('btn-radio-play-genre')?.addEventListener('click', () => {
            this.tuneRadioGenre(this.selectedGenre);
        });

        // Library Matrix Filter & Refresh
        document.getElementById('radio-lib-search')?.addEventListener('input', (e) => {
            this.filterRadioLibrary(e.target.value);
        });

        document.getElementById('btn-radio-lib-refresh')?.addEventListener('click', () => {
            this.loadRadioLibrary(this.selectedGenre);
        });

        // Edit Metadata Modal
        document.getElementById('btn-close-edit-track-modal')?.addEventListener('click', () => {
            document.getElementById('radio-edit-track-modal')?.classList.add('hidden');
        });
        document.getElementById('btn-cancel-edit-track')?.addEventListener('click', () => {
            document.getElementById('radio-edit-track-modal')?.classList.add('hidden');
        });
        document.getElementById('btn-save-edit-track')?.addEventListener('click', () => {
            this.saveRadioTrackMetadata();
        });

        // Load Initial State
        this.loadRadioStatus();
    }

    openRadioDeck() {
        const modal = document.getElementById('stark-radio-deck-modal');
        if (modal) modal.classList.remove('hidden');
        this.sfx?.playHudBeep(950, 0.05);
        this.loadRadioStatus();
    }

    closeRadioDeck() {
        const modal = document.getElementById('stark-radio-deck-modal');
        if (modal) modal.classList.add('hidden');
    }

    async loadRadioStatus() {
        try {
            const res = await fetch('/api/radio/status');
            if (res.ok) {
                const state = await res.json();
                this.updateRadioPlayerState(state);
            }
        } catch (e) {
            console.warn("loadRadioStatus error", e);
        }
    }

    async loadRadioLibrary(genre = null) {
        try {
            const url = genre && genre !== 'ALL' ? `/api/radio/tracks?genre=${encodeURIComponent(genre)}` : '/api/radio/tracks';
            const res = await fetch(url);
            if (res.ok) {
                this.radioLibrary = await res.json();
                this.renderRadioLibrary(this.radioLibrary);
            }
        } catch (e) {
            console.warn("loadRadioLibrary error", e);
        }
    }

    filterRadioLibrary(query) {
        if (!query || !query.trim()) {
            this.renderRadioLibrary(this.radioLibrary);
            return;
        }
        const q = query.toLowerCase();
        const filtered = this.radioLibrary.filter(t =>
            (t.title && t.title.toLowerCase().includes(q)) ||
            (t.artist && t.artist.toLowerCase().includes(q)) ||
            (t.album && t.album.toLowerCase().includes(q)) ||
            (t.genre && t.genre.toLowerCase().includes(q))
        );
        this.renderRadioLibrary(filtered);
    }

    renderRadioLibrary(tracks) {
        const tbody = document.getElementById('radio-lib-table-body');
        const countBadge = document.getElementById('radio-lib-count-badge');
        if (countBadge) countBadge.textContent = `${tracks.length} TRACKS`;
        if (!tbody) return;

        if (!tracks || tracks.length === 0) {
            tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:20px;color:#7eb2cc;">No tracks in library yet. Use Request & Harvest tab or say "Jarvis, play [Song/Artist]" to download YouTube music.</td></tr>`;
            return;
        }

        tbody.innerHTML = tracks.map(t => {
            const mins = Math.floor(t.durationSeconds / 60);
            const secs = t.durationSeconds % 60;
            const durStr = `${mins}:${secs < 10 ? '0' : ''}${secs}`;
            const sizeMb = (t.fileSizeBytes / (1024 * 1024)).toFixed(1);
            const isPlayingThis = this.radioCurrentTrack && this.radioCurrentTrack.id === t.id;

            return `
                <tr style="${isPlayingThis ? 'background:rgba(255,170,0,0.12);' : ''}">
                    <td>
                        <button class="hud-btn btn-play-track" data-id="${t.id}" style="padding:4px 8px;font-size:0.75rem;border-color:#ffaa00;color:#ffaa00;">
                            ${isPlayingThis && this.isRadioPlaying ? '⏸' : '▶'}
                        </button>
                    </td>
                    <td style="font-weight:600;color:#ffaa00;">${this.escape(t.artist)}</td>
                    <td style="color:#fff;">${this.escape(t.title)}</td>
                    <td style="color:#8ab5cd;font-size:0.7rem;">${this.escape(t.album)}</td>
                    <td><span class="radio-genre-pill active" style="font-size:0.65rem;padding:2px 6px;">${this.escape(t.genre || 'Rock')}</span></td>
                    <td style="font-family:var(--font-mono);">${durStr}</td>
                    <td style="font-family:var(--font-mono);">${sizeMb} MB</td>
                    <td>
                        <div style="display:flex;gap:4px;">
                            <button class="hud-btn btn-edit-track" data-id="${t.id}" title="Edit Metadata" style="padding:3px 6px;font-size:0.7rem;">✏️</button>
                            <button class="hud-btn btn-redownload-track" data-id="${t.id}" title="Purge & Redownload Master" style="padding:3px 6px;font-size:0.7rem;border-color:#ff4444;color:#ff7777;">🔄</button>
                            <button class="hud-btn btn-delete-track" data-id="${t.id}" title="Delete Track" style="padding:3px 6px;font-size:0.7rem;border-color:#f44;color:#f44;">🗑️</button>
                        </div>
                    </td>
                </tr>
            `;
        }).join('');

        // Bind Row Action Buttons with toggle Play/Pause handling
        tbody.querySelectorAll('.btn-play-track').forEach(b => {
            b.addEventListener('click', async () => {
                b.disabled = true;
                b.style.pointerEvents = 'none';
                const tid = b.getAttribute('data-id');
                const tr = this.radioLibrary.find(t => t.id === tid);
                const isPlayingThis = this.radioCurrentTrack && this.radioCurrentTrack.id === tid;

                if (isPlayingThis && this.isRadioPlaying) {
                    if (this.radioAudio) this.radioAudio.pause();
                    await this.controlRadio('pause');
                } else {
                    if (tr) {
                        this.radioCurrentTrack = tr;
                        if (this.radioAudio) {
                            this.radioAudio.src = `/api/radio/stream/${tr.id}`;
                            try { await this.radioAudio.play(); } catch (err) { console.warn(err); }
                        }
                    }
                    await this.controlRadio('play', tid);
                }

                setTimeout(() => {
                    b.disabled = false;
                    b.style.pointerEvents = 'auto';
                }, 400);
            });
        });

        tbody.querySelectorAll('.btn-edit-track').forEach(b => {
            b.addEventListener('click', () => {
                const tid = b.getAttribute('data-id');
                this.openEditTrackModal(tid);
            });
        });

        tbody.querySelectorAll('.btn-redownload-track').forEach(b => {
            b.addEventListener('click', async () => {
                b.disabled = true;
                b.style.pointerEvents = 'none';
                b.innerHTML = '⏳';
                const tid = b.getAttribute('data-id');
                await this.replaceCorruptedRadioTrack(tid);
                setTimeout(() => {
                    b.disabled = false;
                    b.style.pointerEvents = 'auto';
                    b.innerHTML = '🔄';
                }, 1200);
            });
        });

        tbody.querySelectorAll('.btn-delete-track').forEach(b => {
            b.addEventListener('click', async () => {
                const tid = b.getAttribute('data-id');
                if (confirm("Delete this track from local library and disk?")) {
                    b.disabled = true;
                    b.innerHTML = '⏳';
                    await fetch(`/api/radio/tracks/${tid}`, { method: 'DELETE' });
                    this.loadRadioLibrary(this.selectedGenre);
                }
            });
        });
    }

    openEditTrackModal(trackId) {
        const track = this.radioLibrary.find(t => t.id === trackId);
        if (!track) return;
        document.getElementById('edit-track-id').value = track.id;
        document.getElementById('edit-track-title').value = track.title;
        document.getElementById('edit-track-artist').value = track.artist;
        document.getElementById('edit-track-album').value = track.album;
        const genreInput = document.getElementById('edit-track-genre');
        if (genreInput) genreInput.value = track.genre || 'Rock';
        document.getElementById('radio-edit-track-modal')?.classList.remove('hidden');
    }

    async saveRadioTrackMetadata() {
        const saveBtn = document.getElementById('btn-save-edit-track');
        if (saveBtn) {
            saveBtn.disabled = true;
            saveBtn.textContent = 'SAVING...';
        }

        const id = document.getElementById('edit-track-id').value;
        const title = document.getElementById('edit-track-title').value.trim();
        const artist = document.getElementById('edit-track-artist').value.trim();
        const album = document.getElementById('edit-track-album').value.trim();
        const genre = document.getElementById('edit-track-genre')?.value || 'Rock';

        try {
            if (!id || !title) return;

            await fetch(`/api/radio/tracks/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Title: title, Artist: artist, Album: album, Genre: genre })
            });

            document.getElementById('radio-edit-track-modal')?.classList.add('hidden');
            this.loadRadioLibrary(this.selectedGenre);
            this.loadRadioStatus();
        } finally {
            if (saveBtn) {
                saveBtn.disabled = false;
                saveBtn.textContent = 'SAVE METADATA';
            }
        }
    }

    async requestRadioSong(queryOrUrl, format = 'mp3', autoPlay = true, genre = null) {
        if (!queryOrUrl || !queryOrUrl.trim()) return;

        // Prevent repeated clicks / spamming
        if (this.isRadioHarvesting) return;
        this.isRadioHarvesting = true;

        const submitBtn = document.getElementById('btn-radio-harvest-submit');
        const queryInput = document.getElementById('radio-harvest-query');
        const formatSelect = document.getElementById('radio-harvest-format');
        const genreSelect = document.getElementById('radio-harvest-genre');
        const presetBtns = document.querySelectorAll('.radio-preset-btn');

        if (submitBtn) {
            submitBtn.disabled = true;
            submitBtn.innerHTML = '<span>⏳ HARVESTING...</span>';
            submitBtn.style.opacity = '0.6';
            submitBtn.style.cursor = 'not-allowed';
        }
        if (queryInput) queryInput.disabled = true;
        if (formatSelect) formatSelect.disabled = true;
        if (genreSelect) genreSelect.disabled = true;
        presetBtns.forEach(b => {
            b.disabled = true;
            b.style.pointerEvents = 'none';
            b.style.opacity = '0.5';
        });

        // Show download monitors
        const monitor = document.getElementById('radio-harvest-monitor');
        if (monitor) monitor.style.display = 'block';
        const miniDl = document.getElementById('radio-mini-download-hud');
        if (miniDl) miniDl.style.display = 'flex';

        this.updateRadioDownloadProgress({
            Percent: 5,
            Status: `Contacting YouTube probe for "${queryOrUrl}"...`,
            Speed: 'Starting',
            Eta: 'Calculating'
        });

        try {
            const res = await fetch('/api/radio/request', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ QueryOrUrl: queryOrUrl, Format: format, AutoPlay: autoPlay, Genre: genre })
            });

            if (res.ok) {
                const track = await res.json();
                this.handleRadioTrackReady(track);
            } else {
                const err = await res.json();
                alert(`Radio request error: ${err.error || 'Failed to download'}`);
            }
        } catch (e) {
            console.error("requestRadioSong error", e);
        } finally {
            this.isRadioHarvesting = false;
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.innerHTML = '<span>⚡ HARVEST & BROADCAST</span>';
                submitBtn.style.opacity = '1';
                submitBtn.style.cursor = 'pointer';
            }
            if (queryInput) {
                queryInput.disabled = false;
                queryInput.value = '';
            }
            if (formatSelect) formatSelect.disabled = false;
            if (genreSelect) genreSelect.disabled = false;
            presetBtns.forEach(b => {
                b.disabled = false;
                b.style.pointerEvents = 'auto';
                b.style.opacity = '1';
            });

            setTimeout(() => {
                if (monitor) monitor.style.display = 'none';
                if (miniDl) miniDl.style.display = 'none';
            }, 3000);
        }
    }

    async requestRadioAlbum(artist, album = null, count = 12, genre = null, autoPlay = true) {
        if (!artist || !artist.trim()) return;

        const albumBtn = document.getElementById('btn-radio-harvest-album-submit');
        if (albumBtn) {
            albumBtn.disabled = true;
            albumBtn.innerHTML = '<span>⏳ HARVESTING DISCOGRAPHY...</span>';
            albumBtn.style.opacity = '0.6';
        }

        // Show download monitors
        const monitor = document.getElementById('radio-harvest-monitor');
        if (monitor) monitor.style.display = 'block';
        const miniDl = document.getElementById('radio-mini-download-hud');
        if (miniDl) miniDl.style.display = 'flex';

        this.updateRadioDownloadProgress({
            Percent: 5,
            Status: `Harvesting discography/album for "${artist}"...`,
            Speed: 'Starting',
            Eta: 'Batch queue'
        });

        try {
            const res = await fetch('/api/radio/harvest-album', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Artist: artist, Album: album, MaxTracks: count, Genre: genre, AutoPlay: autoPlay })
            });

            if (res.ok) {
                const tracks = await res.json();
                this.sfx?.playAccessGrantedChime();
                this.appendAssistantMessage(`💽 **[ALBUM HARVEST COMPLETE]**: Successfully harvested **${tracks.length} tracks** for **${artist}**${album ? ` (${album})` : ''} into \`Music/JarvisRadio/${artist}/\`. Broadcasting first track now.`);
                if (this.voiceStudio) {
                    this.voiceStudio.speak(`Album harvest complete for ${artist}. Broadcasting now, Sir.`);
                }
                this.loadRadioLibrary(this.selectedGenre);
                this.loadRadioStatus();
            } else {
                const err = await res.json();
                alert(`Album harvest failed: ${err.error || 'Server error'}`);
            }
        } catch (e) {
            console.error("requestRadioAlbum error", e);
        } finally {
            if (albumBtn) {
                albumBtn.disabled = false;
                albumBtn.innerHTML = '<span>💽 HARVEST ALBUM / DISCOGRAPHY</span>';
                albumBtn.style.opacity = '1';
            }
            setTimeout(() => {
                if (monitor) monitor.style.display = 'none';
                if (miniDl) miniDl.style.display = 'none';
            }, 4000);
        }
    }

    async tuneRadioGenre(genre) {
        if (!genre || genre === 'ALL') {
            genre = 'Rock';
        }
        try {
            await fetch('/api/radio/control', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Action: 'genre', Genre: genre, AutoPlay: true })
            });
            this.loadRadioStatus();
            this.loadRadioLibrary(genre);
            this.appendAssistantMessage(`📻 **[RADIO STARK]**: Tuned station frequency to **${genre}**.`);
            if (this.voiceStudio) {
                this.voiceStudio.speak(`Tuning Radio Stark to ${genre}, Sir.`);
            }
        } catch (e) {
            console.warn("tuneRadioGenre error", e);
        }
    }

    async replaceCorruptedRadioTrack(trackId = null) {
        if (this.isRadioCorruptedReplacing) return;
        this.isRadioCorruptedReplacing = true;

        const targetId = trackId || (this.radioCurrentTrack ? this.radioCurrentTrack.id : null);
        const quickBtn = document.getElementById('btn-radio-corrupted-quick');
        const deckBtn = document.getElementById('btn-radio-corrupted-deck');

        if (quickBtn) {
            quickBtn.disabled = true;
            quickBtn.style.pointerEvents = 'none';
            quickBtn.style.opacity = '0.5';
            quickBtn.innerHTML = '<span>⏳ REPAIRING...</span>';
        }
        if (deckBtn) {
            deckBtn.disabled = true;
            deckBtn.style.pointerEvents = 'none';
            deckBtn.style.opacity = '0.5';
            deckBtn.innerHTML = '<span>⏳ PURGING & RE-DOWNLOADING MASTER...</span>';
        }

        // Show download banner
        const miniDl = document.getElementById('radio-mini-download-hud');
        if (miniDl) miniDl.style.display = 'flex';
        this.updateRadioDownloadProgress({
            Percent: 10,
            Status: "Purging corrupted file and re-harvesting master...",
            Speed: "--",
            Eta: "--"
        });

        try {
            const url = targetId ? `/api/radio/redownload/${targetId}` : `/api/radio/redownload`;
            const res = await fetch(url, { method: 'POST' });
            if (res.ok) {
                const track = await res.json();
                this.handleRadioCorruptedFixed({
                    track: track,
                    message: `Master replacement downloaded and verified for '${track.title}'.`
                });
            } else {
                const err = await res.json();
                alert(`Corruption replacement failed: ${err.error || 'Track not found'}`);
            }
        } catch (e) {
            console.error("replaceCorruptedRadioTrack error", e);
        } finally {
            this.isRadioCorruptedReplacing = false;
            if (quickBtn) {
                quickBtn.disabled = false;
                quickBtn.style.pointerEvents = 'auto';
                quickBtn.style.opacity = '1';
                quickBtn.innerHTML = '<span>🚨 REPAIR CORRUPTED</span>';
            }
            if (deckBtn) {
                deckBtn.disabled = false;
                deckBtn.style.pointerEvents = 'auto';
                deckBtn.style.opacity = '1';
                deckBtn.innerHTML = '<span>⚡ PURGE & RE-DOWNLOAD MASTER</span>';
            }
            setTimeout(() => {
                if (miniDl) miniDl.style.display = 'none';
            }, 3000);
        }
    }

    async toggleRadioPlayback() {
        if (!this.radioAudio) {
            this.radioAudio = document.getElementById('stark-radio-player');
        }
        if (!this.radioAudio) return;

        // Debounce play button clicks to prevent rapid double-clicks
        const playBtn1 = document.getElementById('btn-radio-play');
        const playBtn2 = document.getElementById('btn-radio-deck-play');
        if (playBtn1) playBtn1.style.pointerEvents = 'none';
        if (playBtn2) playBtn2.style.pointerEvents = 'none';
        setTimeout(() => {
            if (playBtn1) playBtn1.style.pointerEvents = 'auto';
            if (playBtn2) playBtn2.style.pointerEvents = 'auto';
        }, 400);

        // 1. If audio is actively playing, pause it
        if (!this.radioAudio.paused && this.radioAudio.currentTime > 0) {
            this.radioAudio.pause();
            await this.controlRadio('pause');
            return;
        }

        // 2. If paused or idle, ensure track is selected
        if (!this.radioCurrentTrack) {
            if (this.radioLibrary && this.radioLibrary.length > 0) {
                this.radioCurrentTrack = this.radioLibrary[0];
            } else {
                try {
                    const r = await fetch('/api/radio/tracks');
                    if (r.ok) {
                        const trs = await r.json();
                        if (trs && trs.length > 0) {
                            this.radioLibrary = trs;
                            this.radioCurrentTrack = trs[0];
                        }
                    }
                } catch {}
            }
        }

        if (this.radioCurrentTrack) {
            const streamUrl = `/api/radio/stream/${this.radioCurrentTrack.id}`;
            if (!this.radioAudio.src || !this.radioAudio.src.includes(this.radioCurrentTrack.id)) {
                this.radioAudio.src = streamUrl;
            }
            try {
                await this.radioAudio.play();
            } catch (err) {
                console.warn("Direct HTML5 audio play:", err);
            }
            await this.controlRadio('play', this.radioCurrentTrack.id);
        } else {
            this.appendAssistantMessage("📻 **[RADIO STARK]**: No tracks currently in library. Say *\"Jarvis, play Sandman\"* or use the Request & Harvest tab to queue any YouTube song.");
            if (this.voiceStudio) {
                this.voiceStudio.speak("Stark Station library is empty, Sir. What song would you like me to harvest and play?");
            }
        }
    }

    async controlRadio(action, trackId = null) {
        try {
            const res = await fetch('/api/radio/control', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Action: action, TrackId: trackId })
            });
            if (res.ok) {
                const state = await res.json();
                this.updateRadioPlayerState(state);
            }
        } catch (e) {
            console.warn("controlRadio error", e);
        }
    }

    setRadioVolume(vol) {
        if (this.radioAudio) {
            this.radioAudio.volume = vol;
        }
        const miniVol = document.getElementById('radio-mini-volume');
        const deckVol = document.getElementById('radio-deck-volume');
        if (miniVol) miniVol.value = Math.round(vol * 100);
        if (deckVol) deckVol.value = Math.round(vol * 100);
    }

    toggleRadioShuffle() {
        return this.controlRadio('shuffle');
    }

    cycleRadioRepeat() {
        return this.controlRadio('repeat');
    }

    syncRadioPlayIcons(isPlaying) {
        const miniPlayBtn = document.getElementById('btn-radio-play');
        const deckPlayBtn = document.getElementById('btn-radio-deck-play');
        const miniDisc = document.getElementById('radio-mini-disc');
        const deckDisc = document.getElementById('radio-deck-disc');
        const badge = document.getElementById('radio-station-badge');

        if (isPlaying) {
            if (miniPlayBtn) miniPlayBtn.textContent = '⏸';
            if (deckPlayBtn) deckPlayBtn.textContent = '⏸';
            miniDisc?.classList.add('is-spinning');
            deckDisc?.classList.add('is-spinning');
            if (badge) {
                badge.textContent = 'BROADCASTING';
                badge.classList.add('broadcasting');
            }
        } else {
            if (miniPlayBtn) miniPlayBtn.textContent = '▶';
            if (deckPlayBtn) deckPlayBtn.textContent = '▶';
            miniDisc?.classList.remove('is-spinning');
            deckDisc?.classList.remove('is-spinning');
            if (badge) {
                badge.textContent = 'PAUSED';
                badge.classList.remove('broadcasting');
            }
        }

        // Sync table row icons
        document.querySelectorAll('.btn-play-track').forEach(b => {
            const tid = b.getAttribute('data-id');
            const isPlayingThis = this.radioCurrentTrack && this.radioCurrentTrack.id === tid;
            b.textContent = isPlayingThis && isPlaying ? '⏸' : '▶';
        });
    }

    updateRadioPlayerState(state) {
        if (!state) return;
        this.isRadioPlaying = state.isPlaying;
        const track = state.currentTrack;
        this.radioCurrentTrack = track;

        // UI elements
        const badge = document.getElementById('radio-station-badge');
        const miniTitle = document.getElementById('radio-mini-title');
        const miniArtist = document.getElementById('radio-mini-artist');
        const miniAlbum = document.getElementById('radio-mini-album');
        const miniPlayBtn = document.getElementById('btn-radio-play');
        const miniDisc = document.getElementById('radio-mini-disc');

        const deckTitle = document.getElementById('radio-deck-title');
        const deckArtist = document.getElementById('radio-deck-artist');
        const deckAlbum = document.getElementById('radio-deck-album');
        const deckFilepath = document.getElementById('radio-deck-filepath');
        const deckPlayBtn = document.getElementById('btn-radio-deck-play');
        const deckDisc = document.getElementById('radio-deck-disc');
        const deckFmtBadge = document.getElementById('radio-deck-format-badge');
        const deckGenreBadge = document.getElementById('radio-deck-genre-badge');

        // Shuffle state indicators
        const miniShuffle = document.getElementById('btn-radio-shuffle-mini');
        const deckShuffle = document.getElementById('btn-radio-deck-shuffle');
        if (state.isShuffle) {
            miniShuffle?.classList.add('active');
            deckShuffle?.classList.add('active');
        } else {
            miniShuffle?.classList.remove('active');
            deckShuffle?.classList.remove('active');
        }

        // Repeat state indicators (off / all / one)
        const miniRepeat = document.getElementById('btn-radio-repeat-mini');
        const deckRepeat = document.getElementById('btn-radio-deck-repeat');
        const repMode = (state.repeatMode || 'off').toLowerCase();
        const repLabel = repMode === 'one' ? '🔂 ONE' : (repMode === 'all' ? '🔁 ALL' : '🔁 OFF');

        if (miniRepeat) {
            miniRepeat.textContent = repMode === 'one' ? '🔂' : '🔁';
            if (repMode !== 'off') miniRepeat.classList.add('active');
            else miniRepeat.classList.remove('active');
        }
        if (deckRepeat) {
            deckRepeat.innerHTML = `<span>${repLabel}</span>`;
            if (repMode !== 'off') deckRepeat.classList.add('active');
            else deckRepeat.classList.remove('active');
        }

        // Genre badge
        if (deckGenreBadge) {
            const g = state.currentGenre || (track ? track.genre : null) || 'Rock';
            deckGenreBadge.textContent = `GENRE: ${g.toUpperCase()}`;
        }

        if (track) {
            const streamUrl = `/api/radio/stream/${track.id}`;
            if (this.radioAudio && (!this.radioAudio.src || !this.radioAudio.src.includes(track.id))) {
                this.radioAudio.src = streamUrl;
                if (state.isPlaying) {
                    this.radioAudio.play().catch(err => console.warn("Audio autoplay blocked:", err));
                }
            }

            if (state.isPlaying && this.radioAudio && this.radioAudio.paused) {
                this.radioAudio.play().catch(() => {});
            } else if (!state.isPlaying && this.radioAudio && !this.radioAudio.paused) {
                this.radioAudio.pause();
            }

            // Text Updates
            if (miniTitle) miniTitle.textContent = track.title;
            if (miniArtist) miniArtist.textContent = track.artist;
            if (miniAlbum) miniAlbum.textContent = `${track.album} • ${(track.format || 'mp3').toUpperCase()} [${track.genre || 'Rock'}]`;

            if (deckTitle) deckTitle.textContent = track.title;
            if (deckArtist) deckArtist.textContent = track.artist;
            if (deckAlbum) deckAlbum.textContent = `Album: ${track.album}`;
            if (deckFilepath) deckFilepath.textContent = track.filePath || 'Local Library';
            if (deckFmtBadge) deckFmtBadge.textContent = `FORMAT: ${(track.format || 'mp3').toUpperCase()} MASTER`;

            if (badge) {
                badge.textContent = state.isPlaying ? 'BROADCASTING' : 'PAUSED';
                if (state.isPlaying) badge.classList.add('broadcasting');
                else badge.classList.remove('broadcasting');
            }
        } else {
            if (badge) {
                badge.textContent = 'IDLE';
                badge.classList.remove('broadcasting');
            }
            if (miniTitle) miniTitle.textContent = 'Stark Station Idle';
            if (miniArtist) miniArtist.textContent = 'Request any YouTube track or URL';
            if (deckTitle) deckTitle.textContent = 'No Song Playing';
            if (deckArtist) deckArtist.textContent = 'Stark Station Radio Ready';
        }

        this.syncRadioPlayIcons(state.isPlaying);
    }

    syncRadioSeekProgress() {
        if (!this.radioAudio) return;
        const cur = this.radioAudio.currentTime || 0;
        const dur = this.radioAudio.duration || (this.radioCurrentTrack ? this.radioCurrentTrack.durationSeconds : 0) || 1;
        const pct = Math.min(100, Math.max(0, (cur / dur) * 100));

        // Mini-player progress
        const miniFill = document.getElementById('radio-mini-seek-fill');
        const miniCur = document.getElementById('radio-mini-current-time');
        const miniDur = document.getElementById('radio-mini-duration');
        if (miniFill) miniFill.style.width = `${pct}%`;
        if (miniCur) miniCur.textContent = this.formatTimeSecs(cur);
        if (miniDur) miniDur.textContent = this.formatTimeSecs(dur);

        // Deck progress
        const deckFill = document.getElementById('radio-deck-seek-fill');
        const deckCur = document.getElementById('radio-deck-current-time');
        const deckDur = document.getElementById('radio-deck-duration');
        if (deckFill) deckFill.style.width = `${pct}%`;
        if (deckCur) deckCur.textContent = this.formatTimeSecs(cur);
        if (deckDur) deckDur.textContent = this.formatTimeSecs(dur);
    }

    formatTimeSecs(secs) {
        if (isNaN(secs) || secs < 0) return '0:00';
        const m = Math.floor(secs / 60);
        const s = Math.floor(secs % 60);
        return `${m}:${s < 10 ? '0' : ''}${s}`;
    }

    updateRadioDownloadProgress(p) {
        if (!p) return;
        // Mini banner
        const miniDl = document.getElementById('radio-mini-download-hud');
        const miniStatus = document.getElementById('radio-mini-dl-status');
        const miniPct = document.getElementById('radio-mini-dl-pct');
        const miniFill = document.getElementById('radio-mini-dl-fill');
        if (miniDl) miniDl.style.display = 'flex';
        if (miniStatus) miniStatus.textContent = p.status || p.Status || 'DOWNLOADING MASTER...';
        const percent = p.percent ?? p.Percent ?? 0;
        if (miniPct) miniPct.textContent = `${Math.round(percent)}%`;
        if (miniFill) miniFill.style.width = `${percent}%`;

        // Deck monitor
        const deckMonitor = document.getElementById('radio-harvest-monitor');
        const deckStatus = document.getElementById('radio-harvest-status-text');
        const deckPct = document.getElementById('radio-harvest-percent-text');
        const deckFill = document.getElementById('radio-harvest-progress-fill');
        const deckSpeed = document.getElementById('radio-harvest-speed');
        const deckEta = document.getElementById('radio-harvest-eta');
        if (deckMonitor) deckMonitor.style.display = 'block';
        if (deckStatus) deckStatus.textContent = p.status || p.Status || 'DOWNLOADING MASTER...';
        if (deckPct) deckPct.textContent = `${Math.round(percent)}%`;
        if (deckFill) deckFill.style.width = `${percent}%`;
        if (deckSpeed) deckSpeed.textContent = `Speed: ${p.speed || p.Speed || '--'}`;
        if (deckEta) deckEta.textContent = `ETA: ${p.eta || p.Eta || '--'}`;
    }

    handleRadioTrackReady(track) {
        if (!track) return;
        this.sfx?.playAccessGrantedChime();
        this.appendAssistantMessage(`🎵 **[RADIO STARK BROADCAST]**: Harvest complete for **'${track.title}'** by **'${track.artist}'** (${(track.format || 'mp3').toUpperCase()}). Organized into: \`${track.filePath}\``);
        this.loadRadioLibrary(this.selectedGenre);
        this.loadRadioStatus();
    }

    handleRadioCorruptedFixed(data) {
        this.sfx?.playAccessGrantedChime();
        const tr = data.track || data.Track;
        const msg = data.message || data.Message || 'Track corruption resolved with fresh master.';
        this.appendAssistantMessage(`✅ **[INTEGRITY REMEDIATION COMPLETE]**: ${msg} Resuming playback on Stark Radio.`);
        if (this.voiceStudio) {
            this.voiceStudio.speak("Corrupted track has been replaced with a fresh master copy, Sir.");
        }
        this.loadRadioLibrary(this.selectedGenre);
        this.loadRadioStatus();
    }

    updateRadioQueueUi(queue) {
        // Queue refreshed from SignalR
    }

    async handleRadioVoiceCommand(text) {
        const lower = text.toLowerCase().trim();

        // Universal prefix stripper: handles "javis", "jarvis", "travis", "hey javis", "hey jarvis"
        let normalized = lower
            .replace(/\b(?:hey|hi|hello|ok|okay)?\s*(?:jarvis|javis|travis|javus|yavus)[,:]?\s*/gi, '')
            .replace(/\bplease\b/gi, '')
            .replace(/\bcan you\b/gi, '')
            .trim();

        // 1. SPECIFIC CORRUPTED TRACK USER DIRECTIVE:
        // "Jarvis, this song is corrupted, please download a new one and replace this one!"
        const isCorruptedDirective =
            lower.includes('corrupted') || lower.includes('corrupt') || lower.includes('glitched') ||
            (lower.includes('replace') && lower.includes('song')) ||
            (lower.includes('download a new one') && lower.includes('replace'));

        if (isCorruptedDirective) {
            this.appendUserMessage(text);
            if (this.sfx) this.sfx.playProtocolPulse();

            const ack = "Right away, Sir. Purging the corrupted audio file and downloading a pristine master replacement from the source.";
            this.appendAssistantMessage(`🚨 **[RADIO STARK SENTINEL]**: Corrupted track replacement engaged. Purging local flawed track and streaming fresh master from YouTube...`);
            if (this.voiceStudio) this.voiceStudio.speak(ack);

            await this.replaceCorruptedRadioTrack();
            return true;
        }

        // 2. ALBUM & SINGLES BATCH HARVEST DIRECTIVE:
        // "Download all of AD/DC's albums and Singles", "download all AC/DC albums", "download all albums by [artist]", "download all singles"
        const isAlbumOrSinglesDirective =
            (lower.includes('download') && (lower.includes('album') || lower.includes('discography') || lower.includes('single'))) ||
            (lower.includes('play all') && lower.includes('songs')) ||
            lower.includes('download all their albums') || lower.includes('download all albums');

        if (isAlbumOrSinglesDirective) {
            this.appendUserMessage(text);
            if (this.sfx && this.sfx.playProtocolPulse) this.sfx.playProtocolPulse();

            // Pass directly to JarvisHub LLM intent verification to guarantee canonical artist tracking
            await this.sendMessage(text);
            return true;
        }

        // 3. Playback Song Request / YouTube Harvester:
        // "Javis play Sandman", "Jarvis play Enter Sandman", "play Sandman", "request AC/DC", "download [song] as mp4/mp3"
        const isPlayRequest = 
            normalized.startsWith('play ') || normalized.startsWith('request ') || normalized.startsWith('stream ') || normalized.startsWith('put on ') ||
            lower.startsWith('play ') || lower.startsWith('request ') ||
            (lower.includes('download') && (lower.includes('song') || lower.includes('music') || lower.includes('track') || lower.includes('video')));

        if (isPlayRequest) {
            // Check if user is asking to play a genre
            const genreMatch = normalized.match(/^(?:play|tune into|switch to)\s+(classical|rock|pop|hip-hop|hip hop|rap|electronic|edm|r&b|soul|jazz|blues|country|folk|reggae|caribbean|latin)$/i);
            if (genreMatch) {
                let genre = genreMatch[1].trim();
                if (genre.toLowerCase() === 'edm') genre = 'Electronic';
                else if (genre.toLowerCase() === 'rap') genre = 'Hip-Hop';
                else genre = genre.charAt(0).toUpperCase() + genre.slice(1);

                this.appendUserMessage(text);
                await this.tuneRadioGenre(genre);
                return true;
            }

            let songQuery = normalized
                .replace(/^play\s+/i, '')
                .replace(/^request\s+/i, '')
                .replace(/^stream\s+/i, '')
                .replace(/^put on\s+/i, '')
                .replace(/^download\s+(song|music|track|video)?\s*/i, '')
                .trim();

            let format = 'mp3';
            if (songQuery.includes('as mp4') || songQuery.includes('mp4') || songQuery.includes('video')) {
                format = 'mp4';
                songQuery = songQuery.replace('as mp4', '').replace('mp4', '').replace('video', '').trim();
            } else if (songQuery.includes('as mp3')) {
                songQuery = songQuery.replace('as mp3', '').trim();
            }

            if (songQuery.length >= 2) {
                this.appendUserMessage(text);
                if (this.sfx) this.sfx.playProtocolPulse();

                const ack = `Requesting "${songQuery}" on Radio Stark, Sir. Checking our local library or harvesting fresh from YouTube.`;
                this.appendAssistantMessage(`📻 **[RADIO STARK DJ]**: Directive received: **"${songQuery}"** (${format.toUpperCase()}). Querying local archive / YouTube harvester...`);
                if (this.voiceStudio) this.voiceStudio.speak(ack);

                await this.requestRadioSong(songQuery, format, true);
                return true;
            }
        }

        // 4. Playback Controls: Stop / Pause / Resume / Next / Previous
        if (normalized === 'pause' || normalized === 'pause radio' || normalized === 'pause music' || normalized === 'stop radio' || normalized === 'stop music' || normalized === 'stop' || normalized === 'stop the music') {
            this.appendUserMessage(text);
            await this.controlRadio('pause');
            if (this.voiceStudio) this.voiceStudio.speak("Radio station paused, Sir.");
            return true;
        }

        if (normalized === 'resume' || normalized === 'resume radio' || normalized === 'resume music' || normalized === 'play' || normalized === 'play radio' || normalized === 'play music' || normalized === 'unpause') {
            this.appendUserMessage(text);
            await this.toggleRadioPlayback();
            if (this.voiceStudio) this.voiceStudio.speak("Resuming broadcast, Sir.");
            return true;
        }

        if (normalized.includes('next song') || normalized.includes('skip song') || normalized.includes('next track') || normalized.includes('skip track') || normalized === 'skip' || normalized === 'next') {
            this.appendUserMessage(text);
            await this.controlRadio('skip');
            if (this.voiceStudio) this.voiceStudio.speak("Skipping to next track, Sir.");
            return true;
        }

        if (normalized.includes('previous song') || normalized.includes('prev song') || normalized.includes('previous track') || normalized === 'previous' || normalized === 'prev') {
            this.appendUserMessage(text);
            await this.controlRadio('previous');
            if (this.voiceStudio) this.voiceStudio.speak("Replaying previous track, Sir.");
            return true;
        }

        // 5. Shuffle Control
        if (normalized.includes('shuffle music') || normalized.includes('shuffle songs') || normalized === 'shuffle' || normalized.includes('toggle shuffle') || normalized.includes('randomize music')) {
            this.appendUserMessage(text);
            await this.toggleRadioShuffle();
            if (this.voiceStudio) this.voiceStudio.speak("Toggling shuffle rotation, Sir.");
            return true;
        }

        // 6. Repeat Control
        if (normalized.includes('repeat music') || normalized.includes('repeat song') || normalized === 'repeat' || normalized.includes('loop music') || normalized.includes('loop song') || normalized.includes('toggle repeat')) {
            this.appendUserMessage(text);
            await this.cycleRadioRepeat();
            if (this.voiceStudio) this.voiceStudio.speak("Cycling repeat mode, Sir.");
            return true;
        }

        // 7. Volume control e.g. "volume 50%", "radio volume 80", "set volume to 75"
        const volMatch = normalized.match(/(?:radio\s+|music\s+)?volume\s+(?:to\s+)?(\d+)(?:%)?/i);
        if (volMatch) {
            const volNum = parseInt(volMatch[1], 10);
            if (!isNaN(volNum) && volNum >= 0 && volNum <= 100) {
                this.appendUserMessage(text);
                const dec = volNum / 100.0;
                this.setRadioVolume(dec);
                await this.controlRadio('volume', null, null, dec);
                if (this.voiceStudio) this.voiceStudio.speak(`Radio volume set to ${volNum} percent, Sir.`);
                return true;
            }
        }

        // 8. Open / Close Radio Deck
        if (normalized.includes('open radio') || normalized.includes('show radio') || normalized.includes('radio deck') || normalized.includes('music library')) {
            this.appendUserMessage(text);
            this.openRadioDeck();
            if (this.voiceStudio) this.voiceStudio.speak("Stark Radio Deck opened, Sir.");
            return true;
        }

        if (normalized.includes('close radio') || normalized.includes('hide radio')) {
            this.appendUserMessage(text);
            this.closeRadioDeck();
            if (this.voiceStudio) this.voiceStudio.speak("Stark Radio Deck closed, Sir.");
            return true;
        }

        // 9. Open / Close Stark Cinema Deck
        if (normalized.includes('open cinema') || normalized.includes('show cinema') || normalized.includes('cinema deck') || normalized.includes('open video player') || normalized.includes('show video player')) {
            this.appendUserMessage(text);
            this.openCinemaDeck();
            if (this.voiceStudio) this.voiceStudio.speak("Stark Cinema and YouTube Video Deck active, Sir.");
            return true;
        }

        if (normalized.includes('close cinema') || normalized.includes('hide cinema') || normalized.includes('close video player')) {
            this.appendUserMessage(text);
            this.closeCinemaDeck();
            if (this.voiceStudio) this.voiceStudio.speak("Stark Cinema closed, Sir.");
            return true;
        }

        return false;
    }

    // =========================================================================
    // STARK CINEMA // VIDEO PLAYER, CHANNELS & RECOMMENDER ARCHITECTURE
    // =========================================================================
    initStarkCinema() {
        // Tab & General State
        this.cinemaActiveTab = 'movies'; // 'movies' | 'youtube' | 'rumble'
        this.cinemaCurrentMedia = null;  // active movie or video object
        this.cinemaCurrentType = null;   // 'movie' | 'youtube' | 'rumble'
        this.cinemaDownloadingIds = new Set();
        this.cinemaLoaderTimer = null;

        // Movies Vault State
        this.moviesList = [];
        this.movieOnlyUnwatched = false;
        this.movieSearch = '';

        // YouTube State
        this.youtubeChannels = [];
        this.youtubeVideos = [];
        this.youtubeSelectedChannelId = null;
        this.youtubeOnlyDownloaded = false;
        this.youtubeOnlyUnwatched = false;

        // Rumble State
        this.rumbleChannels = [];
        this.rumbleVideos = [];
        this.rumbleSelectedChannelId = null;
        this.rumbleOnlyDownloaded = false;
        this.rumbleOnlyUnwatched = false;

        // Modal Elements
        const modal = document.getElementById('stark-cinema-modal');
        const headerBtn = document.getElementById('btn-header-cinema');
        const subHudItem = document.getElementById('subhud-item-cinema');
        const closeBtn = document.getElementById('btn-close-cinema');
        const closeFooter = document.getElementById('btn-close-cinema-footer');
        const resyncBtn = document.getElementById('btn-cinema-resync');

        // Tab Navigation Buttons
        const tabBtnMovies = document.getElementById('cinema-tab-btn-movies');
        const tabBtnYoutube = document.getElementById('cinema-tab-btn-youtube');
        const tabBtnRumble = document.getElementById('cinema-tab-btn-rumble');

        // Shared Player Controls
        const playerCloseBtn = document.getElementById('btn-cinema-player-close');
        const playerDownloadBtn = document.getElementById('btn-cinema-player-download');
        const playerWatchedBtn = document.getElementById('btn-cinema-player-watched');

        // Movies View Controls
        const movieSearchInput = document.getElementById('movie-search-input');
        const movieFilterUnwatchedBtn = document.getElementById('btn-movie-filter-unwatched');
        const movieResyncBtn = document.getElementById('btn-movie-resync');

        // YouTube View Controls
        const ytChannelInput = document.getElementById('cinema-youtube-input');
        const ytAddBtn = document.getElementById('btn-cinema-add-youtube');
        const ytSearchInput = document.getElementById('cinema-youtube-search-input');
        const ytFilterUnwatchedBtn = document.getElementById('btn-cinema-youtube-filter-unwatched');
        const ytFilterDlBtn = document.getElementById('btn-cinema-youtube-filter-downloaded');
        const ytRefreshBtn = document.getElementById('btn-cinema-youtube-refresh-feed');

        // Rumble View Controls
        const rumbleChannelInput = document.getElementById('cinema-rumble-input');
        const rumbleAddBtn = document.getElementById('btn-cinema-add-rumble');
        const rumbleSearchInput = document.getElementById('cinema-rumble-search-input');
        const rumbleFilterUnwatchedBtn = document.getElementById('btn-cinema-rumble-filter-unwatched');
        const rumbleFilterDlBtn = document.getElementById('btn-cinema-rumble-filter-downloaded');
        const rumbleRefreshBtn = document.getElementById('btn-cinema-rumble-refresh-feed');

        // Open/Close Deck Event Listeners
        headerBtn?.addEventListener('click', () => this.openCinemaDeck());
        subHudItem?.addEventListener('click', () => {
            const subHud = document.getElementById('sub-hud-menu');
            if (subHud) subHud.classList.add('hidden');
            this.openCinemaDeck();
        });

        closeBtn?.addEventListener('click', () => this.closeCinemaDeck());
        closeFooter?.addEventListener('click', () => this.closeCinemaDeck());
        modal?.addEventListener('click', (e) => {
            if (e.target === modal) this.closeCinemaDeck();
        });

        // Tab Switching Listeners
        tabBtnMovies?.addEventListener('click', () => this.switchCinemaTab('movies'));
        tabBtnYoutube?.addEventListener('click', () => this.switchCinemaTab('youtube'));
        tabBtnRumble?.addEventListener('click', () => this.switchCinemaTab('rumble'));

        // Global Resync Button (Syncs active tab or all vaults)
        resyncBtn?.addEventListener('click', async () => {
            if (this.sfx) this.sfx.playProtocolPulse();
            resyncBtn.disabled = true;
            resyncBtn.textContent = 'SYNCING ALL...';
            try {
                if (this.cinemaActiveTab === 'movies') {
                    await this.rescanMovieVault(resyncBtn);
                } else if (this.cinemaActiveTab === 'youtube') {
                    await fetch('/api/video/resync', { method: 'POST' });
                    await this.loadCinemaChannels('YouTube');
                    await this.loadCinemaFeed('YouTube');
                } else if (this.cinemaActiveTab === 'rumble') {
                    await fetch('/api/video/resync', { method: 'POST' });
                    await this.loadCinemaChannels('Rumble');
                    await this.loadCinemaFeed('Rumble');
                }
            } catch (err) {
                console.error("Resync failed:", err);
            } finally {
                resyncBtn.disabled = false;
                resyncBtn.textContent = '🔄 SYNC VAULT';
            }
        });

        // Movies Controls Bindings
        let movieSearchDebounce = null;
        movieSearchInput?.addEventListener('input', () => {
            clearTimeout(movieSearchDebounce);
            movieSearchDebounce = setTimeout(() => {
                this.movieSearch = movieSearchInput.value.trim().toLowerCase();
                this.renderMoviesGrid();
            }, 250);
        });

        movieFilterUnwatchedBtn?.addEventListener('click', () => {
            this.movieOnlyUnwatched = !this.movieOnlyUnwatched;
            if (this.movieOnlyUnwatched) {
                movieFilterUnwatchedBtn.classList.add('active');
                movieFilterUnwatchedBtn.style.background = 'rgba(255,170,0,0.25)';
                movieFilterUnwatchedBtn.style.boxShadow = '0 0 12px rgba(255,170,0,0.5)';
                movieFilterUnwatchedBtn.style.borderColor = '#ffaa00';
            } else {
                movieFilterUnwatchedBtn.classList.remove('active');
                movieFilterUnwatchedBtn.style.background = '';
                movieFilterUnwatchedBtn.style.boxShadow = '';
                movieFilterUnwatchedBtn.style.borderColor = 'rgba(255,170,0,0.4)';
            }
            this.renderMoviesGrid();
        });

        movieResyncBtn?.addEventListener('click', () => this.rescanMovieVault(movieResyncBtn));

        // YouTube Controls Bindings
        ytAddBtn?.addEventListener('click', () => {
            const val = ytChannelInput?.value?.trim();
            if (val) this.subscribeCinemaChannel(val, 'YouTube');
        });
        ytChannelInput?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                const val = ytChannelInput?.value?.trim();
                if (val) this.subscribeCinemaChannel(val, 'YouTube');
            }
        });

        let ytSearchDebounce = null;
        ytSearchInput?.addEventListener('input', () => {
            clearTimeout(ytSearchDebounce);
            ytSearchDebounce = setTimeout(() => this.loadCinemaFeed('YouTube'), 300);
        });

        ytFilterUnwatchedBtn?.addEventListener('click', () => {
            this.youtubeOnlyUnwatched = !this.youtubeOnlyUnwatched;
            if (this.youtubeOnlyUnwatched) {
                ytFilterUnwatchedBtn.classList.add('active');
                ytFilterUnwatchedBtn.style.background = 'rgba(255,170,0,0.25)';
                ytFilterUnwatchedBtn.style.boxShadow = '0 0 12px rgba(255,170,0,0.5)';
                ytFilterUnwatchedBtn.style.borderColor = '#ffaa00';
            } else {
                ytFilterUnwatchedBtn.classList.remove('active');
                ytFilterUnwatchedBtn.style.background = '';
                ytFilterUnwatchedBtn.style.boxShadow = '';
                ytFilterUnwatchedBtn.style.borderColor = 'rgba(255,170,0,0.4)';
            }
            this.loadCinemaFeed('YouTube');
        });

        ytFilterDlBtn?.addEventListener('click', () => {
            this.youtubeOnlyDownloaded = !this.youtubeOnlyDownloaded;
            if (this.youtubeOnlyDownloaded) {
                ytFilterDlBtn.classList.add('active');
                ytFilterDlBtn.style.background = 'rgba(0,255,136,0.25)';
                ytFilterDlBtn.style.boxShadow = '0 0 10px rgba(0,255,136,0.5)';
                ytFilterDlBtn.style.borderColor = '#00ff88';
            } else {
                ytFilterDlBtn.classList.remove('active');
                ytFilterDlBtn.style.background = '';
                ytFilterDlBtn.style.boxShadow = '';
                ytFilterDlBtn.style.borderColor = 'rgba(0,255,136,0.4)';
            }
            this.loadCinemaFeed('YouTube');
        });

        ytRefreshBtn?.addEventListener('click', () => this.loadCinemaFeed('YouTube'));

        // Rumble Controls Bindings
        rumbleAddBtn?.addEventListener('click', () => {
            const val = rumbleChannelInput?.value?.trim();
            if (val) this.subscribeCinemaChannel(val, 'Rumble');
        });
        rumbleChannelInput?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                const val = rumbleChannelInput?.value?.trim();
                if (val) this.subscribeCinemaChannel(val, 'Rumble');
            }
        });

        let rumbleSearchDebounce = null;
        rumbleSearchInput?.addEventListener('input', () => {
            clearTimeout(rumbleSearchDebounce);
            rumbleSearchDebounce = setTimeout(() => this.loadCinemaFeed('Rumble'), 300);
        });

        rumbleFilterUnwatchedBtn?.addEventListener('click', () => {
            this.rumbleOnlyUnwatched = !this.rumbleOnlyUnwatched;
            if (this.rumbleOnlyUnwatched) {
                rumbleFilterUnwatchedBtn.classList.add('active');
                rumbleFilterUnwatchedBtn.style.background = 'rgba(255,170,0,0.25)';
                rumbleFilterUnwatchedBtn.style.boxShadow = '0 0 12px rgba(255,170,0,0.5)';
                rumbleFilterUnwatchedBtn.style.borderColor = '#ffaa00';
            } else {
                rumbleFilterUnwatchedBtn.classList.remove('active');
                rumbleFilterUnwatchedBtn.style.background = '';
                rumbleFilterUnwatchedBtn.style.boxShadow = '';
                rumbleFilterUnwatchedBtn.style.borderColor = 'rgba(255,170,0,0.4)';
            }
            this.loadCinemaFeed('Rumble');
        });

        rumbleFilterDlBtn?.addEventListener('click', () => {
            this.rumbleOnlyDownloaded = !this.rumbleOnlyDownloaded;
            if (this.rumbleOnlyDownloaded) {
                rumbleFilterDlBtn.classList.add('active');
                rumbleFilterDlBtn.style.background = 'rgba(0,255,136,0.25)';
                rumbleFilterDlBtn.style.boxShadow = '0 0 10px rgba(0,255,136,0.5)';
                rumbleFilterDlBtn.style.borderColor = '#00ff88';
            } else {
                rumbleFilterDlBtn.classList.remove('active');
                rumbleFilterDlBtn.style.background = '';
                rumbleFilterDlBtn.style.boxShadow = '';
                rumbleFilterDlBtn.style.borderColor = 'rgba(0,255,136,0.4)';
            }
            this.loadCinemaFeed('Rumble');
        });

        rumbleRefreshBtn?.addEventListener('click', () => this.loadCinemaFeed('Rumble'));

        // Shared Player Control Handlers
        playerWatchedBtn?.addEventListener('click', () => {
            if (!this.cinemaCurrentMedia) return;
            if (this.cinemaCurrentType === 'movie') {
                this.toggleMovieWatched(this.cinemaCurrentMedia.id, !this.cinemaCurrentMedia.isWatched);
            } else {
                this.toggleVideoWatched(this.cinemaCurrentMedia.id, !this.cinemaCurrentMedia.isWatched, this.cinemaCurrentType);
            }
        });

        playerCloseBtn?.addEventListener('click', () => this.closeCinemaScreen());
        playerDownloadBtn?.addEventListener('click', () => {
            if (this.cinemaCurrentMedia && (this.cinemaCurrentType === 'youtube' || this.cinemaCurrentType === 'rumble')) {
                this.downloadCinemaVideo(this.cinemaCurrentMedia, playerDownloadBtn, null, this.cinemaCurrentType);
            }
        });
    }

    // Helper Formatters
    formatDuration(seconds) {
        if (!seconds || isNaN(seconds) || seconds <= 0) return '00:00';
        const s = Math.floor(seconds);
        const hrs = Math.floor(s / 3600);
        const mins = Math.floor((s % 3600) / 60);
        const secs = s % 60;
        if (hrs > 0) {
            return `${hrs}:${mins < 10 ? '0' : ''}${mins}:${secs < 10 ? '0' : ''}${secs}`;
        }
        return `${mins}:${secs < 10 ? '0' : ''}${secs}`;
    }

    formatDate(dateStr) {
        if (!dateStr) return '';
        try {
            const d = new Date(dateStr);
            if (isNaN(d.getTime())) return dateStr;
            return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
        } catch {
            return dateStr;
        }
    }

    formatFileSize(bytes) {
        if (!bytes || bytes <= 0) return '';
        const units = ['B', 'KB', 'MB', 'GB', 'TB'];
        let i = 0;
        let num = bytes;
        while (num >= 1024 && i < units.length - 1) {
            num /= 1024;
            i++;
        }
        return `${num.toFixed(1)} ${units[i]}`;
    }

    // Open & Close Deck
    openCinemaDeck() {
        const modal = document.getElementById('stark-cinema-modal');
        if (!modal) return;
        modal.classList.remove('hidden');
        if (this.sfx && this.sfx.playProtocolPulse) this.sfx.playProtocolPulse();

        // Load all tab badges and initial data
        this.loadMovieVault();
        this.loadCinemaChannels('YouTube');
        this.loadCinemaChannels('Rumble');

        // Activate selected tab (defaults to movies)
        this.switchCinemaTab(this.cinemaActiveTab || 'movies');
    }

    closeCinemaDeck() {
        const modal = document.getElementById('stark-cinema-modal');
        if (modal) modal.classList.add('hidden');
        this.closeCinemaScreen();
    }

    closeCinemaScreen() {
        const container = document.getElementById('cinema-screen-container');
        if (container) container.classList.add('hidden');

        if (this.cinemaLoaderTimer) {
            clearTimeout(this.cinemaLoaderTimer);
            this.cinemaLoaderTimer = null;
        }

        const loader = document.getElementById('cinema-player-loader');
        if (loader) loader.classList.add('hidden');

        const nativePlayer = document.getElementById('cinema-native-player');
        if (nativePlayer) {
            nativePlayer.pause();
            nativePlayer.src = '';
            nativePlayer.style.display = 'none';
        }

        const iframePlayer = document.getElementById('cinema-iframe-player');
        if (iframePlayer) {
            iframePlayer.src = '';
            iframePlayer.style.display = 'none';
        }

        this.cinemaCurrentMedia = null;
        this.cinemaCurrentType = null;
    }

    // Tab Switching
    switchCinemaTab(tab) {
        this.cinemaActiveTab = tab;

        const tabBtnMovies = document.getElementById('cinema-tab-btn-movies');
        const tabBtnYoutube = document.getElementById('cinema-tab-btn-youtube');
        const tabBtnRumble = document.getElementById('cinema-tab-btn-rumble');

        const viewMovies = document.getElementById('cinema-view-movies');
        const viewYoutube = document.getElementById('cinema-view-youtube');
        const viewRumble = document.getElementById('cinema-view-rumble');

        // Update Tab Button Active Classes
        tabBtnMovies?.classList.toggle('active', tab === 'movies');
        tabBtnYoutube?.classList.toggle('active', tab === 'youtube');
        tabBtnRumble?.classList.toggle('active', tab === 'rumble');

        // Update Tab View Visibility
        if (viewMovies) {
            viewMovies.classList.toggle('hidden', tab !== 'movies');
            viewMovies.style.display = tab === 'movies' ? 'block' : 'none';
        }
        if (viewYoutube) {
            viewYoutube.classList.toggle('hidden', tab !== 'youtube');
            viewYoutube.style.display = tab === 'youtube' ? 'block' : 'none';
        }
        if (viewRumble) {
            viewRumble.classList.toggle('hidden', tab !== 'rumble');
            viewRumble.style.display = tab === 'rumble' ? 'block' : 'none';
        }

        if (this.sfx && this.sfx.playHudBeep) this.sfx.playHudBeep(780, 0.05);

        // Fetch / Render Active Tab Data
        if (tab === 'movies') {
            this.loadMovieVault();
        } else if (tab === 'youtube') {
            this.loadCinemaChannels('YouTube');
            this.loadCinemaFeed('YouTube');
        } else if (tab === 'rumble') {
            this.loadCinemaChannels('Rumble');
            this.loadCinemaFeed('Rumble');
        }

        this.updateCinemaGlobalStats();
    }

    updateCinemaGlobalStats() {
        const globalStats = document.getElementById('cinema-global-stats');
        if (!globalStats) return;

        const ytCount = this.youtubeChannels.reduce((sum, ch) => sum + (ch.videoCount || 0), 0);
        const rumbleCount = this.rumbleChannels.reduce((sum, ch) => sum + (ch.videoCount || 0), 0);
        const totalChannels = this.youtubeChannels.length + this.rumbleChannels.length;
        const totalBroadcasts = ytCount + rumbleCount;

        globalStats.textContent = `MOVIES: ${this.moviesList.length} • CHANNELS: ${totalChannels} • BROADCASTS: ${totalBroadcasts}`;
    }

    // =========================================================================
    // TAB 1: STARK MOVIE VAULT ARCHITECTURE
    // =========================================================================
    async loadMovieVault() {
        const countEl = document.getElementById('movie-vault-count');
        const pillEl = document.getElementById('cinema-movies-pill');

        try {
            const resp = await fetch('/api/movies');
            if (!resp.ok) return;

            this.moviesList = await resp.json();
            if (pillEl) pillEl.textContent = this.moviesList.length;

            this.renderMoviesGrid();
            this.updateCinemaGlobalStats();
        } catch (err) {
            console.error("Failed loading movie vault:", err);
            if (countEl) countEl.textContent = '(Library error)';
        }
    }

    renderMoviesGrid() {
        const grid = document.getElementById('cinema-movies-grid');
        const countEl = document.getElementById('movie-vault-count');
        if (!grid) return;

        grid.innerHTML = '';

        // Filter movies
        let list = [...this.moviesList];
        if (this.movieSearch) {
            list = list.filter(m => (m.title && m.title.toLowerCase().includes(this.movieSearch)) || (m.year && m.year.toString().includes(this.movieSearch)));
        }
        if (this.movieOnlyUnwatched) {
            list = list.filter(m => !m.isWatched);
        }

        // Separate and sort: Unwatched at top, watched at bottom!
        const unwatched = list.filter(m => !m.isWatched);
        const watched = list.filter(m => m.isWatched);

        const sortByTitle = (a, b) => (a.title || '').localeCompare(b.title || '');
        unwatched.sort(sortByTitle);
        watched.sort(sortByTitle);

        const ordered = [...unwatched, ...watched];

        if (countEl) {
            const total = this.moviesList.length;
            const unseen = this.moviesList.filter(m => !m.isWatched).length;
            countEl.textContent = `(${total} vault titles • ${unseen} unseen)`;
        }

        if (ordered.length === 0) {
            grid.innerHTML = `
                <div style="grid-column: 1 / -1; text-align:center; padding: 50px 20px; color:#7eb2cc; font-family:var(--font-hud); background:rgba(10,16,28,0.5); border:1px dashed rgba(255,170,0,0.3); border-radius:10px;">
                    <div style="font-size:2.8rem; margin-bottom:12px; filter:drop-shadow(0 0 10px rgba(255,170,0,0.6));">🎬</div>
                    <div style="font-size:1.15rem; color:#ffaa00; letter-spacing:1px; margin-bottom:8px;">STARK MOVIE VAULT EMPTY</div>
                    <div style="font-size:0.78rem; color:#8da4b5; max-width:560px; margin:0 auto; line-height:1.6;">
                        Place your organized movie video files (.mp4, .mkv, .avi, .webm) into <b>Videos\\JarvisMovies\\{Movie Title} ({Year})\\</b> and click <b>RESCAN VAULT</b>.
                    </div>
                </div>
            `;
            return;
        }

        for (const m of ordered) {
            const card = document.createElement('div');
            card.className = `movie-card ${m.isWatched ? 'watched' : 'unwatched'}`;
            card.setAttribute('data-movie-id', m.id);

            const durationText = this.formatDuration(m.durationSeconds);
            const sizeText = this.formatFileSize(m.fileSizeBytes);
            const posterSrc = `/api/movies/poster/${m.id}`;

            card.innerHTML = `
                <div class="movie-poster-wrap">
                    <img src="${posterSrc}" class="movie-poster-img" alt="${this.escape(m.title)}" loading="lazy">
                    <span class="movie-res-pill">${this.escape(m.resolution || '1080p HD')}</span>
                    <span class="movie-format-pill">${this.escape(m.format || '.mp4')}</span>
                    <div class="movie-play-overlay">
                        <div class="movie-play-circle">▶</div>
                    </div>
                </div>
                <div class="movie-body">
                    <div class="movie-title-text" title="${this.escape(m.title)}">${this.escape(m.title)}</div>
                    <div class="movie-meta-row">
                        <span class="movie-year-tag">${m.year ? m.year : 'VAULT'}</span>
                        <span>${durationText !== '00:00' ? durationText : (m.resolution || 'HD')}</span>
                        <span>${sizeText}</span>
                    </div>
                    <div class="movie-actions-row">
                        <button class="movie-watch-btn ${m.isWatched ? 'checked' : ''}" title="${m.isWatched ? 'Mark as Unwatched' : 'Mark as Watched'}">
                            <span>${m.isWatched ? '✓' : '⭕'}</span>
                            <span>${m.isWatched ? 'SEEN' : 'UNSEEN'}</span>
                        </button>
                        <button class="movie-play-btn">
                            <span>▶ PLAY</span>
                        </button>
                    </div>
                </div>
            `;

            // Card Events
            card.querySelector('.movie-poster-wrap')?.addEventListener('click', () => this.playMovieWithLoader(m));
            card.querySelector('.movie-title-text')?.addEventListener('click', () => this.playMovieWithLoader(m));
            card.querySelector('.movie-play-btn')?.addEventListener('click', () => this.playMovieWithLoader(m));

            const watchBtn = card.querySelector('.movie-watch-btn');
            watchBtn?.addEventListener('click', (e) => {
                e.stopPropagation();
                this.toggleMovieWatched(m.id, !m.isWatched);
            });

            grid.appendChild(card);
        }
    }

    async toggleMovieWatched(movieId, isWatched) {
        if (!movieId) return;

        // Optimistically update in memory
        const movie = this.moviesList.find(m => m.id === movieId);
        if (movie) {
            movie.isWatched = isWatched;
            movie.watchedAt = isWatched ? new Date().toISOString() : null;

            if (this.sfx && this.sfx.playHudBeep) {
                this.sfx.playHudBeep(isWatched ? 880 : 620, 0.08);
            }

            // Immediately re-sort & re-render so seen movies sink to bottom!
            this.renderMoviesGrid();

            // Update on-screen player if this movie is playing
            if (this.cinemaCurrentMedia && this.cinemaCurrentMedia.id === movieId && this.cinemaCurrentType === 'movie') {
                this.cinemaCurrentMedia.isWatched = isWatched;
                this.updatePlayerWatchedButton(isWatched);
            }
        }

        try {
            await fetch('/api/movies/watched', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ movieId, isWatched })
            });
        } catch (err) {
            console.error("Failed to update movie watched status on server:", err);
        }
    }

    async rescanMovieVault(btnEl = null) {
        if (btnEl) {
            btnEl.disabled = true;
            btnEl.textContent = 'SCANNING DISK...';
        }
        if (this.sfx) this.sfx.playProtocolPulse();

        try {
            const resp = await fetch('/api/movies/resync', { method: 'POST' });
            if (resp.ok) {
                const movies = await resp.json();
                this.moviesList = movies;
                this.renderMoviesGrid();
                const pillEl = document.getElementById('cinema-movies-pill');
                if (pillEl) pillEl.textContent = this.moviesList.length;
                this.updateCinemaGlobalStats();
            }
        } catch (err) {
            console.error("Failed rescanning movie vault:", err);
        } finally {
            if (btnEl) {
                btnEl.disabled = false;
                btnEl.textContent = '🔄 RESCAN VAULT';
            }
        }
    }

    playMovieWithLoader(movie) {
        if (!movie) return;
        this.cinemaCurrentMedia = movie;
        this.cinemaCurrentType = 'movie';

        const screenContainer = document.getElementById('cinema-screen-container');
        const titleEl = document.getElementById('cinema-player-title');
        const channelEl = document.getElementById('cinema-player-channel');
        const modeBadge = document.getElementById('cinema-player-mode-badge');
        const durationEl = document.getElementById('cinema-player-duration');
        const nativePlayer = document.getElementById('cinema-native-player');
        const iframePlayer = document.getElementById('cinema-iframe-player');
        const playerDlBtn = document.getElementById('btn-cinema-player-download');

        // Pre-buffer loader HUD elements
        const loader = document.getElementById('cinema-player-loader');
        const loaderTitle = document.getElementById('cinema-loader-title');
        const loaderSub = document.getElementById('cinema-loader-sub');
        const loaderVideoName = document.getElementById('cinema-loader-video-name');
        const loaderBar = document.getElementById('cinema-loader-bar');

        const displayTitle = `${movie.title} ${movie.year ? `(${movie.year})` : ''}`;
        if (titleEl) titleEl.textContent = displayTitle;
        if (channelEl) channelEl.textContent = '🎬 STARK MOVIE VAULT';
        if (durationEl) durationEl.textContent = this.formatDuration(movie.durationSeconds || 0);

        if (modeBadge) {
            modeBadge.textContent = `🎬 MASTER STREAM (${movie.resolution || 'HD'})`;
            modeBadge.style.color = '#ffaa00';
            modeBadge.style.borderColor = '#ffaa00';
            modeBadge.style.background = 'rgba(255,170,0,0.15)';
        }

        // Watched button state
        this.updatePlayerWatchedButton(movie.isWatched);

        // Download button: already on disk in movie vault
        if (playerDlBtn) {
            playerDlBtn.disabled = true;
            playerDlBtn.innerHTML = '<span>✓ MASTER VAULT</span>';
            playerDlBtn.style.color = '#ffaa00';
            playerDlBtn.style.borderColor = 'rgba(255,170,0,0.4)';
        }

        // Show theater screen
        screenContainer?.classList.remove('hidden');

        // Show pre-buffer holographic loader
        if (loader) {
            loader.classList.remove('hidden');
            if (loaderTitle) loaderTitle.textContent = 'INITIALIZING QUANTUM MOVIE BUFFER...';
            if (loaderSub) loaderSub.textContent = 'DECODING BITSTREAM • PRE-BUFFERING THEATER CINEMA • AUTO-PLAY READY';
            if (loaderVideoName) loaderVideoName.textContent = displayTitle;
            if (loaderBar) {
                loaderBar.style.width = '15%';
                setTimeout(() => { if (loaderBar) loaderBar.style.width = '80%'; }, 150);
            }
        }

        if (this.cinemaLoaderTimer) {
            clearTimeout(this.cinemaLoaderTimer);
            this.cinemaLoaderTimer = null;
        }

        if (this.sfx && this.sfx.playProtocolPulse) {
            this.sfx.playProtocolPulse();
        }

        const dismissLoader = () => {
            if (loaderBar) loaderBar.style.width = '100%';
            setTimeout(() => {
                if (loader) loader.classList.add('hidden');
            }, 250);
        };

        // Prepare Native Player
        if (iframePlayer) {
            iframePlayer.src = '';
            iframePlayer.style.display = 'none';
        }

        if (nativePlayer) {
            nativePlayer.style.display = 'block';
            nativePlayer.src = `/api/movies/stream/${movie.id}`;

            let hasAutoPlayed = false;
            const onReadyToPlay = () => {
                if (hasAutoPlayed) return;
                hasAutoPlayed = true;
                dismissLoader();
                nativePlayer.play().catch(e => console.warn("Movie auto-play waiting for user interaction:", e));
            };

            nativePlayer.oncanplay = onReadyToPlay;
            nativePlayer.onloadeddata = onReadyToPlay;

            // Fallback timer (1200ms)
            this.cinemaLoaderTimer = setTimeout(() => {
                onReadyToPlay();
            }, 1200);
        }

        screenContainer?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    handleMovieWatchedChanged(data) {
        if (!data || !data.movieId) return;
        const movie = this.moviesList.find(m => m.id === data.movieId);
        if (movie) {
            movie.isWatched = data.isWatched;
            movie.watchedAt = data.watchedAt;
            this.renderMoviesGrid();
        }

        if (this.cinemaCurrentMedia && this.cinemaCurrentMedia.id === data.movieId && this.cinemaCurrentType === 'movie') {
            this.cinemaCurrentMedia.isWatched = data.isWatched;
            this.updatePlayerWatchedButton(data.isWatched);
        }
    }

    // =========================================================================
    // TABS 2 & 3: YOUTUBE & RUMBLE INTELLIGENCE ARCHITECTURE
    // =========================================================================
    async loadCinemaChannels(platform = 'YouTube') {
        const isYt = platform === 'YouTube';
        const pillId = isYt ? 'cinema-youtube-pill' : 'cinema-rumble-pill';

        try {
            const resp = await fetch(`/api/video/channels?platform=${encodeURIComponent(platform)}`);
            if (!resp.ok) return;

            const channels = await resp.json();
            if (isYt) {
                this.youtubeChannels = channels;
            } else {
                this.rumbleChannels = channels;
            }

            const pillEl = document.getElementById(pillId);
            if (pillEl) pillEl.textContent = channels.length;

            this.renderCinemaChannelPills(platform);
            this.updateCinemaGlobalStats();
        } catch (err) {
            console.error(`Failed loading ${platform} channels:`, err);
        }
    }

    renderCinemaChannelPills(platform = 'YouTube') {
        const isYt = platform === 'YouTube';
        const ribbonId = isYt ? 'cinema-youtube-channel-pills' : 'cinema-rumble-channel-pills';
        const ribbon = document.getElementById(ribbonId);
        if (!ribbon) return;

        ribbon.innerHTML = '';
        const channels = isYt ? this.youtubeChannels : this.rumbleChannels;
        const selectedId = isYt ? this.youtubeSelectedChannelId : this.rumbleSelectedChannelId;

        // "ALL CHANNELS" pill
        const allPill = document.createElement('div');
        allPill.className = `cinema-channel-pill ${!selectedId ? 'active' : ''}`;
        allPill.innerHTML = `<span>${isYt ? '🔴' : '🟢'} ALL ${platform.toUpperCase()}</span>`;
        allPill.addEventListener('click', () => {
            if (isYt) this.youtubeSelectedChannelId = null;
            else this.rumbleSelectedChannelId = null;
            this.renderCinemaChannelPills(platform);
            this.loadCinemaFeed(platform);
        });
        ribbon.appendChild(allPill);

        // Individual Channel Pills
        for (const ch of channels) {
            const pill = document.createElement('div');
            const isActive = selectedId === ch.id;
            pill.className = `cinema-channel-pill ${isActive ? 'active' : ''}`;

            const avatarHtml = ch.avatarUrl 
                ? `<img src="${this.escape(ch.avatarUrl)}" class="cinema-channel-pill-avatar" alt="">`
                : `<span style="font-size:0.9rem;">${isYt ? '🔴' : '🟢'}</span>`;

            pill.innerHTML = `
                ${avatarHtml}
                <span>${this.escape(ch.channelName)}</span>
                <span style="font-size:0.65rem;color:#7eb2cc;font-family:var(--font-mono);">(${ch.videoCount || 0})</span>
                <span class="cinema-channel-pill-del" title="Unsubscribe Channel">✕</span>
            `;

            // Filter on click
            pill.addEventListener('click', (e) => {
                if (e.target.classList.contains('cinema-channel-pill-del')) return;
                if (isYt) this.youtubeSelectedChannelId = ch.id;
                else this.rumbleSelectedChannelId = ch.id;
                this.renderCinemaChannelPills(platform);
                this.loadCinemaFeed(platform);
            });

            // Delete subscription on X click
            pill.querySelector('.cinema-channel-pill-del')?.addEventListener('click', (e) => {
                e.stopPropagation();
                if (confirm(`Unsubscribe from ${platform} channel '${ch.channelName}'?`)) {
                    this.deleteCinemaChannel(ch.id, platform);
                }
            });

            ribbon.appendChild(pill);
        }
    }

    async subscribeCinemaChannel(urlOrHandle, platform = 'YouTube') {
        const isYt = platform === 'YouTube';
        const btnId = isYt ? 'btn-cinema-add-youtube' : 'btn-cinema-add-rumble';
        const inputId = isYt ? 'cinema-youtube-input' : 'cinema-rumble-input';

        const btn = document.getElementById(btnId);
        const input = document.getElementById(inputId);

        if (btn) {
            btn.disabled = true;
            btn.textContent = 'PROBING...';
        }

        try {
            const resp = await fetch('/api/video/channels', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ channelUrlOrHandle: urlOrHandle, platform })
            });

            if (!resp.ok) {
                const err = await resp.json();
                alert(`Could not subscribe to ${platform} channel: ${err.error || 'Unknown error'}`);
                return;
            }

            const channel = await resp.json();
            if (input) input.value = '';
            if (this.sfx) this.sfx.playProtocolPulse();

            this.appendAssistantMessage(`🎬 **[STARK CINEMA]**: Subscribed to ${platform} channel **${channel.channelName}**. Fetching newest videos into recommender feed...`);
            await this.loadCinemaChannels(platform);
            await this.loadCinemaFeed(platform);
        } catch (err) {
            console.error(`Error subscribing to ${platform} channel:`, err);
            alert(`Error subscribing channel: ${err.message}`);
        } finally {
            if (btn) {
                btn.disabled = false;
                btn.textContent = `+ SUBSCRIBE ${platform.toUpperCase()}`;
            }
        }
    }

    async deleteCinemaChannel(channelId, platform = 'YouTube') {
        const isYt = platform === 'YouTube';
        try {
            const resp = await fetch(`/api/video/channels/${channelId}`, { method: 'DELETE' });
            if (resp.ok) {
                if (isYt && this.youtubeSelectedChannelId === channelId) this.youtubeSelectedChannelId = null;
                if (!isYt && this.rumbleSelectedChannelId === channelId) this.rumbleSelectedChannelId = null;

                await this.loadCinemaChannels(platform);
                await this.loadCinemaFeed(platform);
            }
        } catch (err) {
            console.error("Error deleting channel:", err);
        }
    }

    async syncChannel(channelId, btnEl = null, platform = 'YouTube') {
        if (btnEl) {
            btnEl.disabled = true;
            btnEl.textContent = 'SYNCING...';
        }
        if (this.sfx) this.sfx.playProtocolPulse();

        try {
            const resp = await fetch(`/api/video/channels/${channelId}/sync`, { method: 'POST' });
            if (resp.ok) {
                await this.loadCinemaChannels(platform);
                await this.loadCinemaFeed(platform);
            }
        } catch (err) {
            console.error("Error syncing channel:", err);
        } finally {
            if (btnEl) {
                btnEl.disabled = false;
                btnEl.textContent = '🔄 SYNC';
            }
        }
    }

    async loadCinemaFeed(platform = 'YouTube') {
        const isYt = platform === 'YouTube';
        const countId = isYt ? 'cinema-youtube-feed-count' : 'cinema-rumble-feed-count';
        const searchInputId = isYt ? 'cinema-youtube-search-input' : 'cinema-rumble-search-input';

        const countEl = document.getElementById(countId);
        const searchInput = document.getElementById(searchInputId);
        const search = searchInput?.value?.trim() || '';

        const selectedChannelId = isYt ? this.youtubeSelectedChannelId : this.rumbleSelectedChannelId;
        const onlyDownloaded = isYt ? this.youtubeOnlyDownloaded : this.rumbleOnlyDownloaded;
        const onlyUnwatched = isYt ? this.youtubeOnlyUnwatched : this.rumbleOnlyUnwatched;

        if (countEl) countEl.textContent = '(Refreshing feed...)';

        try {
            let url = `/api/video/feed?platform=${encodeURIComponent(platform)}&limit=150`;
            if (selectedChannelId) url += `&channelId=${encodeURIComponent(selectedChannelId)}`;
            if (search) url += `&search=${encodeURIComponent(search)}`;
            if (onlyDownloaded) url += `&onlyDownloaded=true`;
            if (onlyUnwatched) url += `&onlyUnwatched=true`;

            const resp = await fetch(url);
            if (!resp.ok) return;

            const videos = await resp.json();
            if (isYt) {
                this.youtubeVideos = videos;
            } else {
                this.rumbleVideos = videos;
            }

            this.renderCinemaChannelSections(videos, platform);

            const unseenCount = videos.filter(v => !v.isWatched).length;
            if (countEl) {
                countEl.textContent = `(${videos.length} broadcasts • ${unseenCount} unseen)`;
            }
            this.updateCinemaGlobalStats();
        } catch (err) {
            console.error(`Failed loading ${platform} feed:`, err);
            if (countEl) countEl.textContent = '(Error loading feed)';
        }
    }

    refreshCinemaFeeds() {
        if (this.cinemaActiveTab === 'movies') {
            this.loadMovieVault();
        } else if (this.cinemaActiveTab === 'youtube') {
            this.loadCinemaFeed('YouTube');
        } else if (this.cinemaActiveTab === 'rumble') {
            this.loadCinemaFeed('Rumble');
        }
    }

    renderCinemaChannelSections(videos, platform = 'YouTube') {
        const isYt = platform === 'YouTube';
        const containerId = isYt ? 'cinema-youtube-channels-container' : 'cinema-rumble-channels-container';
        const container = document.getElementById(containerId);
        if (!container) return;

        container.innerHTML = '';
        const channels = isYt ? this.youtubeChannels : this.rumbleChannels;

        if (!videos || videos.length === 0) {
            const platformAccent = isYt ? '#ff3377' : '#00ff88';
            const platformBorder = isYt ? 'rgba(255,0,85,0.3)' : 'rgba(0,255,136,0.3)';
            const sampleChannels = isYt 
                ? '<b>@DaveShap</b>, <b>@matthew_berman</b>, <b>@airevolutionx</b>, <b>@TheAiGrid</b>'
                : '<b>https://rumble.com/c/Redacted</b>, <b>https://rumble.com/c/RussellBrand</b>, <b>https://rumble.com/c/GlennGreenwald</b>';

            container.innerHTML = `
                <div style="text-align:center;padding:50px 20px;color:#7eb2cc;font-family:var(--font-hud);background:rgba(10,16,28,0.5);border:1px dashed ${platformBorder};border-radius:10px;">
                    <div style="font-size:2.8rem;margin-bottom:12px;filter:drop-shadow(0 0 10px ${platformAccent});">${isYt ? '🔴' : '🟢'}</div>
                    <div style="font-size:1.15rem;color:${platformAccent};letter-spacing:1px;margin-bottom:8px;">NO ${platform.toUpperCase()} BROADCASTS FOUND</div>
                    <div style="font-size:0.78rem;color:#8da4b5;max-width:540px;margin:0 auto;line-height:1.6;">
                        Subscribe to ${platform} channels (e.g. ${sampleChannels}) or click <b>SYNC VAULT</b> to refresh your intelligence streams.
                    </div>
                </div>
            `;
            return;
        }

        // Group videos by channel
        const channelGroups = new Map();
        for (const v of videos) {
            const chKey = v.channelId || v.channelName || 'general';
            if (!channelGroups.has(chKey)) {
                channelGroups.set(chKey, {
                    channelId: v.channelId,
                    channelName: v.channelName,
                    videos: []
                });
            }
            channelGroups.get(chKey).videos.push(v);
        }

        // Render each channel section
        channelGroups.forEach((group, chKey) => {
            const chMeta = channels.find(c => c.id === group.channelId || c.channelName === group.channelName) || {};
            const channelName = chMeta.channelName || group.channelName || `${platform} Channel`;
            const avatarUrl = chMeta.avatarUrl || '';
            const channelHandle = chMeta.channelHandle || '';

            // Separate videos: Unwatched (unseen) stay at top; Watched (seen) sink to the bottom!
            const unwatchedVideos = group.videos.filter(v => !v.isWatched);
            const watchedVideos = group.videos.filter(v => v.isWatched);

            const sortByDate = (a, b) => new Date(b.publishedAt || 0) - new Date(a.publishedAt || 0);
            unwatchedVideos.sort(sortByDate);
            watchedVideos.sort(sortByDate);

            // Combined list: unwatched FIRST, watched LAST (at the bottom)
            const orderedVideos = [...unwatchedVideos, ...watchedVideos];
            const downloadedCount = group.videos.filter(v => v.isDownloaded).length;

            const sectionEl = document.createElement('div');
            sectionEl.className = 'cinema-channel-section';
            sectionEl.setAttribute('data-channel-key', chKey);

            // Channel Header
            const headerEl = document.createElement('div');
            headerEl.className = 'cinema-channel-header';
            headerEl.innerHTML = `
                <div class="cinema-channel-meta-wrap">
                    ${avatarUrl 
                        ? `<img src="${this.escape(avatarUrl)}" class="cinema-channel-avatar-img" alt="${this.escape(channelName)}">`
                        : `<div class="cinema-channel-avatar-img" style="display:flex;align-items:center;justify-content:center;background:rgba(255,0,85,0.2);color:${isYt ? '#ff3377' : '#00ff88'};font-size:1.2rem;">${isYt ? '🔴' : '🟢'}</div>`}
                    <div>
                        <div class="cinema-channel-name-title">
                            <span>${this.escape(channelName)}</span>
                            <span class="cinema-channel-badge-verified" title="Subscribed Channel">✓</span>
                        </div>
                        <div class="cinema-channel-sub-info">
                            <span>${channelHandle ? this.escape(channelHandle) : `${platform} Feed`}</span>
                            <span>•</span>
                            <span>${group.videos.length} broadcasts tracked</span>
                        </div>
                    </div>
                </div>

                <div class="cinema-channel-actions-bar">
                    <span class="cinema-stat-pill ${unwatchedVideos.length > 0 ? 'unwatched' : ''}">⚡ ${unwatchedVideos.length} UNSEEN</span>
                    <span class="cinema-stat-pill ${watchedVideos.length > 0 ? 'watched' : ''}">✓ ${watchedVideos.length} WATCHED</span>
                    <span class="cinema-stat-pill">💾 ${downloadedCount} OFFLINE</span>
                    ${chMeta.id ? `
                        <button class="hud-btn cinema-channel-sync-btn" style="padding:4px 8px;font-size:0.68rem;border-color:rgba(0,212,255,0.4);color:#00d4ff;" title="Sync newest broadcasts from this channel">🔄 SYNC</button>
                    ` : ''}
                </div>
            `;

            // Channel sync button click handler
            headerEl.querySelector('.cinema-channel-sync-btn')?.addEventListener('click', (e) => {
                e.stopPropagation();
                if (chMeta.id) this.syncChannel(chMeta.id, e.currentTarget, platform);
            });

            // Video List
            const videoListEl = document.createElement('div');
            videoListEl.className = 'cinema-video-list';

            for (const v of orderedVideos) {
                const rowEl = document.createElement('div');
                rowEl.className = `cinema-video-row ${v.isWatched ? 'watched' : 'unwatched'}`;
                rowEl.setAttribute('data-video-id', v.id);

                const durationFormatted = this.formatDuration(v.durationSeconds || 0);
                const publishedFormatted = this.formatDate(v.publishedAt);
                const viewsFormatted = v.viewCount ? `${Number(v.viewCount).toLocaleString()} views` : '';

                // Fallback thumbnail: YouTube standard vs Rumble placeholder
                const defaultThumb = isYt ? `https://i.ytimg.com/vi/${v.id}/hqdefault.jpg` : '';
                const thumbSrc = v.thumbnailUrl || defaultThumb;

                rowEl.innerHTML = `
                    <!-- Column 1: Interactive Seen / Unseen Checkbox -->
                    <div class="cinema-watch-col">
                        <button class="cinema-watch-btn ${v.isWatched ? 'checked' : ''}" title="${v.isWatched ? 'Mark as Unwatched' : 'Mark as Watched'}">
                            <span class="cinema-watch-icon">${v.isWatched ? '✓' : '⭕'}</span>
                            <span class="cinema-watch-label">${v.isWatched ? 'SEEN' : 'UNSEEN'}</span>
                        </button>
                    </div>

                    <!-- Column 2: Video Thumbnail -->
                    <div class="cinema-row-thumb-wrap">
                        ${thumbSrc 
                            ? `<img src="${this.escape(thumbSrc)}" alt="" loading="lazy">` 
                            : `<div style="width:100%;height:100%;display:flex;align-items:center;justify-content:center;background:#0c101c;font-size:1.8rem;color:${isYt ? '#ff3377' : '#00ff88'};">${isYt ? '🔴' : '🟢'}</div>`}
                        <span class="cinema-row-duration">${durationFormatted}</span>
                        <div class="cinema-row-play-overlay">
                            <div class="cinema-row-play-icon">▶</div>
                        </div>
                    </div>

                    <!-- Column 3: Video Title & Information -->
                    <div class="cinema-row-info">
                        <div class="cinema-row-title" title="${this.escape(v.title)}">${this.escape(v.title)}</div>
                        <div class="cinema-row-meta">
                            <span class="cinema-row-badge ${v.isWatched ? 'seen' : 'unseen'}">
                                ${v.isWatched ? '✓ WATCHED' : '⚡ NEW / UNWATCHED'}
                            </span>
                            <span class="cinema-row-badge ${v.isDownloaded ? 'downloaded' : 'stream'}">
                                ${v.isDownloaded ? '💾 LOCAL HD' : `⚡ ${platform.toUpperCase()} STREAM`}
                            </span>
                            ${publishedFormatted ? `<span>📅 ${this.escape(publishedFormatted)}</span>` : ''}
                            ${viewsFormatted ? `<span>👁 ${this.escape(viewsFormatted)}</span>` : ''}
                        </div>
                    </div>

                    <!-- Column 4: Row Actions (Play & Download) -->
                    <div class="cinema-row-actions">
                        <button class="cinema-row-btn play-btn" style="border-color:#00d4ff;color:#00d4ff;background:rgba(0,212,255,0.1);">
                            ▶ PLAY
                        </button>
                        <button class="cinema-row-btn download-btn" ${v.isDownloaded ? 'disabled style="color:#00ff88;border-color:rgba(0,255,136,0.3);"' : ''}>
                            ${v.isDownloaded ? '✓ SAVED' : '⬇ DOWNLOAD'}
                        </button>
                    </div>

                    <div class="cinema-row-progress-bar" style="display:none;position:absolute;bottom:0;left:0;height:2px;background:#00ff88;box-shadow:0 0 8px #00ff88;width:0%;"></div>
                `;

                // Interactive Watch Checkbox Click
                const watchBtn = rowEl.querySelector('.cinema-watch-btn');
                watchBtn?.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this.toggleVideoWatched(v.id, !v.isWatched, platform);
                });

                // Play video on clicking thumbnail, title, or Play button
                rowEl.querySelector('.cinema-row-thumb-wrap')?.addEventListener('click', () => this.playVideoWithLoader(v, platform));
                rowEl.querySelector('.cinema-row-title')?.addEventListener('click', () => this.playVideoWithLoader(v, platform));
                rowEl.querySelector('.play-btn')?.addEventListener('click', () => this.playVideoWithLoader(v, platform));

                // Download button click
                const dlBtn = rowEl.querySelector('.download-btn');
                dlBtn?.addEventListener('click', (e) => {
                    e.stopPropagation();
                    if (!v.isDownloaded && !this.cinemaDownloadingIds.has(v.id)) {
                        this.downloadCinemaVideo(v, dlBtn, rowEl, platform);
                    }
                });

                videoListEl.appendChild(rowEl);
            }

            sectionEl.appendChild(headerEl);
            sectionEl.appendChild(videoListEl);
            container.appendChild(sectionEl);
        });
    }

    async toggleVideoWatched(videoId, isWatched, platform = 'YouTube') {
        if (!videoId) return;

        const isYt = (platform || '').toLowerCase().includes('youtube');
        const list = isYt ? this.youtubeVideos : this.rumbleVideos;
        const video = list.find(v => v.id === videoId);

        if (video) {
            video.isWatched = isWatched;
            video.watchedAt = isWatched ? new Date().toISOString() : null;

            if (this.sfx && this.sfx.playHudBeep) {
                this.sfx.playHudBeep(isWatched ? 880 : 620, 0.08);
            }

            // Immediately re-render feed so the video drops to the bottom
            this.renderCinemaChannelSections(list, isYt ? 'YouTube' : 'Rumble');

            // If this video is currently playing, update player controls
            if (this.cinemaCurrentMedia && this.cinemaCurrentMedia.id === videoId && this.cinemaCurrentType !== 'movie') {
                this.cinemaCurrentMedia.isWatched = isWatched;
                this.updatePlayerWatchedButton(isWatched);
            }
        }

        try {
            await fetch('/api/video/watched', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoId, isWatched })
            });
        } catch (err) {
            console.error("Failed to update video watched status on server:", err);
        }
    }

    updatePlayerWatchedButton(isWatched) {
        const btn = document.getElementById('btn-cinema-player-watched');
        const icon = document.getElementById('cinema-player-watched-icon');
        const text = document.getElementById('cinema-player-watched-text');

        if (!btn || !icon || !text) return;

        if (isWatched) {
            icon.textContent = '✓';
            text.textContent = 'WATCHED (CLICK TO UNWATCH)';
            btn.style.color = '#00ff88';
            btn.style.borderColor = '#00ff88';
            btn.style.background = 'rgba(0,255,136,0.2)';
            btn.style.boxShadow = '0 0 10px rgba(0,255,136,0.3)';
        } else {
            icon.textContent = '⭕';
            text.textContent = 'MARK AS WATCHED';
            btn.style.color = '#ffaa00';
            btn.style.borderColor = 'rgba(255,170,0,0.5)';
            btn.style.background = 'rgba(255,170,0,0.1)';
            btn.style.boxShadow = 'none';
        }
    }

    playVideoWithLoader(video, platform = 'YouTube') {
        if (!video) return;
        this.cinemaCurrentMedia = video;
        this.cinemaCurrentType = (platform || video.platform || 'YouTube').toLowerCase();

        const screenContainer = document.getElementById('cinema-screen-container');
        const titleEl = document.getElementById('cinema-player-title');
        const channelEl = document.getElementById('cinema-player-channel');
        const modeBadge = document.getElementById('cinema-player-mode-badge');
        const durationEl = document.getElementById('cinema-player-duration');
        const nativePlayer = document.getElementById('cinema-native-player');
        const iframePlayer = document.getElementById('cinema-iframe-player');
        const playerDlBtn = document.getElementById('btn-cinema-player-download');

        // Pre-buffer loader HUD elements
        const loader = document.getElementById('cinema-player-loader');
        const loaderTitle = document.getElementById('cinema-loader-title');
        const loaderSub = document.getElementById('cinema-loader-sub');
        const loaderVideoName = document.getElementById('cinema-loader-video-name');
        const loaderBar = document.getElementById('cinema-loader-bar');

        if (titleEl) titleEl.textContent = video.title;
        if (channelEl) channelEl.textContent = video.channelName;
        if (durationEl) durationEl.textContent = this.formatDuration(video.durationSeconds || 0);

        // Update Watched toggle button in player
        this.updatePlayerWatchedButton(video.isWatched);

        // Update player download button
        if (playerDlBtn) {
            if (video.isDownloaded) {
                playerDlBtn.disabled = true;
                playerDlBtn.innerHTML = '<span>✓ SAVED OFFLINE</span>';
                playerDlBtn.style.color = '#00ff88';
                playerDlBtn.style.borderColor = '#00ff88';
            } else {
                playerDlBtn.disabled = false;
                playerDlBtn.innerHTML = '<span>⬇️ DOWNLOAD (YT-DLP)</span>';
                playerDlBtn.style.color = '';
                playerDlBtn.style.borderColor = '';
            }
        }

        // Show theater container
        screenContainer?.classList.remove('hidden');

        // Show pre-buffer holographic loader
        if (loader) {
            loader.classList.remove('hidden');
            if (loaderTitle) loaderTitle.textContent = `INITIALIZING ${platform.toUpperCase()} QUANTUM BUFFER...`;
            if (loaderSub) loaderSub.textContent = 'SYNCHRONIZING AUDIO / VIDEO EMITTERS • STANDBY FOR AUTO-PLAY';
            if (loaderVideoName) loaderVideoName.textContent = video.title;
            if (loaderBar) {
                loaderBar.style.width = '15%';
                setTimeout(() => { if (loaderBar) loaderBar.style.width = '75%'; }, 150);
            }
        }

        if (this.cinemaLoaderTimer) {
            clearTimeout(this.cinemaLoaderTimer);
            this.cinemaLoaderTimer = null;
        }

        if (this.sfx && this.sfx.playProtocolPulse) {
            this.sfx.playProtocolPulse();
        }

        const dismissLoader = () => {
            if (loaderBar) loaderBar.style.width = '100%';
            setTimeout(() => {
                if (loader) loader.classList.add('hidden');
            }, 250);
        };

        // Determine playback source: Local HD Master vs Online Embed Stream
        if (video.isDownloaded && video.localFilePath) {
            if (modeBadge) {
                modeBadge.textContent = '💾 LOCAL HD (OFFLINE MASTER)';
                modeBadge.style.color = '#00ff88';
                modeBadge.style.borderColor = '#00ff88';
                modeBadge.style.background = 'rgba(0,255,136,0.15)';
            }

            if (iframePlayer) {
                iframePlayer.src = '';
                iframePlayer.style.display = 'none';
            }

            if (nativePlayer) {
                nativePlayer.style.display = 'block';
                nativePlayer.src = `/api/video/stream/${video.id}`;

                let hasAutoPlayed = false;
                const onReadyToPlay = () => {
                    if (hasAutoPlayed) return;
                    hasAutoPlayed = true;
                    dismissLoader();
                    nativePlayer.play().catch(e => console.warn("Native video auto-play interrupted:", e));
                };

                nativePlayer.oncanplay = onReadyToPlay;
                nativePlayer.onloadeddata = onReadyToPlay;

                this.cinemaLoaderTimer = setTimeout(() => {
                    onReadyToPlay();
                }, 1200);
            }
        } else {
            // Online Stream Embed
            const isRumble = (platform || video.platform || '').toLowerCase().includes('rumble');
            if (modeBadge) {
                modeBadge.textContent = isRumble ? '🟢 ONLINE STREAM (RUMBLE)' : '⚡ ONLINE STREAM (YOUTUBE)';
                modeBadge.style.color = isRumble ? '#00ff88' : '#00d4ff';
                modeBadge.style.borderColor = isRumble ? '#00ff88' : '#00d4ff';
                modeBadge.style.background = isRumble ? 'rgba(0,255,136,0.15)' : 'rgba(0,212,255,0.15)';
            }

            if (nativePlayer) {
                nativePlayer.pause();
                nativePlayer.src = '';
                nativePlayer.style.display = 'none';
            }

            if (iframePlayer) {
                iframePlayer.style.display = 'block';
                if (isRumble) {
                    iframePlayer.src = video.embedUrl || `https://rumble.com/embed/${video.id}/?autoplay=2`;
                } else {
                    iframePlayer.src = `https://www.youtube-nocookie.com/embed/${video.id}?autoplay=1&rel=0&modestbranding=1`;
                }

                let hasAutoPlayed = false;
                const onIframeReady = () => {
                    if (hasAutoPlayed) return;
                    hasAutoPlayed = true;
                    setTimeout(() => {
                        dismissLoader();
                    }, 600);
                };

                iframePlayer.onload = onIframeReady;

                // Fallback timer (1800ms)
                this.cinemaLoaderTimer = setTimeout(() => {
                    onIframeReady();
                }, 1800);
            }
        }

        screenContainer?.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    async downloadCinemaVideo(video, btnEl = null, rowEl = null, platform = 'YouTube') {
        if (!video || this.cinemaDownloadingIds.has(video.id)) return;
        this.cinemaDownloadingIds.add(video.id);

        if (btnEl) {
            btnEl.disabled = true;
            btnEl.textContent = 'DOWNLOADING...';
        }

        const progressBar = rowEl?.querySelector('.cinema-row-progress-bar');
        if (progressBar) {
            progressBar.style.display = 'block';
            progressBar.style.width = '10%';
        }

        if (this.sfx) this.sfx.playProtocolPulse();

        try {
            const resp = await fetch('/api/video/download', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ videoId: video.id })
            });

            if (!resp.ok) {
                const err = await resp.json();
                alert(`Failed to download video: ${err.error || 'Server error'}`);
                this.cinemaDownloadingIds.delete(video.id);
                if (btnEl) {
                    btnEl.disabled = false;
                    btnEl.textContent = '⬇ RETRY';
                }
                if (progressBar) progressBar.style.display = 'none';
                return;
            }

            const downloaded = await resp.json();
            this.handleVideoReady(downloaded);
        } catch (err) {
            console.error("Error downloading video:", err);
            this.cinemaDownloadingIds.delete(video.id);
            if (btnEl) {
                btnEl.disabled = false;
                btnEl.textContent = '⬇ RETRY';
            }
            if (progressBar) progressBar.style.display = 'none';
        }
    }

    updateVideoDownloadProgress(p) {
        if (!p || !p.videoId) return;

        const row = document.querySelector(`.cinema-video-row[data-video-id="${p.videoId}"]`);
        if (row) {
            const bar = row.querySelector('.cinema-row-progress-bar');
            if (bar) {
                bar.style.display = 'block';
                bar.style.width = `${p.percent || 15}%`;
            }

            const btn = row.querySelector('.download-btn');
            if (btn) {
                btn.textContent = `${p.percent || 0}%`;
            }
        }

        if (this.cinemaCurrentMedia && this.cinemaCurrentMedia.id === p.videoId) {
            const playerDlBtn = document.getElementById('btn-cinema-player-download');
            if (playerDlBtn) {
                playerDlBtn.textContent = `DOWNLOADING (${p.percent || 0}%)...`;
            }
        }
    }

    handleVideoReady(video) {
        if (!video || !video.id) return;
        this.cinemaDownloadingIds.delete(video.id);

        const row = document.querySelector(`.cinema-video-row[data-video-id="${video.id}"]`);
        if (row) {
            const badges = row.querySelectorAll('.cinema-row-badge');
            badges.forEach(b => {
                if (b.textContent.includes('STREAM') || b.textContent.includes('LOCAL HD')) {
                    b.className = 'cinema-row-badge downloaded';
                    b.textContent = '💾 LOCAL HD';
                }
            });

            const btn = row.querySelector('.download-btn');
            if (btn) {
                btn.disabled = true;
                btn.textContent = '✓ SAVED';
                btn.style.color = '#00ff88';
                btn.style.borderColor = 'rgba(0,255,136,0.3)';
            }

            const bar = row.querySelector('.cinema-row-progress-bar');
            if (bar) bar.style.display = 'none';
        }

        // Update in-memory collections
        const ytIdx = this.youtubeVideos.findIndex(v => v.id === video.id);
        if (ytIdx >= 0) this.youtubeVideos[ytIdx] = video;

        const rmIdx = this.rumbleVideos.findIndex(v => v.id === video.id);
        if (rmIdx >= 0) this.rumbleVideos[rmIdx] = video;

        // If currently open on screen, switch to local stream
        if (this.cinemaCurrentMedia && this.cinemaCurrentMedia.id === video.id) {
            this.cinemaCurrentMedia = video;
            const playerDlBtn = document.getElementById('btn-cinema-player-download');
            if (playerDlBtn) {
                playerDlBtn.disabled = true;
                playerDlBtn.innerHTML = '<span>✓ SAVED OFFLINE</span>';
                playerDlBtn.style.color = '#00ff88';
                playerDlBtn.style.borderColor = '#00ff88';
            }

            const modeBadge = document.getElementById('cinema-player-mode-badge');
            if (modeBadge) {
                modeBadge.textContent = '💾 LOCAL HD (OFFLINE MASTER)';
                modeBadge.style.color = '#00ff88';
                modeBadge.style.borderColor = '#00ff88';
                modeBadge.style.background = 'rgba(0,255,136,0.15)';
            }
        }
    }

    handleVideoWatchedChanged(data) {
        if (!data || !data.videoId) return;

        const ytVid = this.youtubeVideos.find(v => v.id === data.videoId);
        if (ytVid) {
            ytVid.isWatched = data.isWatched;
            ytVid.watchedAt = data.watchedAt;
            this.renderCinemaChannelSections(this.youtubeVideos, 'YouTube');
        }

        const rmVid = this.rumbleVideos.find(v => v.id === data.videoId);
        if (rmVid) {
            rmVid.isWatched = data.isWatched;
            rmVid.watchedAt = data.watchedAt;
            this.renderCinemaChannelSections(this.rumbleVideos, 'Rumble');
        }

        if (this.cinemaCurrentMedia && this.cinemaCurrentMedia.id === data.videoId && this.cinemaCurrentType !== 'movie') {
            this.cinemaCurrentMedia.isWatched = data.isWatched;
            this.updatePlayerWatchedButton(data.isWatched);
        }
    }
}

// Start application when DOM is ready
window.addEventListener('DOMContentLoaded', () => {
    window.jarvisApp = new JarvisApp();
});


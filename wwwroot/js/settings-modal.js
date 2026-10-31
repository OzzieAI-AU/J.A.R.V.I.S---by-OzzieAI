/**
 * J.A.R.V.I.S. Master Advanced Settings Modal Controller
 * Provides complete configuration for:
 * 1. Persona & Butler identity
 * 2. Dynamic Predicates & Memory
 * 3. Dynamic Agents Studio (Create / Switch / Delete)
 * 4. Dynamic Skills Registry (Create / Edit / Delete)
 * 5. Dynamic Tools & MCP (Create / Test / Execute)
 * 6. Multi-LLM Providers & Keys
 * 7. Voice (TTS & STT) Studio
 * 8. REA (Reverse Engineer Anything) inspection workbench
 * 9. Display & Themes
 */

class JarvisSettingsModal {
    constructor(appContext) {
        this.app = appContext;
        this.modalEl = document.getElementById('settings-modal');
        this.currentConfig = null;
        this.predicatesList = [];
        this.agentsList = [];
        this.skillsList = [];
        this.toolsList = [];

        this.init();
    }

    init() {
        this.bindTabNavigation();
        this.bindActionButtons();
        this.loadAllData();
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

    open() {
        if (!this.modalEl) return;
        this.modalEl.classList.remove('hidden');
        this.loadAllData();
        if (window.sfx) window.sfx.playHudBeep(660);
    }

    close() {
        if (!this.modalEl) return;
        this.modalEl.classList.add('hidden');
        if (window.sfx) window.sfx.playHudBeep(440);
    }

    bindTabNavigation() {
        const parentBtns = document.querySelectorAll('.parent-tab-btn');
        const subtabGroups = document.querySelectorAll('.subtabs-group');
        const subtabBtns = document.querySelectorAll('.subtab-btn, .tab-btn');

        // 1. Parent Category Tab Click Handlers
        parentBtns.forEach(pBtn => {
            pBtn.addEventListener('click', () => {
                const parentKey = pBtn.getAttribute('data-parent');
                parentBtns.forEach(b => b.classList.remove('active'));
                pBtn.classList.add('active');

                // Toggle child subtabs groups
                subtabGroups.forEach(grp => {
                    if (grp.getAttribute('data-parent-group') === parentKey) {
                        grp.classList.add('active');
                        // Activate the currently active subtab or the first subtab in this group
                        let activeSub = grp.querySelector('.subtab-btn.active') || grp.querySelector('.subtab-btn');
                        if (activeSub) {
                            grp.querySelectorAll('.subtab-btn').forEach(s => s.classList.remove('active'));
                            activeSub.classList.add('active');
                            const targetTab = activeSub.getAttribute('data-tab');
                            this.showTabPane(targetTab);
                        }
                    } else {
                        grp.classList.remove('active');
                    }
                });

                if (window.sfx) window.sfx.playHudBeep(700, 0.04);
            });
        });

        // 2. Child Sub-Tab Click Handlers
        subtabBtns.forEach(subBtn => {
            subBtn.addEventListener('click', () => {
                const targetTab = subBtn.getAttribute('data-tab');
                this.activateTab(targetTab);
            });
        });
    }

    showTabPane(targetTab) {
        if (!targetTab) return;
        document.querySelectorAll('.settings-tab-pane').forEach(pane => {
            pane.classList.remove('active');
        });
        const targetPane = document.getElementById(`pane-${targetTab}`);
        if (targetPane) {
            targetPane.classList.add('active');
        }

        // Auto-refresh / render tab content when activated
        if (targetTab === 'contacts') {
            if (!this.contactsList || this.contactsList.length === 0) {
                this.loadContactsData();
            } else {
                this.renderContactsTable();
            }
        } else if (targetTab === 'projects') {
            if (!this.projectsList || this.projectsList.length === 0) {
                this.loadProjectsData();
            } else {
                this.renderSettingsProjects(this.projectsList);
            }
        } else if (targetTab === 'calendar') {
            this.loadCalendarData();
        } else if (targetTab === 'storage') {
            this.loadStorageData();
        } else if (targetTab === 'agents') {
            if (!this.agentsList || this.agentsList.length === 0) this.loadAgents();
            else this.renderAgentsList();
        } else if (targetTab === 'skills') {
            if (!this.skillsList || this.skillsList.length === 0) this.loadSkills();
            else this.renderSkillsList();
        } else if (targetTab === 'tools') {
            if (!this.toolsList || this.toolsList.length === 0) this.loadTools();
            else this.renderToolsList();
        } else if (targetTab === 'predicates') {
            if (!this.predicatesList || this.predicatesList.length === 0) this.loadPredicates();
            else this.renderPredicates();
        } else if (targetTab === 'about-me') {
            if (!this.userProfile) this.loadUserProfile();
        }
    }

    activateTab(targetTab) {
        if (!targetTab) return;
        const subBtn = document.querySelector(`.subtab-btn[data-tab="${targetTab}"], .tab-btn[data-tab="${targetTab}"]`);
        if (!subBtn) {
            this.showTabPane(targetTab);
            return;
        }

        const parentGroup = subBtn.closest('.subtabs-group');
        if (parentGroup) {
            const parentKey = parentGroup.getAttribute('data-parent-group');
            // Activate parent tab button
            document.querySelectorAll('.parent-tab-btn').forEach(p => {
                if (p.getAttribute('data-parent') === parentKey) p.classList.add('active');
                else p.classList.remove('active');
            });

            // Activate subtabs group
            document.querySelectorAll('.subtabs-group').forEach(grp => {
                if (grp.getAttribute('data-parent-group') === parentKey) grp.classList.add('active');
                else grp.classList.remove('active');
            });

            // Activate subtab button
            parentGroup.querySelectorAll('.subtab-btn').forEach(s => s.classList.remove('active'));
            subBtn.classList.add('active');
        } else {
            document.querySelectorAll('.tab-btn').forEach(s => s.classList.remove('active'));
            subBtn.classList.add('active');
        }

        this.showTabPane(targetTab);
        if (window.sfx) window.sfx.playHudBeep(850, 0.04);
    }

    bindActionButtons() {
        // Close buttons
        document.getElementById('btn-close-settings')?.addEventListener('click', () => this.close());
        document.getElementById('btn-cancel-settings')?.addEventListener('click', () => this.close());
        document.getElementById('btn-save-settings')?.addEventListener('click', () => this.saveCurrentConfig());

        // Predicate actions
        document.getElementById('btn-add-predicate')?.addEventListener('click', () => this.addNewPredicate());

        // Agent actions
        document.getElementById('btn-create-agent')?.addEventListener('click', () => this.createNewAgent());

        // Skill actions
        document.getElementById('btn-create-skill')?.addEventListener('click', () => this.createNewSkill());

        // Tool actions
        document.getElementById('btn-create-tool')?.addEventListener('click', () => this.createNewTool());
        document.getElementById('btn-test-tool')?.addEventListener('click', () => this.testExecuteTool());

        // REA actions
        document.getElementById('btn-rea-analyze')?.addEventListener('click', () => this.executeReaAnalyze());

        // Voice Engine selector sync & Test Voice button
        const syncTtsEngineVisibility = () => {
            const engine = document.getElementById('cfg-tts-engine')?.value || 'Browser';
            const grpBrowser = document.getElementById('grp-browser-voice');
            const grpOpenAi = document.getElementById('grp-openai-voice');
            const rowEleven = document.getElementById('row-elevenlabs');
            if (grpBrowser) grpBrowser.style.display = (engine === 'Browser') ? 'flex' : 'none';
            if (grpOpenAi) grpOpenAi.style.display = (engine === 'OpenAI') ? 'flex' : 'none';
            if (rowEleven) rowEleven.style.display = (engine === 'ElevenLabs') ? 'flex' : 'none';
        };

        document.getElementById('cfg-tts-engine')?.addEventListener('change', syncTtsEngineVisibility);

        document.getElementById('btn-test-voice')?.addEventListener('click', () => {
            const text = "At your command, Sir. All holographic neural cores and flight telemetry are operational.";
            const engine = document.getElementById('cfg-tts-engine')?.value || 'Browser';

            if (engine === 'Browser' && this.app.voiceStudio) {
                const selectedVoice = document.getElementById('cfg-browser-voice')?.value;
                const pitch = parseFloat(document.getElementById('cfg-browser-pitch')?.value || 1.0);
                const rate = parseFloat(document.getElementById('cfg-browser-rate')?.value || 1.0);
                if (selectedVoice) this.app.voiceStudio.setVoiceByName(selectedVoice);
                this.app.voiceStudio.setVoiceParams(pitch, rate, 1.0);
                this.app.voiceStudio.speak(text);
            } else if (this.app.voiceStudio) {
                this.app.voiceStudio.speak(text);
            }
        });

        // Listen for speech synthesis voices loaded/changed
        if (window.speechSynthesis && window.speechSynthesis.onvoiceschanged !== undefined) {
            window.speechSynthesis.addEventListener('voiceschanged', () => {
                this.populateBrowserVoicesDropdown();
            });
        }

        // Theme selector change
        document.getElementById('cfg-theme')?.addEventListener('change', (e) => {
            const theme = e.target.value;
            if (this.app) this.app.applyTheme(theme);
        });

        // Interactive theme swatches
        document.querySelectorAll('.theme-swatch').forEach(swatch => {
            swatch.addEventListener('click', () => {
                const theme = swatch.getAttribute('data-theme');
                if (theme) {
                    const sel = document.getElementById('cfg-theme');
                    if (sel) sel.value = theme;
                    if (this.app) this.app.applyTheme(theme);
                    if (window.sfx) window.sfx.playHudBeep(750, 0.05);
                }
            });
        });

        // Calendar & Task actions
        document.getElementById('btn-create-appointment')?.addEventListener('click', () => this.createNewAppointment());
        document.getElementById('btn-create-task')?.addEventListener('click', () => this.createNewScheduledTask());

        // Storage & Ghost Drive actions
        document.getElementById('btn-create-ghost-drive')?.addEventListener('click', () => this.createNewGhostDrive());
        document.getElementById('btn-refresh-files')?.addEventListener('click', () => this.refreshGhostFiles());
        document.getElementById('select-ghost-explorer')?.addEventListener('change', () => this.refreshGhostFiles());

        // Reset TTS symbols
        document.getElementById('btn-reset-tts-symbols')?.addEventListener('click', () => {
            const el = document.getElementById('cfg-tts-exclusion');
            if (el) el.value = '* _ # ~ ` | [ ] ( ) { } < > \\ / @ ^ = + • › » « $ % & ;';
        });

        // About Me / User Profile actions
        document.getElementById('btn-save-user-profile')?.addEventListener('click', () => this.saveUserProfile());
        document.getElementById('profile-color-picker')?.addEventListener('input', (e) => {
            const hex = document.getElementById('profile-favoritecolor');
            if (hex) hex.value = e.target.value;
        });
        document.getElementById('profile-favoritecolor')?.addEventListener('input', (e) => {
            const picker = document.getElementById('profile-color-picker');
            if (picker && e.target.value.startsWith('#')) picker.value = e.target.value;
        });

        // Project Management actions
        document.getElementById('btn-settings-run-research')?.addEventListener('click', () => this.openResearchModal());
        document.getElementById('btn-drawer-run-research')?.addEventListener('click', () => this.openResearchModal());
        document.getElementById('btn-settings-open-projects-drawer')?.addEventListener('click', () => {
            this.close();
            this.openProjectsDrawer();
        });
        document.getElementById('btn-close-projects-drawer')?.addEventListener('click', () => this.closeProjectsDrawer());
        document.getElementById('btn-close-research-modal')?.addEventListener('click', () => this.closeResearchModal());
        document.getElementById('btn-cancel-research-modal')?.addEventListener('click', () => this.closeResearchModal());
        document.getElementById('btn-execute-research-agent')?.addEventListener('click', () => this.executeResearchAgent());

        // Contacts actions in Settings Studio
        document.getElementById('btn-settings-add-contact')?.addEventListener('click', () => this.openContactEditModal(null));
        document.getElementById('settings-contacts-search')?.addEventListener('input', (e) => this.filterSettingsContacts(e.target.value));

        document.querySelectorAll('#settings-contact-tag-filters .contact-filter-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('#settings-contact-tag-filters .contact-filter-btn').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                this.activeContactsFilter = btn.getAttribute('data-filter');
                this.renderContactsTable();
            });
        });

        // Contact Modal buttons
        document.getElementById('btn-close-contact-modal')?.addEventListener('click', () => this.closeContactEditModal());
        document.getElementById('btn-cancel-contact-modal')?.addEventListener('click', () => this.closeContactEditModal());
        document.getElementById('btn-save-contact-modal')?.addEventListener('click', () => this.saveContact());

        // Preset tag chips in Contact Modal
        document.querySelectorAll('.preset-tag-chip').forEach(chip => {
            chip.addEventListener('click', () => {
                const tag = chip.getAttribute('data-tag');
                const input = document.getElementById('edit-contact-tags');
                if (input) {
                    const current = input.value.split(',').map(s => s.trim()).filter(Boolean);
                    if (!current.includes(tag)) {
                        current.push(tag);
                        input.value = current.join(', ');
                    }
                }
            });
        });
    }

    async loadAllData() {
        await Promise.all([
            this.loadConfig(),
            this.loadUserProfile(),
            this.loadPredicates(),
            this.loadAgents(),
            this.loadSkills(),
            this.loadTools(),
            this.loadCalendarData(),
            this.loadContactsData(),
            this.loadStorageData(),
            this.loadProjectsData()
        ]);
    }

    async loadConfig() {
        try {
            const res = await fetch('/api/config');
            if (res.ok) {
                this.currentConfig = await res.json();
                this.populateConfigFields(this.currentConfig);
            }
        } catch (e) {
            console.error("Failed to load config", e);
        }
    }

    populateConfigFields(cfg) {
        if (!cfg) return;

        // Persona
        document.getElementById('cfg-bot-name').value = cfg.persona.botName || 'J.A.R.V.I.S.';
        document.getElementById('cfg-user-name').value = cfg.persona.userName || 'Tony Stark';
        document.getElementById('cfg-system-instructions').value = cfg.persona.systemInstructions || '';
        document.getElementById('cfg-temperature').value = cfg.persona.temperature || 0.7;
        document.getElementById('cfg-wit-level').value = cfg.persona.witLevel || 0.8;
        document.getElementById('cfg-formality-level').value = cfg.persona.formalityLevel || 0.9;

        // Providers
        document.getElementById('cfg-active-provider').value = cfg.providers.activeProvider || 'OpenAI';
        document.getElementById('cfg-openai-key').value = cfg.providers.openAiApiKey || '';
        document.getElementById('cfg-openai-model').value = cfg.providers.openAiModel || 'gpt-4o';
        document.getElementById('cfg-claude-key').value = cfg.providers.claudeApiKey || '';
        document.getElementById('cfg-claude-model').value = cfg.providers.claudeModel || 'claude-3-5-sonnet-20241022';
        document.getElementById('cfg-gemini-key').value = cfg.providers.geminiApiKey || '';
        document.getElementById('cfg-gemini-model').value = cfg.providers.geminiModel || 'gemini-2.5-flash';
        document.getElementById('cfg-groq-key').value = cfg.providers.groqApiKey || '';
        document.getElementById('cfg-groq-model').value = cfg.providers.groqModel || 'llama-3.3-70b-versatile';
        document.getElementById('cfg-ollama-url').value = cfg.providers.ollamaBaseUrl || 'http://localhost:11434';
        document.getElementById('cfg-ollama-model').value = cfg.providers.ollamaModel || 'llama3.2';
        document.getElementById('cfg-deepseek-key').value = cfg.providers.deepSeekApiKey || '';
        document.getElementById('cfg-deepseek-model').value = cfg.providers.deepSeekModel || 'deepseek-chat';

        // Voice
        document.getElementById('cfg-tts-engine').value = cfg.voice.ttsEngine || 'Browser';
        document.getElementById('cfg-browser-pitch').value = cfg.voice.browserPitch || 1.0;
        document.getElementById('cfg-browser-rate').value = cfg.voice.browserRate || 1.05;
        document.getElementById('cfg-elevenlabs-key').value = cfg.voice.elevenLabsApiKey || '';
        document.getElementById('cfg-elevenlabs-voice').value = cfg.voice.elevenLabsVoiceId || '';
        document.getElementById('cfg-stt-engine').value = cfg.voice.sttEngine || 'Browser';
        document.getElementById('cfg-wake-word').value = cfg.voice.wakeWord || 'jarvis';
        document.getElementById('cfg-wake-word-enabled').checked = cfg.voice.wakeWordEnabled !== false;

        // MCP & Sensors
        document.getElementById('cfg-mcp-url').value = cfg.mcp.serverUrl || 'http://localhost:5200/mcp';
        document.getElementById('cfg-sensor-poll').value = cfg.mcp.sensorPollIntervalMs || 1000;
        document.getElementById('cfg-enable-sys-sensors').checked = cfg.mcp.enableSystemSensors !== false;
        document.getElementById('cfg-enable-arc-sensors').checked = cfg.mcp.enableSimulatedArcReactor !== false;

        // Voice Sanitization & Exclusion
        if (cfg.voice) {
            const exclInput = document.getElementById('cfg-tts-exclusion');
            if (exclInput) exclInput.value = cfg.voice.ttsExclusionSymbols || '* _ # ~ ` | [ ] ( ) { } < > \\ / @ ^ = + • › » « $ % & ;';
            const sanitizeCb = document.getElementById('cfg-tts-sanitization-enabled');
            if (sanitizeCb) sanitizeCb.checked = cfg.voice.enableTtsSanitization !== false;
            const mdCb = document.getElementById('cfg-tts-strip-markdown');
            if (mdCb) mdCb.checked = cfg.voice.stripMarkdownFromTts !== false;
            const emojiCb = document.getElementById('cfg-tts-strip-emojis');
            if (emojiCb) emojiCb.checked = cfg.voice.stripEmojisFromTts !== false;
            const sttExcl = document.getElementById('cfg-stt-exclusion');
            if (sttExcl) sttExcl.value = cfg.voice.sttExclusionPhrases || 'um, uh, ah, erm, like, you know, thank you for watching, subtitles by, [music], [applause]';
        }

        // Storage & Low Space Config
        if (cfg.storage) {
            const threshPct = document.getElementById('cfg-storage-threshold-pct');
            if (threshPct) threshPct.value = cfg.storage.lowSpaceAlertThresholdPercent ?? 15;
            const threshGb = document.getElementById('cfg-storage-threshold-gb');
            if (threshGb) threshGb.value = cfg.storage.lowSpaceAlertThresholdGb ?? 10;
            const alertCb = document.getElementById('cfg-storage-enable-alerts');
            if (alertCb) alertCb.checked = cfg.storage.enableLowSpaceAlerts !== false;
        }

        // Display
        document.getElementById('cfg-theme').value = cfg.display.theme || 'JarvisCyan';
        document.getElementById('cfg-particle-density').value = cfg.display.particleDensity || 1200;
        document.getElementById('cfg-sfx-volume').value = cfg.display.sfxVolume || 0.6;

        this.populateBrowserVoicesDropdown();

        // Sync TTS engine section visibility
        const engine = cfg.voice.ttsEngine || 'Browser';
        const grpBrowser = document.getElementById('grp-browser-voice');
        const grpOpenAi = document.getElementById('grp-openai-voice');
        const rowEleven = document.getElementById('row-elevenlabs');
        if (grpBrowser) grpBrowser.style.display = (engine === 'Browser') ? 'flex' : 'none';
        if (grpOpenAi) grpOpenAi.style.display = (engine === 'OpenAI') ? 'flex' : 'none';
        if (rowEleven) rowEleven.style.display = (engine === 'ElevenLabs') ? 'flex' : 'none';
    }

    populateBrowserVoicesDropdown() {
        const select = document.getElementById('cfg-browser-voice');
        if (!select || !window.speechSynthesis) return;

        const voices = window.speechSynthesis.getVoices();
        if (!voices || voices.length === 0) return;

        // Helper to locate best matching voice for J.A.R.V.I.S. (Paul Bettany Butler persona)
        const findBestJarvisVoice = (list) => {
            // 1. British / UK English Male
            const ukMale = list.find(v => 
                (v.lang.includes('en-GB') || v.name.includes('UK') || v.name.includes('British') || v.name.includes('George') || v.name.includes('Ryan')) &&
                !v.name.includes('Female') && !v.name.includes('Zira') && !v.name.includes('Susan') && !v.name.includes('Hazel') && !v.name.includes('Sonia')
            );
            if (ukMale) return ukMale;

            // 2. Microsoft Mark (Deep, calm, resonant masculine tone - closest match to Paul Bettany on Windows)
            const mark = list.find(v => v.name.includes('Mark'));
            if (mark) return mark;

            // 3. Microsoft David
            const david = list.find(v => v.name.includes('David'));
            if (david) return david;

            // 4. Any English male voice
            const enMale = list.find(v => 
                v.lang.includes('en') && !v.name.includes('Female') && !v.name.includes('Zira') && !v.name.includes('Susan') && !v.name.includes('Hazel')
            );
            if (enMale) return enMale;

            return list[0];
        };

        const bestVoice = findBestJarvisVoice(voices);

        select.innerHTML = '';
        voices.forEach(v => {
            const opt = document.createElement('option');
            opt.value = v.name;
            const isBest = bestVoice && v.name === bestVoice.name;
            opt.textContent = isBest 
                ? `🌟 [RECOMMENDED JARVIS] ${v.name} (${v.lang})` 
                : `${v.name} (${v.lang})`;
            select.appendChild(opt);
        });

        // Determine selection: if saved voice exists in current voices list, keep it; otherwise auto-select best Jarvis voice!
        const savedVoiceName = this.currentConfig && this.currentConfig.voice ? this.currentConfig.voice.browserVoiceName : '';
        const savedExists = voices.some(v => v.name === savedVoiceName);

        if (savedExists) {
            select.value = savedVoiceName;
        } else if (bestVoice) {
            select.value = bestVoice.name;
            if (this.currentConfig && this.currentConfig.voice) {
                this.currentConfig.voice.browserVoiceName = bestVoice.name;
            }
        }
    }

    async saveCurrentConfig() {
        const cfg = this.currentConfig || {
            security: {},
            persona: {},
            providers: {},
            voice: {},
            mcp: {},
            storage: {},
            scheduler: {},
            display: {}
        };

        // Persona
        cfg.persona.botName = document.getElementById('cfg-bot-name').value.trim();
        cfg.persona.userName = document.getElementById('cfg-user-name').value.trim();
        cfg.persona.systemInstructions = document.getElementById('cfg-system-instructions').value.trim();
        cfg.persona.temperature = parseFloat(document.getElementById('cfg-temperature').value);
        cfg.persona.witLevel = parseFloat(document.getElementById('cfg-wit-level').value);
        cfg.persona.formalityLevel = parseFloat(document.getElementById('cfg-formality-level').value);

        // Providers
        cfg.providers.activeProvider = document.getElementById('cfg-active-provider').value;
        cfg.providers.openAiApiKey = document.getElementById('cfg-openai-key').value.trim();
        cfg.providers.openAiModel = document.getElementById('cfg-openai-model').value.trim();
        cfg.providers.claudeApiKey = document.getElementById('cfg-claude-key').value.trim();
        cfg.providers.claudeModel = document.getElementById('cfg-claude-model').value.trim();
        cfg.providers.geminiApiKey = document.getElementById('cfg-gemini-key').value.trim();
        cfg.providers.geminiModel = document.getElementById('cfg-gemini-model').value.trim();
        cfg.providers.groqApiKey = document.getElementById('cfg-groq-key').value.trim();
        cfg.providers.groqModel = document.getElementById('cfg-groq-model').value.trim();
        cfg.providers.ollamaBaseUrl = document.getElementById('cfg-ollama-url').value.trim();
        cfg.providers.ollamaModel = document.getElementById('cfg-ollama-model').value.trim();
        cfg.providers.deepSeekApiKey = document.getElementById('cfg-deepseek-key').value.trim();
        cfg.providers.deepSeekModel = document.getElementById('cfg-deepseek-model').value.trim();

        // Voice
        cfg.voice.ttsEngine = document.getElementById('cfg-tts-engine').value;
        cfg.voice.browserVoiceName = document.getElementById('cfg-browser-voice').value;
        cfg.voice.openAiTtsVoice = document.getElementById('cfg-openai-tts-voice')?.value || 'onyx';
        cfg.voice.browserPitch = parseFloat(document.getElementById('cfg-browser-pitch').value);
        cfg.voice.browserRate = parseFloat(document.getElementById('cfg-browser-rate').value);
        cfg.voice.elevenLabsApiKey = document.getElementById('cfg-elevenlabs-key').value.trim();
        cfg.voice.elevenLabsVoiceId = document.getElementById('cfg-elevenlabs-voice').value.trim();
        cfg.voice.sttEngine = document.getElementById('cfg-stt-engine').value;
        cfg.voice.wakeWord = document.getElementById('cfg-wake-word').value.trim().toLowerCase();
        cfg.voice.wakeWordEnabled = document.getElementById('cfg-wake-word-enabled').checked;

        // Voice Sanitization
        cfg.voice.enableTtsSanitization = document.getElementById('cfg-tts-sanitization-enabled')?.checked ?? true;
        cfg.voice.ttsExclusionSymbols = document.getElementById('cfg-tts-exclusion')?.value ?? '* _ # ~ ` | [ ] ( ) { } < > \\ / @ ^ = + • › » « $ % & ;';
        cfg.voice.stripMarkdownFromTts = document.getElementById('cfg-tts-strip-markdown')?.checked ?? true;
        cfg.voice.stripEmojisFromTts = document.getElementById('cfg-tts-strip-emojis')?.checked ?? true;
        cfg.voice.sttExclusionPhrases = document.getElementById('cfg-stt-exclusion')?.value ?? 'um, uh, ah, erm, like, you know, thank you for watching, subtitles by, [music], [applause]';

        // Storage Config
        if (!cfg.storage) cfg.storage = {};
        cfg.storage.lowSpaceAlertThresholdPercent = parseFloat(document.getElementById('cfg-storage-threshold-pct')?.value || 15);
        cfg.storage.lowSpaceAlertThresholdGb = parseFloat(document.getElementById('cfg-storage-threshold-gb')?.value || 10);
        cfg.storage.enableLowSpaceAlerts = document.getElementById('cfg-storage-enable-alerts')?.checked ?? true;

        // MCP & Sensors
        cfg.mcp.serverUrl = document.getElementById('cfg-mcp-url').value.trim();
        cfg.mcp.sensorPollIntervalMs = parseInt(document.getElementById('cfg-sensor-poll').value, 10);
        cfg.mcp.enableSystemSensors = document.getElementById('cfg-enable-sys-sensors').checked;
        cfg.mcp.enableSimulatedArcReactor = document.getElementById('cfg-enable-arc-sensors').checked;

        // Display
        cfg.display.theme = document.getElementById('cfg-theme').value;
        cfg.display.particleDensity = parseInt(document.getElementById('cfg-particle-density').value, 10);
        cfg.display.sfxVolume = parseFloat(document.getElementById('cfg-sfx-volume').value);

        try {
            const res = await fetch('/api/config/update', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(cfg)
            });

            if (res.ok) {
                this.currentConfig = cfg;
                this.app.applyConfig(cfg);
                this.close();
                if (window.sfx) window.sfx.playAccessGrantedChime();
            }
        } catch (e) {
            console.error("Failed to update config", e);
        }
    }

    // ==================== PREDICATES ====================
    async loadPredicates() {
        try {
            const res = await fetch('/api/predicates');
            if (res.ok) {
                this.predicatesList = await res.json();
                this.renderPredicatesTable(this.predicatesList);
            }
        } catch (e) {
            console.error("Failed to load predicates", e);
        }
    }

    renderPredicatesTable(list) {
        const tbody = document.querySelector('#table-predicates tbody');
        if (!tbody) return;
        tbody.innerHTML = '';

        list.forEach(item => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td><strong>${this.escape(item.key)}</strong></td>
                <td><span style="color:var(--primary-color)">${this.escape(item.value)}</span></td>
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;">${this.escape(item.category)}</span></td>
                <td><small>${this.escape(item.description || '')}</small></td>
                <td>
                    <button class="hud-btn hud-btn-danger" style="padding:2px 8px;font-size:0.7rem;" onclick="window.jarvisApp.settingsModal.deletePredicate('${item.key}')">DEL</button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    async addNewPredicate() {
        const key = document.getElementById('new-predicate-key').value.trim();
        const value = document.getElementById('new-predicate-value').value.trim();
        const category = document.getElementById('new-predicate-category').value.trim();
        const description = document.getElementById('new-predicate-desc').value.trim();

        if (!key || !value) {
            alert("Key and Value are required.");
            return;
        }

        try {
            const res = await fetch('/api/predicates/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ key, value, category, description })
            });

            if (res.ok) {
                document.getElementById('new-predicate-key').value = '';
                document.getElementById('new-predicate-value').value = '';
                document.getElementById('new-predicate-desc').value = '';
                await this.loadPredicates();
                if (window.sfx) window.sfx.playHudBeep(900);
            }
        } catch (e) {
            console.error(e);
        }
    }

    async deletePredicate(key) {
        if (!confirm(`Delete predicate ${key}?`)) return;
        try {
            const res = await fetch(`/api/predicates/${encodeURIComponent(key)}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadPredicates();
            }
        } catch (e) {
            console.error(e);
        }
    }

    // ==================== AGENTS ====================
    async loadAgents() {
        try {
            const res = await fetch('/api/agents');
            if (res.ok) {
                this.agentsList = await res.json();
                this.renderAgentsList(this.agentsList);
                this.app.updateHeaderAgentPicker(this.agentsList);
            }
        } catch (e) {
            console.error("Failed to load agents", e);
        }
    }

    renderAgentsList(list) {
        const container = document.getElementById('agents-card-grid');
        if (!container) return;
        container.innerHTML = '';

        list.forEach(agent => {
            const card = document.createElement('div');
            card.className = `hud-panel ${agent.isActive ? 'active-agent-glow' : ''}`;
            card.style.padding = '14px';
            card.style.gap = '8px';
            card.style.borderColor = agent.colorTheme || 'var(--border-hud)';

            card.innerHTML = `
                <div style="display:flex;justify-content:space-between;align-items:center;">
                    <div style="display:flex;align-items:center;gap:8px;">
                        <span style="width:10px;height:10px;border-radius:50%;background:${agent.colorTheme || '#00d4ff'};box-shadow:0 0 8px ${agent.colorTheme || '#00d4ff'};"></span>
                        <strong style="font-family:var(--font-hud);color:#fff;">${this.escape(agent.name)}</strong>
                    </div>
                    ${agent.isActive ? '<span class="status-dot"></span>' : ''}
                </div>
                <div style="color:var(--primary-color);font-size:0.75rem;">${this.escape(agent.role)}</div>
                <p style="font-size:0.8rem;color:#b0d8ea;flex:1;">${this.escape(agent.description)}</p>
                <div style="display:flex;gap:8px;margin-top:8px;">
                    ${!agent.isActive ? `<button class="hud-btn hud-btn-primary" style="flex:1;padding:4px 8px;font-size:0.75rem;" onclick="window.jarvisApp.settingsModal.activateAgent('${agent.id}')">ACTIVATE</button>` : '<span style="color:var(--accent-color);font-family:var(--font-hud);font-size:0.75rem;">ONLINE</span>'}
                    ${!agent.isBuiltIn ? `<button class="hud-btn hud-btn-danger" style="padding:4px 8px;font-size:0.75rem;" onclick="window.jarvisApp.settingsModal.deleteAgent('${agent.id}')">DELETE</button>` : ''}
                </div>
            `;
            container.appendChild(card);
        });
    }

    async activateAgent(id) {
        try {
            const res = await fetch(`/api/agents/activate/${id}`, { method: 'POST' });
            if (res.ok) {
                await this.loadAgents();
                if (window.sfx) window.sfx.playProtocolPulse();
            }
        } catch (e) {
            console.error(e);
        }
    }

    async createNewAgent() {
        const name = document.getElementById('new-agent-name').value.trim();
        const role = document.getElementById('new-agent-role').value.trim();
        const systemPrompt = document.getElementById('new-agent-prompt').value.trim();
        const tone = document.getElementById('new-agent-tone').value.trim();
        const color = document.getElementById('new-agent-color').value.trim();

        if (!name || !systemPrompt) {
            alert("Name and System Prompt are required.");
            return;
        }

        const agent = {
            id: 'agent-' + Date.now(),
            name,
            role: role || 'Stark Specialist',
            description: `Custom agent created: ${name}`,
            systemPrompt,
            tone: tone || 'Direct',
            colorTheme: color || '#00d4ff',
            isActive: false,
            isBuiltIn: false
        };

        try {
            const res = await fetch('/api/agents/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(agent)
            });

            if (res.ok) {
                document.getElementById('new-agent-name').value = '';
                document.getElementById('new-agent-role').value = '';
                document.getElementById('new-agent-prompt').value = '';
                await this.loadAgents();
                if (window.sfx) window.sfx.playHudBeep(920);
            }
        } catch (e) {
            console.error(e);
        }
    }

    async deleteAgent(id) {
        if (!confirm("Delete this custom agent?")) return;
        try {
            const res = await fetch(`/api/agents/${id}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadAgents();
            }
        } catch (e) {
            console.error(e);
        }
    }

    // ==================== SKILLS ====================
    async loadSkills() {
        try {
            const res = await fetch('/api/skills');
            if (res.ok) {
                this.skillsList = await res.json();
                this.renderSkillsTable(this.skillsList);
            }
        } catch (e) {
            console.error("Failed to load skills", e);
        }
    }

    renderSkillsTable(list) {
        const tbody = document.querySelector('#table-skills tbody');
        if (!tbody) return;
        tbody.innerHTML = '';

        list.forEach(skill => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td><strong>${this.escape(skill.name)}</strong></td>
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;">${this.escape(skill.category)}</span></td>
                <td><small>${this.escape(skill.description)}</small></td>
                <td><code>${this.escape(skill.instructions.substring(0, 60))}...</code></td>
                <td>
                    ${!skill.isBuiltIn ? `<button class="hud-btn hud-btn-danger" style="padding:2px 8px;font-size:0.7rem;" onclick="window.jarvisApp.settingsModal.deleteSkill('${skill.id}')">DEL</button>` : '<span style="color:#6ea2be;font-size:0.7rem;">BUILT-IN</span>'}
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    async createNewSkill() {
        const name = document.getElementById('new-skill-name').value.trim();
        const category = document.getElementById('new-skill-category').value.trim();
        const description = document.getElementById('new-skill-desc').value.trim();
        const instructions = document.getElementById('new-skill-instructions').value.trim();

        if (!name || !instructions) {
            alert("Skill name and instructions are required.");
            return;
        }

        const skill = {
            id: 'skill-' + Date.now(),
            name,
            category: category || 'Engineering',
            description,
            instructions,
            isBuiltIn: false
        };

        try {
            const res = await fetch('/api/skills/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(skill)
            });

            if (res.ok) {
                document.getElementById('new-skill-name').value = '';
                document.getElementById('new-skill-desc').value = '';
                document.getElementById('new-skill-instructions').value = '';
                await this.loadSkills();
                if (window.sfx) window.sfx.playHudBeep(880);
            }
        } catch (e) {
            console.error(e);
        }
    }

    async deleteSkill(id) {
        if (!confirm("Delete this custom skill?")) return;
        try {
            const res = await fetch(`/api/skills/${id}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadSkills();
            }
        } catch (e) {
            console.error(e);
        }
    }

    // ==================== TOOLS ====================
    async loadTools() {
        try {
            const res = await fetch('/api/tools');
            if (res.ok) {
                this.toolsList = await res.json();
                this.renderToolsTable(this.toolsList);
                this.populateToolsTestPicker(this.toolsList);
            }
        } catch (e) {
            console.error("Failed to load tools", e);
        }
    }

    renderToolsTable(list) {
        const tbody = document.querySelector('#table-tools tbody');
        if (!tbody) return;
        tbody.innerHTML = '';

        list.forEach(tool => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td><code>${this.escape(tool.name)}</code></td>
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;">${this.escape(tool.executionType)}</span></td>
                <td><small>${this.escape(tool.description)}</small></td>
                <td>
                    ${!tool.isBuiltIn ? `<button class="hud-btn hud-btn-danger" style="padding:2px 8px;font-size:0.7rem;" onclick="window.jarvisApp.settingsModal.deleteTool('${tool.id}')">DEL</button>` : '<span style="color:#6ea2be;font-size:0.7rem;">BUILT-IN</span>'}
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    populateToolsTestPicker(list) {
        const select = document.getElementById('test-tool-select');
        if (!select) return;
        select.innerHTML = '';
        list.forEach(t => {
            const opt = document.createElement('option');
            opt.value = t.name;
            opt.textContent = `${t.name} (${t.executionType})`;
            select.appendChild(opt);
        });
    }

    async createNewTool() {
        const name = document.getElementById('new-tool-name').value.trim();
        const description = document.getElementById('new-tool-desc').value.trim();
        const type = document.getElementById('new-tool-type').value;
        const endpoint = document.getElementById('new-tool-endpoint').value.trim();
        const schema = document.getElementById('new-tool-schema').value.trim() || '{}';

        if (!name) {
            alert("Tool name is required.");
            return;
        }

        const tool = {
            id: 'tool-' + Date.now(),
            name,
            description,
            executionType: type,
            endpointUrl: endpoint,
            parametersJsonSchema: schema,
            isBuiltIn: false
        };

        try {
            const res = await fetch('/api/tools/save', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(tool)
            });

            if (res.ok) {
                document.getElementById('new-tool-name').value = '';
                document.getElementById('new-tool-desc').value = '';
                document.getElementById('new-tool-endpoint').value = '';
                await this.loadTools();
                if (window.sfx) window.sfx.playHudBeep(850);
            }
        } catch (e) {
            console.error(e);
        }
    }

    async deleteTool(id) {
        if (!confirm("Delete this custom tool?")) return;
        try {
            const res = await fetch(`/api/tools/${id}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadTools();
            }
        } catch (e) {
            console.error(e);
        }
    }

    async testExecuteTool() {
        const select = document.getElementById('test-tool-select');
        const argsInput = document.getElementById('test-tool-args');
        const outputPre = document.getElementById('test-tool-output');

        if (!select || !outputPre) return;
        const toolName = select.value;
        let args = {};

        try {
            if (argsInput.value.trim()) {
                args = JSON.parse(argsInput.value);
            }
        } catch (e) {
            alert("Invalid JSON arguments");
            return;
        }

        outputPre.textContent = "Executing tool...";

        try {
            const res = await fetch('/api/tools/execute', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ toolNameOrId: toolName, arguments: args })
            });

            const result = await res.json();
            outputPre.textContent = JSON.stringify(result, null, 2);
            if (window.sfx) window.sfx.playHudBeep(750);
        } catch (e) {
            outputPre.textContent = "Error executing tool: " + e.message;
        }
    }

    // ==================== REA (Reverse Engineer Anything) ====================
    async executeReaAnalyze() {
        const targetInput = document.getElementById('rea-target-path');
        const outputPre = document.getElementById('rea-analysis-output');
        if (!targetInput || !outputPre) return;

        const target = targetInput.value.trim();
        if (!target) {
            alert("Please enter a target path or binary to reverse engineer.");
            return;
        }

        outputPre.textContent = `[REA Engine]: Inspecting target '${target}'...\nExtracting PE/ELF headers, .NET reflection symbols, and disassembly signatures...`;

        try {
            const res = await fetch('/api/rea/analyze', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ target })
            });

            const result = await res.json();
            outputPre.textContent = result.summaryMarkdown || JSON.stringify(result, null, 2);
            if (window.sfx) window.sfx.playAccessGrantedChime();
        } catch (e) {
            outputPre.textContent = `[REA Exception]: ${e.message}`;
        }
    }

    // ==================== CALENDAR & APPOINTMENTS ====================
    async loadCalendarData() {
        try {
            const [apptRes, taskRes] = await Promise.all([
                fetch('/api/calendar/appointments'),
                fetch('/api/scheduler/tasks')
            ]);

            if (apptRes.ok) {
                this.appointmentsList = await apptRes.json();
                this.renderAppointmentsTable(this.appointmentsList);
            }

            if (taskRes.ok) {
                this.tasksList = await taskRes.json();
                this.renderTasksTable(this.tasksList);
            }

            this.populateTaskAgentSelect();
        } catch (e) {
            console.error("Failed to load calendar & scheduler data", e);
        }
    }

    populateTaskAgentSelect() {
        const select = document.getElementById('new-task-agent');
        if (!select) return;
        select.innerHTML = '';
        (this.agentsList || []).forEach(a => {
            const opt = document.createElement('option');
            opt.value = a.name;
            opt.textContent = `${a.name} (${a.role})`;
            select.appendChild(opt);
        });
    }

    renderAppointmentsTable(list) {
        const tbody = document.querySelector('#table-appointments tbody');
        if (!tbody) return;
        tbody.innerHTML = '';

        if (!list || list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="5" style="text-align:center;color:#7eb2cc;">No appointments scheduled in Stark Tactical Calendar.</td></tr>';
            return;
        }

        list.forEach(item => {
            const tr = document.createElement('tr');
            const d = new Date(item.startTime);
            const timeStr = isNaN(d) ? item.startTime : d.toLocaleString();

            let statusColor = 'var(--primary-color)';
            if (item.status === 'Reminding') statusColor = 'var(--accent-color)';
            else if (item.status === 'Dismissed') statusColor = '#4caf50';
            else if (item.status === 'Cancelled') statusColor = '#f44336';

            tr.innerHTML = `
                <td><strong>${this.escape(item.title)}</strong><br><small style="color:#7eb2cc;">${this.escape(item.description || '')}</small></td>
                <td><span style="color:#fff;">${this.escape(item.attendees || 'None')}</span></td>
                <td><span style="font-family:monospace;color:var(--primary-color);">${timeStr}</span></td>
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;border-color:${statusColor};color:${statusColor}">${this.escape(item.status)}</span></td>
                <td>
                    ${item.status !== 'Dismissed' && item.status !== 'Cancelled' ? `
                        <button class="hud-btn" style="padding:2px 8px;font-size:0.7rem;margin-right:4px;" onclick="window.jarvisApp.settingsModal.dismissAppointment('${item.id}')">DISMISS</button>
                    ` : ''}
                    <button class="hud-btn hud-btn-danger" style="padding:2px 8px;font-size:0.7rem;" onclick="window.jarvisApp.settingsModal.deleteAppointment('${item.id}')">DEL</button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    async createNewAppointment() {
        const title = document.getElementById('new-appt-title')?.value.trim();
        const attendees = document.getElementById('new-appt-attendees')?.value.trim();
        const timeVal = document.getElementById('new-appt-time')?.value;
        const lead = parseInt(document.getElementById('new-appt-lead')?.value || 15, 10);
        const desc = document.getElementById('new-appt-desc')?.value.trim();

        if (!title) {
            alert("Please specify appointment title.");
            return;
        }

        const startTime = timeVal ? new Date(timeVal).toISOString() : new Date(Date.now() + 3600000).toISOString();

        try {
            const res = await fetch('/api/calendar/appointments', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    title,
                    attendees,
                    startTime,
                    leadReminderMinutes: lead,
                    description: desc
                })
            });

            if (res.ok) {
                document.getElementById('new-appt-title').value = '';
                document.getElementById('new-appt-attendees').value = '';
                document.getElementById('new-appt-desc').value = '';
                await this.loadCalendarData();
                if (window.sfx) window.sfx.playAccessGrantedChime();
            }
        } catch (e) {
            console.error("Create appointment failed", e);
        }
    }

    async dismissAppointment(id) {
        try {
            const res = await fetch(`/api/calendar/appointments/${id}/dismiss`, { method: 'POST' });
            if (res.ok) {
                await this.loadCalendarData();
                if (window.sfx) window.sfx.playHudBeep(880, 0.05);
            }
        } catch (e) {
            console.error(e);
        }
    }

    async deleteAppointment(id) {
        if (!confirm("Delete this appointment?")) return;
        try {
            const res = await fetch(`/api/calendar/appointments/${id}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadCalendarData();
            }
        } catch (e) {
            console.error(e);
        }
    }

    // ==================== AUTONOMOUS TASKS & SCHEDULER ====================
    renderTasksTable(list) {
        const tbody = document.querySelector('#table-tasks tbody');
        if (!tbody) return;
        tbody.innerHTML = '';

        if (!list || list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="6" style="text-align:center;color:#7eb2cc;">No autonomous tasks deployed in Scheduler.</td></tr>';
            return;
        }

        list.forEach(item => {
            const tr = document.createElement('tr');
            const d = new Date(item.nextRun);
            const nextStr = isNaN(d) ? item.nextRun : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });

            tr.innerHTML = `
                <td><strong>${this.escape(item.name)}</strong><br><small style="color:#7eb2cc;">${this.escape(item.promptOrDirective)}</small></td>
                <td><span style="color:var(--primary-color);">${this.escape(item.assignedAgentName || 'J.A.R.V.I.S.')}</span></td>
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;">${item.runsPerDay}x / day (~${item.intervalMinutes}m)</span></td>
                <td><span style="font-family:monospace;color:var(--primary-color);">${nextStr}</span></td>
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;border-color:#4caf50;color:#4caf50;">${this.escape(item.status)}</span></td>
                <td>
                    <button class="hud-btn" style="padding:2px 8px;font-size:0.7rem;margin-right:4px;" onclick="window.jarvisApp.settingsModal.executeTaskNow('${item.id}')">RUN NOW</button>
                    <button class="hud-btn hud-btn-danger" style="padding:2px 8px;font-size:0.7rem;" onclick="window.jarvisApp.settingsModal.deleteScheduledTask('${item.id}')">DEL</button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    async createNewScheduledTask() {
        const name = document.getElementById('new-task-name')?.value.trim();
        const agent = document.getElementById('new-task-agent')?.value.trim();
        const runs = parseInt(document.getElementById('new-task-runs')?.value || 7, 10);
        const directive = document.getElementById('new-task-directive')?.value.trim();

        if (!name || !directive) {
            alert("Please enter Task Name and Research Directive.");
            return;
        }

        try {
            const res = await fetch('/api/scheduler/tasks', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    assignedAgentName: agent,
                    runsPerDay: runs,
                    promptOrDirective: directive
                })
            });

            if (res.ok) {
                document.getElementById('new-task-name').value = '';
                document.getElementById('new-task-directive').value = '';
                await this.loadCalendarData();
                if (window.sfx) window.sfx.playAccessGrantedChime();
            }
        } catch (e) {
            console.error("Create task failed", e);
        }
    }

    async executeTaskNow(id) {
        try {
            const res = await fetch(`/api/scheduler/tasks/${id}/run`, { method: 'POST' });
            if (res.ok) {
                const data = await res.json();
                alert(`Task executed successfully.\nOutput snippet:\n${(data.output || '').substring(0, 300)}...`);
                await this.loadCalendarData();
            }
        } catch (e) {
            console.error(e);
        }
    }

    async deleteScheduledTask(id) {
        if (!confirm("Cancel and delete this autonomous task?")) return;
        try {
            const res = await fetch(`/api/scheduler/tasks/${id}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadCalendarData();
            }
        } catch (e) {
            console.error(e);
        }
    }

    // ==================== STORAGE & GHOST DRIVES ====================
    async loadStorageData() {
        try {
            const [telRes, ghostRes] = await Promise.all([
                fetch('/api/storage/telemetry'),
                fetch('/api/storage/ghost-drives')
            ]);

            if (telRes.ok) {
                const telemetry = await telRes.json();
                this.renderStorageDrives(telemetry);
            }

            if (ghostRes.ok) {
                this.ghostDrivesList = await ghostRes.json();
                this.renderGhostDrivesTable(this.ghostDrivesList);
                this.populateGhostExplorerSelect(this.ghostDrivesList);
            }
        } catch (e) {
            console.error("Failed to load storage telemetry & ghost drives", e);
        }
    }

    renderStorageDrives(drives) {
        const grid = document.getElementById('storage-drives-grid');
        if (!grid) return;
        grid.innerHTML = '';

        drives.forEach(d => {
            const card = document.createElement('div');
            card.className = 'drive-card';
            if (d.isLowSpace) card.classList.add('low-space');

            const pctUsed = d.percentUsed || 0;
            const barClass = d.isLowSpace ? 'drive-bar-danger' : (pctUsed > 75 ? 'drive-bar-warn' : 'drive-bar-normal');

            card.innerHTML = `
                <div style="display:flex;justify-content:space-between;align-items:center;">
                    <strong style="color:#fff;letter-spacing:1px;font-size:0.95rem;">${this.escape(d.name)}</strong>
                    <span class="telemetry-pill" style="font-size:0.65rem;padding:1px 6px;">${d.isVirtual ? 'VIRTUAL GHOST' : 'PHYSICAL'}</span>
                </div>
                <div style="font-size:0.75rem;color:#7eb2cc;margin-top:2px;">
                    ${this.escape(d.volumeLabel || 'Local Volume')} (${this.escape(d.driveFormat || 'NTFS')})
                </div>
                <div class="drive-progress-bar" style="margin-top:8px;">
                    <div class="drive-progress-fill ${barClass}" style="width:${Math.min(100, pctUsed)}%;"></div>
                </div>
                <div style="display:flex;justify-content:space-between;margin-top:4px;font-size:0.75rem;">
                    <span style="color:#aaa;">Used: <strong style="color:#fff;">${d.usedSpaceGb.toFixed(1)} GB</strong></span>
                    <span style="color:#aaa;">Free: <strong style="color:${d.isLowSpace ? 'var(--accent-color)' : 'var(--primary-color)'};">${d.freeSpaceGb.toFixed(1)} GB</strong> / ${d.totalSizeGb.toFixed(1)} GB</span>
                </div>
                ${d.isLowSpace ? `
                    <div style="color:var(--accent-color);font-size:0.7rem;margin-top:6px;font-weight:bold;letter-spacing:1px;">
                        ⚠ LOW SPACE ALERT: SPACE CRITICAL
                    </div>
                ` : ''}
            `;
            grid.appendChild(card);
        });
    }

    renderGhostDrivesTable(list) {
        const tbody = document.querySelector('#table-ghost-drives tbody');
        if (!tbody) return;
        tbody.innerHTML = '';

        if (!list || list.length === 0) {
            tbody.innerHTML = '<tr><td colspan="7" style="text-align:center;color:#7eb2cc;">No Ghost Drives mounted.</td></tr>';
            return;
        }

        list.forEach(g => {
            const tr = document.createElement('tr');
            tr.innerHTML = `
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;border-color:var(--accent-color);color:var(--accent-color);">${this.escape(g.tag)}</span></td>
                <td><strong>${this.escape(g.name)}</strong></td>
                <td><code style="font-size:0.75rem;">${this.escape(g.mountPath)}</code></td>
                <td>${g.usedSpaceMb.toFixed(1)} MB</td>
                <td>${g.maxCapacityGb} GB</td>
                <td><span class="telemetry-pill" style="font-size:0.7rem;padding:2px 8px;border-color:#4caf50;color:#4caf50;">${this.escape(g.status)}</span></td>
                <td>
                    <button class="hud-btn hud-btn-danger" style="padding:2px 8px;font-size:0.7rem;" onclick="window.jarvisApp.settingsModal.deleteGhostDrive('${g.id}')">UNMOUNT</button>
                </td>
            `;
            tbody.appendChild(tr);
        });
    }

    populateGhostExplorerSelect(list) {
        const select = document.getElementById('select-ghost-explorer');
        if (!select) return;
        select.innerHTML = '';
        list.forEach(g => {
            const opt = document.createElement('option');
            opt.value = g.tag;
            opt.textContent = `${g.tag} (${g.name})`;
            select.appendChild(opt);
        });
        this.refreshGhostFiles();
    }

    async createNewGhostDrive() {
        const name = document.getElementById('new-ghost-name')?.value.trim();
        const tag = document.getElementById('new-ghost-tag')?.value.trim();
        const maxGb = parseFloat(document.getElementById('new-ghost-maxgb')?.value || 10);
        const mountPath = document.getElementById('new-ghost-path')?.value.trim();

        if (!name) {
            alert("Please enter a Ghost Drive name.");
            return;
        }

        try {
            const res = await fetch('/api/storage/ghost-drives', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    tag,
                    maxCapacityGb: maxGb,
                    mountPath
                })
            });

            if (res.ok) {
                document.getElementById('new-ghost-name').value = '';
                document.getElementById('new-ghost-tag').value = '';
                document.getElementById('new-ghost-path').value = '';
                await this.loadStorageData();
                if (window.sfx) window.sfx.playAccessGrantedChime();
            }
        } catch (e) {
            console.error("Create ghost drive failed", e);
        }
    }

    async deleteGhostDrive(id) {
        if (!confirm("Unmount and delete this Ghost Drive?")) return;
        try {
            const res = await fetch(`/api/storage/ghost-drives/${id}`, { method: 'DELETE' });
            if (res.ok) {
                await this.loadStorageData();
            }
        } catch (e) {
            console.error(e);
        }
    }

    async refreshGhostFiles() {
        const select = document.getElementById('select-ghost-explorer');
        const listDiv = document.getElementById('ghost-files-list');
        if (!select || !listDiv) return;

        const tag = select.value;
        if (!tag) {
            listDiv.innerHTML = '<span style="color:#7eb2cc;font-size:0.8rem;">No Ghost Drive selected.</span>';
            return;
        }

        try {
            const res = await fetch(`/api/storage/ghost-drives/${encodeURIComponent(tag)}/files`);
            if (res.ok) {
                const files = await res.json();
                if (files.length === 0) {
                    listDiv.innerHTML = '<span style="color:#7eb2cc;font-size:0.8rem;">Ghost Drive volume is empty. Jarvis can write telemetry and research files here.</span>';
                    return;
                }

                listDiv.innerHTML = '';
                files.forEach(f => {
                    const item = document.createElement('div');
                    item.className = 'ghost-file-item';
                    const icon = f.isDirectory ? '📁' : '📄';
                    const sizeStr = f.isDirectory ? 'DIR' : `${(f.sizeBytes / 1024).toFixed(1)} KB`;
                    item.innerHTML = `
                        <span>${icon} <strong>${this.escape(f.name)}</strong></span>
                        <span style="color:#7eb2cc;font-size:0.75rem;">${sizeStr} | ${new Date(f.lastModified).toLocaleDateString()}</span>
                    `;
                    listDiv.appendChild(item);
                });
            }
        } catch (e) {
            listDiv.innerHTML = `<span style="color:var(--accent-color);font-size:0.8rem;">Error reading drive: ${e.message}</span>`;
        }
    }

    // ==================== STARK SECURE CONTACTS ====================
    async loadContactsData() {
        try {
            const res = await fetch('/api/contacts', {
                headers: this.app.authToken ? { 'Authorization': `Bearer ${this.app.authToken}` } : {}
            });
            if (res.ok) {
                this.contactsList = await res.json();
                this.renderContactsTable();
                if (this.app) {
                    this.app.contacts = this.contactsList;
                    this.app.renderContactsList(this.contactsList);
                }
            }
        } catch (e) {
            console.warn("Failed to load contacts:", e);
        }
    }

    getContactTags(c) {
        if (!c || !c.tags) return [];
        if (Array.isArray(c.tags)) return c.tags;
        return String(c.tags).split(',').map(t => t.trim()).filter(Boolean);
    }

    filterSettingsContacts(query) {
        this.contactsSearchQuery = (query || '').toLowerCase().trim();
        this.renderContactsTable();
    }

    renderContactsTable() {
        const tbody = document.getElementById('table-contacts-tbody');
        if (!tbody) return;

        tbody.innerHTML = '';
        const filter = (this.activeContactsFilter || 'ALL').toUpperCase();
        const q = (this.contactsSearchQuery || '').toLowerCase();

        const filtered = (this.contactsList || []).filter(c => {
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

        if (filtered.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" style="text-align:center;color:#6fa0bc;padding:20px;">No contacts found matching criteria.</td></tr>`;
            return;
        }

        filtered.forEach(c => {
            const tr = document.createElement('tr');
            
            const name = c.fullName || c.name || 'Unnamed Contact';
            const initials = name.split(' ').map(n => n[0]).slice(0, 2).join('').toUpperCase();
            const color = c.avatarColor || '#00d4ff';
            const clrBadge = c.clearanceLevel >= 10 
                ? `<span class="clearance-badge clearance-lvl-10">LVL ${c.clearanceLevel}</span>` 
                : (c.clearanceLevel >= 8 ? `<span class="clearance-badge clearance-lvl-high">LVL ${c.clearanceLevel}</span>` : `<span class="clearance-badge clearance-lvl-std">LVL ${c.clearanceLevel}</span>`);

            const tagBadges = this.getContactTags(c).map(t => `<span class="contact-tag-badge">${this.escape(t)}</span>`).join(' ');

            const phoneList = [];
            if (c.primaryPhone) phoneList.push(`<div>📞 <a href="tel:${encodeURIComponent(c.primaryPhone)}" class="comms-link" title="Click to call">${this.escape(c.primaryPhone)}</a> <small style="color:#6fa0bc;">(Pri)</small></div>`);
            if (c.secondaryPhone) phoneList.push(`<div>📡 <a href="tel:${encodeURIComponent(c.secondaryPhone)}" class="comms-link" title="Click to call">${this.escape(c.secondaryPhone)}</a> <small style="color:#6fa0bc;">(Sec)</small></div>`);
            const phonesHtml = phoneList.length > 0 ? phoneList.join('') : '<span style="color:#5a8a9f;font-size:0.75rem;">None</span>';

            const emailList = [];
            if (c.primaryEmail) emailList.push(`<div>✉️ <a href="mailto:${encodeURIComponent(c.primaryEmail)}" class="comms-link" title="Click to email">${this.escape(c.primaryEmail)}</a> <small style="color:#6fa0bc;">(Pri)</small></div>`);
            if (c.workEmail) emailList.push(`<div>💼 <a href="mailto:${encodeURIComponent(c.workEmail)}" class="comms-link" title="Click to email">${this.escape(c.workEmail)}</a> <small style="color:#6fa0bc;">(Work)</small></div>`);
            const emailsHtml = emailList.length > 0 ? emailList.join('') : '<span style="color:#5a8a9f;font-size:0.75rem;">None</span>';

            const avatarHtml = c.avatarUrl ? `
                <div class="contact-avatar-wrapper" style="width:36px;height:36px;">
                    <img src="${this.escape(c.avatarUrl)}" class="contact-avatar-img" onerror="this.style.display='none';">
                    <div class="contact-face-reticle"></div>
                </div>` : `
                <div class="contact-avatar" style="width:36px;height:36px;font-size:0.8rem;background:linear-gradient(135deg, ${color}33, ${color}99);border-color:${color};">
                    ${initials}
                </div>`;

            tr.innerHTML = `
                <td>
                    <div style="display:flex;align-items:center;gap:8px;">
                        ${avatarHtml}
                        ${clrBadge}
                    </div>
                </td>
                <td>
                    <strong style="color:#fff;">${this.escape(name)}</strong>
                    ${c.callSign ? `<div style="font-size:0.75rem;color:var(--secondary-color);font-style:italic;">"${this.escape(c.callSign)}"</div>` : ''}
                </td>
                <td>
                    <div>${this.escape(c.role || 'Operative')}</div>
                    <small style="color:#79d6ff;">${this.escape(c.organization || 'Stark Industries')}</small>
                </td>
                <td>${phonesHtml}</td>
                <td>${emailsHtml}</td>
                <td>${tagBadges}</td>
                <td>
                    <div style="display:flex;gap:4px;">
                        <button class="hud-btn btn-view-dossier" title="Biometric Dossier" style="padding:2px 6px;font-size:0.75rem;">👤</button>
                        <button class="hud-btn btn-schedule-contact" title="Schedule Appointment" style="padding:2px 6px;font-size:0.75rem;">📅</button>
                        <button class="hud-btn btn-edit-contact" title="Edit Contact" style="padding:2px 6px;font-size:0.75rem;">✏️</button>
                        <button class="hud-btn hud-btn-danger btn-delete-contact" title="Delete Contact" style="padding:2px 6px;font-size:0.75rem;">🗑️</button>
                    </div>
                </td>
            `;

            tr.querySelector('.btn-view-dossier')?.addEventListener('click', () => {
                if (this.app) {
                    this.app.openContactDossier(c);
                }
            });

            tr.querySelector('.btn-schedule-contact')?.addEventListener('click', () => {
                this.close();
                if (this.app) {
                    this.app.scheduleAppointmentForContact(c);
                }
            });

            tr.querySelector('.btn-edit-contact')?.addEventListener('click', () => {
                this.openContactEditModal(c);
            });

            tr.querySelector('.btn-delete-contact')?.addEventListener('click', () => {
                this.deleteContact(c.id, c.name);
            });

            tbody.appendChild(tr);
        });
    }

    openContactEditModal(contact) {
        const modal = document.getElementById('contact-edit-modal');
        if (!modal) return;

        const title = document.getElementById('contact-modal-title');
        const idInput = document.getElementById('edit-contact-id');
        const nameInput = document.getElementById('edit-contact-name');
        const callsignInput = document.getElementById('edit-contact-callsign');
        const avatarInput = document.getElementById('edit-contact-avatar');
        const avatarPreview = document.getElementById('edit-contact-avatar-preview');
        const roleInput = document.getElementById('edit-contact-role');
        const orgInput = document.getElementById('edit-contact-org');
        const phonePriInput = document.getElementById('edit-contact-phone-primary');
        const phoneSecInput = document.getElementById('edit-contact-phone-secondary');
        const emailPriInput = document.getElementById('edit-contact-email-primary');
        const emailWorkInput = document.getElementById('edit-contact-email-work');
        const clearanceInput = document.getElementById('edit-contact-clearance');
        const colorInput = document.getElementById('edit-contact-color');
        const tagsInput = document.getElementById('edit-contact-tags');
        const notesInput = document.getElementById('edit-contact-notes');
        const metaInput = document.getElementById('edit-contact-meta');

        const defaultAvatar = 'https://images.unsplash.com/photo-1507003211169-0a1dd7228f2d?w=150';

        if (contact) {
            const displayName = contact.fullName || contact.name || 'CONTACT';
            if (title) title.textContent = `EDIT CONTACT: ${displayName.toUpperCase()}`;
            if (idInput) idInput.value = contact.id || '';
            if (nameInput) nameInput.value = contact.fullName || contact.name || '';
            if (callsignInput) callsignInput.value = contact.callSign || '';
            if (avatarInput) avatarInput.value = contact.avatarUrl || '';
            if (avatarPreview) avatarPreview.src = contact.avatarUrl || defaultAvatar;
            if (roleInput) roleInput.value = contact.role || '';
            if (orgInput) orgInput.value = contact.organization || '';
            if (phonePriInput) phonePriInput.value = contact.primaryPhone || '';
            if (phoneSecInput) phoneSecInput.value = contact.secondaryPhone || '';
            if (emailPriInput) emailPriInput.value = contact.primaryEmail || '';
            if (emailWorkInput) emailWorkInput.value = contact.workEmail || '';
            if (clearanceInput) clearanceInput.value = contact.clearanceLevel || 5;
            if (colorInput) colorInput.value = contact.avatarColor || '#00d4ff';
            if (tagsInput) {
                if (Array.isArray(contact.tags)) {
                    tagsInput.value = contact.tags.join(', ');
                } else {
                    tagsInput.value = contact.tags || '';
                }
            }
            if (notesInput) notesInput.value = contact.notes || '';
            if (metaInput) {
                metaInput.value = typeof contact.metadataJson === 'object'
                    ? JSON.stringify(contact.metadataJson)
                    : (contact.metadataJson || '');
            }
        } else {
            if (title) title.textContent = 'REGISTER SECURE CONTACT';
            if (idInput) idInput.value = '';
            if (nameInput) nameInput.value = '';
            if (callsignInput) callsignInput.value = '';
            if (avatarInput) avatarInput.value = '';
            if (avatarPreview) avatarPreview.src = defaultAvatar;
            if (roleInput) roleInput.value = '';
            if (orgInput) orgInput.value = 'Stark Industries';
            if (phonePriInput) phonePriInput.value = '';
            if (phoneSecInput) phoneSecInput.value = '';
            if (emailPriInput) emailPriInput.value = '';
            if (emailWorkInput) emailWorkInput.value = '';
            if (clearanceInput) clearanceInput.value = '5';
            if (colorInput) colorInput.value = '#00d4ff';
            if (tagsInput) tagsInput.value = 'VIP, Stark Industries';
            if (notesInput) notesInput.value = '';
            if (metaInput) metaInput.value = '';
        }

        if (avatarInput && avatarPreview) {
            avatarInput.oninput = (e) => {
                avatarPreview.src = e.target.value.trim() || defaultAvatar;
            };
        }

        modal.style.zIndex = '3500';
        modal.classList.remove('hidden');
        if (window.sfx) window.sfx.playHudBeep(750, 0.05);
    }

    closeContactEditModal() {
        const modal = document.getElementById('contact-edit-modal');
        if (modal) modal.classList.add('hidden');
    }

    async saveContact() {
        const id = document.getElementById('edit-contact-id')?.value;
        const name = document.getElementById('edit-contact-name')?.value.trim();
        if (!name) {
            alert("Contact name is required.");
            return;
        }

        const tagsRaw = document.getElementById('edit-contact-tags')?.value || '';
        const tagsArr = tagsRaw.split(',').map(t => t.trim()).filter(Boolean);
        const tagsStr = tagsArr.join(', ');

        const payload = {
            id: id || undefined,
            fullName: name,
            name: name,
            callSign: document.getElementById('edit-contact-callsign')?.value.trim() || '',
            avatarUrl: document.getElementById('edit-contact-avatar')?.value.trim() || '',
            role: document.getElementById('edit-contact-role')?.value.trim() || '',
            organization: document.getElementById('edit-contact-org')?.value.trim() || '',
            primaryPhone: document.getElementById('edit-contact-phone-primary')?.value.trim() || '',
            secondaryPhone: document.getElementById('edit-contact-phone-secondary')?.value.trim() || '',
            primaryEmail: document.getElementById('edit-contact-email-primary')?.value.trim() || '',
            workEmail: document.getElementById('edit-contact-email-work')?.value.trim() || '',
            clearanceLevel: parseInt(document.getElementById('edit-contact-clearance')?.value) || 5,
            avatarColor: document.getElementById('edit-contact-color')?.value || '#00d4ff',
            tags: tagsStr,
            notes: document.getElementById('edit-contact-notes')?.value.trim() || '',
            metadataJson: document.getElementById('edit-contact-meta')?.value.trim() || '{}'
        };

        try {
            const url = id ? `/api/contacts/${id}` : '/api/contacts';
            const method = id ? 'PUT' : 'POST';

            const token = this.app?.authToken || localStorage.getItem('jarvis_auth_token');
            const res = await fetch(url, {
                method: method,
                headers: {
                    'Content-Type': 'application/json',
                    ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const updatedItem = await res.json().catch(() => null);
                this.closeContactEditModal();
                await this.loadContactsData();
                if (this.app) {
                    if (this.app.contacts && updatedItem) {
                        const targetId = updatedItem.id || id;
                        const idx = this.app.contacts.findIndex(c => c.id === targetId);
                        if (idx >= 0) this.app.contacts[idx] = updatedItem;
                        else this.app.contacts.push(updatedItem);
                        this.app.renderContactsList(this.app.contacts);
                    }
                }
                if (window.sfx) window.sfx.playAccessGrantedChime();
            } else {
                const err = await res.text();
                alert(`Error saving contact: ${err}`);
            }
        } catch (e) {
            alert(`Network error saving contact: ${e.message}`);
        }
    }

    async deleteContact(id, name) {
        if (!confirm(`Purge contact records for "${name}" from Stark secure directory?`)) return;

        try {
            const token = this.app?.authToken || localStorage.getItem('jarvis_auth_token');
            const res = await fetch(`/api/contacts/${id}`, {
                method: 'DELETE',
                headers: token ? { 'Authorization': `Bearer ${token}` } : {}
            });

            if (res.ok) {
                await this.loadContactsData();
                if (this.app && this.app.contacts) {
                    this.app.contacts = this.app.contacts.filter(c => c.id !== id);
                    this.app.renderContactsList(this.app.contacts);
                }
                if (window.sfx) window.sfx.playHudBeep(400, 0.05);
            } else {
                alert("Failed to delete contact.");
            }
        } catch (e) {
            alert(`Error deleting contact: ${e.message}`);
        }
    }

    // ==================== USER PROFILE ("ABOUT ME") ====================
    async loadUserProfile() {
        try {
            const res = await fetch('/api/config/user-profile');
            if (res.ok) {
                const profile = await res.json();
                this.populateUserProfile(profile);
            }
        } catch (e) {
            console.error("Failed to load user profile", e);
        }
    }

    populateUserProfile(p) {
        if (!p) return;
        const setVal = (id, val) => { const el = document.getElementById(id); if (el && val !== undefined) el.value = val; };
        setVal('profile-fullname', p.fullName || 'Tony Stark');
        setVal('profile-preferredname', p.preferredName || 'Tony');
        setVal('profile-honorific', p.honorific || 'Sir');
        setVal('profile-dob', p.dateOfBirth || 'May 29, 1970');
        setVal('profile-address', p.address || '10880 Malibu Point, Malibu, CA 90265');
        setVal('profile-favoritecolor', p.favoriteColor || '#00d4ff');
        setVal('profile-color-picker', p.favoriteColor || '#00d4ff');
        setVal('profile-role', p.roleTitle || 'Chief Executive & Lead Inventor');
        setVal('profile-org', p.organization || 'Stark Industries / Avengers Initiative');
        setVal('profile-clearance', p.securityClearance || 'Level 10 (Supreme Command)');
        setVal('profile-emergency', p.emergencyContact || 'Pepper Potts (+1-555-0102)');
        setVal('profile-preferences', p.preferences || 'Double espresso with raw sugar; chlorophyll drink; vintage AC/DC rock music; prompt, crisp, witty responses.');
        setVal('profile-instructions', p.customAiInstructions || "Always address me with respect as 'Sir'. Keep answers razor sharp, witty, and loyal. Proactively track suit power, projects, and defenses.");
    }

    async saveUserProfile() {
        const getVal = (id) => document.getElementById(id)?.value?.trim() || '';
        const profile = {
            fullName: getVal('profile-fullname') || 'Tony Stark',
            preferredName: getVal('profile-preferredname') || 'Tony',
            honorific: getVal('profile-honorific') || 'Sir',
            dateOfBirth: getVal('profile-dob') || 'May 29, 1970',
            address: getVal('profile-address') || '10880 Malibu Point, Malibu, CA 90265',
            favoriteColor: getVal('profile-favoritecolor') || '#00d4ff',
            roleTitle: getVal('profile-role') || 'Chief Executive & Lead Inventor',
            organization: getVal('profile-org') || 'Stark Industries / Avengers Initiative',
            securityClearance: getVal('profile-clearance') || 'Level 10 (Supreme Command)',
            emergencyContact: getVal('profile-emergency') || 'Pepper Potts (+1-555-0102)',
            preferences: getVal('profile-preferences') || '',
            customAiInstructions: getVal('profile-instructions') || ''
        };

        try {
            const res = await fetch('/api/config/user-profile', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(profile)
            });
            if (res.ok) {
                if (window.sfx) window.sfx.playAccessGrantedChime();
                if (this.app?.voiceStudio) {
                    this.app.voiceStudio.speak(`Dossier recalibrated and saved, ${profile.honorific}. All neural responses are personalized.`);
                }
                const btn = document.getElementById('btn-save-user-profile');
                if (btn) {
                    const original = btn.textContent;
                    btn.textContent = '✅ SAVED!';
                    setTimeout(() => btn.textContent = original, 2000);
                }
            }
        } catch (e) {
            console.error("Failed to save user profile", e);
        }
    }

    // ==================== ADVANCED PROJECT MANAGEMENT ====================
    async loadProjectsData() {
        try {
            const res = await fetch('/api/projects');
            if (res.ok) {
                this.projectsList = await res.json();
                this.renderSettingsProjects(this.projectsList);
                this.renderDrawerProjects(this.projectsList);
                this.populateTargetProjectSelect(this.projectsList);
            }
        } catch (e) {
            console.error("Failed to load projects", e);
        }
    }

    renderSettingsProjects(projects) {
        const container = document.getElementById('settings-projects-list');
        if (!container) return;
        container.innerHTML = '';

        const statCount = document.getElementById('stat-proj-count');
        const statTasks = document.getElementById('stat-proj-tasks');
        if (statCount) statCount.textContent = `${projects.length} INITIATIVES`;
        
        let totalTasks = 0;
        projects.forEach(p => totalTasks += (p.tasks ? p.tasks.length : 0));
        if (statTasks) statTasks.textContent = `${totalTasks} TASKS`;

        projects.forEach(p => {
            const card = document.createElement('div');
            card.className = 'project-card';
            const completedCount = (p.tasks || []).filter(t => t.status === 'Completed').length;
            const taskTotal = (p.tasks || []).length;

            card.innerHTML = `
                <div class="project-card-header">
                    <div>
                        <div class="project-title">${this.escape(p.name)}</div>
                        <div style="font-size:0.75rem;color:#7eb2cc;margin-top:2px;">${this.escape(p.description)}</div>
                    </div>
                    <span class="project-code-tag">${this.escape(p.category)}</span>
                </div>
                <div class="project-progress-bar-bg">
                    <div class="project-progress-bar-fill" style="width:${p.progressPercent || 0}%;"></div>
                </div>
                <div style="display:flex;justify-content:space-between;font-size:0.75rem;font-family:var(--font-mono);color:#ffb74d;">
                    <span>PROGRESS: ${Math.round(p.progressPercent || 0)}%</span>
                    <span>TASKS: ${completedCount}/${taskTotal}</span>
                </div>
                <div style="display:flex;gap:6px;margin-top:12px;flex-wrap:wrap;">
                    <button class="hud-btn btn-open-proj-drawer" style="font-size:0.72rem;padding:3px 8px;border-color:#ff9800;color:#ff9800;">📂 BLUEPRINT MATRIX</button>
                    <button class="hud-btn btn-run-proj-research" style="font-size:0.72rem;padding:3px 8px;">🔬 RESEARCH AGENT</button>
                </div>
            `;

            card.querySelector('.btn-open-proj-drawer')?.addEventListener('click', (e) => {
                e.stopPropagation();
                this.close();
                this.openProjectsDrawer(p.id);
            });

            card.querySelector('.btn-run-proj-research')?.addEventListener('click', (e) => {
                e.stopPropagation();
                this.openResearchModal(p.id);
            });

            container.appendChild(card);
        });
    }

    renderDrawerProjects(projects) {
        const container = document.getElementById('projects-drawer-content');
        if (!container) return;
        container.innerHTML = '';

        projects.forEach(p => {
            const card = document.createElement('div');
            card.className = 'project-card';
            card.id = `drawer-project-${p.id}`;
            const completedCount = (p.tasks || []).filter(t => t.status === 'Completed').length;
            const taskTotal = (p.tasks || []).length;

            let tasksHtml = '';
            (p.tasks || []).forEach(t => {
                const isDone = t.status === 'Completed';
                tasksHtml += `
                    <div class="task-item-card ${isDone ? 'completed' : ''}">
                        <div style="display:flex;justify-content:space-between;align-items:flex-start;">
                            <strong style="font-size:0.85rem;color:${isDone ? '#6ee7b7' : '#fff'};">${isDone ? '✓ ' : '○ '}${this.escape(t.title)}</strong>
                            <span style="font-size:0.65rem;font-family:var(--font-mono);padding:1px 5px;border-radius:3px;background:rgba(255,152,0,0.15);color:#ffb74d;">${t.status}</span>
                        </div>
                        <div style="font-size:0.75rem;color:#7eb2cc;">${this.escape(t.description)}</div>
                        <div style="display:flex;justify-content:space-between;align-items:center;margin-top:4px;">
                            <span style="font-size:0.7rem;color:#00ffcc;">🤖 ${this.escape(t.assignedAgentName || 'J.A.R.V.I.S.')}</span>
                            ${!isDone ? `<button class="btn-auto-exec" data-proj="${p.id}" data-task="${t.id}">⚡ AUTO-EXECUTE</button>` : `<span style="font-size:0.7rem;color:#39ff14;font-family:var(--font-mono);">EXECUTED</span>`}
                        </div>
                        ${t.automationResult ? `
                            <div style="background:#010a14;padding:8px;border-radius:4px;margin-top:6px;font-family:var(--font-mono);font-size:0.7rem;color:#8cc3df;max-height:90px;overflow-y:auto;white-space:pre-wrap;">${this.escape(t.automationResult)}</div>
                        ` : ''}
                    </div>
                `;
            });

            card.innerHTML = `
                <div class="project-card-header">
                    <div>
                        <div class="project-title" style="color:#ffb74d;">${this.escape(p.name)}</div>
                        <div style="font-size:0.75rem;color:#7eb2cc;margin-top:2px;">${this.escape(p.description)}</div>
                        <div style="font-size:0.7rem;font-family:var(--font-mono);color:#00d4ff;margin-top:4px;">📁 ${this.escape(p.ghostDrivePath)}</div>
                    </div>
                    <span class="project-code-tag">${this.escape(p.priority)}</span>
                </div>
                <div class="project-progress-bar-bg">
                    <div class="project-progress-bar-fill" style="width:${p.progressPercent || 0}%;"></div>
                </div>
                <div style="display:flex;justify-content:space-between;font-size:0.75rem;font-family:var(--font-mono);color:#ffb74d;">
                    <span>PROGRESS: ${Math.round(p.progressPercent || 0)}%</span>
                    <span>COMPLETED: ${completedCount}/${taskTotal}</span>
                </div>

                <div style="margin-top:14px;">
                    <div style="font-family:var(--font-hud);font-size:0.8rem;color:#fff;letter-spacing:1px;margin-bottom:6px;">AUTONOMOUS TASKS & DELIVERABLES:</div>
                    ${tasksHtml}
                </div>

                <div style="display:flex;gap:8px;margin-top:14px;">
                    <button class="hud-btn btn-trigger-research" data-proj="${p.id}" style="flex:1;border-color:#ff9800;color:#ff9800;font-size:0.75rem;">🔬 RESEARCH AGENT</button>
                    <button class="hud-btn btn-view-files" data-proj="${p.id}" style="flex:1;font-size:0.75rem;">📁 GHOST FILES (${p.artifacts ? p.artifacts.length : 0})</button>
                </div>
            `;

            card.querySelectorAll('.btn-auto-exec').forEach(btn => {
                btn.addEventListener('click', async (e) => {
                    const projId = btn.getAttribute('data-proj');
                    const taskId = btn.getAttribute('data-task');
                    btn.textContent = '⏳ EXECUTING...';
                    btn.disabled = true;
                    await this.executeProjectTask(projId, taskId);
                });
            });

            card.querySelector('.btn-trigger-research')?.addEventListener('click', () => {
                this.openResearchModal(p.id);
            });

            card.querySelector('.btn-view-files')?.addEventListener('click', async () => {
                const files = await this.fetchProjectFiles(p.id);
                if (files && files.length > 0) {
                    alert(`Ghost Drive files for ${p.name}:\n` + files.map(f => `• ${f.fileName} (${(f.sizeBytes/1024).toFixed(1)} KB)`).join('\n'));
                } else {
                    alert(`No artifacts generated in ${p.ghostDrivePath} yet.`);
                }
            });

            container.appendChild(card);
        });
    }

    populateTargetProjectSelect(projects) {
        const sel = document.getElementById('research-target-project');
        if (!sel) return;
        sel.innerHTML = '';
        projects.forEach(p => {
            const opt = document.createElement('option');
            opt.value = p.id;
            opt.textContent = `${p.name} (${p.category})`;
            sel.appendChild(opt);
        });
    }

    openProjectsDrawer(projectId) {
        if (this.app?.projectsStudio) {
            this.app.projectsStudio.open(projectId);
        } else {
            const modal = document.getElementById('projects-fullscreen-modal');
            if (modal) modal.classList.remove('hidden');
        }
        if (window.sfx) window.sfx.playHudBeep(700, 0.04);
    }

    closeProjectsDrawer() {
        if (this.app?.projectsStudio) {
            this.app.projectsStudio.close();
        } else {
            const modal = document.getElementById('projects-fullscreen-modal');
            if (modal) modal.classList.add('hidden');
        }
        if (window.sfx) window.sfx.playHudBeep(450, 0.04);
    }

    async executeProjectTask(projectId, taskId) {
        try {
            if (window.sfx) window.sfx.playArcPulse();
            const res = await fetch(`/api/projects/${projectId}/tasks/${taskId}/execute`, {
                method: 'POST',
                headers: this.app.authToken ? { 'Authorization': `Bearer ${this.app.authToken}` } : {}
            });
            if (res.ok) {
                const task = await res.json();
                await this.loadProjectsData();
                if (window.sfx) window.sfx.playAccessGrantedChime();
                if (this.app?.voiceStudio) {
                    this.app.voiceStudio.speak(`Autonomous task ${task.title} finalized, Sir. Deliverables persisted to Ghost Drive.`);
                }
            } else {
                alert("Autonomous task execution failed.");
            }
        } catch (e) {
            alert(`Execution error: ${e.message}`);
        }
    }

    openResearchModal(projectId) {
        const modal = document.getElementById('project-research-modal');
        if (!modal) return;
        if (projectId) {
            const sel = document.getElementById('research-target-project');
            if (sel) sel.value = projectId;
        }
        document.getElementById('research-progress-box').style.display = 'none';
        modal.classList.remove('hidden');
        if (window.sfx) window.sfx.playHudBeep(750, 0.05);
    }

    closeResearchModal() {
        const modal = document.getElementById('project-research-modal');
        if (modal) modal.classList.add('hidden');
    }

    async executeResearchAgent() {
        const projId = document.getElementById('research-target-project').value;
        const topic = document.getElementById('research-topic-input').value.trim();
        const diff = document.getElementById('research-difficulty-input').value.trim();

        if (!topic) {
            alert("Please specify a research topic or problem statement.");
            return;
        }

        const progressBox = document.getElementById('research-progress-box');
        const outputPreview = document.getElementById('research-output-preview');
        const executeBtn = document.getElementById('btn-execute-research-agent');

        progressBox.style.display = 'block';
        outputPreview.textContent = 'Engaging Deep Research Agent...\nScanning quantum archives and intelligence feeds...';
        executeBtn.disabled = true;
        if (window.sfx) window.sfx.playArcPulse();

        try {
            const res = await fetch('/api/projects/research', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...(this.app.authToken ? { 'Authorization': `Bearer ${this.app.authToken}` } : {})
                },
                body: JSON.stringify({
                    projectId: projId,
                    researchTopic: topic,
                    specificDifficulty: diff
                })
            });

            if (res.ok) {
                const data = await res.json();
                outputPreview.textContent = data.report || 'Research report finalized.';
                executeBtn.disabled = false;
                await this.loadProjectsData();
                if (window.sfx) window.sfx.playAccessGrantedChime();
                if (this.app?.voiceStudio) {
                    this.app.voiceStudio.speak(`Research synthesis complete, Sir. Stored under Projects directory.`);
                }
            } else {
                const err = await res.text();
                outputPreview.textContent = `Research agent error: ${err}`;
                executeBtn.disabled = false;
            }
        } catch (e) {
            outputPreview.textContent = `Network error: ${e.message}`;
            executeBtn.disabled = false;
        }
    }

    async fetchProjectFiles(projectId) {
        try {
            const res = await fetch(`/api/projects/${projectId}/files`);
            if (res.ok) return await res.json();
        } catch (e) {
            console.error("Failed to fetch project files", e);
        }
        return [];
    }

    escape(str) {
        if (!str) return '';
        return String(str).replace(/[&<>"']/g, s => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;'
        })[s]);
    }
}

window.JarvisSettingsModal = JarvisSettingsModal;

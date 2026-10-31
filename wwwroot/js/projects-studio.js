/**
 * J.A.R.V.I.S. Full-Screen Projects Command Studio
 * Immersive full-screen environment for Stark Advanced Project Management,
 * 3D Holographic Blueprints, Engineering Logbook, Task Automation, and Ghost Drive repos.
 */

class JarvisProjectsStudio {
    constructor(appContext) {
        this.app = appContext;
        this.modalEl = document.getElementById('projects-fullscreen-modal');
        this.projectsList = [];
        this.activeProject = null;
        this.activeFilter = 'ALL';
        this.searchQuery = '';
        this.activeWbTab = 'tasks';

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

    init() {
        this.bindGlobalEvents();
        this.bindWorkbenchEvents();
    }

    bindGlobalEvents() {
        // Close studio button
        document.getElementById('btn-close-projects-studio')?.addEventListener('click', () => this.close());

        // Keyboard ESC to close
        window.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this.modalEl && !this.modalEl.classList.contains('hidden')) {
                // If sub-modals open, close them first
                const newProjModal = document.getElementById('create-project-modal');
                if (newProjModal && !newProjModal.classList.contains('hidden')) {
                    newProjModal.classList.add('hidden');
                    return;
                }
                this.close();
            }
        });

        // Search & Category Filters in Navigator
        document.getElementById('studio-proj-search')?.addEventListener('input', (e) => {
            this.searchQuery = (e.target.value || '').toLowerCase().trim();
            this.renderProjectList();
        });

        document.querySelectorAll('.studio-filter-pill').forEach(pill => {
            pill.addEventListener('click', () => {
                document.querySelectorAll('.studio-filter-pill').forEach(p => p.classList.remove('active'));
                pill.classList.add('active');
                this.activeFilter = pill.getAttribute('data-cat') || 'ALL';
                this.renderProjectList();
                if (window.sfx) window.sfx.playHudBeep(700, 0.04);
            });
        });

        // Global New Project Button
        document.getElementById('btn-studio-new-project')?.addEventListener('click', () => {
            this.openNewProjectModal();
        });

        // Global Research Agent Button
        document.getElementById('btn-studio-run-research')?.addEventListener('click', () => {
            if (this.app?.settingsModal) {
                this.app.settingsModal.openResearchModal(this.activeProject?.id);
            }
        });

        // New Project Modal wiring
        document.getElementById('btn-close-create-proj-modal')?.addEventListener('click', () => {
            document.getElementById('create-project-modal')?.classList.add('hidden');
        });
        document.getElementById('btn-cancel-create-proj')?.addEventListener('click', () => {
            document.getElementById('create-project-modal')?.classList.add('hidden');
        });
        document.getElementById('btn-submit-create-proj')?.addEventListener('click', () => {
            this.submitNewProject();
        });
    }

    bindWorkbenchEvents() {
        // Workbench Sub-Tabs Navigation
        document.querySelectorAll('.wb-tab-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const tabKey = btn.getAttribute('data-wb-tab');
                this.switchWorkbenchTab(tabKey);
            });
        });

        // Save Project Changes Button
        document.getElementById('btn-wb-save-project')?.addEventListener('click', () => {
            this.saveCurrentProjectChanges();
        });

        // Delete Project Button
        document.getElementById('btn-wb-delete-project')?.addEventListener('click', () => {
            if (this.activeProject) this.deleteProject(this.activeProject.id);
        });

        // Progress Slider sync
        const progressSlider = document.getElementById('wb-progress-slider');
        progressSlider?.addEventListener('input', (e) => {
            const val = e.target.value;
            const label = document.getElementById('wb-progress-label');
            if (label) label.textContent = `${val}%`;
            if (this.activeProject) this.activeProject.progressPercent = parseFloat(val);
        });

        // Add Task inline form
        document.getElementById('btn-wb-add-task')?.addEventListener('click', () => {
            this.addProjectTask();
        });

        // Notes Toolbar Actions
        document.getElementById('btn-notes-insert-timestamp')?.addEventListener('click', () => {
            this.insertNotesTimestamp();
        });
        document.getElementById('btn-notes-insert-template')?.addEventListener('click', () => {
            this.insertNotesTemplate();
        });
        document.getElementById('btn-wb-save-notes')?.addEventListener('click', () => {
            this.saveProjectNotes();
        });

        // Quick Log Post
        document.getElementById('btn-post-quick-log')?.addEventListener('click', () => {
            this.postQuickLog();
        });
        document.getElementById('input-quick-log')?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                this.postQuickLog();
            }
        });

        // 3D HUD Sync Button
        document.getElementById('btn-wb-sync-3d')?.addEventListener('click', () => {
            this.syncWith3DHud();
        });

        // Research Form in Research tab
        document.getElementById('btn-wb-deploy-research')?.addEventListener('click', () => {
            this.deployInlineResearch();
        });
    }

    async open(targetProjectId = null) {
        if (!this.modalEl) return;
        this.modalEl.classList.remove('hidden');
        try { window.sfx?.playPowerUp?.(); } catch {}

        await this.loadProjects();

        if (targetProjectId) {
            this.selectProject(targetProjectId);
        } else if (this.projectsList.length > 0 && !this.activeProject) {
            this.selectProject(this.projectsList[0].id);
        } else if (this.activeProject) {
            this.selectProject(this.activeProject.id);
        }
    }

    close() {
        if (!this.modalEl) return;
        this.modalEl.classList.add('hidden');
        try { window.sfx?.playHudBeep?.(440); } catch {}
    }

    async loadProjects() {
        try {
            const res = await fetch('/api/projects');
            if (res.ok) {
                this.projectsList = await res.json();
                
                // If empty, auto-seed rich Stark test projects and workflows
                if (Array.isArray(this.projectsList) && this.projectsList.length === 0) {
                    try {
                        const seedRes = await fetch('/api/projects/seed-test-data', { method: 'POST' });
                        if (seedRes.ok) {
                            const seedData = await seedRes.json();
                            this.projectsList = seedData.projects || [];
                        }
                    } catch {}
                }

                this.updateGlobalHeaderStats();
                this.renderProjectList();
                if (this.app) this.app.projects = this.projectsList;
            }
        } catch (e) {
            console.error("Failed to load projects in studio:", e);
        }
    }

    updateGlobalHeaderStats() {
        const totalPill = document.getElementById('proj-stat-total');
        const tasksPill = document.getElementById('proj-stat-tasks');
        
        if (totalPill) {
            totalPill.textContent = `${this.projectsList.length} ACTIVE`;
        }

        let totalTasks = 0;
        let completedTasks = 0;
        this.projectsList.forEach(p => {
            const tasks = p.tasks || [];
            totalTasks += tasks.length;
            completedTasks += tasks.filter(t => t.status === 'Completed').length;
        });

        if (tasksPill) {
            tasksPill.textContent = `${completedTasks}/${totalTasks} TASKS AUTOMATED`;
        }
    }

    renderProjectList() {
        const container = document.getElementById('studio-projects-list');
        if (!container) return;

        container.innerHTML = '';
        const filter = (this.activeFilter || 'ALL').toUpperCase();
        const q = (this.searchQuery || '').toLowerCase();

        const filtered = this.projectsList.filter(p => {
            if (filter !== 'ALL' && (p.category || '').toUpperCase() !== filter) {
                return false;
            }
            if (q) {
                const matchStr = `${p.name} ${p.slug || ''} ${p.description || ''} ${p.category || ''} ${p.tags || ''}`.toLowerCase();
                if (!matchStr.includes(q)) return false;
            }
            return true;
        });

        if (filtered.length === 0) {
            container.innerHTML = `
                <div style="text-align:center;padding:30px 14px;color:#6fa0bc;">
                    <div style="font-size:2rem;margin-bottom:8px;">🔍</div>
                    <div style="font-family:var(--font-hud);font-size:0.85rem;color:#fff;">NO INITIATIVES FOUND</div>
                    <p style="font-size:0.75rem;margin-top:4px;">Adjust search criteria or create a new initiative.</p>
                </div>
            `;
            return;
        }

        filtered.forEach(p => {
            const card = document.createElement('div');
            const isSelected = this.activeProject && this.activeProject.id === p.id;
            card.className = `studio-project-card ${isSelected ? 'selected' : ''}`;
            card.setAttribute('data-id', p.id);

            const tasks = p.tasks || [];
            const completedCount = tasks.filter(t => t.status === 'Completed').length;
            const progress = Math.round(p.progressPercent || 0);

            let priorityColor = '#ff9800';
            if (p.priority === 'Critical') priorityColor = '#ff2233';
            else if (p.priority === 'High') priorityColor = '#ff9800';
            else if (p.priority === 'Medium') priorityColor = '#00d4ff';
            else priorityColor = '#39ff14';

            card.innerHTML = `
                <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:8px;">
                    <div>
                        <div class="studio-card-title">${this.escape(p.name)}</div>
                        <div class="studio-card-desc">${this.escape(p.description || 'Active Stark Blueprint Initiative')}</div>
                    </div>
                    <span class="project-code-tag" style="border-color:${priorityColor};color:${priorityColor};font-size:0.65rem;">
                        ${this.escape(p.priority || 'HIGH')}
                    </span>
                </div>

                <div class="project-progress-bar-bg" style="margin:8px 0 6px 0;">
                    <div class="project-progress-bar-fill" style="width:${progress}%;"></div>
                </div>

                <div style="display:flex;justify-content:space-between;align-items:center;font-size:0.72rem;font-family:var(--font-mono);">
                    <span style="color:#ffb74d;">${progress}% COMPLETE</span>
                    <span style="color:#79d6ff;">TASKS: ${completedCount}/${tasks.length}</span>
                </div>

                <div style="display:flex;justify-content:space-between;align-items:center;margin-top:6px;font-size:0.7rem;color:#6fa0bc;">
                    <span>📁 ${this.escape(p.ghostDrivePath || 'Projects/' + (p.slug || p.id))}</span>
                    <span style="color:var(--primary-color);">${this.escape(p.category)}</span>
                </div>
            `;

            card.addEventListener('click', () => {
                this.selectProject(p.id);
            });

            container.appendChild(card);
        });
    }

    selectProject(projectId) {
        const found = this.projectsList.find(p => p.id === projectId);
        if (!found) return;

        this.activeProject = found;

        // Update active selection style in cards list
        document.querySelectorAll('.studio-project-card').forEach(c => {
            if (c.getAttribute('data-id') === projectId) c.classList.add('selected');
            else c.classList.remove('selected');
        });

        // Hide empty state, show active content
        document.getElementById('workbench-empty-state').style.display = 'none';
        document.getElementById('workbench-active-content').style.display = 'flex';

        this.renderActiveProjectWorkbench();
        if (window.sfx) window.sfx.playHudBeep(780, 0.04);
    }

    renderActiveProjectWorkbench() {
        const p = this.activeProject;
        if (!p) return;

        // 1. Hero inputs
        const nameInput = document.getElementById('wb-proj-name');
        if (nameInput) nameInput.value = p.name || '';

        const descInput = document.getElementById('wb-proj-desc');
        if (descInput) descInput.value = p.description || '';

        const categoryBadge = document.getElementById('wb-badge-category');
        if (categoryBadge) categoryBadge.textContent = p.category || 'Engineering';

        const ghostBadge = document.getElementById('wb-badge-ghostpath');
        if (ghostBadge) ghostBadge.textContent = `📁 ${p.ghostDrivePath || 'Projects/' + (p.slug || p.id)}`;

        const leadAgentLabel = document.getElementById('wb-lead-agent-name');
        if (leadAgentLabel) leadAgentLabel.textContent = p.leadAgentName || 'J.A.R.V.I.S. Core';

        const statusSelect = document.getElementById('wb-proj-status');
        if (statusSelect) statusSelect.value = p.status || 'Active';

        const prioritySelect = document.getElementById('wb-proj-priority');
        if (prioritySelect) prioritySelect.value = p.priority || 'High';

        const progressSlider = document.getElementById('wb-progress-slider');
        const progressLabel = document.getElementById('wb-progress-label');
        if (progressSlider) progressSlider.value = p.progressPercent || 0;
        if (progressLabel) progressLabel.textContent = `${Math.round(p.progressPercent || 0)}%`;

        // 2. Render sub-panes
        this.renderTasksPane();
        this.renderNotesPane();
        this.renderResearchPane();
        this.renderGhostFilesPane();
        this.renderBlueprint3DPane();
    }

    switchWorkbenchTab(tabKey) {
        this.activeWbTab = tabKey;

        // Tab buttons active state
        document.querySelectorAll('.wb-tab-btn').forEach(btn => {
            if (btn.getAttribute('data-wb-tab') === tabKey) btn.classList.add('active');
            else btn.classList.remove('active');
        });

        // Tab panes active state
        document.querySelectorAll('.wb-tab-pane').forEach(pane => {
            if (pane.id === `wb-pane-${tabKey}`) pane.classList.add('active');
            else pane.classList.remove('active');
        });

        if (window.sfx) window.sfx.playHudBeep(720, 0.03);

        // Tab specific actions
        if (tabKey === 'ghost-files') {
            this.loadProjectGhostFiles();
        }
    }

    // -------------------------------------------------------------
    // TAB 1: Tasks & Autonomous AI Execution
    // -------------------------------------------------------------
    renderTasksPane() {
        const container = document.getElementById('wb-tasks-list');
        if (!container) return;

        const tasks = this.activeProject?.tasks || [];
        container.innerHTML = '';

        if (tasks.length === 0) {
            container.innerHTML = `
                <div style="text-align:center;padding:24px;color:#6fa0bc;">
                    <div style="font-size:1.8rem;margin-bottom:6px;">📋</div>
                    <div style="font-family:var(--font-hud);font-size:0.85rem;color:#fff;">NO DELIVERABLES DEFINED</div>
                    <p style="font-size:0.75rem;margin-top:4px;">Add a task above to schedule autonomous AI execution.</p>
                </div>
            `;
            return;
        }

        tasks.forEach(t => {
            const isDone = t.status === 'Completed';
            const item = document.createElement('div');
            item.className = `task-item-card ${isDone ? 'completed' : ''}`;
            item.setAttribute('data-task-id', t.id);

            item.innerHTML = `
                <div style="display:flex;justify-content:space-between;align-items:flex-start;gap:10px;">
                    <div style="display:flex;align-items:center;gap:10px;flex:1;">
                        <input type="checkbox" class="task-checkbox" ${isDone ? 'checked' : ''} style="cursor:pointer;width:16px;height:16px;">
                        <div>
                            <div class="task-title" style="font-weight:700;color:${isDone ? '#6ee7b7' : '#fff'};font-size:0.88rem;">
                                ${isDone ? '✓ ' : ''}${this.escape(t.title)}
                            </div>
                            ${t.description ? `<div style="font-size:0.75rem;color:#7eb2cc;margin-top:2px;">${this.escape(t.description)}</div>` : ''}
                        </div>
                    </div>
                    <div style="display:flex;align-items:center;gap:6px;">
                        <span class="project-code-tag" style="font-size:0.65rem;">${this.escape(t.priority || 'Medium')}</span>
                        <button class="hud-btn hud-btn-danger btn-del-task" style="padding:2px 6px;font-size:0.7rem;" title="Delete Task">🗑️</button>
                    </div>
                </div>

                <div style="display:flex;justify-content:space-between;align-items:center;margin-top:8px;padding-top:6px;border-top:1px solid rgba(255,255,255,0.06);">
                    <span style="font-size:0.72rem;color:#00ffcc;">🤖 Assigned: ${this.escape(t.assignedAgentName || 'J.A.R.V.I.S. Core')}</span>
                    <div>
                        ${!isDone 
                            ? `<button class="btn-auto-exec btn-trigger-task-exec" data-task-id="${t.id}">⚡ AUTO-EXECUTE WITH AGENT</button>`
                            : `<span style="font-size:0.72rem;color:#39ff14;font-family:var(--font-mono);font-weight:700;">✓ AUTONOMOUSLY EXECUTED</span>`
                        }
                    </div>
                </div>

                ${t.automationResult ? `
                    <div class="task-automation-result-box">
                        <div style="font-family:var(--font-hud);font-size:0.68rem;color:#39ff14;letter-spacing:1px;margin-bottom:4px;">
                            🤖 AGENT EXECUTION SYNTHESIS:
                        </div>
                        <div style="font-family:var(--font-mono);font-size:0.75rem;color:#c2e7ff;white-space:pre-wrap;line-height:1.4;">${this.escape(t.automationResult)}</div>
                    </div>
                ` : ''}
            `;

            // Checkbox toggle
            item.querySelector('.task-checkbox')?.addEventListener('change', (e) => {
                this.toggleTaskStatus(t.id, e.target.checked);
            });

            // Auto-execute button
            item.querySelector('.btn-trigger-task-exec')?.addEventListener('click', () => {
                this.executeTaskAutonomous(t.id);
            });

            // Delete task button
            item.querySelector('.btn-del-task')?.addEventListener('click', () => {
                this.deleteTask(t.id);
            });

            container.appendChild(item);
        });
    }

    async addProjectTask() {
        if (!this.activeProject) return;

        const titleInput = document.getElementById('wb-new-task-title');
        const prioritySelect = document.getElementById('wb-new-task-priority');
        const agentSelect = document.getElementById('wb-new-task-agent');

        const title = (titleInput?.value || '').trim();
        if (!title) {
            titleInput?.focus();
            return;
        }

        const newTask = {
            id: 'task-' + Math.random().toString(36).substring(2, 9),
            projectId: this.activeProject.id,
            title: title,
            priority: prioritySelect?.value || 'High',
            assignedAgentId: agentSelect?.value || 'agent-jarvis-core',
            assignedAgentName: agentSelect?.options[agentSelect.selectedIndex]?.text || 'J.A.R.V.I.S. Core',
            status: 'Pending',
            canAutoExecute: true
        };

        try {
            const res = await fetch(`/api/projects/${this.activeProject.id}/tasks`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(newTask)
            });

            if (res.ok) {
                const created = await res.json();
                if (!this.activeProject.tasks) this.activeProject.tasks = [];
                this.activeProject.tasks.push(created || newTask);

                titleInput.value = '';
                this.renderTasksPane();
                this.updateGlobalHeaderStats();
                if (window.sfx) window.sfx.playHudBeep(880, 0.05);
            }
        } catch (e) {
            console.error("Failed to add task:", e);
        }
    }

    async toggleTaskStatus(taskId, isChecked) {
        if (!this.activeProject) return;
        const task = (this.activeProject.tasks || []).find(t => t.id === taskId);
        if (!task) return;

        task.status = isChecked ? 'Completed' : 'Pending';

        try {
            await fetch(`/api/projects/${this.activeProject.id}/tasks/${taskId}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(task)
            });
            this.renderTasksPane();
            this.updateGlobalHeaderStats();
            if (window.sfx) window.sfx.playHudBeep(isChecked ? 900 : 500, 0.04);
        } catch (e) {
            console.error("Failed to toggle task status:", e);
        }
    }

    async executeTaskAutonomous(taskId) {
        if (!this.activeProject) return;
        const task = (this.activeProject.tasks || []).find(t => t.id === taskId);
        if (!task) return;

        const card = document.querySelector(`.task-item-card[data-task-id="${taskId}"]`);
        const btn = card?.querySelector('.btn-trigger-task-exec');
        if (btn) {
            btn.textContent = '⏳ AGENT PROCESSING...';
            btn.disabled = true;
        }

        if (window.sfx) window.sfx.playProtocolPulse();

        try {
            const res = await fetch(`/api/projects/${this.activeProject.id}/tasks/${taskId}/execute`, {
                method: 'POST'
            });

            if (res.ok) {
                const executed = await res.json();
                task.status = 'Completed';
                task.automationResult = executed.automationResult || 'Autonomous execution verified. Deliverable checked into project log.';
                
                this.renderTasksPane();
                this.updateGlobalHeaderStats();
                if (window.sfx) window.sfx.playPowerUp();
            }
        } catch (e) {
            console.error("Failed to execute task:", e);
            if (btn) {
                btn.textContent = '⚡ AUTO-EXECUTE WITH AGENT';
                btn.disabled = false;
            }
        }
    }

    async deleteTask(taskId) {
        if (!this.activeProject) return;
        if (!confirm("Are you sure you want to remove this task?")) return;

        try {
            const res = await fetch(`/api/projects/${this.activeProject.id}/tasks/${taskId}`, {
                method: 'DELETE'
            });
            if (res.ok) {
                this.activeProject.tasks = (this.activeProject.tasks || []).filter(t => t.id !== taskId);
                this.renderTasksPane();
                this.updateGlobalHeaderStats();
                if (window.sfx) window.sfx.playHudBeep(450, 0.04);
            }
        } catch (e) {
            console.error("Failed to delete task:", e);
        }
    }

    // -------------------------------------------------------------
    // TAB 2: Notes & Engineering Logbook
    // -------------------------------------------------------------
    renderNotesPane() {
        const p = this.activeProject;
        if (!p) return;

        const editor = document.getElementById('wb-notes-editor');
        if (editor) {
            editor.value = p.researchDossier || `# ${p.name} - Engineering Logbook\n\nProject initialized under Stark Industries defense directive.\n\n## Objectives & Specifications\n- Target milestone: Active prototyping\n- Telemetry sensor feed: Synchronized\n`;
        }

        this.renderQuickLogEntries();
    }

    insertNotesTimestamp() {
        const editor = document.getElementById('wb-notes-editor');
        if (!editor) return;

        const now = new Date();
        const stamp = `\n\n### [${now.toISOString().replace('T', ' ').substring(0, 19)} UTC] - Log Entry\n- `;
        
        const start = editor.selectionStart;
        const end = editor.selectionEnd;
        editor.value = editor.value.substring(0, start) + stamp + editor.value.substring(end);
        editor.selectionStart = editor.selectionEnd = start + stamp.length;
        editor.focus();

        if (window.sfx) window.sfx.playHudBeep(800, 0.03);
    }

    insertNotesTemplate() {
        const editor = document.getElementById('wb-notes-editor');
        if (!editor) return;

        const template = `\n\n## 🛠️ TECHNICAL SPECIFICATIONS & CALIBRATION MATRIX
- **Architectural Directive:** High-cohesion plasma stabilization & telemetry
- **Lattice Topology:** Vibranium-Titanium hexagonal weave (99.4% kinetic absorption)
- **Harmonic Frequency:** 48.2 THz inverse resonance
- **Power Draw:** Arc Reactor Auxiliary Sub-bus 3.14 GW
- **Defense Countermeasures:** Sentry Perimeter Deflection enabled
`;
        editor.value += template;
        editor.scrollTop = editor.scrollHeight;
        editor.focus();

        if (window.sfx) window.sfx.playHudBeep(920, 0.04);
    }

    async saveProjectNotes() {
        if (!this.activeProject) return;

        const editor = document.getElementById('wb-notes-editor');
        if (!editor) return;

        this.activeProject.researchDossier = editor.value;

        try {
            const res = await fetch(`/api/projects/${this.activeProject.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(this.activeProject)
            });

            if (res.ok) {
                const saveBadge = document.getElementById('notes-save-badge');
                if (saveBadge) {
                    saveBadge.style.display = 'inline-block';
                    setTimeout(() => saveBadge.style.display = 'none', 2500);
                }
                if (window.sfx) window.sfx.playHudBeep(850, 0.05);
            }
        } catch (e) {
            console.error("Failed to save project notes:", e);
        }
    }

    postQuickLog() {
        if (!this.activeProject) return;
        const input = document.getElementById('input-quick-log');
        const text = (input?.value || '').trim();
        if (!text) return;

        const editor = document.getElementById('wb-notes-editor');
        const now = new Date();
        const stamp = `\n- **[${now.toLocaleTimeString()}]**: ${text}`;

        if (editor) {
            editor.value += stamp;
            editor.scrollTop = editor.scrollHeight;
        }

        input.value = '';
        this.saveProjectNotes();
        this.renderQuickLogEntries();
        if (window.sfx) window.sfx.playHudBeep(840, 0.04);
    }

    renderQuickLogEntries() {
        const stream = document.getElementById('quick-log-stream');
        if (!stream) return;

        const dossier = this.activeProject?.researchDossier || '';
        const lines = dossier.split('\n')
            .filter(l => l.includes('**[') || l.includes('Log Entry') || l.startsWith('- '))
            .slice(-6);

        stream.innerHTML = '';
        if (lines.length === 0) {
            stream.innerHTML = `<div style="font-size:0.75rem;color:#6fa0bc;font-style:italic;">No quick log entries yet. Post one below.</div>`;
            return;
        }

        lines.forEach(line => {
            const row = document.createElement('div');
            row.className = 'quick-log-pill';
            row.innerHTML = `<span style="color:#00d4ff;">•</span> <span>${this.escape(line.replace(/^[#-]\s*/, ''))}</span>`;
            stream.appendChild(row);
        });
    }

    // -------------------------------------------------------------
    // TAB 3: Research Dossier & Autonomous Investigation
    // -------------------------------------------------------------
    renderResearchPane() {
        const p = this.activeProject;
        if (!p) return;

        const preview = document.getElementById('wb-research-preview');
        if (preview) {
            preview.textContent = p.researchDossier || 'No deep intelligence dossier recorded. Deploy research agent below.';
        }

        const topicInput = document.getElementById('wb-inline-research-topic');
        if (topicInput && !topicInput.value) {
            topicInput.value = `Optimize ${p.name} engineering bottlenecks and spatial telemetry`;
        }
    }

    async deployInlineResearch() {
        if (!this.activeProject) return;

        const topicInput = document.getElementById('wb-inline-research-topic');
        const diffInput = document.getElementById('wb-inline-research-difficulty');
        const outputBox = document.getElementById('wb-inline-research-output');
        const btn = document.getElementById('btn-wb-deploy-research');

        const topic = (topicInput?.value || '').trim();
        const difficulty = (diffInput?.value || '').trim();

        if (!topic) {
            topicInput?.focus();
            return;
        }

        if (btn) {
            btn.textContent = '🚀 SYNTHESIZING RESEARCH...';
            btn.disabled = true;
        }

        if (outputBox) {
            outputBox.style.display = 'block';
            outputBox.textContent = "Autonomous Research Agent deployed. Synthesizing quantum calculations, materials lattices, and telemetry simulations...";
        }

        if (window.sfx) window.sfx.playProtocolPulse();

        try {
            const res = await fetch('/api/projects/research', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    projectId: this.activeProject.id,
                    researchTopic: topic,
                    specificDifficulty: difficulty,
                    agentId: this.activeProject.leadAgentId || 'agent-research'
                })
            });

            if (res.ok) {
                const data = await res.json();
                const report = data.report || "Research investigation completed. Results verified.";
                
                if (outputBox) outputBox.textContent = report;
                
                // Append to project notes/dossier
                this.activeProject.researchDossier = (this.activeProject.researchDossier || '') + `\n\n### 🔬 Autonomous Research Briefing: ${topic}\n${report}\n`;
                this.renderNotesPane();
                this.renderResearchPane();
                
                if (window.sfx) window.sfx.playPowerUp();
            }
        } catch (e) {
            console.error("Failed to deploy inline research:", e);
        } finally {
            if (btn) {
                btn.textContent = '🚀 ENGAGE RESEARCH AGENT';
                btn.disabled = false;
            }
        }
    }

    // -------------------------------------------------------------
    // TAB 4: Ghost Drive Files & Assets
    // -------------------------------------------------------------
    async loadProjectGhostFiles() {
        if (!this.activeProject) return;

        const container = document.getElementById('wb-ghost-files-list');
        const pathLabel = document.getElementById('wb-ghost-dir-label');
        if (!container) return;

        const path = this.activeProject.ghostDrivePath || `Projects/${this.activeProject.slug || this.activeProject.id}`;
        if (pathLabel) pathLabel.textContent = `Partition: ${path}`;

        container.innerHTML = '<div style="padding:16px;color:#7eb2cc;">Scanning holographic Ghost Drive partition...</div>';

        try {
            const res = await fetch(`/api/storage/files?path=${encodeURIComponent(path)}`);
            if (res.ok) {
                const files = await res.json();
                container.innerHTML = '';

                if (!files || files.length === 0) {
                    container.innerHTML = `
                        <div style="text-align:center;padding:24px;color:#6fa0bc;">
                            <div style="font-size:1.8rem;margin-bottom:6px;">📁</div>
                            <div style="font-family:var(--font-hud);font-size:0.85rem;color:#fff;">PARTITION INITIALIZED</div>
                            <p style="font-size:0.75rem;margin-top:4px;">Ghost drive partition ready at <code>${this.escape(path)}</code>. Project artifacts and notes will appear here.</p>
                        </div>
                    `;
                    return;
                }

                files.forEach(f => {
                    const row = document.createElement('div');
                    row.className = 'ghost-file-row';
                    const icon = f.isDirectory ? '📁' : '📄';

                    row.innerHTML = `
                        <div style="display:flex;align-items:center;gap:10px;">
                            <span>${icon}</span>
                            <span style="font-family:var(--font-mono);font-size:0.82rem;color:#fff;">${this.escape(f.name)}</span>
                        </div>
                        <div style="display:flex;align-items:center;gap:10px;font-family:var(--font-mono);font-size:0.75rem;color:#7eb2cc;">
                            <span>${f.sizeBytes ? (f.sizeBytes / 1024).toFixed(1) + ' KB' : 'DIR'}</span>
                            <span style="color:#4caf50;">ONLINE</span>
                        </div>
                    `;
                    container.appendChild(row);
                });
            }
        } catch (e) {
            console.error("Failed to load ghost files:", e);
            container.innerHTML = '<div style="padding:16px;color:#ff5555;">Failed to connect to Ghost Drive storage bus.</div>';
        }
    }

    renderGhostFilesPane() {
        this.loadProjectGhostFiles();
    }

    // -------------------------------------------------------------
    // TAB 5: 3D Blueprint Matrix
    // -------------------------------------------------------------
    renderBlueprint3DPane() {
        const p = this.activeProject;
        if (!p) return;

        const titleEl = document.getElementById('wb-3d-blueprint-title');
        if (titleEl) titleEl.textContent = `${p.name} • 3D Holographic Matrix`;

        const codeEl = document.getElementById('wb-3d-schematic-code');
        if (codeEl) {
            codeEl.textContent = `
// STARK LABS 3D HOLOGRAPHIC BLUEPRINT SCHEMATIC
// TARGET: ${p.name.toUpperCase()}
// SECTOR: ${p.category.toUpperCase()} / DEFENSE-CORE
// STATUS: ${p.status.toUpperCase()} [PROGRESS: ${p.progressPercent}%]
// GHOST PARTITION: ${p.ghostDrivePath}

struct HolographicLattice {
    vec3 origin = vec3(0.0, 1.2, 0.0);
    float harmonicResonance = 48.2; // THz
    float kineticShieldAbsorb = 0.994;
    mat4 tensorFluxTransformation;
};

void mainHologramProjection() {
    DeployKineticStabilizationGrid();
    SyncOrbitalTelemetryDownlink(40.0 /* Gbps */);
    EngageArcReactorPowerBus(3.14 /* GW */);
}
`;
        }
    }

    syncWith3DHud() {
        if (!this.app || !this.app.hologram3D) return;

        // Switch 3D WebGL HUD to PROJECT_MATRIX mode
        this.app.hologram3D.setHologramMode('PROJECT_MATRIX');

        // Update pills on HUD
        document.querySelectorAll('.holo-mode-pill').forEach(pill => {
            if (pill.getAttribute('data-mode') === 'PROJECT_MATRIX') pill.classList.add('active');
            else pill.classList.remove('active');
        });

        const syncBadge = document.getElementById('wb-3d-sync-badge');
        if (syncBadge) {
            syncBadge.style.display = 'inline-block';
            setTimeout(() => syncBadge.style.display = 'none', 3000);
        }

        if (window.sfx) window.sfx.playPowerUp();
    }

    // -------------------------------------------------------------
    // Save & Delete Project
    // -------------------------------------------------------------
    async saveCurrentProjectChanges() {
        if (!this.activeProject) return;

        const nameInput = document.getElementById('wb-proj-name');
        const descInput = document.getElementById('wb-proj-desc');
        const statusSelect = document.getElementById('wb-proj-status');
        const prioritySelect = document.getElementById('wb-proj-priority');
        const progressSlider = document.getElementById('wb-progress-slider');

        if (nameInput) this.activeProject.name = nameInput.value.trim();
        if (descInput) this.activeProject.description = descInput.value.trim();
        if (statusSelect) this.activeProject.status = statusSelect.value;
        if (prioritySelect) this.activeProject.priority = prioritySelect.value;
        if (progressSlider) this.activeProject.progressPercent = parseFloat(progressSlider.value);

        try {
            const res = await fetch(`/api/projects/${this.activeProject.id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(this.activeProject)
            });

            if (res.ok) {
                const updated = await res.json();
                this.activeProject = updated;
                await this.loadProjects();
                this.selectProject(this.activeProject.id);

                if (window.sfx) window.sfx.playHudBeep(940, 0.05);
            }
        } catch (e) {
            console.error("Failed to save project:", e);
        }
    }

    async deleteProject(projectId) {
        if (!confirm("Are you sure you want to decommission this project initiative and purge its active blueprints?")) return;

        try {
            const res = await fetch(`/api/projects/${projectId}`, {
                method: 'DELETE'
            });

            if (res.ok) {
                this.activeProject = null;
                await this.loadProjects();

                if (this.projectsList.length > 0) {
                    this.selectProject(this.projectsList[0].id);
                } else {
                    document.getElementById('workbench-empty-state').style.display = 'flex';
                    document.getElementById('workbench-active-content').style.display = 'none';
                }

                if (window.sfx) window.sfx.playHudBeep(450, 0.05);
            }
        } catch (e) {
            console.error("Failed to delete project:", e);
        }
    }

    // -------------------------------------------------------------
    // Create New Project Modal
    // -------------------------------------------------------------
    openNewProjectModal() {
        const modal = document.getElementById('create-project-modal');
        if (!modal) return;

        document.getElementById('new-proj-name').value = '';
        document.getElementById('new-proj-desc').value = '';
        document.getElementById('new-proj-category').value = 'Engineering';
        document.getElementById('new-proj-priority').value = 'High';
        document.getElementById('new-proj-tags').value = 'Stark Labs, Blueprint';

        modal.style.zIndex = '3500';
        modal.classList.remove('hidden');
        document.getElementById('new-proj-name')?.focus();
        try { window.sfx?.playHudBeep?.(700, 0.04); } catch {}
    }

    async submitNewProject() {
        const nameInput = document.getElementById('new-proj-name');
        const descInput = document.getElementById('new-proj-desc');
        const catSelect = document.getElementById('new-proj-category');
        const priSelect = document.getElementById('new-proj-priority');
        const tagsInput = document.getElementById('new-proj-tags');

        const name = (nameInput?.value || '').trim();
        if (!name) {
            nameInput?.focus();
            return;
        }

        const slug = name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');

        const newProject = {
            id: 'proj-' + Math.random().toString(36).substring(2, 9),
            name: name,
            slug: slug,
            description: (descInput?.value || '').trim() || 'New Stark Engineering Initiative',
            category: catSelect?.value || 'Engineering',
            status: 'Active',
            priority: priSelect?.value || 'High',
            progressPercent: 10.0,
            ghostDriveTag: 'PROJECTS',
            ghostDrivePath: `Projects/${slug}`,
            leadAgentId: 'agent-jarvis-core',
            leadAgentName: 'J.A.R.V.I.S. Core',
            tags: (tagsInput?.value || '').trim() || 'Stark Labs',
            researchDossier: `# ${name} - Project Initiative\n\nBlueprint initialized for laboratory prototyping and testing.\n`,
            tasks: [
                {
                    id: 'task-' + Math.random().toString(36).substring(2, 9),
                    title: `Initialize baseline CAD architecture & telemetry for ${name}`,
                    priority: 'High',
                    status: 'Pending',
                    canAutoExecute: true
                }
            ]
        };

        try {
            const res = await fetch('/api/projects', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(newProject)
            });

            if (res.ok) {
                const created = await res.json();
                document.getElementById('create-project-modal')?.classList.add('hidden');
                
                await this.loadProjects();
                this.selectProject(created?.id || newProject.id);
                if (window.sfx) window.sfx.playPowerUp();
            }
        } catch (e) {
            console.error("Failed to create project:", e);
        }
    }
}

window.JarvisProjectsStudio = JarvisProjectsStudio;

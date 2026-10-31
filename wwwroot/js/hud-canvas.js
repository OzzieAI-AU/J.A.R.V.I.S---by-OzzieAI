/**
 * J.A.R.V.I.S. Full-Screen Canvas 2D Dynamic Renderer
 * Allows rendering arbitrary graphics, HUD telemetry, radar reticles,
 * and data streams at any time on the full-screen canvas.
 */

class JarvisHudCanvas {
    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.ctx = null;
        this.width = window.innerWidth;
        this.height = window.innerHeight;
        
        this.dynamicEntities = [];
        this.audioFftData = new Uint8Array(64);
        this.radarAngle = 0;
        this.primaryColor = '#00d4ff';
        this.secondaryColor = '#ff9d00';
        
        this.init();
    }

    init() {
        if (!this.canvas) return;
        this.ctx = this.canvas.getContext('2d');
        this.resize();
        
        window.addEventListener('resize', () => this.resize());
        this.animate();
    }

    resize() {
        this.width = window.innerWidth;
        this.height = window.innerHeight;
        this.canvas.width = this.width;
        this.canvas.height = this.height;
    }

    setAudioFft(fftArray) {
        if (fftArray && fftArray.length) {
            this.audioFftData = fftArray;
        }
    }

    setThemeColors(primary, secondary) {
        this.primaryColor = primary || '#00d4ff';
        this.secondaryColor = secondary || '#ff9d00';
    }

    addDynamicEntity(entity) {
        this.dynamicEntities.push(entity);
    }

    clearDynamicEntities() {
        this.dynamicEntities = [];
    }

    animate() {
        requestAnimationFrame(() => this.animate());
        if (!this.ctx) return;

        this.ctx.clearRect(0, 0, this.width, this.height);

        // 1. Draw Holographic Corner HUD Brackets
        this.drawHudCornerBrackets();

        // 2. Draw Center Target Tracking Reticles
        this.drawTargetReticles();

        // 3. Draw Audio Spectrum Equalizer (at bottom)
        this.drawAudioSpectrum();

        // 4. Render All Dynamic Custom Canvas Entities
        const now = performance.now();
        for (let i = this.dynamicEntities.length - 1; i >= 0; i--) {
            const entity = this.dynamicEntities[i];
            if (entity.render) {
                entity.render(this.ctx, this.width, this.height, now);
            }
            if (entity.expiresAt && now > entity.expiresAt) {
                this.dynamicEntities.splice(i, 1);
            }
        }
    }

    drawHudCornerBrackets() {
        const ctx = this.ctx;
        const pad = 18;
        const len = 35;
        ctx.strokeStyle = this.primaryColor;
        ctx.lineWidth = 1.5;
        ctx.shadowColor = this.primaryColor;
        ctx.shadowBlur = 8;

        // Top-Left
        ctx.beginPath();
        ctx.moveTo(pad, pad + len);
        ctx.lineTo(pad, pad);
        ctx.lineTo(pad + len, pad);
        ctx.stroke();

        // Top-Right
        ctx.beginPath();
        ctx.moveTo(this.width - pad - len, pad);
        ctx.lineTo(this.width - pad, pad);
        ctx.lineTo(this.width - pad, pad + len);
        ctx.stroke();

        // Bottom-Left
        ctx.beginPath();
        ctx.moveTo(pad, this.height - pad - len);
        ctx.lineTo(pad, this.height - pad);
        ctx.lineTo(pad + len, this.height - pad);
        ctx.stroke();

        // Bottom-Right
        ctx.beginPath();
        ctx.moveTo(this.width - pad - len, this.height - pad);
        ctx.lineTo(this.width - pad, this.height - pad);
        ctx.lineTo(this.width - pad, this.height - pad - len);
        ctx.stroke();

        ctx.shadowBlur = 0;
    }

    drawTargetReticles() {
        const ctx = this.ctx;
        const cx = this.width / 2;
        const cy = this.height / 2;

        this.radarAngle += 0.012;

        ctx.save();
        ctx.translate(cx, cy);

        // Rotating Segmented Outer Ring
        ctx.strokeStyle = 'rgba(0, 212, 255, 0.2)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(0, 0, 160, 0, Math.PI * 2);
        ctx.stroke();

        // Segmented Tick Marks
        ctx.rotate(this.radarAngle);
        for (let i = 0; i < 12; i++) {
            ctx.rotate(Math.PI / 6);
            ctx.beginPath();
            ctx.moveTo(150, 0);
            ctx.lineTo(162, 0);
            ctx.stroke();
        }

        // Counter-rotating Inner Arc
        ctx.rotate(-this.radarAngle * 2.2);
        ctx.strokeStyle = this.primaryColor;
        ctx.lineWidth = 2;
        ctx.shadowColor = this.primaryColor;
        ctx.shadowBlur = 10;
        ctx.beginPath();
        ctx.arc(0, 0, 120, 0, Math.PI * 0.75);
        ctx.stroke();

        ctx.beginPath();
        ctx.arc(0, 0, 120, Math.PI, Math.PI * 1.75);
        ctx.stroke();

        ctx.restore();
    }

    drawAudioSpectrum() {
        const ctx = this.ctx;
        const barCount = 48;
        const barWidth = 4;
        const gap = 5;
        const totalW = barCount * (barWidth + gap);
        const startX = (this.width - totalW) / 2;
        const baseY = this.height - 85;

        for (let i = 0; i < barCount; i++) {
            const val = this.audioFftData[i % this.audioFftData.length] || 4;
            const h = Math.max(3, (val / 255) * 35);

            ctx.fillStyle = i % 2 === 0 ? this.primaryColor : this.secondaryColor;
            ctx.shadowColor = ctx.fillStyle;
            ctx.shadowBlur = 6;
            ctx.fillRect(startX + i * (barWidth + gap), baseY - h, barWidth, h);
        }
        ctx.shadowBlur = 0;
    }

    // Dynamic API to spawn holographic widgets on screen
    renderCustomHologramCard(x, y, w, h, title, lines) {
        this.addDynamicEntity({
            render: (ctx) => {
                ctx.save();
                ctx.fillStyle = 'rgba(2, 16, 28, 0.8)';
                ctx.strokeStyle = this.primaryColor;
                ctx.lineWidth = 1;
                ctx.shadowColor = this.primaryColor;
                ctx.shadowBlur = 12;

                ctx.fillRect(x, y, w, h);
                ctx.strokeRect(x, y, w, h);

                ctx.fillStyle = '#fff';
                ctx.font = '12px Orbitron';
                ctx.fillText(title, x + 12, y + 20);

                ctx.fillStyle = '#99d9ea';
                ctx.font = '11px Inter';
                lines.forEach((line, idx) => {
                    ctx.fillText(line, x + 12, y + 42 + idx * 16);
                });

                ctx.restore();
            }
        });
    }
}

window.JarvisHudCanvas = JarvisHudCanvas;

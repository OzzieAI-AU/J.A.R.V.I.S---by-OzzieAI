/**
 * J.A.R.V.I.S. Audio Synthesizer (SFX)
 * Generates Iron Man HUD sound effects natively via Web Audio API oscillators.
 */

class JarvisAudioSfx {
    constructor() {
        this.ctx = null;
        this.enabled = true;
        this.volume = 0.5;
    }

    ensureContext() {
        if (!this.ctx) {
            this.ctx = new (window.AudioContext || window.webkitAudioContext)();
        }
        if (this.ctx.state === 'suspended') {
            this.ctx.resume();
        }
    }

    playHudBeep(freq = 880, duration = 0.08) {
        if (!this.enabled) return;
        this.ensureContext();
        try {
            const osc = this.ctx.createOscillator();
            const gain = this.ctx.createGain();

            osc.type = 'sine';
            osc.frequency.setValueAtTime(freq, this.ctx.currentTime);
            osc.frequency.exponentialRampToValueAtTime(freq * 1.5, this.ctx.currentTime + duration);

            gain.gain.setValueAtTime(this.volume * 0.3, this.ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + duration);

            osc.connect(gain);
            gain.connect(this.ctx.destination);

            osc.start();
            osc.stop(this.ctx.currentTime + duration);
        } catch {}
    }

    playAccessGrantedChime() {
        if (!this.enabled) return;
        this.ensureContext();
        const notes = [523.25, 659.25, 783.99, 1046.50]; // C5, E5, G5, C6
        notes.forEach((freq, idx) => {
            setTimeout(() => this.playHudBeep(freq, 0.18), idx * 120);
        });
    }

    playAccessDeniedAlarm() {
        if (!this.enabled) return;
        this.ensureContext();
        try {
            const osc = this.ctx.createOscillator();
            const gain = this.ctx.createGain();

            osc.type = 'sawtooth';
            osc.frequency.setValueAtTime(180, this.ctx.currentTime);
            osc.frequency.setValueAtTime(120, this.ctx.currentTime + 0.15);

            gain.gain.setValueAtTime(this.volume * 0.4, this.ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + 0.35);

            osc.connect(gain);
            gain.connect(this.ctx.destination);

            osc.start();
            osc.stop(this.ctx.currentTime + 0.35);
        } catch {}
    }

    playProtocolPulse() {
        if (!this.enabled) return;
        this.ensureContext();
        try {
            const osc = this.ctx.createOscillator();
            const gain = this.ctx.createGain();

            osc.type = 'triangle';
            osc.frequency.setValueAtTime(220, this.ctx.currentTime);
            osc.frequency.exponentialRampToValueAtTime(880, this.ctx.currentTime + 0.4);

            gain.gain.setValueAtTime(this.volume * 0.4, this.ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + 0.5);

            osc.connect(gain);
            gain.connect(this.ctx.destination);

            osc.start();
            osc.stop(this.ctx.currentTime + 0.5);
        } catch {}
    }

    playPowerUp() {
        if (!this.enabled) return;
        this.ensureContext();
        try {
            const osc = this.ctx.createOscillator();
            const gain = this.ctx.createGain();
            osc.type = 'sawtooth';
            osc.frequency.setValueAtTime(140, this.ctx.currentTime);
            osc.frequency.exponentialRampToValueAtTime(880, this.ctx.currentTime + 0.35);
            gain.gain.setValueAtTime(this.volume * 0.3, this.ctx.currentTime);
            gain.gain.exponentialRampToValueAtTime(0.001, this.ctx.currentTime + 0.4);
            osc.connect(gain);
            gain.connect(this.ctx.destination);
            osc.start();
            osc.stop(this.ctx.currentTime + 0.4);
        } catch {}
    }

    playArcPulse() {
        this.playHudBeep(940, 0.05);
    }

    // ==================== STARK AMBIENT SOUNDSCAPE GENERATOR ====================
    startSoundscape(preset = 'ArcReactor', volume = 0.25) {
        this.ensureContext();
        this.stopSoundscape();

        this.soundscapeGain = this.ctx.createGain();
        this.soundscapeGain.gain.setValueAtTime(volume * this.volume, this.ctx.currentTime);
        this.soundscapeGain.connect(this.ctx.destination);

        this.soundscapeNodes = [];
        this.isSoundscapeActive = true;

        if (preset === 'ArcReactor') {
            // Low frequency 55Hz (A1) resonant sub-harmonic drone
            const osc1 = this.ctx.createOscillator();
            osc1.type = 'sawtooth';
            osc1.frequency.setValueAtTime(55, this.ctx.currentTime);

            const filter = this.ctx.createBiquadFilter();
            filter.type = 'lowpass';
            filter.frequency.setValueAtTime(140, this.ctx.currentTime);

            // Secondary harmonic resonance
            const osc2 = this.ctx.createOscillator();
            osc2.type = 'sine';
            osc2.frequency.setValueAtTime(110, this.ctx.currentTime);

            osc1.connect(filter);
            filter.connect(this.soundscapeGain);
            osc2.connect(this.soundscapeGain);

            osc1.start();
            osc2.start();
            this.soundscapeNodes.push(osc1, osc2, filter);
        } else if (preset === 'CyberRain') {
            // Filtered pink noise rain simulation
            const bufferSize = this.ctx.sampleRate * 2;
            const noiseBuffer = this.ctx.createBuffer(1, bufferSize, this.ctx.sampleRate);
            const output = noiseBuffer.getChannelData(0);
            let b0 = 0, b1 = 0, b2 = 0;
            for (let i = 0; i < bufferSize; i++) {
                const white = Math.random() * 2 - 1;
                b0 = 0.99886 * b0 + white * 0.0555179;
                b1 = 0.99332 * b1 + white * 0.0750759;
                b2 = 0.96900 * b2 + white * 0.1538520;
                output[i] = (b0 + b1 + b2) * 0.12;
            }

            const noise = this.ctx.createBufferSource();
            noise.buffer = noiseBuffer;
            noise.loop = true;

            const rainFilter = this.ctx.createBiquadFilter();
            rainFilter.type = 'bandpass';
            rainFilter.frequency.setValueAtTime(800, this.ctx.currentTime);
            rainFilter.Q.setValueAtTime(1.2, this.ctx.currentTime);

            noise.connect(rainFilter);
            rainFilter.connect(this.soundscapeGain);
            noise.start();
            this.soundscapeNodes.push(noise, rainFilter);
        } else {
            // Turbine & Lab ventilation
            const osc = this.ctx.createOscillator();
            osc.type = 'triangle';
            osc.frequency.setValueAtTime(72, this.ctx.currentTime);
            osc.connect(this.soundscapeGain);
            osc.start();
            this.soundscapeNodes.push(osc);
        }
    }

    stopSoundscape() {
        if (this.soundscapeNodes) {
            this.soundscapeNodes.forEach(n => {
                try { if (n.stop) n.stop(); } catch {}
                try { if (n.disconnect) n.disconnect(); } catch {}
            });
            this.soundscapeNodes = [];
        }
        if (this.soundscapeGain) {
            try { this.soundscapeGain.disconnect(); } catch {}
            this.soundscapeGain = null;
        }
        this.isSoundscapeActive = false;
    }

    setSoundscapeVolume(vol) {
        if (this.soundscapeGain && this.ctx) {
            this.soundscapeGain.gain.setValueAtTime(vol * this.volume, this.ctx.currentTime);
        }
    }
}

window.JarvisAudioSfx = JarvisAudioSfx;


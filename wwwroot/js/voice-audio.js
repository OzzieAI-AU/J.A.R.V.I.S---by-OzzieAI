/**
 * J.A.R.V.I.S. Hybrid Voice & Audio Studio
 * Handles:
 * 1. Client-side Web Speech STT (Continuous listening + Wake word "Hey Jarvis")
 * 2. Client-side Web Speech Synthesis (TTS) with configurable British Butler voice
 * 3. Web Audio API FFT Analyzer for real-time 3D Hologram audio reactivity
 */

class JarvisVoiceStudio {
    constructor(callbacks) {
        this.callbacks = callbacks || {}; // onTranscript, onInterim, onWakeWord, onAudioFft, onAudioStateChange
        
        // STT State & Wake Machine
        this.recognition = null;
        this.isListening = false;
        this.continuousMode = false;
        this.wakeWord = "jarvis";
        this.wakeWordEnabled = true;
        this.isWakeActive = false;
        this.wakeTimeout = null;
        this.sttExclusions = ['um', 'uh', 'ah', 'erm', 'like', 'you know', 'thank you for watching', 'subtitles by', '[music]', '[applause]'];

        // TTS State
        this.synth = window.speechSynthesis;
        this.availableVoices = [];
        this.selectedVoice = null;
        this.pitch = 1.0;
        this.rate = 1.05;
        this.volume = 1.0;
        this.enableTtsSanitization = true;
        this.ttsExclusionSymbols = "* _ # ~ ` | [ ] ( ) { } < > \\ / @ ^ = + • › » « $ % & ;";
        this.stripMarkdown = true;

        // Streaming Sentence-by-Sentence TTS Queue
        this.speechQueue = [];
        this.isSpeakingQueue = false;
        this.activeUtterance = null;

        // Audio Context & FFT Analyzer
        this.audioCtx = null;
        this.analyser = null;
        this.micStream = null;
        this.fftArray = new Uint8Array(64);
        this.isAnalyzing = false;

        this.init();
    }

    init() {
        this.initTtsVoices();
        this.initStt();
    }

    // ==================== TTS (Text-to-Speech) ====================
    initTtsVoices() {
        if (!this.synth) return;

        const loadVoices = () => {
            const voices = this.synth.getVoices();
            if (voices && voices.length > 0) {
                this.availableVoices = voices;
                this.selectedVoice = this.getBestJarvisVoice();
                
                if (this.callbacks.onVoicesLoaded) {
                    this.callbacks.onVoicesLoaded(this.availableVoices);
                }
            }
        };

        loadVoices();
        if (this.synth.onvoiceschanged !== undefined) {
            this.synth.onvoiceschanged = loadVoices;
        }

        // Retry in 300ms and 1000ms for Chromium/Edge async voice registry hydration
        setTimeout(loadVoices, 300);
        setTimeout(loadVoices, 1000);
    }

    getBestJarvisVoice() {
        if (!this.availableVoices || this.availableVoices.length === 0) return null;

        // 1. First priority: British / UK English Male (Paul Bettany Butler style)
        const ukMale = this.availableVoices.find(v => 
            (v.lang.includes('en-GB') || v.name.includes('UK') || v.name.includes('British') || v.name.includes('George') || v.name.includes('Ryan')) &&
            !v.name.includes('Female') && !v.name.includes('Zira') && !v.name.includes('Susan') && !v.name.includes('Hazel') && !v.name.includes('Sonia')
        );
        if (ukMale) return ukMale;

        // 2. Second priority: Microsoft Mark (Deep, calm, resonant American male - sounds closest to Jarvis on Windows)
        const mark = this.availableVoices.find(v => v.name.includes('Mark'));
        if (mark) return mark;

        // 3. Third priority: Microsoft David
        const david = this.availableVoices.find(v => v.name.includes('David'));
        if (david) return david;

        // 4. Any English male voice
        const enMale = this.availableVoices.find(v => 
            v.lang.includes('en') && !v.name.includes('Female') && !v.name.includes('Zira') && !v.name.includes('Susan') && !v.name.includes('Hazel')
        );
        if (enMale) return enMale;

        return this.availableVoices[0];
    }

    setVoiceByName(name) {
        if (!name) return;
        const found = this.availableVoices.find(v => v.name === name);
        if (found) {
            this.selectedVoice = found;
        } else {
            // If requested name wasn't found in current browser, select the best Jarvis voice
            this.selectedVoice = this.getBestJarvisVoice();
        }
    }

    setVoiceParams(pitch, rate, volume) {
        this.pitch = pitch !== undefined ? pitch : this.pitch;
        this.rate = rate !== undefined ? rate : this.rate;
        this.volume = volume !== undefined ? volume : this.volume;
    }

    applyVoicePreset(presetName) {
        if (presetName === 'JarvisClassicButler') {
            this.setVoiceParams(0.95, 1.02, 1.0);
            this.selectedVoice = this.getBestJarvisVoice();
        } else if (presetName === 'FridayTactical') {
            this.setVoiceParams(1.15, 1.10, 1.0);
            const female = this.availableVoices.find(v => v.name.includes('Female') || v.name.includes('Zira') || v.name.includes('Samantha') || (v.lang.includes('en') && v.name.includes('Neural')));
            if (female) this.selectedVoice = female;
        } else if (presetName === 'VeronicaOrbital') {
            this.setVoiceParams(0.85, 0.95, 1.0);
        } else if (presetName === 'EdithSecurity') {
            this.setVoiceParams(1.05, 1.05, 1.0);
        }
    }

    setTtsSanitization(enabled, exclusionSymbols) {
        this.enableTtsSanitization = !!enabled;
        if (exclusionSymbols !== undefined) this.ttsExclusionSymbols = exclusionSymbols;
    }

    sanitizeForTts(text) {
        if (!text || typeof text !== 'string') return '';
        if (!this.enableTtsSanitization) return text;

        let cleaned = text;

        // 1. Remove markdown code blocks ```...```
        cleaned = cleaned.replace(/```[\s\S]*?```/g, ' code block omitted ');

        // 2. Remove inline code `...`
        cleaned = cleaned.replace(/`([^`]+)`/g, '$1');

        // 3. Remove markdown images and replace links with anchor text [title](url) -> title
        cleaned = cleaned.replace(/!\[.*?\]\(.*?\)/g, '');
        cleaned = cleaned.replace(/\[(.*?)\]\(.*?\)/g, '$1');

        // 4. Remove markdown headers (e.g. ### Header)
        cleaned = cleaned.replace(/^#{1,6}\s+/gm, '');

        // 5. Remove blockquote markers >
        cleaned = cleaned.replace(/^>\s+/gm, '');

        // 6. Clean markdown table borders and replace pipes with commas
        cleaned = cleaned.replace(/\|[-:\s|]+\|/g, '');
        cleaned = cleaned.replace(/\|/g, ', ');

        // 7. Strip configured exclusion symbols
        if (this.ttsExclusionSymbols) {
            const syms = this.ttsExclusionSymbols.split(/\s+/).filter(Boolean);
            for (const sym of syms) {
                const escaped = sym.replace(/[-\/\\^$*+?.()|[\]{}]/g, '\\$&');
                try {
                    cleaned = cleaned.replace(new RegExp(escaped, 'g'), ' ');
                } catch {
                    cleaned = cleaned.replaceAll(sym, ' ');
                }
            }
        }

        // 8. Always strip persistent annoying speech symbols
        cleaned = cleaned.replace(/[*#`~_•›»«\^=@+\\\/]/g, ' ');

        // 9. Strip emojis and miscellaneous symbols
        cleaned = cleaned.replace(/[\u{1F600}-\u{1F64F}\u{1F300}-\u{1F5FF}\u{1F680}-\u{1F6FF}\u{1F700}-\u{1F77F}\u{1F780}-\u{1F7FF}\u{1F800}-\u{1F8FF}\u{1F900}-\u{1F9FF}\u{1FA00}-\u{1FA6F}\u{2600}-\u{26FF}\u{2700}-\u{27BF}]/gu, '');

        // 10. Collapse multiple spaces and trim
        cleaned = cleaned.replace(/\s+/g, ' ').trim();

        return cleaned;
    }

    // =========================================================================
    // J.A.R.V.I.S. STREAMING TTS ENGINE & SENTENCE QUEUE PIPELINE
    // Reads out sentences at a calculated early point as tokens stream from LLM
    // =========================================================================
    enqueueSentence(sentence, onFinishedQueue) {
        if (!this.synth) return;
        const cleanText = this.sanitizeForTts(sentence);
        if (!cleanText || cleanText.length < 2) return;

        this.speechQueue.push({ text: cleanText, onFinishedQueue });
        if (!this.isSpeakingQueue) {
            this.processNextInQueue();
        }
    }

    processNextInQueue() {
        if (!this.synth) return;
        if (this.speechQueue.length === 0) {
            this.isSpeakingQueue = false;
            this.activeUtterance = null;
            if (this.callbacks.onSpeakingChange) this.callbacks.onSpeakingChange(false);
            this.startSimulatedAudioFft(false);
            return;
        }

        this.isSpeakingQueue = true;
        const item = this.speechQueue.shift();

        const utterance = new SpeechSynthesisUtterance(item.text);
        if (this.selectedVoice) utterance.voice = this.selectedVoice;
        utterance.pitch = this.pitch;
        utterance.rate = this.rate;
        utterance.volume = this.volume;
        this.activeUtterance = utterance;

        utterance.onstart = () => {
            if (this.callbacks.onSpeakingChange) this.callbacks.onSpeakingChange(true);
            this.startSimulatedAudioFft(true);
        };

        utterance.onend = () => {
            this.activeUtterance = null;
            if (item.onFinishedQueue && this.speechQueue.length === 0) {
                item.onFinishedQueue();
            }
            // Advance to next queued sentence immediately for smooth seamless playback
            this.processNextInQueue();
        };

        utterance.onerror = (e) => {
            console.warn("TTS utterance playback error, advancing speech queue:", e);
            this.activeUtterance = null;
            this.processNextInQueue();
        };

        this.synth.speak(utterance);
    }

    clearSpeechQueue() {
        this.speechQueue = [];
        this.isSpeakingQueue = false;
        this.activeUtterance = null;
        if (this.synth) this.synth.cancel();
        if (this.callbacks.onSpeakingChange) this.callbacks.onSpeakingChange(false);
        this.startSimulatedAudioFft(false);
    }

    speak(text, onEnd) {
        if (!this.synth) return;
        this.clearSpeechQueue(); // Stop any ongoing speech or queue

        const cleanText = this.sanitizeForTts(text);
        if (!cleanText) return;

        const utterance = new SpeechSynthesisUtterance(cleanText);
        if (this.selectedVoice) utterance.voice = this.selectedVoice;
        utterance.pitch = this.pitch;
        utterance.rate = this.rate;
        utterance.volume = this.volume;
        this.activeUtterance = utterance;

        utterance.onstart = () => {
            if (this.callbacks.onSpeakingChange) this.callbacks.onSpeakingChange(true);
            this.startSimulatedAudioFft(true);
        };

        utterance.onend = () => {
            this.activeUtterance = null;
            if (this.callbacks.onSpeakingChange) this.callbacks.onSpeakingChange(false);
            this.startSimulatedAudioFft(false);
            if (onEnd) onEnd();
        };

        utterance.onerror = () => {
            this.activeUtterance = null;
            if (this.callbacks.onSpeakingChange) this.callbacks.onSpeakingChange(false);
            this.startSimulatedAudioFft(false);
        };

        this.synth.speak(utterance);
    }

    stopSpeaking() {
        this.clearSpeechQueue();
    }

    // ==================== STT (Speech-to-Text) ====================
    setSttExclusions(phrasesStr) {
        if (!phrasesStr || typeof phrasesStr !== 'string') return;
        this.sttExclusions = phrasesStr
            .split(/[,;\n]+/)
            .map(p => p.trim().toLowerCase())
            .filter(Boolean);
    }

    sanitizeSttTranscript(text) {
        if (!text || typeof text !== 'string') return '';
        let cleaned = text.trim();

        // 1. Filter out configured STT exclusion words and filler phrases
        if (this.sttExclusions && this.sttExclusions.length > 0) {
            for (const phrase of this.sttExclusions) {
                if (!phrase) continue;
                const escaped = phrase.replace(/[-\/\\^$*+?.()|[\]{}]/g, '\\$&');
                const regex = new RegExp(`\\b${escaped}\\b`, 'gi');
                cleaned = cleaned.replace(regex, ' ');
            }
        }

        // 2. Clean leading/trailing punctuation and collapse multiple spaces
        cleaned = cleaned.replace(/^[,\.\-?!:\s]+/, '');
        cleaned = cleaned.replace(/\s+/g, ' ').trim();

        return cleaned;
    }

    activateWakeWindow() {
        this.isWakeActive = true;
        if (this.wakeTimeout) clearTimeout(this.wakeTimeout);
        this.wakeTimeout = setTimeout(() => {
            this.isWakeActive = false;
        }, 8000);
    }

    resetWakeWindow() {
        this.isWakeActive = false;
        if (this.wakeTimeout) {
            clearTimeout(this.wakeTimeout);
            this.wakeTimeout = null;
        }
    }

    initStt() {
        const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SpeechRec) {
            console.warn("Web Speech API not supported in this browser.");
            return;
        }

        this.recognition = new SpeechRec();
        this.recognition.continuous = true;
        this.recognition.interimResults = true;
        this.recognition.lang = 'en-US';

        this.recognition.onstart = () => {
            this.isListening = true;
            if (this.callbacks.onListeningChange) this.callbacks.onListeningChange(true);
        };

        this.recognition.onresult = (event) => {
            let interimTranscript = '';
            let finalTranscript = '';

            for (let i = event.resultIndex; i < event.results.length; ++i) {
                if (event.results[i].isFinal) {
                    finalTranscript += event.results[i][0].transcript;
                } else {
                    interimTranscript += event.results[i][0].transcript;
                }
            }

            const activeRaw = (finalTranscript || interimTranscript).trim();
            if (!activeRaw) return;

            // Full-Duplex Voice Barge-In: silence Jarvis when user speaks a substantive word
            if (activeRaw.length >= 3) {
                if (this.synth && this.synth.speaking) {
                    this.synth.cancel();
                    this.startSimulatedAudioFft(false);
                    if (this.callbacks.onSpeakingChange) this.callbacks.onSpeakingChange(false);
                    if (window.jarvisApp && window.jarvisApp.hub) {
                        try { window.jarvisApp.hub.invoke('InterruptSpeech'); } catch {}
                    }
                }
                document.querySelectorAll('.card-audio-player').forEach(p => {
                    try { p.pause(); } catch {}
                });
            }

            // Live interim feedback callback for HUD
            if (interimTranscript && this.callbacks.onInterim) {
                const livePreview = this.sanitizeSttTranscript(interimTranscript);
                if (livePreview) this.callbacks.onInterim(livePreview);
            }

            // Final transcript processing
            if (!finalTranscript) return;

            const wakeRegex = /\b(?:hey|hi|hello|ok|okay)?\s*(?:jarvis|javis|travis|javus|yavus)[\s,.:;!?]*/i;
            const hasWakeWord = wakeRegex.test(finalTranscript);

            if (this.wakeWordEnabled) {
                if (hasWakeWord) {
                    // Activate wake state
                    this.activateWakeWindow();
                    if (this.callbacks.onWakeWord) this.callbacks.onWakeWord();

                    // Strip wake word & greeting prefix cleanly
                    let directive = finalTranscript.replace(wakeRegex, '').trim();
                    directive = this.sanitizeSttTranscript(directive);

                    // If user said ONLY "Hey Jarvis" / "Javis" (without a command):
                    // do NOT dispatch "Hey" or blank text to LLM!
                    if (!directive || directive.length < 2) {
                        return; // Wake state active, waiting for directive
                    }

                    // User said "Hey Jarvis, [directive]" in one continuous breath:
                    this.resetWakeWindow();
                    if (this.callbacks.onTranscript) {
                        this.callbacks.onTranscript(directive);
                    }
                } else if (this.isWakeActive) {
                    // User was already in active listening window after waking Jarvis
                    const directive = this.sanitizeSttTranscript(finalTranscript);
                    if (directive && directive.length >= 2) {
                        this.resetWakeWindow();
                        if (this.callbacks.onTranscript) {
                            this.callbacks.onTranscript(directive);
                        }
                    }
                } else {
                    // Check for direct high-intent music/playback directives even without explicit wake prefix
                    const trimmed = finalTranscript.trim().toLowerCase();
                    const isDirectMusicDirective = 
                        trimmed.startsWith('play ') || trimmed.startsWith('request ') ||
                        trimmed.startsWith('pause ') || trimmed.startsWith('resume ') ||
                        trimmed.startsWith('stop music') || trimmed.startsWith('stop radio') ||
                        trimmed.startsWith('next ') || trimmed.startsWith('skip ') ||
                        trimmed.startsWith('shuffle') || trimmed.startsWith('repeat') ||
                        trimmed.startsWith('download all') || trimmed.startsWith('download album') ||
                        trimmed.includes('corrupted') || trimmed.includes('radio volume');

                    if (isDirectMusicDirective) {
                        const directive = this.sanitizeSttTranscript(finalTranscript);
                        if (directive && directive.length >= 2 && this.callbacks.onTranscript) {
                            this.callbacks.onTranscript(directive);
                            return;
                        }
                    }

                    // Cleanly ignore background room noise / TV chatter!
                    return;
                }
            } else {
                // Wake word disabled (Manual push-to-talk mode)
                const directive = this.sanitizeSttTranscript(finalTranscript);
                if (directive && directive.length >= 2 && this.callbacks.onTranscript) {
                    this.callbacks.onTranscript(directive);
                }
            }
        };

        this.recognition.onerror = (event) => {
            console.warn("STT Error:", event.error);
        };

        this.recognition.onend = () => {
            this.isListening = false;
            if (this.continuousMode) {
                // Auto restart in continuous mode
                try { this.recognition.start(); } catch {}
            } else if (this.callbacks.onListeningChange) {
                this.callbacks.onListeningChange(false);
            }
        };
    }

    startListening(continuous = false) {
        this.continuousMode = continuous;
        if (!this.recognition) return;
        try {
            this.initMicrophoneAnalyser();
            this.recognition.start();
        } catch (e) {
            console.warn(e);
        }
    }

    stopListening() {
        this.continuousMode = false;
        if (this.recognition) {
            try { this.recognition.stop(); } catch {}
        }
    }

    toggleListening() {
        if (this.isListening) {
            this.stopListening();
        } else {
            this.startListening(this.continuousMode);
        }
    }

    // ==================== Web Audio FFT Analyzer ====================
    async initMicrophoneAnalyser() {
        if (this.analyser) return;
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: false });
            this.micStream = stream;
            this.audioCtx = new (window.AudioContext || window.webkitAudioContext)();
            const source = this.audioCtx.createMediaStreamSource(stream);
            this.analyser = this.audioCtx.createAnalyser();
            this.analyser.fftSize = 128;
            source.connect(this.analyser);

            this.isAnalyzing = true;
            this.analyzeAudioLoop();
        } catch (err) {
            console.log("Microphone FFT analysis bypass or permission denied.", err);
        }
    }

    analyzeAudioLoop() {
        if (!this.isAnalyzing || !this.analyser) return;

        this.analyser.getByteFrequencyData(this.fftArray);

        // Compute RMS
        let sum = 0;
        for (let i = 0; i < this.fftArray.length; i++) {
            sum += (this.fftArray[i] / 255) ** 2;
        }
        const rms = Math.sqrt(sum / this.fftArray.length);

        if (this.callbacks.onAudioFft) {
            this.callbacks.onAudioFft(this.fftArray, rms);
        }

        requestAnimationFrame(() => this.analyzeAudioLoop());
    }

    startSimulatedAudioFft(speaking) {
        if (speaking) {
            if (this.simInterval) clearInterval(this.simInterval);
            this.simInterval = setInterval(() => {
                for (let i = 0; i < this.fftArray.length; i++) {
                    this.fftArray[i] = Math.floor(Math.random() * 220);
                }
                const rms = 0.45 + Math.random() * 0.35;
                if (this.callbacks.onAudioFft) {
                    this.callbacks.onAudioFft(this.fftArray, rms);
                }
            }, 60);
        } else {
            if (this.simInterval) clearInterval(this.simInterval);
            this.fftArray.fill(0);
            if (this.callbacks.onAudioFft) {
                this.callbacks.onAudioFft(this.fftArray, 0);
            }
        }
    }
}

window.JarvisVoiceStudio = JarvisVoiceStudio;

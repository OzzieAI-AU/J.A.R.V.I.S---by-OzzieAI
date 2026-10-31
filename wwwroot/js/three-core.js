/**
 * J.A.R.V.I.S. 10X Holographic 3D WebGL Engine (Three.js)
 * Implements Stark Industries Advanced Holographic Modes:
 * 1. MARK_HELMET: Procedural Iron Man Mark Armor Faceplate / Helmet wireframe with glowing eye slit optics, cheek bevels, audio vocalizer, and orbital targeting HUD.
 * 2. TACTICAL_GLOBE: Planetary Defense Earth Hologram with glowing continental nodes, Veronica orbital satellite, and targeting telemetry vectors.
 * 3. NEURAL_CORE: Jarvis Dual Filamentous Synaptic Core + Arc Reactor Orbital Gyro Rings.
 * 4. QUANTUM_VORTEX: Hyper-dimensional Torus Knot and Particle Double-Helix Accelerator.
 * Plus: Audio-reactive vertex oscillation, cursor parallax, and 3D shockwave ripple on click.
 */

class JarvisHologram3D {
    constructor(canvasId) {
        this.canvas = document.getElementById(canvasId);
        this.scene = null;
        this.camera = null;
        this.renderer = null;
        
        // Mode Management
        this.activeMode = 'MARK_HELMET'; // MARK_HELMET, TACTICAL_GLOBE, NEURAL_CORE, QUANTUM_VORTEX
        this.modes = {};
        
        // Groups
        this.helmetGroup = null;
        this.globeGroup = null;
        this.neuralCoreGroup = null;
        this.arcReactorGroup = null;
        this.quantumVortexGroup = null;
        this.projectMatrixGroup = null;
        this.matrixNodes = [];
        this.matrixPackets = null;
        this.matrixPacketData = [];
        this.starfield = null;
        this.gridFloor = null;
        this.shockwaves = [];
        
        // Animation State
        this.clock = new THREE.Clock();
        this.audioRms = 0.0;
        this.activeThemeColor = 0x00d4ff;
        this.secondaryColor = 0xff9d00;
        this.accentColor = 0x00ffaa;
        
        // Mouse parallax & interaction
        this.mouseX = 0;
        this.mouseY = 0;
        this.targetRotationX = 0;
        this.targetRotationY = 0;
        this.raycaster = new THREE.Raycaster();
        this.mouseVec = new THREE.Vector2();
        
        this.init();
    }

    init() {
        if (!this.canvas) return;

        // 1. Scene & Camera
        this.scene = new THREE.Scene();
        this.scene.fog = new THREE.FogExp2(0x02070d, 0.0016);

        const width = window.innerWidth;
        const height = window.innerHeight;
        this.camera = new THREE.PerspectiveCamera(50, width / height, 0.1, 2000);
        this.camera.position.set(0, 0, 110);

        // 2. Renderer
        this.renderer = new THREE.WebGLRenderer({
            canvas: this.canvas,
            antialias: true,
            alpha: true,
            powerPreference: 'high-performance'
        });
        this.renderer.setSize(width, height);
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

        // 3. Build All 6 Holographic Modes
        this.buildMarkHelmet();
        this.buildTacticalGlobe();
        this.buildNeuralAndArcCore();
        this.buildQuantumVortex();
        this.buildProjectBlueprintMatrix();
        this.buildCadModelGroup();
        
        // 4. Background Elements
        this.buildFloatingStarfield();
        this.buildRadarFloorGrid();

        // 5. Lighting
        const ambientLight = new THREE.AmbientLight(0x051828, 2.0);
        this.scene.add(ambientLight);

        this.keyLight = new THREE.PointLight(0x00d4ff, 3.5, 400);
        this.keyLight.position.set(35, 25, 70);
        this.scene.add(this.keyLight);

        this.fillLight = new THREE.PointLight(0xff9d00, 2.5, 400);
        this.fillLight.position.set(-35, -20, 60);
        this.scene.add(this.fillLight);

        // 6. Set initial mode
        this.setHologramMode('MARK_HELMET');

        // 7. Event Listeners
        window.addEventListener('resize', () => this.onWindowResize());
        window.addEventListener('mousemove', (e) => this.onMouseMove(e));
        window.addEventListener('click', (e) => this.onCanvasClick(e));

        // Start animation loop
        this.animate();
    }

    // ==========================================
    // 1. MODE: STARK MARK ARMOR HELMET / FACEPLATE
    // ==========================================
    buildMarkHelmet() {
        this.helmetGroup = new THREE.Group();
        this.helmetGroup.name = 'MARK_HELMET';

        // Helmet Dome / Cranium (Geodesic Wireframe Sphere upper segment)
        const craniumGeo = new THREE.SphereGeometry(18, 32, 24, 0, Math.PI * 2, 0, Math.PI * 0.62);
        const craniumMat = new THREE.MeshBasicMaterial({
            color: 0x00d4ff,
            wireframe: true,
            transparent: true,
            opacity: 0.35,
            blending: THREE.AdditiveBlending
        });
        const cranium = new THREE.Mesh(craniumGeo, craniumMat);
        cranium.position.set(0, 3, 0);
        this.helmetGroup.add(cranium);

        // Brow Plate / Forehead Chevron (Gold/Cyan angled wedge)
        const browGeo = new THREE.ConeGeometry(14, 8, 4, 1, true);
        const browMat = new THREE.MeshBasicMaterial({
            color: 0xffd700,
            wireframe: true,
            transparent: true,
            opacity: 0.55,
            blending: THREE.AdditiveBlending
        });
        const brow = new THREE.Mesh(browGeo, browMat);
        brow.rotation.x = Math.PI * 0.45;
        brow.rotation.y = Math.PI * 0.25;
        brow.position.set(0, 9, 8);
        brow.scale.set(1.2, 0.4, 0.7);
        this.helmetGroup.add(brow);

        // Cheekbones / Faceplate Side Plates
        [-1, 1].forEach((side) => {
            const cheekGeo = new THREE.CylinderGeometry(4, 9, 14, 4, 1, true);
            const cheekMat = new THREE.MeshBasicMaterial({
                color: 0x00d4ff,
                wireframe: true,
                transparent: true,
                opacity: 0.45,
                blending: THREE.AdditiveBlending
            });
            const cheek = new THREE.Mesh(cheekGeo, cheekMat);
            cheek.position.set(side * 8.5, 0, 7.5);
            cheek.rotation.z = side * -0.25;
            cheek.rotation.y = side * 0.4;
            cheek.scale.set(0.6, 1.0, 0.7);
            this.helmetGroup.add(cheek);

            // Ear Disc Nodes (Temporal Receptors)
            const earGeo = new THREE.CylinderGeometry(3.5, 3.5, 2, 16);
            const earMat = new THREE.MeshBasicMaterial({
                color: 0xff9d00,
                wireframe: true,
                transparent: true,
                opacity: 0.65,
                blending: THREE.AdditiveBlending
            });
            const ear = new THREE.Mesh(earGeo, earMat);
            ear.position.set(side * 17.5, 2, 0);
            ear.rotation.z = Math.PI / 2;
            this.helmetGroup.add(ear);

            // Orbiting Ear Reticle Rings
            const earRingGeo = new THREE.RingGeometry(4, 4.4, 24);
            const earRingMat = new THREE.MeshBasicMaterial({
                color: 0x00d4ff,
                side: THREE.DoubleSide,
                transparent: true,
                opacity: 0.5
            });
            const earRing = new THREE.Mesh(earRingGeo, earRingMat);
            earRing.position.set(side * 18.2, 2, 0);
            earRing.rotation.y = Math.PI / 2;
            this.helmetGroup.add(earRing);
        });

        // Glowing Eye Slit Optics (Tony Stark Iconic Angled Eyes)
        this.eyeMeshes = [];
        [-1, 1].forEach((side) => {
            // Inner Bright Eye Flare
            const eyeGeo = new THREE.PlaneGeometry(5.8, 1.4);
            const eyeMat = new THREE.MeshBasicMaterial({
                color: 0xffffff,
                side: THREE.DoubleSide,
                transparent: true,
                opacity: 0.95,
                blending: THREE.AdditiveBlending
            });
            const eye = new THREE.Mesh(eyeGeo, eyeMat);
            eye.position.set(side * 4.6, 2.8, 14.8);
            eye.rotation.y = side * 0.22;
            eye.rotation.z = side * -0.15; // Aggressive Stark eye angle
            this.helmetGroup.add(eye);
            this.eyeMeshes.push(eye);

            // Outer Eye Glow Halo
            const glowGeo = new THREE.PlaneGeometry(8.0, 3.0);
            const glowMat = new THREE.MeshBasicMaterial({
                color: 0x00d4ff,
                side: THREE.DoubleSide,
                transparent: true,
                opacity: 0.5,
                blending: THREE.AdditiveBlending
            });
            const glow = new THREE.Mesh(glowGeo, glowMat);
            glow.position.set(side * 4.6, 2.8, 14.6);
            glow.rotation.y = side * 0.22;
            glow.rotation.z = side * -0.15;
            this.helmetGroup.add(glow);
            this.eyeMeshes.push(glow);
        });

        // Jawline & Chin Plate
        const jawGeo = new THREE.CylinderGeometry(8, 4, 8, 6, 2, true);
        const jawMat = new THREE.MeshBasicMaterial({
            color: 0x00d4ff,
            wireframe: true,
            transparent: true,
            opacity: 0.45,
            blending: THREE.AdditiveBlending
        });
        const jaw = new THREE.Mesh(jawGeo, jawMat);
        jaw.position.set(0, -9, 8);
        jaw.rotation.x = 0.2;
        jaw.scale.set(1.1, 0.9, 0.8);
        this.helmetGroup.add(jaw);

        // Vocalizer Audio Waveform Grille (Oscillates when Jarvis speaks)
        this.vocalizerBars = [];
        const barCount = 11;
        for (let i = 0; i < barCount; i++) {
            const barGeo = new THREE.BufferGeometry();
            const x = (i - Math.floor(barCount / 2)) * 0.9;
            const pts = [
                new THREE.Vector3(x, -5.5, 14.2),
                new THREE.Vector3(x, -6.8, 14.2)
            ];
            barGeo.setFromPoints(pts);
            const barMat = new THREE.LineBasicMaterial({
                color: 0x00ffaa,
                transparent: true,
                opacity: 0.85,
                linewidth: 2
            });
            const line = new THREE.Line(barGeo, barMat);
            this.helmetGroup.add(line);
            this.vocalizerBars.push(line);
        }

        // Concentric Orbital Targeting Reticles around Helmet
        this.helmetReticles = [];
        [24, 28, 33].forEach((radius, idx) => {
            const ringGeo = new THREE.RingGeometry(radius - 0.2, radius + 0.2, 48);
            const ringMat = new THREE.MeshBasicMaterial({
                color: idx === 1 ? 0xff9d00 : 0x00d4ff,
                side: THREE.DoubleSide,
                transparent: true,
                opacity: 0.45,
                blending: THREE.AdditiveBlending
            });
            const ring = new THREE.Mesh(ringGeo, ringMat);
            ring.rotation.x = Math.PI / 2 + (idx * 0.25);
            ring.rotation.y = idx * 0.3;
            this.helmetReticles.push({ mesh: ring, speed: (idx % 2 === 0 ? 0.008 : -0.01) });
            this.helmetGroup.add(ring);
        });

        // Helmet Particle Cloud (Floating Nano-Assembly Sparks)
        const nanoCount = 450;
        const nanoGeo = new THREE.BufferGeometry();
        const nanoPos = new Float32Array(nanoCount * 3);
        for (let i = 0; i < nanoCount; i++) {
            const r = 17 + Math.random() * 8;
            const theta = Math.random() * Math.PI * 2;
            const phi = Math.acos((Math.random() * 2) - 1);
            nanoPos[i * 3] = r * Math.sin(phi) * Math.cos(theta);
            nanoPos[i * 3 + 1] = r * Math.sin(phi) * Math.sin(theta);
            nanoPos[i * 3 + 2] = r * Math.cos(phi);
        }
        nanoGeo.setAttribute('position', new THREE.BufferAttribute(nanoPos, 3));
        const nanoMat = new THREE.PointsMaterial({
            color: 0x00d4ff,
            size: 0.9,
            transparent: true,
            opacity: 0.65,
            blending: THREE.AdditiveBlending
        });
        this.nanoParticles = new THREE.Points(nanoGeo, nanoMat);
        this.helmetGroup.add(this.nanoParticles);

        this.scene.add(this.helmetGroup);
    }

    // ==========================================
    // 2. MODE: TACTICAL GLOBAL DEFENSE GRID
    // ==========================================
    buildTacticalGlobe() {
        this.globeGroup = new THREE.Group();
        this.globeGroup.name = 'TACTICAL_GLOBE';

        // Outer Wireframe Longitude / Latitude Sphere
        const sphereGeo = new THREE.SphereGeometry(22, 32, 20);
        const sphereMat = new THREE.MeshBasicMaterial({
            color: 0x004466,
            wireframe: true,
            transparent: true,
            opacity: 0.22
        });
        const globeWire = new THREE.Mesh(sphereGeo, sphereMat);
        this.globeGroup.add(globeWire);

        // Continental Density Point Cloud (Simulated Earth Landmasses)
        const landPointCount = 2800;
        const landGeo = new THREE.BufferGeometry();
        const landPos = new Float32Array(landPointCount * 3);
        const landColors = new Float32Array(landPointCount * 3);
        const c1 = new THREE.Color(0x00d4ff);
        const c2 = new THREE.Color(0x00ffaa);

        for (let i = 0; i < landPointCount; i++) {
            // Biased distribution creating continent clusters
            let lat = (Math.random() - 0.5) * Math.PI * 0.85;
            let lon = (Math.random() - 0.5) * Math.PI * 2;

            // Simple landmass density weighting (Americas, Eurasia, Africa, Australia)
            const r = 22.2 + (Math.random() - 0.5) * 0.4;
            landPos[i * 3] = r * Math.cos(lat) * Math.sin(lon);
            landPos[i * 3 + 1] = r * Math.sin(lat);
            landPos[i * 3 + 2] = r * Math.cos(lat) * Math.cos(lon);

            const mix = Math.random();
            const col = c1.clone().lerp(c2, mix);
            landColors[i * 3] = col.r;
            landColors[i * 3 + 1] = col.g;
            landColors[i * 3 + 2] = col.b;
        }

        landGeo.setAttribute('position', new THREE.BufferAttribute(landPos, 3));
        landGeo.setAttribute('color', new THREE.BufferAttribute(landColors, 3));
        const landMat = new THREE.PointsMaterial({
            size: 1.1,
            vertexColors: true,
            transparent: true,
            opacity: 0.85,
            blending: THREE.AdditiveBlending
        });
        this.continentPoints = new THREE.Points(landGeo, landMat);
        this.globeGroup.add(this.continentPoints);

        // Equatorial Tactical Defense Ring
        const eqGeo = new THREE.RingGeometry(25, 25.4, 64);
        const eqMat = new THREE.MeshBasicMaterial({
            color: 0x00d4ff,
            side: THREE.DoubleSide,
            transparent: true,
            opacity: 0.6
        });
        const eqRing = new THREE.Mesh(eqGeo, eqMat);
        eqRing.rotation.x = Math.PI / 2;
        this.globeGroup.add(eqRing);

        // Veronica Orbital Defense Satellite (Stark Deployable Platform)
        this.satellites = [];
        const satGeo = new THREE.BoxGeometry(1.6, 0.8, 1.2);
        const satMat = new THREE.MeshBasicMaterial({
            color: 0xffd700,
            wireframe: true
        });
        this.veronicaSat = new THREE.Mesh(satGeo, satMat);
        this.globeGroup.add(this.veronicaSat);
        this.satellites.push({ mesh: this.veronicaSat, distance: 34, speed: 0.015, angle: 0, tilt: 0.45 });

        // Satellite Laser Targeting Vector (Pointing to target on Earth)
        const laserGeo = new THREE.BufferGeometry();
        const laserPts = [new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 0, 0)];
        laserGeo.setFromPoints(laserPts);
        const laserMat = new THREE.LineBasicMaterial({
            color: 0xff3344,
            transparent: true,
            opacity: 0.85,
            blending: THREE.AdditiveBlending
        });
        this.satLaser = new THREE.Line(laserGeo, laserMat);
        this.globeGroup.add(this.satLaser);

        // Ground Target Reticle Ping (Blinking Beacon)
        const targetGeo = new THREE.RingGeometry(0.8, 1.4, 16);
        const targetMat = new THREE.MeshBasicMaterial({
            color: 0xff3344,
            side: THREE.DoubleSide,
            transparent: true,
            opacity: 0.9,
            blending: THREE.AdditiveBlending
        });
        this.targetReticle = new THREE.Mesh(targetGeo, targetMat);
        this.targetReticle.position.set(12, 14, 12);
        this.targetReticle.lookAt(0, 0, 0);
        this.globeGroup.add(this.targetReticle);

        this.scene.add(this.globeGroup);
    }

    // ==========================================
    // 3. MODE: DUAL NEURAL CORE & ARC REACTOR
    // ==========================================
    buildNeuralAndArcCore() {
        this.dualCoreGroup = new THREE.Group();
        this.dualCoreGroup.name = 'NEURAL_CORE';

        // 1. Jarvis Neural Core (Right Position)
        this.jarvisCoreGroup = new THREE.Group();
        this.jarvisCoreGroup.position.set(18, 0, 0);

        const icoGeo = new THREE.IcosahedronGeometry(18, 5);
        this.originalIcoVertices = icoGeo.attributes.position.clone();
        const icoMat = new THREE.MeshBasicMaterial({
            color: 0x00d4ff,
            wireframe: true,
            transparent: true,
            opacity: 0.35,
            blending: THREE.AdditiveBlending
        });
        this.neuralMesh = new THREE.Mesh(icoGeo, icoMat);
        this.jarvisCoreGroup.add(this.neuralMesh);

        // Synaptic Particle Cloud
        const ptCount = 1800;
        const ptGeo = new THREE.BufferGeometry();
        const ptPos = new Float32Array(ptCount * 3);
        const ptCols = new Float32Array(ptCount * 3);
        const cBlue = new THREE.Color(0x00d4ff);
        const cWhite = new THREE.Color(0xffffff);

        for (let i = 0; i < ptCount; i++) {
            const r = 16 + (Math.random() - 0.5) * 6;
            const theta = Math.random() * Math.PI * 2;
            const phi = Math.acos((Math.random() * 2) - 1);
            ptPos[i * 3] = r * Math.sin(phi) * Math.cos(theta);
            ptPos[i * 3 + 1] = r * Math.sin(phi) * Math.sin(theta);
            ptPos[i * 3 + 2] = r * Math.cos(phi);

            const m = cBlue.clone().lerp(cWhite, Math.random() * 0.4);
            ptCols[i * 3] = m.r;
            ptCols[i * 3 + 1] = m.g;
            ptCols[i * 3 + 2] = m.b;
        }
        ptGeo.setAttribute('position', new THREE.BufferAttribute(ptPos, 3));
        ptGeo.setAttribute('color', new THREE.BufferAttribute(ptCols, 3));
        const ptMat = new THREE.PointsMaterial({
            size: 1.2,
            vertexColors: true,
            transparent: true,
            opacity: 0.85,
            blending: THREE.AdditiveBlending
        });
        this.neuralPoints = new THREE.Points(ptGeo, ptMat);
        this.jarvisCoreGroup.add(this.neuralPoints);

        const nucleusGeo = new THREE.SphereGeometry(6, 32, 32);
        const nucleusMat = new THREE.MeshBasicMaterial({
            color: 0x80ecff,
            transparent: true,
            opacity: 0.65,
            blending: THREE.AdditiveBlending
        });
        this.jarvisCoreGroup.add(new THREE.Mesh(nucleusGeo, nucleusMat));
        this.dualCoreGroup.add(this.jarvisCoreGroup);

        // 2. Arc Reactor Orbital Core (Left Position)
        this.arcReactorGroup = new THREE.Group();
        this.arcReactorGroup.position.set(-22, 0, 0);

        this.orbitalRings = [];
        const ringRadii = [12, 16, 20, 24];
        ringRadii.forEach((radius, idx) => {
            const ringGeo = new THREE.RingGeometry(radius - 0.2, radius + 0.2, 48);
            const ringMat = new THREE.MeshBasicMaterial({
                color: idx % 2 === 0 ? 0xff9d00 : 0xffcc00,
                side: THREE.DoubleSide,
                transparent: true,
                opacity: 0.65,
                blending: THREE.AdditiveBlending
            });
            const ringMesh = new THREE.Mesh(ringGeo, ringMat);
            ringMesh.rotation.x = Math.PI / 2 + (idx * 0.3);
            ringMesh.rotation.y = idx * 0.4;
            this.orbitalRings.push({ mesh: ringMesh, speedX: 0.007 * (idx + 1), speedY: 0.009 * (4 - idx) });
            this.arcReactorGroup.add(ringMesh);
        });

        const arcMeshGeo = new THREE.IcosahedronGeometry(7, 3);
        const arcMeshMat = new THREE.MeshBasicMaterial({
            color: 0xffa500,
            wireframe: true,
            transparent: true,
            opacity: 0.8,
            blending: THREE.AdditiveBlending
        });
        this.arcReactorGroup.add(new THREE.Mesh(arcMeshGeo, arcMeshMat));

        const flareGeo = new THREE.SphereGeometry(4, 32, 32);
        const flareMat = new THREE.MeshBasicMaterial({
            color: 0xffffff,
            transparent: true,
            opacity: 0.9,
            blending: THREE.AdditiveBlending
        });
        this.arcFlare = new THREE.Mesh(flareGeo, flareMat);
        this.arcReactorGroup.add(this.arcFlare);

        this.dualCoreGroup.add(this.arcReactorGroup);
        this.scene.add(this.dualCoreGroup);
    }

    // ==========================================
    // 4. MODE: QUANTUM VORTEX ACCELERATOR
    // ==========================================
    buildQuantumVortex() {
        this.quantumVortexGroup = new THREE.Group();
        this.quantumVortexGroup.name = 'QUANTUM_VORTEX';

        // Torus Knot Core Wireframe
        const knotGeo = new THREE.TorusKnotGeometry(14, 3.8, 128, 24, 2, 3);
        const knotMat = new THREE.MeshBasicMaterial({
            color: 0x00d4ff,
            wireframe: true,
            transparent: true,
            opacity: 0.45,
            blending: THREE.AdditiveBlending
        });
        this.vortexMesh = new THREE.Mesh(knotGeo, knotMat);
        this.quantumVortexGroup.add(this.vortexMesh);

        // Double-Helix Quantum Particle Accelerator Stream
        const streamCount = 2000;
        const streamGeo = new THREE.BufferGeometry();
        const streamPos = new Float32Array(streamCount * 3);
        const streamCols = new Float32Array(streamCount * 3);
        const cCyan = new THREE.Color(0x00d4ff);
        const cGold = new THREE.Color(0xff9d00);

        for (let i = 0; i < streamCount; i++) {
            const strand = i % 2 === 0 ? 1 : -1;
            const t = (i / streamCount) * Math.PI * 12;
            const radius = 18 + Math.sin(t * 0.5) * 5;
            const y = ((i / streamCount) - 0.5) * 60;

            streamPos[i * 3] = radius * Math.cos(t) * strand;
            streamPos[i * 3 + 1] = y;
            streamPos[i * 3 + 2] = radius * Math.sin(t) * strand;

            const col = (strand === 1 ? cCyan : cGold).clone();
            streamCols[i * 3] = col.r;
            streamCols[i * 3 + 1] = col.g;
            streamCols[i * 3 + 2] = col.b;
        }

        streamGeo.setAttribute('position', new THREE.BufferAttribute(streamPos, 3));
        streamGeo.setAttribute('color', new THREE.BufferAttribute(streamCols, 3));
        const streamMat = new THREE.PointsMaterial({
            size: 1.3,
            vertexColors: true,
            transparent: true,
            opacity: 0.85,
            blending: THREE.AdditiveBlending
        });
        this.streamParticles = new THREE.Points(streamGeo, streamMat);
        this.quantumVortexGroup.add(this.streamParticles);

        this.scene.add(this.quantumVortexGroup);
    }

    // ==========================================
    // 5. MODE: 3D PROJECT BLUEPRINT MATRIX
    // ==========================================
    buildProjectBlueprintMatrix() {
        this.projectMatrixGroup = new THREE.Group();
        this.projectMatrixGroup.name = 'PROJECT_MATRIX';
        this.matrixNodes = [];
        this.matrixPacketData = [];

        // Project Node Definitions
        const nodeDefs = [
            { id: 'p1', name: 'MARK LXXXV ARMOR', pos: new THREE.Vector3(0, 4, 0), color: 0x00d4ff, scale: 5.5, geoType: 'ico' },
            { id: 'p2', name: 'VERONICA ORBITAL', pos: new THREE.Vector3(-26, 14, 8), color: 0xff9d00, scale: 4.0, geoType: 'octa' },
            { id: 'p3', name: 'QUANTUM TUNNEL', pos: new THREE.Vector3(26, 12, -6), color: 0x00ffcc, scale: 4.2, geoType: 'dodeca' },
            { id: 'p4', name: 'GHOST DRIVE HUB', pos: new THREE.Vector3(-18, -16, 10), color: 0x00aaff, scale: 3.8, geoType: 'box' },
            { id: 'p5', name: 'RESEARCH AGENT CLUSTER', pos: new THREE.Vector3(20, -14, 12), color: 0xff0055, scale: 3.8, geoType: 'octa' },
            { id: 'p6', name: 'AI DIRECTIVE MATRIX', pos: new THREE.Vector3(0, -22, -10), color: 0xffd700, scale: 3.5, geoType: 'ico' }
        ];

        // Inter-project dependency links
        const links = [
            ['p1', 'p2'],
            ['p1', 'p3'],
            ['p1', 'p4'],
            ['p1', 'p5'],
            ['p4', 'p6'],
            ['p5', 'p6'],
            ['p2', 'p3']
        ];

        // Build each node
        const nodeMap = {};
        nodeDefs.forEach(def => {
            const nodeGrp = new THREE.Group();
            nodeGrp.position.copy(def.pos);

            let geo;
            if (def.geoType === 'ico') geo = new THREE.IcosahedronGeometry(def.scale, 1);
            else if (def.geoType === 'octa') geo = new THREE.OctahedronGeometry(def.scale, 0);
            else if (def.geoType === 'dodeca') geo = new THREE.DodecahedronGeometry(def.scale, 0);
            else geo = new THREE.BoxGeometry(def.scale * 1.4, def.scale * 1.4, def.scale * 1.4);

            const mat = new THREE.MeshBasicMaterial({
                color: def.color,
                wireframe: true,
                transparent: true,
                opacity: 0.75,
                blending: THREE.AdditiveBlending
            });
            const mesh = new THREE.Mesh(geo, mat);
            nodeGrp.add(mesh);

            // Pulsating internal core sphere
            const nucGeo = new THREE.SphereGeometry(def.scale * 0.45, 16, 16);
            const nucMat = new THREE.MeshBasicMaterial({
                color: 0xffffff,
                transparent: true,
                opacity: 0.85,
                blending: THREE.AdditiveBlending
            });
            const nucleus = new THREE.Mesh(nucGeo, nucMat);
            nodeGrp.add(nucleus);

            // Concentric orbit rings
            const rings = [];
            for (let r = 0; r < 2; r++) {
                const ringRad = def.scale * (1.5 + r * 0.4);
                const ringGeo = new THREE.RingGeometry(ringRad - 0.15, ringRad + 0.15, 32);
                const ringMat = new THREE.MeshBasicMaterial({
                    color: def.color,
                    side: THREE.DoubleSide,
                    transparent: true,
                    opacity: 0.5,
                    blending: THREE.AdditiveBlending
                });
                const rMesh = new THREE.Mesh(ringGeo, ringMat);
                rMesh.rotation.x = Math.PI / 2 + (r * 0.4);
                rMesh.rotation.y = r * 0.6;
                nodeGrp.add(rMesh);
                rings.push({ mesh: rMesh, speed: (r % 2 === 0 ? 0.02 : -0.015) });
            }

            this.projectMatrixGroup.add(nodeGrp);
            const nodeObj = { id: def.id, group: nodeGrp, mesh, nucleus, rings, pos: def.pos, color: def.color };
            this.matrixNodes.push(nodeObj);
            nodeMap[def.id] = nodeObj;
        });

        // Build glowing vector lines between linked nodes
        const linePositions = [];
        const lineColors = [];
        links.forEach(([fromId, toId]) => {
            const from = nodeMap[fromId];
            const to = nodeMap[toId];
            if (from && to) {
                linePositions.push(from.pos.x, from.pos.y, from.pos.z);
                linePositions.push(to.pos.x, to.pos.y, to.pos.z);

                const c1 = new THREE.Color(from.color);
                const c2 = new THREE.Color(to.color);
                lineColors.push(c1.r, c1.g, c1.b);
                lineColors.push(c2.r, c2.g, c2.b);

                // Add data packets
                for (let k = 0; k < 3; k++) {
                    this.matrixPacketData.push({
                        start: from.pos,
                        end: to.pos,
                        t: Math.random(),
                        speed: 0.005 + Math.random() * 0.008,
                        color: from.color
                    });
                }
            }
        });

        const lineGeo = new THREE.BufferGeometry();
        lineGeo.setAttribute('position', new THREE.Float32BufferAttribute(linePositions, 3));
        lineGeo.setAttribute('color', new THREE.Float32BufferAttribute(lineColors, 3));
        const lineMat = new THREE.LineBasicMaterial({
            vertexColors: true,
            transparent: true,
            opacity: 0.55,
            blending: THREE.AdditiveBlending
        });
        const lineSegments = new THREE.LineSegments(lineGeo, lineMat);
        this.projectMatrixGroup.add(lineSegments);

        // Animated Data Packets
        const packetCount = this.matrixPacketData.length;
        const packetGeo = new THREE.BufferGeometry();
        const packetPos = new Float32Array(packetCount * 3);
        const packetCols = new Float32Array(packetCount * 3);

        for (let i = 0; i < packetCount; i++) {
            const p = this.matrixPacketData[i];
            const cur = p.start.clone().lerp(p.end, p.t);
            packetPos[i * 3] = cur.x;
            packetPos[i * 3 + 1] = cur.y;
            packetPos[i * 3 + 2] = cur.z;

            const c = new THREE.Color(p.color);
            packetCols[i * 3] = c.r;
            packetCols[i * 3 + 1] = c.g;
            packetCols[i * 3 + 2] = c.b;
        }

        packetGeo.setAttribute('position', new THREE.BufferAttribute(packetPos, 3));
        packetGeo.setAttribute('color', new THREE.BufferAttribute(packetCols, 3));
        const packetMat = new THREE.PointsMaterial({
            size: 2.2,
            vertexColors: true,
            transparent: true,
            opacity: 0.95,
            blending: THREE.AdditiveBlending
        });
        this.matrixPackets = new THREE.Points(packetGeo, packetMat);
        this.projectMatrixGroup.add(this.matrixPackets);

        // Blueprint Floor Reticle
        const blueprintGrid = new THREE.PolarGridHelper(48, 16, 8, 64, 0x00d4ff, 0x004466);
        blueprintGrid.position.y = -30;
        blueprintGrid.material.opacity = 0.4;
        blueprintGrid.material.transparent = true;
        this.projectMatrixGroup.add(blueprintGrid);

        this.scene.add(this.projectMatrixGroup);
    }

    // ==========================================
    // BACKGROUND & ENVIRONMENT
    // ==========================================
    buildFloatingStarfield() {
        const count = 1600;
        const geo = new THREE.BufferGeometry();
        const pos = new Float32Array(count * 3);

        for (let i = 0; i < count; i++) {
            pos[i * 3] = (Math.random() - 0.5) * 450;
            pos[i * 3 + 1] = (Math.random() - 0.5) * 280;
            pos[i * 3 + 2] = (Math.random() - 0.5) * 350;
        }

        geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
        const mat = new THREE.PointsMaterial({
            color: 0x00d4ff,
            size: 0.9,
            transparent: true,
            opacity: 0.45,
            blending: THREE.AdditiveBlending
        });
        this.starfield = new THREE.Points(geo, mat);
        this.scene.add(this.starfield);
    }

    buildRadarFloorGrid() {
        this.gridFloor = new THREE.GridHelper(180, 45, 0x00d4ff, 0x02253d);
        this.gridFloor.position.y = -38;
        this.gridFloor.material.opacity = 0.28;
        this.gridFloor.material.transparent = true;
        this.scene.add(this.gridFloor);
    }

    // ==========================================
    // 6. MODE: 3D CAD & MESH VIEWER (.OBJ / .STL)
    // ==========================================
    buildCadModelGroup() {
        this.cadModelGroup = new THREE.Group();
        this.cadModelGroup.name = 'CAD_MODEL';
        this.cadModelGroup.visible = false;

        // Default initial CAD placeholder: Arc Reactor Plasma Torus
        const defaultGeo = new THREE.TorusGeometry(12, 3.5, 16, 100);
        const defaultMat = new THREE.MeshBasicMaterial({
            color: 0x00d4ff,
            wireframe: true,
            transparent: true,
            opacity: 0.65,
            blending: THREE.AdditiveBlending
        });
        this.currentCadMesh = new THREE.Mesh(defaultGeo, defaultMat);
        this.cadModelGroup.add(this.currentCadMesh);

        // Caliper Reticle Ring
        const caliperGeo = new THREE.RingGeometry(18, 18.5, 64);
        const caliperMat = new THREE.MeshBasicMaterial({
            color: 0xff9800,
            wireframe: true,
            transparent: true,
            opacity: 0.4
        });
        const caliper = new THREE.Mesh(caliperGeo, caliperMat);
        this.cadModelGroup.add(caliper);

        this.scene.add(this.cadModelGroup);
    }

    loadObjGeometry(objText) {
        const vertices = [];
        const positions = [];
        const lines = objText.split('\n');

        for (const line of lines) {
            const trimmed = line.trim();
            if (trimmed.startsWith('v ')) {
                const parts = trimmed.split(/\s+/).slice(1).map(Number);
                vertices.push(new THREE.Vector3(parts[0], parts[1], parts[2]));
            } else if (trimmed.startsWith('f ')) {
                const indices = trimmed.split(/\s+/).slice(1).map(p => parseInt(p.split('/')[0]) - 1);
                if (indices.length >= 3) {
                    for (let i = 1; i < indices.length - 1; i++) {
                        const v0 = vertices[indices[0]];
                        const v1 = vertices[indices[i]];
                        const v2 = vertices[indices[i + 1]];
                        if (v0 && v1 && v2) {
                            positions.push(v0.x, v0.y, v0.z, v1.x, v1.y, v1.z, v2.x, v2.y, v2.z);
                        }
                    }
                }
            }
        }

        const geo = new THREE.BufferGeometry();
        geo.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        geo.computeVertexNormals();
        geo.center();
        return geo;
    }

    displayCadMesh(geometry, name = "Custom CAD Schematic") {
        if (!geometry || !this.cadModelGroup) return;
        
        while (this.cadModelGroup.children.length > 0) {
            const child = this.cadModelGroup.children[0];
            this.cadModelGroup.remove(child);
            if (child.geometry) child.geometry.dispose();
        }

        const mat = new THREE.MeshBasicMaterial({
            color: this.activeThemeColor,
            wireframe: true,
            transparent: true,
            opacity: 0.75,
            blending: THREE.AdditiveBlending
        });

        this.currentCadMesh = new THREE.Mesh(geometry, mat);
        geometry.computeBoundingSphere();
        const radius = geometry.boundingSphere ? geometry.boundingSphere.radius : 15;
        const scale = 22 / (radius || 1);
        this.currentCadMesh.scale.set(scale, scale, scale);

        this.cadModelGroup.add(this.currentCadMesh);
        this.setHologramMode('CAD_MODEL');
    }

    // ==========================================
    // INTERACTION & CONTROL
    // ==========================================
    setHologramMode(modeName) {
        this.activeMode = modeName;
        
        if (this.helmetGroup) this.helmetGroup.visible = (modeName === 'MARK_HELMET');
        if (this.globeGroup) this.globeGroup.visible = (modeName === 'TACTICAL_GLOBE');
        if (this.dualCoreGroup) this.dualCoreGroup.visible = (modeName === 'NEURAL_CORE');
        if (this.quantumVortexGroup) this.quantumVortexGroup.visible = (modeName === 'QUANTUM_VORTEX');
        if (this.projectMatrixGroup) this.projectMatrixGroup.visible = (modeName === 'PROJECT_MATRIX');
        if (this.cadModelGroup) this.cadModelGroup.visible = (modeName === 'CAD_MODEL');

        // Play Stark sound chime if available
        if (window.sfx) window.sfx.playHudBeep(720, 0.05);

        // Update active class on on-screen mode selector buttons
        document.querySelectorAll('.holo-mode-pill').forEach(btn => {
            if (btn.getAttribute('data-mode') === modeName) {
                btn.classList.add('active');
            } else {
                btn.classList.remove('active');
            }
        });
    }

    onCanvasClick(e) {
        // Spawn 3D Shockwave ring at click coordinates
        this.mouseVec.x = (e.clientX / window.innerWidth) * 2 - 1;
        this.mouseVec.y = -(e.clientY / window.innerHeight) * 2 + 1;

        this.raycaster.setFromCamera(this.mouseVec, this.camera);
        const intersectPoint = this.raycaster.ray.at(90, new THREE.Vector3());

        const ringGeo = new THREE.RingGeometry(0.5, 1.2, 32);
        const ringMat = new THREE.MeshBasicMaterial({
            color: this.activeThemeColor,
            side: THREE.DoubleSide,
            transparent: true,
            opacity: 0.9,
            blending: THREE.AdditiveBlending
        });
        const wave = new THREE.Mesh(ringGeo, ringMat);
        wave.position.copy(intersectPoint);
        wave.lookAt(this.camera.position);

        this.scene.add(wave);
        this.shockwaves.push({ mesh: wave, scale: 1.0, maxScale: 40.0, opacity: 0.9 });

        if (window.sfx) window.sfx.playHudBeep(880, 0.04);
    }

    setAudioRms(rms) {
        this.audioRms = Math.min(1.0, rms * 2.8);
    }

    setTheme(themeName) {
        let primary = 0x00d4ff;
        let secondary = 0xff9d00;

        if (themeName === 'ArcOrange') {
            primary = 0xff9d00;
            secondary = 0x00d4ff;
        } else if (themeName === 'MarkGold') {
            primary = 0xffd700;
            secondary = 0xff3344;
        } else if (themeName === 'UltronCrimson') {
            primary = 0xff2233;
            secondary = 0xff9d00;
        } else if (themeName === 'StealthGreen') {
            primary = 0x39ff14;
            secondary = 0x00d4ff;
        }

        this.activeThemeColor = primary;
        this.secondaryColor = secondary;

        if (this.keyLight) this.keyLight.color.setHex(primary);
        if (this.fillLight) this.fillLight.color.setHex(secondary);
        if (this.starfield) this.starfield.material.color.setHex(primary);
        if (this.vortexMesh) this.vortexMesh.material.color.setHex(primary);
    }

    onMouseMove(e) {
        this.mouseX = (e.clientX - window.innerWidth / 2) * 0.0006;
        this.mouseY = (e.clientY - window.innerHeight / 2) * 0.0006;
    }

    onWindowResize() {
        if (!this.camera || !this.renderer) return;
        const width = window.innerWidth;
        const height = window.innerHeight;
        this.camera.aspect = width / height;
        this.camera.updateProjectionMatrix();
        this.renderer.setSize(width, height);
    }

    // ==========================================
    // MAIN ANIMATION LOOP
    // ==========================================
    animate() {
        requestAnimationFrame(() => this.animate());

        const elapsedTime = this.clock.getElapsedTime();
        const audioBoost = 1.0 + (this.audioRms * 2.2);

        // Smooth camera parallax
        this.targetRotationY += (this.mouseX * 16 - this.targetRotationY) * 0.06;
        this.targetRotationX += (this.mouseY * 16 - this.targetRotationX) * 0.06;
        this.camera.position.x = this.targetRotationY;
        this.camera.position.y = -this.targetRotationX;
        this.camera.lookAt(0, 0, 0);

        // 1. ANIMATE MARK HELMET
        if (this.helmetGroup && this.helmetGroup.visible) {
            // Gentle hovering breathing motion
            this.helmetGroup.position.y = Math.sin(elapsedTime * 1.5) * 1.2;
            this.helmetGroup.rotation.y = Math.sin(elapsedTime * 0.4) * 0.15 + (this.mouseX * 2);
            this.helmetGroup.rotation.x = Math.sin(elapsedTime * 0.6) * 0.05 + (this.mouseY * 2);

            // Audio-reactive Eye Slits glow
            if (this.eyeMeshes) {
                const eyePulse = 0.75 + (this.audioRms * 0.35) + Math.sin(elapsedTime * 8) * 0.08;
                this.eyeMeshes.forEach(eye => {
                    eye.material.opacity = Math.min(1.0, eyePulse);
                });
            }

            // Audio-reactive vocalizer equalizer bars
            if (this.vocalizerBars) {
                this.vocalizerBars.forEach((bar, idx) => {
                    const wave = (Math.sin(elapsedTime * 14 + idx * 0.8) * 0.5 + 0.5) * (this.audioRms * 2.5 + 0.2);
                    bar.scale.y = Math.max(0.2, wave);
                });
            }

            // Orbital helmet reticle rotation
            if (this.helmetReticles) {
                this.helmetReticles.forEach(reticle => {
                    reticle.mesh.rotation.z += reticle.speed * audioBoost;
                });
            }

            // Nano particle cloud swirling
            if (this.nanoParticles) {
                this.nanoParticles.rotation.y = elapsedTime * 0.12;
            }
        }

        // 2. ANIMATE TACTICAL GLOBE
        if (this.globeGroup && this.globeGroup.visible) {
            this.globeGroup.rotation.y = elapsedTime * 0.08;

            // Veronica Orbit
            if (this.satellites && this.satellites.length > 0) {
                const sat = this.satellites[0];
                sat.angle += sat.speed * audioBoost;
                const x = sat.distance * Math.cos(sat.angle);
                const z = sat.distance * Math.sin(sat.angle);
                const y = Math.sin(sat.angle * 2) * (sat.distance * 0.35);
                sat.mesh.position.set(x, y, z);
                sat.mesh.rotation.y += 0.02;

                // Laser Vector connecting Veronica to Earth ground target
                if (this.satLaser && this.targetReticle) {
                    const targetWorldPos = new THREE.Vector3();
                    this.targetReticle.getWorldPosition(targetWorldPos);
                    const pts = [sat.mesh.position, targetWorldPos];
                    this.satLaser.geometry.setFromPoints(pts);
                }
            }

            // Target Ping Pulse
            if (this.targetReticle) {
                const tScale = 1.0 + (Math.sin(elapsedTime * 6) * 0.4) + (this.audioRms * 0.5);
                this.targetReticle.scale.set(tScale, tScale, tScale);
            }
        }

        // 3. ANIMATE NEURAL CORE & ARC REACTOR
        if (this.dualCoreGroup && this.dualCoreGroup.visible) {
            if (this.jarvisCoreGroup) {
                this.jarvisCoreGroup.rotation.y = elapsedTime * 0.25;
                this.jarvisCoreGroup.rotation.z = Math.sin(elapsedTime * 0.3) * 0.15;

                // Audio vertex oscillation
                if (this.neuralMesh && this.originalIcoVertices) {
                    const pos = this.neuralMesh.geometry.attributes.position;
                    const orig = this.originalIcoVertices;
                    for (let i = 0; i < pos.count; i++) {
                        const u = orig.getX(i);
                        const v = orig.getY(i);
                        const w = orig.getZ(i);
                        const wave = Math.sin(elapsedTime * 4.0 + (u * 0.3) + (v * 0.3)) * (1.4 * this.audioRms);
                        pos.setXYZ(i, u + wave, v + wave, w + wave);
                    }
                    pos.needsUpdate = true;
                }

                const s = 1.0 + (this.audioRms * 0.22);
                this.jarvisCoreGroup.scale.set(s, s, s);
            }

            if (this.arcReactorGroup) {
                this.arcReactorGroup.rotation.y = -elapsedTime * 0.18;
                this.orbitalRings.forEach(ring => {
                    ring.mesh.rotation.x += ring.speedX * audioBoost;
                    ring.mesh.rotation.y += ring.speedY * audioBoost;
                });
                if (this.arcFlare) {
                    const flareScale = 1.0 + Math.sin(elapsedTime * 6) * 0.18 + (this.audioRms * 0.7);
                    this.arcFlare.scale.set(flareScale, flareScale, flareScale);
                }
            }
        }

        // 4. ANIMATE QUANTUM VORTEX
        if (this.quantumVortexGroup && this.quantumVortexGroup.visible) {
            if (this.vortexMesh) {
                this.vortexMesh.rotation.x = elapsedTime * 0.25 * audioBoost;
                this.vortexMesh.rotation.y = elapsedTime * 0.45 * audioBoost;
            }
            if (this.streamParticles) {
                this.streamParticles.rotation.y = -elapsedTime * 0.6 * audioBoost;
            }
        }

        // 5. ANIMATE PROJECT BLUEPRINT MATRIX
        if (this.projectMatrixGroup && this.projectMatrixGroup.visible) {
            this.projectMatrixGroup.rotation.y = elapsedTime * 0.09;
            if (this.matrixNodes) {
                this.matrixNodes.forEach((node, idx) => {
                    node.mesh.rotation.x += 0.01 * (idx % 2 === 0 ? 1 : -1);
                    node.mesh.rotation.y += 0.015;
                    if (node.rings) {
                        node.rings.forEach(r => {
                            r.mesh.rotation.z += r.speed * audioBoost;
                        });
                    }
                    if (node.nucleus) {
                        const s = 1.0 + Math.sin(elapsedTime * 4 + idx) * 0.15 + (this.audioRms * 0.4);
                        node.nucleus.scale.set(s, s, s);
                    }
                });
            }
            if (this.matrixPackets && this.matrixPacketData) {
                const pos = this.matrixPackets.geometry.attributes.position;
                const pts = pos.array;
                for (let i = 0; i < this.matrixPacketData.length; i++) {
                    const p = this.matrixPacketData[i];
                    p.t = (p.t + p.speed * audioBoost) % 1.0;
                    const cur = p.start.clone().lerp(p.end, p.t);
                    pts[i * 3] = cur.x;
                    pts[i * 3 + 1] = cur.y;
                    pts[i * 3 + 2] = cur.z;
                }
                pos.needsUpdate = true;
            }
        }

        // 6. ANIMATE CAD MODEL VIEWER
        if (this.cadModelGroup && this.cadModelGroup.visible) {
            this.cadModelGroup.rotation.y = elapsedTime * 0.15;
            this.cadModelGroup.rotation.x = Math.sin(elapsedTime * 0.5) * 0.08;
        }

        // 5. ANIMATE SHOCKWAVE RIPPLES
        for (let i = this.shockwaves.length - 1; i >= 0; i--) {
            const sw = this.shockwaves[i];
            sw.scale += 1.6;
            sw.opacity -= 0.035;
            sw.mesh.scale.set(sw.scale, sw.scale, 1);
            sw.mesh.material.opacity = Math.max(0, sw.opacity);

            if (sw.opacity <= 0 || sw.scale >= sw.maxScale) {
                this.scene.remove(sw.mesh);
                sw.mesh.geometry.dispose();
                sw.mesh.material.dispose();
                this.shockwaves.splice(i, 1);
            }
        }

        // Starfield gentle drift
        if (this.starfield) {
            this.starfield.rotation.y = elapsedTime * 0.015;
        }

        this.renderer.render(this.scene, this.camera);
    }
}

window.JarvisHologram3D = JarvisHologram3D;

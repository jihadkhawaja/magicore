/**
 * MagiCore Graph Memory Visualizer
 * High-Performance Vanilla JS Graph Engine & Dynamic Results Loader
 */

(function () {
  'use strict';

  // --- Entity & Category Color Maps ---
  const COLORS = {
    person: '#ec4899',
    location: '#f59e0b',
    org: '#a855f7',
    organization: '#a855f7',
    habit: '#10b981',
    diet: '#f97316',
    event: '#06b6d4',
    pet: '#eab308',
    concept: '#14b8a6',
    memory: '#6366f1',
    question: '#38bdf8',
    session: '#94a3b8',
    correct: '#10b981',
    incorrect: '#ef4444',
    'single-hop': '#38bdf8',
    'multi-hop': '#a855f7',
    temporal: '#f59e0b',
    contradiction: '#ec4899',
    adversarial: '#94a3b8'
  };

  const CLUSTER_CENTERS = {
    northstar: { x: -420, y: -260, label: 'NORTHSTAR ENTERPRISE ROLLOUT', color: 'rgba(56, 189, 248, 0.15)', borderColor: 'rgba(56, 189, 248, 0.25)' },
    care: { x: 420, y: -260, label: 'FAMILY CARE COORDINATION', color: 'rgba(236, 72, 153, 0.15)', borderColor: 'rgba(236, 72, 153, 0.25)' },
    household: { x: -420, y: 260, label: 'HOUSEHOLD & TRAVEL PLANNING', color: 'rgba(245, 158, 11, 0.15)', borderColor: 'rgba(245, 158, 11, 0.25)' },
    cafe: { x: 420, y: 260, label: 'NEIGHBORHOOD CAFE OPERATIONS', color: 'rgba(168, 85, 247, 0.15)', borderColor: 'rgba(168, 85, 247, 0.25)' }
  };

  const DOMAIN_KEYWORDS = {
    northstar: ['northstar', 'priya', 'sam', 'dana', 'cho', 'morgan', 'luis', 'ortega', 'nsh-417', 'sso', 'residency', 'audit', 'export', 'eng-8821', 'ns-'],
    care: ['care', 'elena', 'roberto', 'alvarez', 'mehta', 'lakeside', 'penicillin', 'cityride', 'cr-9082', 'quickcab', 'harbor pharmacy', 'pine avenue', 'parcel room', 'care-'],
    household: ['home', 'household', 'marcus', 'jo', 'eli', 'avery', 'valencia', 'malvarrosa', 'madeira', 'cashew', 'allergy', 'blue finch', 'noor', 'patel', 'lockbox', 'home-'],
    cafe: ['cafe', 'cedar', 'steam', 'nia', 'owen', 'mina', 'shah', 'willow', 'market', 'grinder', 'roastline', 'espresso', 'oat milk', 'cafe-']
  };

  // --- Available Benchmark Evaluation Run Paths ---
  const AVAILABLE_RUNS = [
    {
      id: 'evaluation-20260830-203014',
      label: '2026-08-30 20:30 UTC (Authoritative 12 Scenarios Benchmark)',
      path: '../results/evaluation-20260830-203014.json'
    }
  ];

  // --- Extracted Domain Ontology ---
  const ONTOLOGY = {
    entities: [
      { id: 'ent-northstar', name: 'Northstar Health', type: 'Organization', description: 'Tenant NSH-417, 2,400 staff, EU West Europe residency, SAML SSO, 7-year audit retention.' },
      { id: 'ent-priya', name: 'Priya', type: 'Person', description: 'Project director and rollout co-lead for Northstar Health.' },
      { id: 'ent-sam', name: 'Sam', type: 'Person', description: 'Technical operations lead managing rollout milestones.' },
      { id: 'ent-dana', name: 'Dana Cho', type: 'Person', description: 'Operational rollout owner at Northstar Health.' },
      { id: 'ent-morgan', name: 'Morgan Li', type: 'Person', description: 'Billing contact replacing Dan Cole on monthly invoices.' },
      { id: 'ent-luis', name: 'Luis Ortega', type: 'Person', description: 'Security analyst reviewing weekly audit-log exports.' },
      { id: 'ent-eng8821', name: 'Fix ENG-8821', type: 'Event', description: 'Engineering fix resolving 1.2GB large export archive timeouts.' },
      { id: 'ent-elena', name: 'Elena', type: 'Person', description: "Daughter and care coordinator managing Roberto's medical appointments." },
      { id: 'ent-roberto', name: 'Roberto Alvarez', type: 'Person', description: 'Father and patient, prefers Spanish for clinical conversations, allergic to penicillin.' },
      { id: 'ent-dr-mehta', name: 'Dr. Mehta', type: 'Person', description: 'Cardiologist at Lakeside Heart clinic.' },
      { id: 'ent-penicillin', name: 'Penicillin Allergy', type: 'Concept', description: 'Prominent patient medical allergy constraint.' },
      { id: 'ent-cityride', name: 'CityRide Transport', type: 'Organization', description: 'Approved backup transportation company (booking CR-9082); QuickCab prohibited.' },
      { id: 'ent-harbor-pharmacy', name: 'Harbor Pharmacy', type: 'Organization', description: 'Pine Avenue pharmacy delivering Wednesdays to parcel room (not Harbor Drugs).' },
      { id: 'ent-marcus', name: 'Marcus', type: 'Person', description: 'Family travel planner with remote work call requirement.' },
      { id: 'ent-jo', name: 'Jo', type: 'Person', description: 'Vegetarian family member.' },
      { id: 'ent-eli', name: 'Eli', type: 'Person', description: 'Son with severe cashew allergy (peanuts fine) and carsickness on winding roads.' },
      { id: 'ent-valencia', name: 'Valencia Trip', type: 'Location', description: 'Summer trip July 13-20 near Malvarrosa Beach with verified 310 Mbps fiber Wi-Fi.' },
      { id: 'ent-blue-finch', name: 'Blue Finch Renovations', type: 'Organization', description: '$8,700 kitchen contractor led by Noor Patel, completed May 24 (not Bluebird Plumbing).' },
      { id: 'ent-cashew-allergy', name: 'Cashew Allergy', type: 'Concept', description: 'Severe food allergy; distinct from general nut or peanut allergies.' },
      { id: 'ent-nia', name: 'Nia', type: 'Person', description: 'Cedar & Steam owner managing second location expansion.' },
      { id: 'ent-owen', name: 'Owen', type: 'Person', description: 'Operations assistant tracking schedules and supplier inventory.' },
      { id: 'ent-mina', name: 'Mina Shah', type: 'Person', description: 'Store manager for Willow Market cafe location.' },
      { id: 'ent-cedar-steam', name: 'Cedar & Steam (Willow Market)', type: 'Location', description: 'Second shop at 18 Willow Market opening 7am weekdays, 8am weekends.' },
      { id: 'ent-roastline-equip', name: 'Roastline Equipment', type: 'Organization', description: 'Espresso machine supplier (distinct from canceled Roastline Coffee subscription).' },
      { id: 'ent-oat-milk', name: 'Oat Milk Par (18 cases)', type: 'Habit', description: 'Willow Market inventory par set to 18 cases with weekly Monday review.' }
    ],
    triples: [
      { source: 'ent-northstar', relation: 'has_rollout_owner', target: 'ent-dana', session: '2025-01-09' },
      { source: 'ent-northstar', relation: 'has_billing_contact', target: 'ent-morgan', session: '2025-04-10' },
      { source: 'ent-dana', relation: 'assigned_audit_review', target: 'ent-luis', session: '2025-06-18' },
      { source: 'ent-northstar', relation: 'resolved_blocker', target: 'ent-eng8821', session: '2025-04-10' },
      { source: 'ent-roberto', relation: 'has_caregiver', target: 'ent-elena', session: '2025-01-16' },
      { source: 'ent-roberto', relation: 'has_cardiologist', target: 'ent-dr-mehta', session: '2025-01-16' },
      { source: 'ent-roberto', relation: 'has_allergy', target: 'ent-penicillin', session: '2025-01-16' },
      { source: 'ent-elena', relation: 'booked_backup_transit', target: 'ent-cityride', session: '2025-02-11' },
      { source: 'ent-roberto', relation: 'receives_delivery_from', target: 'ent-harbor-pharmacy', session: '2025-03-06' },
      { source: 'ent-marcus', relation: 'planned_travel_to', target: 'ent-valencia', session: '2025-02-19' },
      { source: 'ent-eli', relation: 'has_allergy', target: 'ent-cashew-allergy', session: '2025-01-28' },
      { source: 'ent-marcus', relation: 'contracted', target: 'ent-blue-finch', session: '2025-04-05' },
      { source: 'ent-nia', relation: 'opened_location', target: 'ent-cedar-steam', session: '2025-01-07' },
      { source: 'ent-mina', relation: 'manages_location', target: 'ent-cedar-steam', session: '2025-03-21' },
      { source: 'ent-cedar-steam', relation: 'sourced_machine_from', target: 'ent-roastline-equip', session: '2025-01-07' },
      { source: 'ent-cedar-steam', relation: 'manages_inventory', target: 'ent-oat-milk', session: '2025-08-01' }
    ]
  };

  function detectCluster(text, id) {
    const lower = ((text || '') + ' ' + (id || '')).toLowerCase();
    if (id) {
      if (id.startsWith('ns-') || id.includes('northstar')) return 'northstar';
      if (id.startsWith('care-') || id.includes('care')) return 'care';
      if (id.startsWith('home-') || id.includes('household') || id.includes('travel')) return 'household';
      if (id.startsWith('cafe-') || id.includes('cafe')) return 'cafe';
    }
    for (const [domain, keywords] of Object.entries(DOMAIN_KEYWORDS)) {
      for (const kw of keywords) {
        if (lower.includes(kw)) return domain;
      }
    }
    return 'northstar';
  }

  // --- Application State ---
  const state = {
    runs: {},
    ontology: ONTOLOGY,
    activeRunKey: 'evaluation-20260830-203014',
    activeScenarioName: 'baseline',
    activeViewMode: 'retrieval', // 'retrieval', 'knowledge', 'reasoning'
    
    // Graph Model
    nodes: [],
    links: [],
    nodeMap: new Map(),
    
    // Selection & Highlighting
    selectedNode: null,
    hoveredNode: null,
    highlightedNodeIds: new Set(),
    searchQuery: '',
    
    // Filters
    filters: {
      categories: new Set(['single-hop', 'multi-hop', 'temporal', 'contradiction', 'adversarial']),
      verdict: 'all', // 'all', 'correct', 'incorrect', 'hit', 'miss'
      nodeTypes: new Set(['question', 'memory', 'entity'])
    },
    
    // Layout & Physics
    layoutMode: 'force', // 'force', 'bipartite', 'radial', 'timeline'
    physics: {
      isRunning: true,
      charge: -520,
      linkDistance: 150,
      gravity: 0.015,
      damping: 0.85,
      collisionRadius: 44,
      alpha: 1.0,
      alphaMin: 0.001,
      alphaDecay: 0.018
    },
    
    // Camera Transform (Pan & Zoom)
    camera: {
      x: 0,
      y: 0,
      scale: 0.65,
      targetX: 0,
      targetY: 0,
      targetScale: 0.65,
      isPanning: false,
      startX: 0,
      startY: 0
    },
    
    // Interaction
    dragNode: null,
    dragStarted: false,
    dragStartPos: { x: 0, y: 0 }
  };

  // --- DOM Elements ---
  const els = {
    canvas: document.getElementById('graph-canvas'),
    minimapCanvas: document.getElementById('minimap-canvas'),
    viewport: document.getElementById('canvas-viewport'),
    runSelect: document.getElementById('run-select'),
    scenarioSelect: document.getElementById('scenario-select'),
    searchInput: document.getElementById('search-input'),
    clearSearchBtn: document.getElementById('clear-search-btn'),
    inspectorDrawer: document.getElementById('inspector-drawer'),
    closeInspectorBtn: document.getElementById('close-inspector-btn'),
    inspectorBadges: document.getElementById('inspector-badges'),
    inspectorTitle: document.getElementById('inspector-title'),
    inspectorBody: document.getElementById('inspector-body'),
    scenarioHud: document.getElementById('scenario-hud'),
    hudTitle: document.getElementById('hud-title'),
    hudDesc: document.getElementById('hud-desc'),
    hudAccuracy: document.getElementById('hud-accuracy'),
    hudAccuracyCi: document.getElementById('hud-accuracy-ci'),
    hudRetrieval: document.getElementById('hud-retrieval'),
    hudRetrievalCi: document.getElementById('hud-retrieval-ci'),
    hudF1: document.getElementById('hud-f1'),
    hudLatency: document.getElementById('hud-latency'),
    hudMemories: document.getElementById('hud-memories'),
    tooltip: document.getElementById('canvas-tooltip'),
    helpModal: document.getElementById('help-modal'),
    closeHelpBtn: document.getElementById('close-help-btn'),
    openHelpBtn: document.getElementById('open-help-btn'),
    fileInput: document.getElementById('file-input'),
    btnResetView: document.getElementById('btn-reset-view'),
    btnTogglePhysics: document.getElementById('btn-toggle-physics'),
    btnExportPng: document.getElementById('btn-export-png'),
    btnExportJson: document.getElementById('btn-export-json'),
    btnFullscreen: document.getElementById('btn-fullscreen'),
    sliderCharge: document.getElementById('slider-charge'),
    valCharge: document.getElementById('val-charge'),
    sliderDistance: document.getElementById('slider-distance'),
    valDistance: document.getElementById('val-distance'),
    sliderGravity: document.getElementById('slider-gravity'),
    valGravity: document.getElementById('val-gravity'),
    sliderZoomVertical: document.getElementById('slider-zoom-vertical'),
    valZoomLevel: document.getElementById('zoom-level-val'),
    btnZoomIn: document.getElementById('btn-zoom-in'),
    btnZoomOut: document.getElementById('btn-zoom-out'),
    loadingOverlay: document.getElementById('loading-overlay'),
    localFilePrompt: document.getElementById('local-file-prompt')
  };

  const ctx = els.canvas ? els.canvas.getContext('2d') : null;
  const minimapCtx = els.minimapCanvas ? els.minimapCanvas.getContext('2d') : null;

  // --- Entity Extraction Mapping ---
  function findEntitiesInText(text) {
    if (!text || !state.ontology.entities) return [];
    const lower = text.toLowerCase();
    const matches = [];
    state.ontology.entities.forEach(ent => {
      const name = ent.name.toLowerCase();
      const cleanName = name.replace(/\([^)]*\)/g, '').trim();
      if (
        (cleanName.length > 2 && lower.includes(cleanName.toLowerCase())) ||
        (ent.id === 'ent-northstar' && (lower.includes('northstar') || lower.includes('nsh-417'))) ||
        (ent.id === 'ent-dana' && lower.includes('dana')) ||
        (ent.id === 'ent-morgan' && lower.includes('morgan')) ||
        (ent.id === 'ent-luis' && lower.includes('luis')) ||
        (ent.id === 'ent-priya' && lower.includes('priya')) ||
        (ent.id === 'ent-sam' && lower.includes('sam')) ||
        (ent.id === 'ent-eng8821' && lower.includes('eng-8821')) ||
        (ent.id === 'ent-elena' && lower.includes('elena')) ||
        (ent.id === 'ent-roberto' && lower.includes('roberto')) ||
        (ent.id === 'ent-dr-mehta' && lower.includes('mehta')) ||
        (ent.id === 'ent-penicillin' && lower.includes('penicillin')) ||
        (ent.id === 'ent-cityride' && lower.includes('cityride')) ||
        (ent.id === 'ent-harbor-pharmacy' && lower.includes('harbor pharmacy')) ||
        (ent.id === 'ent-marcus' && lower.includes('marcus')) ||
        (ent.id === 'ent-jo' && lower.includes('jo')) ||
        (ent.id === 'ent-eli' && lower.includes('eli')) ||
        (ent.id === 'ent-valencia' && lower.includes('valencia')) ||
        (ent.id === 'ent-blue-finch' && lower.includes('blue finch')) ||
        (ent.id === 'ent-cashew-allergy' && lower.includes('cashew')) ||
        (ent.id === 'ent-nia' && lower.includes('nia')) ||
        (ent.id === 'ent-owen' && lower.includes('owen')) ||
        (ent.id === 'ent-mina' && (lower.includes('mina') || lower.includes('shah'))) ||
        (ent.id === 'ent-cedar-steam' && (lower.includes('cedar') || lower.includes('willow'))) ||
        (ent.id === 'ent-roastline-equip' && lower.includes('roastline')) ||
        (ent.id === 'ent-oat-milk' && lower.includes('oat milk'))
      ) {
        matches.push(ent);
      }
    });
    return matches;
  }

  // --- Center and Fit All Nodes Into View ---
  function fitToScreen() {
    if (!state.nodes || state.nodes.length === 0 || !els.canvas) return;

    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
    for (let i = 0; i < state.nodes.length; i++) {
      const n = state.nodes[i];
      if (n.x < minX) minX = n.x;
      if (n.x > maxX) maxX = n.x;
      if (n.y < minY) minY = n.y;
      if (n.y > maxY) maxY = n.y;
    }

    if (!isFinite(minX) || !isFinite(maxX)) return;

    const w = els.canvas.clientWidth || (window.innerWidth - 320);
    const h = els.canvas.clientHeight || (window.innerHeight - 56);
    const graphW = Math.max(100, maxX - minX + 260);
    const graphH = Math.max(100, maxY - minY + 260);

    const fitScale = Math.min(1.0, Math.max(0.25, Math.min((w - 120) / graphW, (h - 120) / graphH)));
    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;

    state.camera.targetX = -centerX * fitScale;
    state.camera.targetY = -centerY * fitScale;
    state.camera.targetScale = fitScale;
    state.camera.x = state.camera.targetX;
    state.camera.y = state.camera.targetY;
    state.camera.scale = fitScale;
  }

  // --- Graph Construction with Separated Persona Clusters ---
  function buildGraphData() {
    const run = state.runs[state.activeRunKey];
    if (!run || !run.data) return;

    const report = run.data.ScenarioReports.find(s => s.Name === state.activeScenarioName) || run.data.ScenarioReports[0];
    if (!report) return;

    const nodes = [];
    const links = [];
    const nodeMap = new Map();
    const memoryMap = new Map();

    // 1. Build Entity Nodes from Ontology
    if (state.activeViewMode === 'knowledge' || state.activeViewMode === 'retrieval' || state.activeViewMode === 'reasoning') {
      const domainEntities = {};
      Object.keys(CLUSTER_CENTERS).forEach(k => { domainEntities[k] = []; });

      state.ontology.entities.forEach(ent => {
        const cluster = detectCluster(ent.name + ' ' + ent.description, ent.id);
        (domainEntities[cluster] || domainEntities.northstar).push(ent);
      });

      Object.entries(domainEntities).forEach(([cluster, ents]) => {
        const center = CLUSTER_CENTERS[cluster] || CLUSTER_CENTERS.northstar;
        ents.forEach((ent, groupIndex) => {
          const entTypeKey = ent.type.toLowerCase();
          if (!state.filters.nodeTypes.has('entity') && state.activeViewMode !== 'knowledge') return;

          const angle = (groupIndex / Math.max(1, ents.length)) * Math.PI * 2;
          const node = {
            id: ent.id,
            type: 'entity',
            entityType: ent.type,
            label: ent.name,
            description: ent.description,
            cluster: cluster,
            clusterCenter: center,
            radius: 18,
            color: COLORS[entTypeKey] || COLORS.concept,
            x: center.x + Math.cos(angle) * (130 + (groupIndex % 2) * 35),
            y: center.y + Math.sin(angle) * (130 + (groupIndex % 2) * 35),
            vx: 0,
            vy: 0,
            degree: 0,
            connectedMemories: [],
            triples: []
          };
          nodes.push(node);
          nodeMap.set(node.id, node);
        });
      });

      // Semantic Triples
      state.ontology.triples.forEach(triple => {
        const sourceNode = nodeMap.get(triple.source);
        const targetNode = nodeMap.get(triple.target);
        if (sourceNode && targetNode) {
          links.push({
            id: `${triple.source}-${triple.relation}-${triple.target}`,
            source: sourceNode,
            target: targetNode,
            type: 'triple',
            relation: triple.relation,
            session: triple.session,
            color: 'rgba(148, 163, 184, 0.55)',
            width: 1.6,
            directed: true
          });
          sourceNode.degree = (sourceNode.degree || 0) + 1;
          targetNode.degree = (targetNode.degree || 0) + 1;
          sourceNode.triples.push(triple);
        }
      });
    }

    // 2. Build Question Nodes & Retrieval Links
    if (state.activeViewMode !== 'knowledge') {
      const domainQuestions = {};
      Object.keys(CLUSTER_CENTERS).forEach(k => { domainQuestions[k] = []; });

      report.Results.forEach(res => {
        if (!state.filters.categories.has(res.Category)) return;
        const isCorrect = res.Correct === true;
        const isHit = res.RetrievalHit === true;
        if (state.filters.verdict === 'correct' && !isCorrect) return;
        if (state.filters.verdict === 'incorrect' && isCorrect) return;
        if (state.filters.verdict === 'hit' && !isHit) return;
        if (state.filters.verdict === 'miss' && isHit) return;

        const cluster = detectCluster(res.Question, res.QuestionId);
        (domainQuestions[cluster] || domainQuestions.northstar).push(res);
      });

      Object.entries(domainQuestions).forEach(([cluster, questions]) => {
        const center = CLUSTER_CENTERS[cluster] || CLUSTER_CENTERS.northstar;
        questions.forEach((res, groupIndex) => {
          if (!state.filters.nodeTypes.has('question')) return;

          const isCorrect = res.Correct === true;
          const isHit = res.RetrievalHit === true;
          const angle = (groupIndex / Math.max(1, questions.length)) * Math.PI * 2;

          const qNode = {
            id: `q-${res.QuestionId}`,
            questionId: res.QuestionId,
            type: 'question',
            category: res.Category,
            label: res.QuestionId,
            title: res.Question,
            expectedAnswer: res.ExpectedAnswer,
            generatedAnswer: res.GeneratedAnswer,
            judgeVerdict: res.JudgeVerdict,
            judgeReasoning: res.JudgeReasoning,
            isCorrect: isCorrect,
            retrievalHit: isHit,
            f1: res.F1,
            bleu1: res.Bleu1,
            latency: res.SearchLatencyMs,
            retrievedMemories: res.RetrievedMemories || [],
            cluster: cluster,
            clusterCenter: center,
            radius: 22,
            color: isCorrect ? COLORS.correct : COLORS.incorrect,
            categoryColor: COLORS[res.Category] || COLORS.question,
            x: center.x + Math.cos(angle) * 290,
            y: center.y + Math.sin(angle) * 290,
            vx: 0,
            vy: 0,
            degree: 0
          };
          nodes.push(qNode);
          nodeMap.set(qNode.id, qNode);
        });
      });

        // 3. Process Retrieved Memories
        if (state.filters.nodeTypes.has('memory')) {
          (res.RetrievedMemories || []).forEach((memText, rankIdx) => {
            const memKey = memText.trim();
            let memNode = memoryMap.get(memKey);

            if (!memNode) {
              const memId = `mem-${memoryMap.size + 1}`;
              const linkedEntities = findEntitiesInText(memText);
              const memCluster = detectCluster(memText, '');
              const memCenter = CLUSTER_CENTERS[memCluster];

              memNode = {
                id: memId,
                type: 'memory',
                label: memText.length > 34 ? memText.substring(0, 32) + '…' : memText,
                fullText: memText,
                cluster: memCluster,
                clusterCenter: memCenter,
                radius: 16,
                color: COLORS.memory,
                retrievedBy: [],
                linkedEntities: linkedEntities,
                x: memCenter.x + (Math.random() - 0.5) * 200,
                y: memCenter.y + (Math.random() - 0.5) * 200,
                vx: 0,
                vy: 0,
                degree: 0
              };
              memoryMap.set(memKey, memNode);
              nodes.push(memNode);
              nodeMap.set(memNode.id, memNode);

              // Link memory to its extracted entities
              linkedEntities.forEach(ent => {
                const entNode = nodeMap.get(ent.id);
                if (entNode) {
                  links.push({
                    id: `${memNode.id}-${entNode.id}`,
                    source: memNode,
                    target: entNode,
                    type: 'memory_entity',
                    color: 'rgba(99, 102, 241, 0.4)',
                    width: 1.4,
                    dashed: true
                  });
                  memNode.degree++;
                  entNode.degree++;
                  entNode.connectedMemories.push(memNode);
                }
              });
            }

            // Track Question Retrieval
            memNode.retrievedBy.push({
              questionId: res.QuestionId,
              rank: rankIdx + 1,
              verdict: res.JudgeVerdict
            });
            memNode.radius = Math.min(25, 15 + memNode.retrievedBy.length * 1.2);

            // Link Question -> Memory
            if (rankIdx < 3 || state.selectedNode) {
              links.push({
                id: `${qNode.id}-${memNode.id}`,
                source: qNode,
                target: memNode,
                type: 'retrieval',
                rank: rankIdx + 1,
                color: rankIdx === 0 ? 'rgba(56, 189, 248, 0.85)' : 'rgba(56, 189, 248, 0.35)',
                width: rankIdx === 0 ? 2.2 : 1.2,
                directed: true
              });
              qNode.degree++;
              memNode.degree++;
            }
          });
        }
      });
    }

    // 4. Multi-Hop & Temporal Reasoning Chains
    if (state.activeViewMode === 'reasoning') {
      report.Results.filter(r => r.Category === 'multi-hop' || r.Category === 'temporal').forEach(res => {
        if (res.RetrievedMemories && res.RetrievedMemories.length >= 2) {
          const memNode1 = memoryMap.get(res.RetrievedMemories[0].trim());
          const memNode2 = memoryMap.get(res.RetrievedMemories[1].trim());
          if (memNode1 && memNode2) {
            links.push({
              id: `reasoning-${res.QuestionId}`,
              source: memNode1,
              target: memNode2,
              type: res.Category === 'multi-hop' ? 'multi_hop_chain' : 'temporal_chain',
              color: res.Category === 'multi-hop' ? 'rgba(168, 85, 247, 0.95)' : 'rgba(245, 158, 11, 0.95)',
              width: 3.0,
              dashed: true,
              animated: true
            });
          }
        }
      });
    }

    state.nodes = nodes;
    state.links = links;
    state.nodeMap = nodeMap;

    // Apply layout positioning if not force
    if (state.layoutMode !== 'force') {
      applyLayout();
    }

    // Reset physics alpha for clean settle
    state.physics.alpha = 1.0;

    // Frame nodes in viewport
    fitToScreen();
  }

  // --- Layout Positioning Algorithms ---
  function applyLayout() {
    if (state.layoutMode === 'bipartite') {
      const qNodes = state.nodes.filter(n => n.type === 'question');
      const mNodes = state.nodes.filter(n => n.type === 'memory');
      const eNodes = state.nodes.filter(n => n.type === 'entity');

      const colSpacingX = 420;
      const leftX = -colSpacingX;
      const midX = 0;
      const rightX = colSpacingX;

      qNodes.forEach((n, i) => {
        n.x = leftX;
        n.y = (i - qNodes.length / 2) * 52;
        n.vx = 0;
        n.vy = 0;
      });

      mNodes.forEach((n, i) => {
        n.x = midX;
        n.y = (i - mNodes.length / 2) * 38;
        n.vx = 0;
        n.vy = 0;
      });

      eNodes.forEach((n, i) => {
        n.x = rightX;
        n.y = (i - eNodes.length / 2) * 56;
        n.vx = 0;
        n.vy = 0;
      });
    } else if (state.layoutMode === 'radial') {
      const eNodes = state.nodes.filter(n => n.type === 'entity');
      const mNodes = state.nodes.filter(n => n.type === 'memory');
      const qNodes = state.nodes.filter(n => n.type === 'question');

      eNodes.forEach((n, i) => {
        const angle = (i / Math.max(1, eNodes.length)) * Math.PI * 2;
        n.x = Math.cos(angle) * 220;
        n.y = Math.sin(angle) * 220;
        n.vx = 0;
        n.vy = 0;
      });

      mNodes.forEach((n, i) => {
        const angle = (i / Math.max(1, mNodes.length)) * Math.PI * 2;
        n.x = Math.cos(angle) * 420;
        n.y = Math.sin(angle) * 420;
        n.vx = 0;
        n.vy = 0;
      });

      qNodes.forEach((n, i) => {
        const angle = (i / Math.max(1, qNodes.length)) * Math.PI * 2;
        n.x = Math.cos(angle) * 620;
        n.y = Math.sin(angle) * 620;
        n.vx = 0;
        n.vy = 0;
      });
    } else if (state.layoutMode === 'timeline') {
      const sessions = [
        { date: '2025-01-08', x: -500 },
        { date: '2025-01-27', x: -260 },
        { date: '2025-02-14', x: -60 },
        { date: '2025-03-16', x: 130 },
        { date: '2025-04-02', x: 310 },
        { date: '2025-05-11', x: 500 },
        { date: '2025-06-18', x: 680 }
      ];

      state.nodes.forEach((n) => {
        const hash = Math.abs(hashString(n.id || n.label));
        const session = sessions[hash % sessions.length];
        n.x = session.x + (Math.random() - 0.5) * 100;
        n.y = (Math.random() - 0.5) * 500;
        n.vx = 0;
        n.vy = 0;
      });
    }

    fitToScreen();
  }

  function hashString(str) {
    let hash = 0;
    for (let i = 0; i < str.length; i++) {
      hash = (hash << 5) - hash + str.charCodeAt(i);
      hash |= 0;
    }
    return hash;
  }

  // --- Force Simulation Physics Tick with Cluster Centering ---
  function tickPhysics() {
    if (!state.physics.isRunning) return;
    if (state.physics.alpha <= state.physics.alphaMin) return;
    if (state.layoutMode !== 'force' && state.physics.alpha <= 0.05) return;

    const alpha = state.physics.alpha;
    const nodes = state.nodes;
    const links = state.links;
    const nodeCount = nodes.length;

    // 1. Cluster Centering Force
    for (let i = 0; i < nodeCount; i++) {
      const n = nodes[i];
      if (n === state.dragNode) continue;
      if (n.clusterCenter) {
        n.vx += (n.clusterCenter.x - n.x) * 0.03 * alpha;
        n.vy += (n.clusterCenter.y - n.y) * 0.03 * alpha;
      } else {
        n.vx -= n.x * state.physics.gravity * alpha;
        n.vy -= n.y * state.physics.gravity * alpha;
      }
    }

    // 2. Electrostatic Repulsion (with extra push between different clusters)
    const charge = state.physics.charge * alpha;
    for (let i = 0; i < nodeCount; i++) {
      const n1 = nodes[i];
      for (let j = i + 1; j < nodeCount; j++) {
        const n2 = nodes[j];
        let dx = n2.x - n1.x;
        let dy = n2.y - n1.y;
        let distSq = dx * dx + dy * dy;
        if (distSq === 0) {
          dx = (Math.random() - 0.5) * 2;
          dy = (Math.random() - 0.5) * 2;
          distSq = dx * dx + dy * dy;
        }

        const dist = Math.sqrt(distSq);
        const minDist = (n1.radius + n2.radius + state.physics.collisionRadius);

        if (dist < minDist) {
          const push = (minDist - dist) * 0.6 * alpha;
          const px = (dx / dist) * push;
          const py = (dy / dist) * push;
          if (n1 !== state.dragNode) { n1.vx -= px; n1.vy -= py; }
          if (n2 !== state.dragNode) { n2.vx += px; n2.vy += py; }
        }

        const isCrossCluster = n1.cluster && n2.cluster && n1.cluster !== n2.cluster;
        const currentCharge = isCrossCluster ? charge * 1.6 : charge;

        if (dist < 800) {
          const force = (currentCharge / (distSq + 200));
          const fx = (dx / dist) * force;
          const fy = (dy / dist) * force;
          if (n1 !== state.dragNode) { n1.vx += fx; n1.vy += fy; }
          if (n2 !== state.dragNode) { n2.vx -= fx; n2.vy -= fy; }
        }
      }
    }

    // 3. Spring Link Forces
    const linkDist = state.physics.linkDistance;
    for (let i = 0; i < links.length; i++) {
      const l = links[i];
      const source = l.source;
      const target = l.target;
      if (!source || !target) continue;

      let dx = target.x - source.x;
      let dy = target.y - source.y;
      let dist = Math.sqrt(dx * dx + dy * dy);
      if (dist === 0) dist = 1;

      const delta = (dist - linkDist) * 0.04 * alpha;
      const fx = (dx / dist) * delta;
      const fy = (dy / dist) * delta;

      if (source !== state.dragNode) { source.vx += fx; source.vy += fy; }
      if (target !== state.dragNode) { target.vx -= fx; target.vy -= fy; }
    }

    // 4. Position Update & Damping
    const damping = state.physics.damping;
    for (let i = 0; i < nodeCount; i++) {
      const n = nodes[i];
      if (n === state.dragNode) continue;
      n.vx *= damping;
      n.vy *= damping;
      n.x += n.vx;
      n.y += n.vy;
    }

    if (state.physics.alpha > state.physics.alphaMin) {
      state.physics.alpha += (state.physics.alphaMin - state.physics.alpha) * state.physics.alphaDecay;
    }
  }

  // --- Draw Label Pill Helper ---
  function drawTextPill(c, text, x, y, isHighlight) {
    c.font = isHighlight ? 'bold 11px sans-serif' : '10px sans-serif';
    const textMetrics = c.measureText(text);
    const textW = textMetrics.width;
    const paddingX = 6;
    const paddingY = 3;
    const rectX = x - textW / 2 - paddingX;
    const rectY = y;
    const rectW = textW + paddingX * 2;
    const rectH = 16;
    const radius = 4;

    c.save();
    c.beginPath();
    if (c.roundRect) {
      c.roundRect(rectX, rectY, rectW, rectH, radius);
    } else {
      c.rect(rectX, rectY, rectW, rectH);
    }
    c.fillStyle = isHighlight ? 'rgba(15, 23, 42, 0.95)' : 'rgba(15, 23, 42, 0.82)';
    c.fill();
    c.strokeStyle = isHighlight ? '#38bdf8' : 'rgba(148, 163, 184, 0.3)';
    c.lineWidth = 1;
    c.stroke();

    c.fillStyle = isHighlight ? '#ffffff' : '#e2e8f0';
    c.textAlign = 'center';
    c.textBaseline = 'middle';
    c.fillText(text, x, rectY + rectH / 2 + 1);
    c.restore();
  }

  // --- Rendering Loop ---
  let animTime = 0;
  function render() {
    animTime += 0.02;
    tickPhysics();

    if (!ctx || !els.canvas) {
      requestAnimationFrame(render);
      return;
    }

    const dpr = window.devicePixelRatio || 1;
    const width = els.canvas.clientWidth || (window.innerWidth - 320);
    const height = els.canvas.clientHeight || (window.innerHeight - 56);

    if (width <= 0 || height <= 0) {
      requestAnimationFrame(render);
      return;
    }

    const targetW = Math.floor(width * dpr);
    const targetH = Math.floor(height * dpr);
    if (els.canvas.width !== targetW || els.canvas.height !== targetH) {
      els.canvas.width = targetW;
      els.canvas.height = targetH;
    }

    state.camera.x += (state.camera.targetX - state.camera.x) * 0.15;
    state.camera.y += (state.camera.targetY - state.camera.y) * 0.15;
    state.camera.scale += (state.camera.targetScale - state.camera.scale) * 0.15;

    if (els.sliderZoomVertical && document.activeElement !== els.sliderZoomVertical) {
      els.sliderZoomVertical.value = state.camera.scale.toFixed(2);
    }
    if (els.valZoomLevel) {
      els.valZoomLevel.textContent = Math.round(state.camera.scale * 100) + '%';
    }

    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, width, height);

    ctx.translate(width / 2 + state.camera.x, height / 2 + state.camera.y);
    ctx.scale(state.camera.scale, state.camera.scale);

    drawGrid(ctx, width, height);
    drawClusterHulls(ctx);
    drawLinks(ctx);
    drawNodes(ctx);
    drawMinimap();

    requestAnimationFrame(render);
  }

  // --- Draw Grid ---
  function drawGrid(c, width, height) {
    const gridSize = 60;
    const halfW = width / (2 * state.camera.scale);
    const halfH = height / (2 * state.camera.scale);
    const viewLeft = -halfW - state.camera.x / state.camera.scale;
    const viewRight = halfW - state.camera.x / state.camera.scale;
    const viewTop = -halfH - state.camera.y / state.camera.scale;
    const viewBottom = halfH - state.camera.y / state.camera.scale;

    const startX = Math.floor(viewLeft / gridSize) * gridSize;
    const startY = Math.floor(viewTop / gridSize) * gridSize;

    c.fillStyle = 'rgba(255, 255, 255, 0.05)';
    for (let x = startX; x <= viewRight; x += gridSize) {
      for (let y = startY; y <= viewBottom; y += gridSize) {
        c.fillRect(x - 1, y - 1, 2, 2);
      }
    }
  }

  // --- Draw Soft Cluster Background Regions ---
  function drawClusterHulls(c) {
    if (state.layoutMode !== 'force') return;

    Object.keys(CLUSTER_CENTERS).forEach(key => {
      const cluster = CLUSTER_CENTERS[key];
      c.save();

      const grad = c.createRadialGradient(cluster.x, cluster.y, 40, cluster.x, cluster.y, 360);
      grad.addColorStop(0, cluster.color || 'rgba(56, 189, 248, 0.06)');
      grad.addColorStop(0.85, (cluster.color || 'rgba(56, 189, 248, 0.015)').replace('0.15', '0.02'));
      grad.addColorStop(1, 'rgba(15, 23, 42, 0)');

      c.fillStyle = grad;
      c.beginPath();
      c.arc(cluster.x, cluster.y, 360, 0, Math.PI * 2);
      c.fill();

      c.strokeStyle = cluster.borderColor || 'rgba(56, 189, 248, 0.18)';
      c.lineWidth = 1;
      c.setLineDash([6, 6]);
      c.stroke();

      c.setLineDash([]);
      drawTextPill(c, cluster.label, cluster.x, cluster.y - 330, false);

      c.restore();
    });
  }

  // --- Draw Links ---
  function drawLinks(c) {
    const links = state.links;
    const hasHighlight = state.highlightedNodeIds.size > 0;

    for (let i = 0; i < links.length; i++) {
      const l = links[i];
      const source = l.source;
      const target = l.target;
      if (!source || !target) continue;

      const isConnectedToSelected = state.selectedNode && (source.id === state.selectedNode.id || target.id === state.selectedNode.id);
      const isConnectedToHover = state.hoveredNode && (source.id === state.hoveredNode.id || target.id === state.hoveredNode.id);
      const isDimmed = hasHighlight && !(state.highlightedNodeIds.has(source.id) && state.highlightedNodeIds.has(target.id));

      c.save();
      c.beginPath();

      if (l.dashed || l.type === 'memory_entity') {
        c.setLineDash([4, 4]);
      } else if (l.animated) {
        c.setLineDash([6, 6]);
        c.lineDashOffset = -animTime * 20;
      } else {
        c.setLineDash([]);
      }

      c.moveTo(source.x, source.y);
      c.lineTo(target.x, target.y);

      c.strokeStyle = isConnectedToSelected || isConnectedToHover
        ? '#38bdf8'
        : isDimmed
          ? 'rgba(71, 85, 105, 0.1)'
          : l.color;
      c.lineWidth = isConnectedToSelected || isConnectedToHover ? Math.max(2.5, l.width * 1.5) : l.width;
      c.stroke();

      if (l.directed && !isDimmed && state.camera.scale > 0.45) {
        c.setLineDash([]);
        drawArrow(c, source.x, source.y, target.x, target.y, target.radius + 4, c.strokeStyle);
      }

      if (l.type === 'triple' && l.relation && state.camera.scale > 0.65 && !isDimmed) {
        const midX = (source.x + target.x) / 2;
        const midY = (source.y + target.y) / 2;
        c.font = '9px monospace';
        c.fillStyle = isConnectedToSelected ? '#38bdf8' : 'rgba(148, 163, 184, 0.8)';
        c.textAlign = 'center';
        c.textBaseline = 'middle';
        c.fillText(l.relation, midX, midY - 6);
      }

      c.restore();
    }
  }

  function drawArrow(c, fromX, fromY, toX, toY, offsetRadius, color) {
    const angle = Math.atan2(toY - fromY, toX - fromX);
    const arrowLen = 8;

    const targetX = toX - Math.cos(angle) * offsetRadius;
    const targetY = toY - Math.sin(angle) * offsetRadius;

    c.save();
    c.fillStyle = color;
    c.beginPath();
    c.moveTo(targetX, targetY);
    c.lineTo(targetX - arrowLen * Math.cos(angle - Math.PI / 7), targetY - arrowLen * Math.sin(angle - Math.PI / 7));
    c.lineTo(targetX - arrowLen * Math.cos(angle + Math.PI / 7), targetY - arrowLen * Math.sin(angle + Math.PI / 7));
    c.closePath();
    c.fill();
    c.restore();
  }

  // --- Draw Nodes ---
  function drawNodes(c) {
    const nodes = state.nodes;
    const hasHighlight = state.highlightedNodeIds.size > 0;

    for (let i = 0; i < nodes.length; i++) {
      const n = nodes[i];
      const isSelected = state.selectedNode && state.selectedNode.id === n.id;
      const isHovered = state.hoveredNode && state.hoveredNode.id === n.id;
      const isHighlighted = state.highlightedNodeIds.has(n.id);
      const isDimmed = hasHighlight && !isHighlighted;

      c.save();
      c.globalAlpha = isDimmed ? 0.15 : 1.0;

      if (isSelected || isHovered) {
        c.beginPath();
        c.arc(n.x, n.y, n.radius + 9, 0, Math.PI * 2);
        c.fillStyle = isSelected ? 'rgba(56, 189, 248, 0.35)' : 'rgba(255, 255, 255, 0.2)';
        c.fill();

        c.lineWidth = 2.5;
        c.strokeStyle = isSelected ? '#38bdf8' : '#94a3b8';
        c.stroke();
      }

      if (n.type === 'question') {
        c.beginPath();
        c.arc(n.x, n.y, n.radius, 0, Math.PI * 2);
        c.fillStyle = '#1e293b';
        c.fill();

        c.lineWidth = 3.5;
        c.strokeStyle = n.categoryColor || COLORS.question;
        c.stroke();

        c.beginPath();
        c.arc(n.x, n.y, n.radius - 7, 0, Math.PI * 2);
        c.fillStyle = n.isCorrect ? '#10b981' : '#ef4444';
        c.fill();

        c.fillStyle = '#ffffff';
        c.font = 'bold 10px monospace';
        c.textAlign = 'center';
        c.textBaseline = 'middle';
        const labelText = n.label;
        c.fillText(labelText, n.x, n.y);

        if (isSelected || isHovered || state.camera.scale > 1.25) {
          const shortTitle = n.title.length > 36 ? n.title.substring(0, 34) + '…' : n.title;
          drawTextPill(c, shortTitle, n.x, n.y + n.radius + 6, isSelected || isHovered);
        }
      } else if (n.type === 'entity') {
        c.beginPath();
        const r = n.radius;
        c.moveTo(n.x, n.y - r);
        c.lineTo(n.x + r, n.y);
        c.lineTo(n.x, n.y + r);
        c.lineTo(n.x - r, n.y);
        c.closePath();

        c.fillStyle = n.color;
        c.fill();
        c.lineWidth = 2;
        c.strokeStyle = '#ffffff';
        c.stroke();

        drawTextPill(c, n.label, n.x, n.y + r + 5, isSelected || isHovered);
      } else if (n.type === 'memory') {
        c.beginPath();
        c.arc(n.x, n.y, n.radius, 0, Math.PI * 2);
        c.fillStyle = '#4f46e5';
        c.fill();
        c.lineWidth = 2;
        c.strokeStyle = '#a5b4fc';
        c.stroke();

        if (n.retrievedBy && n.retrievedBy.length > 0) {
          c.fillStyle = '#ffffff';
          c.font = 'bold 10px monospace';
          c.textAlign = 'center';
          c.textBaseline = 'middle';
          c.fillText(n.retrievedBy.length.toString(), n.x, n.y);
        }

        if (isSelected || isHovered || state.camera.scale > 1.3) {
          drawTextPill(c, n.label, n.x, n.y + n.radius + 5, isSelected || isHovered);
        }
      }

      c.restore();
    }
  }

  // --- Draw Minimap ---
  function drawMinimap() {
    if (!minimapCtx || !els.minimapCanvas) return;
    const mWidth = els.minimapCanvas.width;
    const mHeight = els.minimapCanvas.height;

    minimapCtx.clearRect(0, 0, mWidth, mHeight);

    let minX = -800, maxX = 800, minY = -500, maxY = 500;
    state.nodes.forEach(n => {
      if (n.x < minX) minX = n.x;
      if (n.x > maxX) maxX = n.x;
      if (n.y < minY) minY = n.y;
      if (n.y > maxY) maxY = n.y;
    });

    const graphW = Math.max(100, maxX - minX + 200);
    const graphH = Math.max(100, maxY - minY + 200);
    const scale = Math.min((mWidth - 20) / graphW, (mHeight - 20) / graphH);

    const mapX = (x) => mWidth / 2 + (x - (minX + maxX) / 2) * scale;
    const mapY = (y) => mHeight / 2 + (y - (minY + maxY) / 2) * scale;

    minimapCtx.strokeStyle = 'rgba(148, 163, 184, 0.25)';
    minimapCtx.lineWidth = 0.5;
    state.links.forEach(l => {
      if (l.source && l.target) {
        minimapCtx.beginPath();
        minimapCtx.moveTo(mapX(l.source.x), mapY(l.source.y));
        minimapCtx.lineTo(mapX(l.target.x), mapY(l.target.y));
        minimapCtx.stroke();
      }
    });

    state.nodes.forEach(n => {
      minimapCtx.fillStyle = n.color || '#94a3b8';
      minimapCtx.beginPath();
      minimapCtx.arc(mapX(n.x), mapY(n.y), 2, 0, Math.PI * 2);
      minimapCtx.fill();
    });

    const vpW = (els.canvas.clientWidth / state.camera.scale) * scale;
    const vpH = (els.canvas.clientHeight / state.camera.scale) * scale;
    const vpCenterX = mapX(-state.camera.x / state.camera.scale);
    const vpCenterY = mapY(-state.camera.y / state.camera.scale);

    minimapCtx.strokeStyle = '#38bdf8';
    minimapCtx.lineWidth = 1;
    minimapCtx.strokeRect(vpCenterX - vpW / 2, vpCenterY - vpH / 2, vpW, vpH);
  }

  // --- Interaction & Event Handlers ---
  function getNodeAtPosition(screenX, screenY) {
    if (!els.canvas) return null;
    const rect = els.canvas.getBoundingClientRect();
    const clientX = screenX - rect.left;
    const clientY = screenY - rect.top;

    const worldX = (clientX - els.canvas.clientWidth / 2 - state.camera.x) / state.camera.scale;
    const worldY = (clientY - els.canvas.clientHeight / 2 - state.camera.y) / state.camera.scale;

    for (let i = state.nodes.length - 1; i >= 0; i--) {
      const n = state.nodes[i];
      const dx = worldX - n.x;
      const dy = worldY - n.y;
      const hitRadius = n.radius + 8;
      if (dx * dx + dy * dy <= hitRadius * hitRadius) {
        return n;
      }
    }
    return null;
  }

  function handlePointerDown(e) {
    const node = getNodeAtPosition(e.clientX, e.clientY);
    if (node) {
      state.dragNode = node;
      state.dragStarted = false;
      state.dragStartPos = { x: e.clientX, y: e.clientY };
      node.vx = 0;
      node.vy = 0;
      if (state.physics.isRunning) {
        state.physics.alpha = Math.max(state.physics.alpha, 0.4);
      }
    } else {
      state.camera.isPanning = true;
      state.camera.startX = e.clientX - state.camera.targetX;
      state.camera.startY = e.clientY - state.camera.targetY;
      if (els.viewport) els.viewport.classList.add('panning');
    }
  }

  function handlePointerMove(e) {
    if (state.dragNode && els.canvas) {
      const distMoved = Math.hypot(e.clientX - state.dragStartPos.x, e.clientY - state.dragStartPos.y);
      if (distMoved > 4) state.dragStarted = true;

      const rect = els.canvas.getBoundingClientRect();
      const clientX = e.clientX - rect.left;
      const clientY = e.clientY - rect.top;

      state.dragNode.x = (clientX - els.canvas.clientWidth / 2 - state.camera.x) / state.camera.scale;
      state.dragNode.y = (clientY - els.canvas.clientHeight / 2 - state.camera.y) / state.camera.scale;
      if (state.physics.isRunning) {
        state.physics.alpha = Math.max(state.physics.alpha, 0.25);
      }
      return;
    }

    if (state.camera.isPanning) {
      state.camera.targetX = e.clientX - state.camera.startX;
      state.camera.targetY = e.clientY - state.camera.startY;
      return;
    }

    const hoverNode = getNodeAtPosition(e.clientX, e.clientY);
    if (hoverNode !== state.hoveredNode) {
      state.hoveredNode = hoverNode;
      if (hoverNode) {
        showTooltip(e.clientX, e.clientY, hoverNode);
        if (els.viewport) els.viewport.style.cursor = 'pointer';
      } else {
        hideTooltip();
        if (els.viewport) els.viewport.style.cursor = 'grab';
      }
    } else if (hoverNode) {
      updateTooltipPos(e.clientX, e.clientY);
    }
  }

  function handlePointerUp(e) {
    if (state.dragNode) {
      if (!state.dragStarted) {
        selectNode(state.dragNode);
      }
      state.dragNode = null;
      state.dragStarted = false;
    }

    if (state.camera.isPanning) {
      state.camera.isPanning = false;
      if (els.viewport) els.viewport.classList.remove('panning');
    }
  }

  function handleWheel(e) {
    e.preventDefault();
    if (!els.canvas) return;
    const zoomFactor = e.deltaY < 0 ? 1.15 : 0.87;
    const newScale = Math.max(0.2, Math.min(3.0, state.camera.targetScale * zoomFactor));

    const rect = els.canvas.getBoundingClientRect();
    const mouseX = e.clientX - rect.left - els.canvas.clientWidth / 2;
    const mouseY = e.clientY - rect.top - els.canvas.clientHeight / 2;

    const scaleDiff = newScale - state.camera.targetScale;
    state.camera.targetX -= (mouseX - state.camera.targetX) * (scaleDiff / state.camera.targetScale);
    state.camera.targetY -= (mouseY - state.camera.targetY) * (scaleDiff / state.camera.targetScale);
    state.camera.targetScale = newScale;
  }

  // --- Tooltips ---
  function showTooltip(x, y, node) {
    if (!els.tooltip) return;
    let typeBadge = node.type.toUpperCase();
    let text = node.label;
    if (node.type === 'question') {
      typeBadge = `${node.category} • ${node.judgeVerdict || 'JUDGED'}`;
      text = node.title;
    } else if (node.type === 'memory') {
      typeBadge = `MEMORY NODE • ${node.retrievedBy ? node.retrievedBy.length : 0} RETRIEVALS`;
      text = node.fullText;
    } else if (node.type === 'entity') {
      typeBadge = `${node.entityType.toUpperCase()} ENTITY`;
      text = node.description || node.label;
    }

    const badgeEl = els.tooltip.querySelector('.tooltip-badge');
    const contentEl = els.tooltip.querySelector('.tooltip-content');
    if (badgeEl) badgeEl.textContent = typeBadge;
    if (contentEl) contentEl.textContent = text;
    els.tooltip.style.left = `${x}px`;
    els.tooltip.style.top = `${y}px`;
    els.tooltip.classList.add('visible');
  }

  function updateTooltipPos(x, y) {
    if (!els.tooltip) return;
    els.tooltip.style.left = `${x}px`;
    els.tooltip.style.top = `${y}px`;
  }

  function hideTooltip() {
    if (!els.tooltip) return;
    els.tooltip.classList.remove('visible');
  }

  // --- Node Selection & Inspector Drawer ---
  function selectNode(node) {
    state.selectedNode = node;
    highlightNeighbors(node);
    openInspector(node);

    state.camera.targetX = -node.x * state.camera.targetScale;
    state.camera.targetY = -node.y * state.camera.targetScale;
  }

  function highlightNeighbors(node) {
    if (!node) {
      state.highlightedNodeIds.clear();
      return;
    }

    const neighborIds = new Set([node.id]);
    state.links.forEach(l => {
      if (l.source.id === node.id) neighborIds.add(l.target.id);
      if (l.target.id === node.id) neighborIds.add(l.source.id);
    });
    state.highlightedNodeIds = neighborIds;
  }

  function openInspector(node) {
    if (!els.inspectorDrawer) return;
    els.inspectorBadges.innerHTML = '';
    els.inspectorBody.innerHTML = '';

    if (node.type === 'question') {
      els.inspectorBadges.innerHTML = `
        <span class="badge ${node.isCorrect ? 'badge-correct' : 'badge-incorrect'}">${node.judgeVerdict || (node.isCorrect ? 'CORRECT' : 'INCORRECT')}</span>
        <span class="badge badge-cat">${node.category}</span>
        <span class="badge" style="background: var(--bg-surface); color: var(--text-secondary);">${node.questionId}</span>
      `;
      els.inspectorTitle.textContent = node.title;

      let retrievedHtml = '';
      (node.retrievedMemories || []).forEach((mem, idx) => {
        retrievedHtml += `
          <div class="memory-item" data-mem-text="${escapeHtml(mem)}">
            <span class="rank-badge">#${idx + 1}</span>
            <div class="memory-content">${escapeHtml(mem)}</div>
          </div>
        `;
      });

      els.inspectorBody.innerHTML = `
        <div class="inspector-card">
          <div class="card-heading">
            <span>Answer Comparison</span>
            <span style="font-family: var(--font-mono); color: var(--accent-cyan);">F1: ${node.f1 ? node.f1.toFixed(2) : 'N/A'}</span>
          </div>
          <div class="answer-comparison">
            <div class="answer-box expected">
              <div style="font-size: 10px; font-weight: 600; text-transform: uppercase; color: var(--accent-cyan); margin-bottom: 2px;">Expected Gold Answer</div>
              <div>${escapeHtml(node.expectedAnswer || 'None')}</div>
            </div>
            <div class="answer-box generated ${node.isCorrect ? '' : 'incorrect'}">
              <div style="font-size: 10px; font-weight: 600; text-transform: uppercase; color: ${node.isCorrect ? 'var(--verdict-correct)' : 'var(--verdict-incorrect)'}; margin-bottom: 2px;">Generated Response</div>
              <div>${escapeHtml(node.generatedAnswer || 'None')}</div>
            </div>
          </div>
        </div>

        ${node.judgeReasoning ? `
          <div class="inspector-card">
            <div class="card-heading">LLM Judge Reasoning</div>
            <div class="reasoning-box">${escapeHtml(node.judgeReasoning)}</div>
          </div>
        ` : ''}

        <div class="inspector-card">
          <div class="card-heading">Retrieval Performance</div>
          <div class="metric-strip">
            <div class="metric-pill">
              <div class="pill-title">Hit Status</div>
              <div class="pill-val" style="color: ${node.retrievalHit ? 'var(--verdict-correct)' : 'var(--verdict-incorrect)'};">${node.retrievalHit ? 'HIT' : 'MISS'}</div>
            </div>
            <div class="metric-pill">
              <div class="pill-title">Latency</div>
              <div class="pill-val">${node.latency ? node.latency.toFixed(0) + 'ms' : 'N/A'}</div>
            </div>
            <div class="metric-pill">
              <div class="pill-title">BLEU-1</div>
              <div class="pill-val">${node.bleu1 ? node.bleu1.toFixed(2) : 'N/A'}</div>
            </div>
          </div>
        </div>

        <div class="inspector-card">
          <div class="card-heading">
            <span>Retrieved Memories (${node.retrievedMemories ? node.retrievedMemories.length : 0})</span>
          </div>
          <div class="retrieved-list">
            ${retrievedHtml || '<div style="color: var(--text-muted); font-size: 12px;">No memories retrieved.</div>'}
          </div>
        </div>

        <div class="inspector-actions">
          <button class="btn-primary" id="btn-focus-node">Focus Node</button>
          <button class="btn-secondary" id="btn-isolate-subgraph">Isolate Path</button>
        </div>
      `;
    } else if (node.type === 'memory') {
      els.inspectorBadges.innerHTML = `
        <span class="badge" style="background: rgba(99, 102, 241, 0.15); color: #818cf8; border: 1px solid rgba(99, 102, 241, 0.3);">MEMORY NODE</span>
        <span class="badge badge-cat">${node.retrievedBy ? node.retrievedBy.length : 0} Retrievals</span>
      `;
      els.inspectorTitle.textContent = 'Stored Memory Fact';

      let entitiesHtml = '';
      (node.linkedEntities || []).forEach(ent => {
        entitiesHtml += `<span class="filter-chip active" style="font-size: 11px; padding: 2px 8px;">${ent.name} (${ent.type})</span>`;
      });

      let questionsHtml = '';
      (node.retrievedBy || []).forEach(q => {
        questionsHtml += `
          <div class="memory-item">
            <span class="rank-badge">Rank #${q.rank}</span>
            <div class="memory-content">
              <strong>${q.questionId}</strong>: Verdict <em>${q.verdict}</em>
            </div>
          </div>
        `;
      });

      els.inspectorBody.innerHTML = `
        <div class="inspector-card">
          <div class="card-heading">Memory Statement</div>
          <div class="text-block">${escapeHtml(node.fullText)}</div>
        </div>

        <div class="inspector-card">
          <div class="card-heading">Linked Knowledge Entities</div>
          <div class="chip-grid">
            ${entitiesHtml || '<div style="color: var(--text-muted); font-size: 12px;">No linked entities.</div>'}
          </div>
        </div>

        <div class="inspector-card">
          <div class="card-heading">Retrieved by Questions (${node.retrievedBy ? node.retrievedBy.length : 0})</div>
          <div class="retrieved-list">
            ${questionsHtml || '<div style="color: var(--text-muted); font-size: 12px;">Not retrieved in this run.</div>'}
          </div>
        </div>

        <div class="inspector-actions">
          <button class="btn-primary" id="btn-focus-node">Focus Node</button>
        </div>
      `;
    } else if (node.type === 'entity') {
      els.inspectorBadges.innerHTML = `
        <span class="badge" style="background: rgba(236, 72, 153, 0.15); color: #ec4899; border: 1px solid rgba(236, 72, 153, 0.3);">${node.entityType.toUpperCase()}</span>
        <span class="badge badge-cat">Degree: ${node.degree || 0}</span>
      `;
      els.inspectorTitle.textContent = node.label;

      let triplesHtml = '';
      (node.triples || []).forEach(t => {
        triplesHtml += `
          <div class="memory-item">
            <span class="rank-badge">${t.relation}</span>
            <div class="memory-content"><strong>${t.source}</strong> &rarr; <strong>${t.target}</strong> (Session: ${t.session})</div>
          </div>
        `;
      });

      let memoriesHtml = '';
      (node.connectedMemories || []).forEach(m => {
        memoriesHtml += `
          <div class="memory-item">
            <div class="memory-content">${escapeHtml(m.fullText)}</div>
          </div>
        `;
      });

      els.inspectorBody.innerHTML = `
        <div class="inspector-card">
          <div class="card-heading">Entity Description</div>
          <div class="text-block">${escapeHtml(node.description || 'Knowledge entity extracted from conversation dataset.')}</div>
        </div>

        <div class="inspector-card">
          <div class="card-heading">Semantic Relations / Triples (${node.triples ? node.triples.length : 0})</div>
          <div class="retrieved-list">
            ${triplesHtml || '<div style="color: var(--text-muted); font-size: 12px;">No triples registered.</div>'}
          </div>
        </div>

        <div class="inspector-card">
          <div class="card-heading">Connected Memory Nodes (${node.connectedMemories ? node.connectedMemories.length : 0})</div>
          <div class="retrieved-list">
            ${memoriesHtml || '<div style="color: var(--text-muted); font-size: 12px;">No memory nodes linked.</div>'}
          </div>
        </div>

        <div class="inspector-actions">
          <button class="btn-primary" id="btn-focus-node">Focus Node</button>
        </div>
      `;
    }

    const focusBtn = document.getElementById('btn-focus-node');
    if (focusBtn) {
      focusBtn.addEventListener('click', () => {
        state.camera.targetX = -node.x * state.camera.targetScale;
        state.camera.targetY = -node.y * state.camera.targetScale;
      });
    }

    const isolateBtn = document.getElementById('btn-isolate-subgraph');
    if (isolateBtn) {
      isolateBtn.addEventListener('click', () => {
        highlightNeighbors(node);
      });
    }

    els.inspectorDrawer.classList.add('open');
  }

  function closeInspector() {
    if (els.inspectorDrawer) els.inspectorDrawer.classList.remove('open');
    state.selectedNode = null;
    state.highlightedNodeIds.clear();
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  // --- Scenario HUD Updates ---
  function updateScenarioHud() {
    const run = state.runs[state.activeRunKey];
    if (!run || !run.data || !els.hudTitle) return;

    const report = run.data.ScenarioReports.find(s => s.Name === state.activeScenarioName) || run.data.ScenarioReports[0];
    if (!report) return;

    els.hudTitle.textContent = report.Name;
    els.hudDesc.textContent = report.Description;

    const accPct = report.Accuracy != null ? (report.Accuracy * 100).toFixed(0) + '%' : 'N/A';
    els.hudAccuracy.innerHTML = `${accPct} <span class="metric-sub">(${report.Correct}/${report.Judged || report.Questions})</span>`;
    if (report.AccuracyLower95 != null && report.AccuracyUpper95 != null) {
      els.hudAccuracyCi.textContent = `95% CI [${(report.AccuracyLower95 * 100).toFixed(0)}%–${(report.AccuracyUpper95 * 100).toFixed(0)}%]`;
    } else {
      els.hudAccuracyCi.textContent = '';
    }

    const retPct = report.RetrievalHitRate != null ? (report.RetrievalHitRate * 100).toFixed(0) + '%' : 'N/A';
    els.hudRetrieval.innerHTML = `${retPct} <span class="metric-sub">(${report.RetrievalHits}/${report.RetrievalQuestions})</span>`;
    if (report.RetrievalHitRateLower95 != null && report.RetrievalHitRateUpper95 != null) {
      els.hudRetrievalCi.textContent = `95% CI [${(report.RetrievalHitRateLower95 * 100).toFixed(0)}%–${(report.RetrievalHitRateUpper95 * 100).toFixed(0)}%]`;
    } else {
      els.hudRetrievalCi.textContent = '';
    }

    els.hudF1.textContent = report.MeanF1 != null ? report.MeanF1.toFixed(2) : 'N/A';
    els.hudLatency.textContent = report.MeanSearchLatencyMs != null ? report.MeanSearchLatencyMs.toFixed(0) + ' ms' : 'N/A';
    els.hudMemories.textContent = report.MemoriesStored != null ? report.MemoriesStored.toString() : 'N/A';
  }

  // --- Populate Dropdowns ---
  function populateRunSelector() {
    if (!els.runSelect) return;
    els.runSelect.innerHTML = '';
    AVAILABLE_RUNS.forEach(run => {
      const opt = document.createElement('option');
      opt.value = run.id;
      opt.textContent = run.label;
      if (run.id === state.activeRunKey) opt.selected = true;
      els.runSelect.appendChild(opt);
    });
  }

  function populateScenarioSelector() {
    if (!els.scenarioSelect) return;
    const run = state.runs[state.activeRunKey];
    if (!run || !run.data) return;

    els.scenarioSelect.innerHTML = '';
    run.data.ScenarioReports.forEach(sc => {
      const opt = document.createElement('option');
      opt.value = sc.Name;
      opt.textContent = sc.Name;
      if (sc.Name === state.activeScenarioName) opt.selected = true;
      els.scenarioSelect.appendChild(opt);
    });
  }

  // --- Dynamic Asynchronous Run Loader ---
  async function loadRun(runId) {
    const runMeta = AVAILABLE_RUNS.find(r => r.id === runId) || AVAILABLE_RUNS[0];
    if (!runMeta) return;

    if (state.runs[runId] && state.runs[runId].data) {
      state.activeRunKey = runId;
      state.activeScenarioName = state.runs[runId].data.ScenarioReports[0].Name;
      populateScenarioSelector();
      buildGraphData();
      updateScenarioHud();
      if (els.localFilePrompt) els.localFilePrompt.classList.remove('visible');
      return;
    }

    if (els.loadingOverlay) els.loadingOverlay.classList.add('visible');

    try {
      const res = await fetch(runMeta.path);
      if (!res.ok) throw new Error(`HTTP ${res.status}: Failed to fetch ${runMeta.path}`);
      const json = await res.json();

      state.runs[runId] = {
        id: runId,
        label: runMeta.label,
        path: runMeta.path,
        data: json
      };

      state.activeRunKey = runId;
      state.activeScenarioName = json.ScenarioReports[0].Name;

      populateScenarioSelector();
      buildGraphData();
      updateScenarioHud();
      fitToScreen();

      if (els.localFilePrompt) els.localFilePrompt.classList.remove('visible');
    } catch (err) {
      console.warn('Could not fetch evaluation JSON directly:', err);
      showLocalFilePrompt(runMeta);
    } finally {
      if (els.loadingOverlay) els.loadingOverlay.classList.remove('visible');
    }
  }

  function showLocalFilePrompt(runMeta) {
    if (!els.localFilePrompt) return;
    els.localFilePrompt.innerHTML = `
      <div class="prompt-card">
        <div class="prompt-icon">
          <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>
        </div>
        <div class="prompt-title">Referencing ${runMeta.id}.json</div>
        <div class="prompt-desc">
          Referencing <code>${runMeta.path}</code>.<br>
          Select the JSON results file from your disk or drag and drop it anywhere.
        </div>
        <div class="prompt-actions">
          <label for="file-input" class="tab-btn active" style="cursor: pointer; display: inline-block; padding: 8px 18px; font-size: 13px;">
            Browse File from evaluation/results
          </label>
        </div>
      </div>
    `;
    els.localFilePrompt.classList.add('visible');
  }

  // --- Search Handler ---
  function handleSearch(query) {
    state.searchQuery = query.trim().toLowerCase();
    if (!state.searchQuery) {
      state.highlightedNodeIds.clear();
      if (els.clearSearchBtn) els.clearSearchBtn.style.display = 'none';
      return;
    }

    if (els.clearSearchBtn) els.clearSearchBtn.style.display = 'block';
    const matches = new Set();

    state.nodes.forEach(n => {
      const labelMatch = (n.label || '').toLowerCase().includes(state.searchQuery);
      const textMatch = (n.fullText || n.title || n.description || '').toLowerCase().includes(state.searchQuery);
      if (labelMatch || textMatch) {
        matches.add(n.id);
        state.links.forEach(l => {
          if (l.source.id === n.id) matches.add(l.target.id);
          if (l.target.id === n.id) matches.add(l.source.id);
        });
      }
    });

    state.highlightedNodeIds = matches;
  }

  // --- Export Features ---
  function exportSnapshotPng() {
    if (!els.canvas) return;
    const exportCanvas = document.createElement('canvas');
    exportCanvas.width = els.canvas.width;
    exportCanvas.height = els.canvas.height;
    const expCtx = exportCanvas.getContext('2d');

    expCtx.fillStyle = '#090d16';
    expCtx.fillRect(0, 0, exportCanvas.width, exportCanvas.height);
    expCtx.drawImage(els.canvas, 0, 0);

    expCtx.font = 'bold 16px sans-serif';
    expCtx.fillStyle = '#f8fafc';
    expCtx.fillText(`MagiCore Graph Memory Visualizer • ${state.activeScenarioName}`, 24, 36);

    const link = document.createElement('a');
    link.download = `magicore-graph-${state.activeScenarioName}-${Date.now()}.png`;
    link.href = exportCanvas.toDataURL('image/png');
    link.click();
  }

  function exportGraphJson() {
    const data = {
      scenario: state.activeScenarioName,
      timestamp: new Date().toISOString(),
      nodes: state.nodes.map(n => ({ id: n.id, type: n.type, label: n.label, ...n })),
      links: state.links.map(l => ({ id: l.id, source: l.source.id, target: l.target.id, type: l.type }))
    };

    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.download = `magicore-graph-${state.activeScenarioName}.json`;
    link.href = url;
    link.click();
    URL.revokeObjectURL(url);
  }

  // --- File Drag & Drop Loader ---
  function handleFileUpload(file) {
    const reader = new FileReader();
    reader.onload = (e) => {
      try {
        const json = JSON.parse(e.target.result);
        if (json.ScenarioReports) {
          const runKey = file.name.replace(/\.json$/, '');
          state.runs[runKey] = {
            id: runKey,
            label: `Custom Run (${json.Timestamp || file.name})`,
            isLatest: true,
            data: json
          };
          state.activeRunKey = runKey;
          state.activeScenarioName = json.ScenarioReports[0].Name;

          if (!AVAILABLE_RUNS.some(r => r.id === runKey)) {
            AVAILABLE_RUNS.unshift({ id: runKey, label: file.name, path: file.name });
          }

          populateRunSelector();
          populateScenarioSelector();
          buildGraphData();
          updateScenarioHud();
          if (els.localFilePrompt) els.localFilePrompt.classList.remove('visible');
        } else {
          alert('Invalid MagiCore Evaluation Report JSON: Missing ScenarioReports.');
        }
      } catch (err) {
        alert('Error parsing JSON file: ' + err.message);
      }
    };
    reader.readAsText(file);
  }

  // --- Update Physics Button Visual State ---
  function updatePhysicsButton() {
    if (!els.btnTogglePhysics) return;
    if (state.physics.isRunning) {
      els.btnTogglePhysics.classList.add('active');
      els.btnTogglePhysics.title = 'Pause Physics Simulation';
      els.btnTogglePhysics.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></svg>';
    } else {
      els.btnTogglePhysics.classList.remove('active');
      els.btnTogglePhysics.title = 'Resume / Heat Physics Simulation';
      els.btnTogglePhysics.innerHTML = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="5 3 19 12 5 21 5 3"/></svg>';
    }
  }

  // --- Initialize Event Listeners ---
  function initEvents() {
    if (els.canvas) {
      els.canvas.addEventListener('mousedown', handlePointerDown);
      els.canvas.addEventListener('wheel', handleWheel, { passive: false });
    }
    window.addEventListener('mousemove', handlePointerMove);
    window.addEventListener('mouseup', handlePointerUp);

    // Header Selectors
    if (els.runSelect) {
      els.runSelect.addEventListener('change', (e) => {
        loadRun(e.target.value);
      });
    }

    if (els.scenarioSelect) {
      els.scenarioSelect.addEventListener('change', (e) => {
        state.activeScenarioName = e.target.value;
        buildGraphData();
        updateScenarioHud();
      });
    }

    // View Mode Tabs
    document.querySelectorAll('.tab-btn[data-mode]').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.tab-btn[data-mode]').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        state.activeViewMode = btn.dataset.mode;
        buildGraphData();
      });
    });

    // Search Input
    if (els.searchInput) els.searchInput.addEventListener('input', (e) => handleSearch(e.target.value));
    if (els.clearSearchBtn) {
      els.clearSearchBtn.addEventListener('click', () => {
        els.searchInput.value = '';
        handleSearch('');
      });
    }

    // Category Filter Chips
    document.querySelectorAll('.filter-chip[data-cat]').forEach(chip => {
      chip.addEventListener('click', () => {
        const cat = chip.dataset.cat;
        if (cat === 'all') {
          state.filters.categories = new Set(['single-hop', 'multi-hop', 'temporal', 'adversarial']);
          document.querySelectorAll('.filter-chip[data-cat]').forEach(c => c.classList.add('active'));
        } else {
          if (state.filters.categories.has(cat)) {
            state.filters.categories.delete(cat);
            chip.classList.remove('active');
            const allChip = document.querySelector('.filter-chip[data-cat="all"]');
            if (allChip) allChip.classList.remove('active');
          } else {
            state.filters.categories.add(cat);
            chip.classList.add('active');
            if (state.filters.categories.size === 4) {
              const allChip = document.querySelector('.filter-chip[data-cat="all"]');
              if (allChip) allChip.classList.add('active');
            }
          }
        }
        buildGraphData();
      });
    });

    // Verdict Filter Chips
    document.querySelectorAll('.filter-chip[data-verdict]').forEach(chip => {
      chip.addEventListener('click', () => {
        document.querySelectorAll('.filter-chip[data-verdict]').forEach(c => c.classList.remove('active'));
        chip.classList.add('active');
        state.filters.verdict = chip.dataset.verdict;
        buildGraphData();
      });
    });

    // Layout Radio Buttons
    document.querySelectorAll('input[name="layout-mode"]').forEach(radio => {
      radio.addEventListener('change', (e) => {
        state.layoutMode = e.target.value;
        applyLayout();
        state.physics.alpha = 0.5;
      });
    });

    // Physics Tuning Sliders
    if (els.sliderCharge) {
      els.sliderCharge.addEventListener('input', (e) => {
        state.physics.charge = parseFloat(e.target.value);
        if (els.valCharge) els.valCharge.textContent = e.target.value;
        state.physics.alpha = 0.4;
      });
    }

    if (els.sliderDistance) {
      els.sliderDistance.addEventListener('input', (e) => {
        state.physics.linkDistance = parseFloat(e.target.value);
        if (els.valDistance) els.valDistance.textContent = e.target.value;
        state.physics.alpha = 0.4;
      });
    }

    if (els.sliderGravity) {
      els.sliderGravity.addEventListener('input', (e) => {
        state.physics.gravity = parseFloat(e.target.value);
        if (els.valGravity) els.valGravity.textContent = e.target.value;
        state.physics.alpha = 0.4;
      });
    }

    // Vertical Zoom Slider Controls
    if (els.sliderZoomVertical) {
      els.sliderZoomVertical.addEventListener('input', (e) => {
        state.camera.targetScale = parseFloat(e.target.value);
      });
    }

    if (els.btnZoomIn) {
      els.btnZoomIn.addEventListener('click', () => {
        state.camera.targetScale = Math.min(3.0, state.camera.targetScale * 1.25);
      });
    }

    if (els.btnZoomOut) {
      els.btnZoomOut.addEventListener('click', () => {
        state.camera.targetScale = Math.max(0.2, state.camera.targetScale * 0.8);
      });
    }

    if (els.valZoomLevel) {
      els.valZoomLevel.addEventListener('click', () => {
        state.camera.targetScale = 1.0;
      });
    }

    // Toolbar Buttons
    if (els.btnResetView) {
      els.btnResetView.addEventListener('click', () => {
        fitToScreen();
        state.physics.alpha = 0.6;
      });
    }

    if (els.btnTogglePhysics) {
      els.btnTogglePhysics.addEventListener('click', () => {
        state.physics.isRunning = !state.physics.isRunning;
        if (state.physics.isRunning) {
          state.physics.alpha = 0.8;
        }
        updatePhysicsButton();
      });
    }

    if (els.btnExportPng) els.btnExportPng.addEventListener('click', exportSnapshotPng);
    if (els.btnExportJson) els.btnExportJson.addEventListener('click', exportGraphJson);

    if (els.btnFullscreen) {
      els.btnFullscreen.addEventListener('click', () => {
        if (!document.fullscreenElement) {
          document.documentElement.requestFullscreen();
        } else {
          document.exitFullscreen();
        }
      });
    }

    // Inspector Close
    if (els.closeInspectorBtn) els.closeInspectorBtn.addEventListener('click', closeInspector);

    // Help Modal
    if (els.openHelpBtn && els.helpModal) els.openHelpBtn.addEventListener('click', () => els.helpModal.classList.add('open'));
    if (els.closeHelpBtn && els.helpModal) els.closeHelpBtn.addEventListener('click', () => els.helpModal.classList.remove('open'));
    if (els.helpModal) {
      els.helpModal.addEventListener('click', (e) => {
        if (e.target === els.helpModal) els.helpModal.classList.remove('open');
      });
    }

    // File Drop Zone
    window.addEventListener('dragover', (e) => e.preventDefault());
    window.addEventListener('drop', (e) => {
      e.preventDefault();
      if (e.dataTransfer.files.length > 0) {
        handleFileUpload(e.dataTransfer.files[0]);
      }
    });

    if (els.fileInput) {
      els.fileInput.addEventListener('change', (e) => {
        if (e.target.files.length > 0) {
          handleFileUpload(e.target.files[0]);
        }
      });
    }

    // Window Resize -> Re-fit to view
    window.addEventListener('resize', () => {
      if (els.canvas) {
        const dpr = window.devicePixelRatio || 1;
        const w = els.canvas.clientWidth || (window.innerWidth - 320);
        const h = els.canvas.clientHeight || (window.innerHeight - 56);
        els.canvas.width = Math.floor(w * dpr);
        els.canvas.height = Math.floor(h * dpr);
      }
    });
  }

  // --- Bootstrap Visualizer ---
  function init() {
    populateRunSelector();
    initEvents();
    updatePhysicsButton();
    requestAnimationFrame(render);
    // Load initial run by path
    loadRun(AVAILABLE_RUNS[0].id);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();

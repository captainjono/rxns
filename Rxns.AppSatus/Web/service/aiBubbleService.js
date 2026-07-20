/// <reference path="../app.js" />

// Singleton state holder for the always-on AI ask-bubble. Mounted outside
// <ui-view> in index.html so the bubble survives every route change; this
// service holds the engines / model / conversation / monitor-suggestions
// state in memory and persists user preferences (selected engine + model,
// open/closed, active tab) to localStorage.
//
// The /#/claude and /#/monitor full-page views can also read from this
// service — that way switching engine in the bubble and switching it on
// the page reflect the same conversation/suggestions stack.
angular.module('portal').factory('aiBubbleService', function ($http, $rootScope, eventHubService) {

    var STORAGE_KEY = 'ai.bubble.state.v1';

    // ── state ────────────────────────────────────────────────────────────
    var state = {
        open: false,
        activeTab: 'suggestions',  // 'chat' | 'suggestions' | 'settings'
        unread: 0,

        // engines + models
        engines: [],
        defaultEngineId: null,
        selectedEngineId: null,
        modelsByEngine: {},          // { engineId: { list, loading, defaultModel, available } }
        selectedModelByEngine: {},   // { engineId: 'model-name' }
        allowToolsByEngine: {},      // { engineId: bool } — default true; false for engines whose model mis-handles tool descriptions (e.g. qwen on Foundry/NPU)

        // Per-chat context attachments. With tools off the model has no way
        // to fetch live data — these chips let the operator manually attach
        // current logs/errors/infra-status to the next prompt so a non-tool
        // model still has something to reason about.
        contextChips: { logs: false, errors: false, infra: false },
        contextPaste: '',

        // CLAUDE.md-style project knowledge — fetched from the server on
        // first bubble open, edited via Settings, prepended to every system
        // prompt by the controller.
        projectContext: '',

        // Workspace auto-discovery state — multi-root list, discovery globs,
        // last scan results, and the operator's tick-set. Files in
        // selectedKnowledgeFiles are auto-inlined into every system prompt.
        workspace: {
            roots: [],
            discoveryPatterns: [],
            defaultPatterns: [],
            selectedKnowledgeFiles: [],
            scanResults: [],         // [{root, relativePath, absolutePath, sizeBytes, modifiedUtc}]
            scanGroupedByRoot: {},
            loading: false,
            scanning: false,
            lastError: null,
            lastScanSummary: null,

            // Embeddings + knowledge index (RAG).
            embeddingsEngines: [],
            pendingRestartEmbeddingsEngines: [],
            defaultEmbeddingsEngineId: '',
            indexStatusByRoot: {},     // { rootPath: { built, chunkCount, dimensions, builtAtUtc, ... } }
            indexBuildingByRoot: {},   // { rootPath: bool }
            lastIndexError: null
        },

        // conversation
        conversation: [],            // [{role, content, calls?, name?, toolUseId?}]
        sending: false,

        // monitor + suggestions
        mode: 'manual',
        sources: [],
        suggestions: [],
        trustedActions: [],

        // tools + flags
        readOnly: true,
        tools: [],

        // bookkeeping
        loaded: false,
        lastError: null
    };

    try {
        var saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || '{}');
        if (typeof saved.open === 'boolean')   state.open = saved.open;
        if (saved.activeTab)                   state.activeTab = saved.activeTab;
        if (saved.selectedEngineId)            state.selectedEngineId = saved.selectedEngineId;
        if (saved.selectedModelByEngine)       state.selectedModelByEngine = saved.selectedModelByEngine;
        if (saved.allowToolsByEngine)          state.allowToolsByEngine = saved.allowToolsByEngine;
    } catch (e) { /* ignore */ }

    function persist() {
        try {
            localStorage.setItem(STORAGE_KEY, JSON.stringify({
                open: state.open,
                activeTab: state.activeTab,
                selectedEngineId: state.selectedEngineId,
                selectedModelByEngine: state.selectedModelByEngine,
                allowToolsByEngine: state.allowToolsByEngine
            }));
        } catch (e) { /* ignore */ }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    function pf(o, k) {
        if (!o) return undefined;
        if (o[k] !== undefined) return o[k];
        var lc = k.charAt(0).toLowerCase() + k.slice(1);
        return o[lc];
    }

    function severityRank(s) {
        var sev = (pf(s, 'Severity') || 'info').toLowerCase();
        if (sev === 'error') return 0;
        if (sev === 'warn')  return 1;
        return 2;
    }

    function sortSuggestions() {
        state.suggestions.sort(function (a, b) {
            var d = severityRank(a) - severityRank(b);
            if (d !== 0) return d;
            var ta = new Date(pf(a, 'RaisedAt') || 0).getTime();
            var tb = new Date(pf(b, 'RaisedAt') || 0).getTime();
            return tb - ta;
        });
    }

    function findEngine(id) {
        for (var i = 0; i < state.engines.length; i++) {
            if (state.engines[i].id === id) return state.engines[i];
        }
        return null;
    }

    function firstAvailableEngineId() {
        var avail = state.engines.find(function (e) { return e.available; });
        return (avail || state.engines[0] || {}).id || null;
    }

    // ── loaders ──────────────────────────────────────────────────────────

    function loadInfo() {
        return $http.get('/api/ai/info').then(function (r) {
            var d = r.data || {};
            state.engines = d.engines || [];
            state.defaultEngineId = d.defaultEngine || '';
            state.readOnly = !!d.readOnly;
            state.tools = d.tools || [];

            // Resolve selected engine: stored → server default → first available.
            if (!state.selectedEngineId || !findEngine(state.selectedEngineId)) {
                state.selectedEngineId = state.defaultEngineId || firstAvailableEngineId();
            }
            persist();
            state.loaded = true;
        }, function (err) {
            state.lastError = 'GET /api/ai/info HTTP ' + (err && err.status);
        });
    }

    function loadModels(engineId) {
        if (!engineId) return;
        state.modelsByEngine[engineId] = { list: [], loading: true, defaultModel: null, available: false };
        return $http.get('/api/ai/engines/' + encodeURIComponent(engineId) + '/models').then(function (r) {
            var d = r.data || {};
            var list = d.models || [];
            var def = d.defaultModel || null;
            if (def && list.indexOf(def) === -1) list = [def].concat(list);

            state.modelsByEngine[engineId] = {
                list: list, loading: false, defaultModel: def,
                available: !!d.available, warning: d.warning || null
            };

            var sel = state.selectedModelByEngine[engineId];
            if (!sel || list.indexOf(sel) === -1) {
                state.selectedModelByEngine[engineId] = def || list[0] || null;
                persist();
            }
        }, function () {
            state.modelsByEngine[engineId] = { list: [], loading: false, defaultModel: null, available: false };
        });
    }

    function loadMonitorState() {
        return $http.get('/api/monitor/state').then(function (r) {
            var d = r.data || {};
            state.mode = d.mode || 'manual';
            state.sources = d.sources || [];
            state.suggestions = d.suggestions || [];
            state.trustedActions = d.trustedActions || [];
            sortSuggestions();
        }, function () { /* monitor module may not be wired in non-AppStatus hosts */ });
    }

    function ensureLoaded() {
        var p = state.loaded ? Promise.resolve() : loadInfo();
        return Promise.resolve(p).then(function () {
            if (state.selectedEngineId && !state.modelsByEngine[state.selectedEngineId]) {
                loadModels(state.selectedEngineId);
            }
            loadMonitorState();
            loadProjectContext();
            loadWorkspaceConfig();
            loadEmbeddingsEngines();
            loadIndexStatus();
        });
    }

    // ── commands ─────────────────────────────────────────────────────────

    function setSelectedEngine(id) {
        state.selectedEngineId = id;
        persist();
        if (id && !state.modelsByEngine[id]) loadModels(id);
    }

    function setSelectedModel(model) {
        var id = state.selectedEngineId;
        if (!id) return;
        state.selectedModelByEngine[id] = model;
        persist();
    }

    function setAllowTools(allow) {
        var id = state.selectedEngineId;
        if (!id) return;
        state.allowToolsByEngine[id] = !!allow;
        persist();
    }
    function getAllowTools() {
        var id = state.selectedEngineId;
        if (!id) return true;
        var v = state.allowToolsByEngine[id];
        return v === undefined || v === null ? true : !!v;
    }

    function buildMessagesForRequest() {
        // Strip thinking/tool-calls UI markers; keep only roles the API expects.
        return state.conversation
            .filter(function (m) { return m.role === 'user' || m.role === 'assistant' || m.role === 'tool'; })
            .map(function (m) {
                return {
                    Role: m.role, Content: m.content,
                    ToolName: m.name || m.toolName,
                    ToolUseId: m.toolUseId
                };
            });
    }

    function buildContextPreamble() {
        // Pull the operator-selected context chips synchronously where we
        // can — these endpoints already produce JSON that the model can
        // read. Failures don't abort the send; missing chips just stay out
        // of the preamble.
        var promises = [];
        var sections = [];

        if (state.contextChips.logs) {
            promises.push($http.get('/api/appstatus/log?take=30').then(function (r) {
                sections.push('[recent logs (30)]:\n' + JSON.stringify(r.data, null, 2));
            }, function () { /* endpoint absent; skip */ }));
        }
        if (state.contextChips.errors) {
            promises.push($http.get('/api/appstatus/errors?take=30').then(function (r) {
                sections.push('[recent errors (30)]:\n' + JSON.stringify(r.data, null, 2));
            }, function () { /* skip */ }));
        }
        if (state.contextChips.infra) {
            promises.push($http.get('/api/infra/components').then(function (r) {
                // Trim each entry — full component bodies are huge.
                var slim = (r.data || []).map(function (c) {
                    return { name: c.name, kind: c.kind, state: (c.status || {}).state, lastError: (c.status || {}).lastError };
                });
                sections.push('[infra components]:\n' + JSON.stringify(slim, null, 2));
            }, function () { /* augment not present; skip */ }));
        }
        if (state.contextPaste && state.contextPaste.trim()) {
            sections.push('[operator-pasted context]:\n' + state.contextPaste.trim());
        }

        return Promise.all(promises).then(function () {
            if (!sections.length) return '';
            return 'Context:\n' + sections.join('\n\n') + '\n\n';
        });
    }

    function sendMessage(text) {
        if (!text || state.sending) return Promise.resolve();
        var prior = buildMessagesForRequest();

        state.conversation.push({ role: 'user', content: text });
        state.conversation.push({ role: 'thinking' });
        state.sending = true;

        // Per-engine tool toggle. Default true; operator turns off for engines
        // whose model emits tool calls as text instead of using tool_calls.
        var allowTools = state.allowToolsByEngine[state.selectedEngineId];
        if (allowTools === undefined || allowTools === null) allowTools = true;

        return buildContextPreamble().then(function (preamble) {
            var augmented = preamble + text;
            prior.push({ Role: 'user', Content: augmented });

            return $http.post('/api/ai/chat', {
                Messages: prior,
                ResolveTools: allowTools,
                AllowToolCalls: allowTools,
                Engine: state.selectedEngineId,
                Model: state.selectedModelByEngine[state.selectedEngineId] || null
            });
        }).then(function (r) {
            removeThinking();
            var data = r.data || {};
            var first = pf(data, 'FirstTurn');
            if (first) {
                var firstText = pf(first, 'AssistantText');
                if (firstText) state.conversation.push({ role: 'assistant', content: firstText });
                var calls = pf(first, 'ToolCalls');
                if (calls && calls.length) state.conversation.push({ role: 'tool-calls', calls: calls });
            }
            var resolved = pf(data, 'ToolsResolved');
            var msgsAfter = pf(data, 'MessagesAfter');
            if (resolved && msgsAfter) {
                msgsAfter.forEach(function (m) {
                    if ((m.Role || m.role) === 'tool') {
                        state.conversation.push({
                            role: 'tool',
                            name: m.ToolName || m.toolName,
                            content: m.Content || m.content,
                            toolUseId: m.ToolUseId || m.toolUseId
                        });
                    }
                });
                var second = pf(data, 'SecondTurn');
                var secondText = second && pf(second, 'AssistantText');
                if (secondText) state.conversation.push({ role: 'assistant', content: secondText });
            }
        }, function (err) {
            removeThinking();
            state.conversation.push({
                role: 'error',
                content: 'HTTP ' + (err && err.status) + ': ' + ((err && err.data) || 'request failed')
            });
        })['finally'](function () { state.sending = false; });
    }

    function removeThinking() {
        for (var i = state.conversation.length - 1; i >= 0; i--) {
            if (state.conversation[i].role === 'thinking') state.conversation.splice(i, 1);
        }
    }

    function clearConversation() { state.conversation.length = 0; }

    function ackSuggestion(id) {
        return $http.post('/api/monitor/suggestions/' + encodeURIComponent(id) + '/ack').then(function () {
            state.suggestions = state.suggestions.filter(function (s) { return pf(s, 'Id') !== id; });
        });
    }

    function snoozeSuggestion(id, mins) {
        return $http.post('/api/monitor/suggestions/' + encodeURIComponent(id) + '/snooze', { Minutes: mins || 30 }).then(function () {
            state.suggestions = state.suggestions.filter(function (s) { return pf(s, 'Id') !== id; });
        });
    }

    function setMode(mode) {
        state.mode = mode;
        return $http.post('/api/monitor/mode', { Mode: mode });
    }

    function toggleSource(id, enabled) {
        var src = state.sources.find(function (s) { return s.id === id; });
        if (src) src.enabled = enabled;
        return $http.post('/api/monitor/sources/' + encodeURIComponent(id), { Enabled: enabled }).catch(function () {
            if (src) src.enabled = !enabled;
        });
    }

    function analyseNow() {
        return $http.post('/api/monitor/analyse-now');
    }

    // ── engine management (add / remove / scan) ────────────────────────

    function addEngine(body) {
        // body: { id?, kind, label?, endpoint, apiKey?, model?, default? }
        return $http.post('/api/ai/engines', body || {}).then(function (r) {
            // Refresh the engine list (server is the source of truth — added
            // engine is dynamic + persisted).
            return loadInfo().then(function () { return r.data; });
        });
    }

    function removeEngine(id) {
        return $http({ method: 'DELETE', url: '/api/ai/engines/' + encodeURIComponent(id) }).then(function () {
            return loadInfo();
        });
    }

    function scanEngines(cidr, ports) {
        return $http.post('/api/ai/engines/scan', { Cidr: cidr, Ports: ports || null, Confirmed: true })
            .then(function (r) { return r.data; });
    }

    function discoverEngines() {
        return $http.post('/api/ai/engines/discover').then(function (r) { return r.data; });
    }

    function loadWorkspaceConfig() {
        state.workspace.loading = true;
        return $http.get('/api/ai/workspace/config').then(function (r) {
            var d = r.data || {};
            state.workspace.roots                  = d.roots                  || [];
            state.workspace.discoveryPatterns      = d.discoveryPatterns      || [];
            state.workspace.defaultPatterns        = d.defaultPatterns        || [];
            state.workspace.selectedKnowledgeFiles = d.selectedKnowledgeFiles || [];
            state.workspace.loading = false;
        }, function () { state.workspace.loading = false; });
    }

    function saveWorkspaceConfig(partial) {
        // partial: { roots?, discoveryPatterns?, selectedKnowledgeFiles? }
        return $http.put('/api/ai/workspace/config', {
            Roots:                  partial && partial.roots !== undefined ? partial.roots : null,
            DiscoveryPatterns:      partial && partial.discoveryPatterns !== undefined ? partial.discoveryPatterns : null,
            SelectedKnowledgeFiles: partial && partial.selectedKnowledgeFiles !== undefined ? partial.selectedKnowledgeFiles : null
        }).then(function (r) {
            var d = r.data || {};
            if (d.roots) state.workspace.roots = d.roots;
            if (d.discoveryPatterns) state.workspace.discoveryPatterns = d.discoveryPatterns;
            if (d.selectedKnowledgeFiles) state.workspace.selectedKnowledgeFiles = d.selectedKnowledgeFiles;
            return r.data;
        });
    }

    function scanWorkspace() {
        state.workspace.scanning = true;
        state.workspace.lastError = null;
        return $http.get('/api/ai/workspace/scan').then(function (r) {
            var d = r.data || {};
            var files = d.files || [];
            state.workspace.scanResults = files;
            // Group by root for tree rendering.
            var grouped = {};
            files.forEach(function (f) {
                if (!grouped[f.root]) grouped[f.root] = [];
                grouped[f.root].push(f);
            });
            state.workspace.scanGroupedByRoot = grouped;
            state.workspace.lastScanSummary = 'scanned ' + (d.roots || []).length + ' root(s) · found ' + files.length + ' file(s)';
            state.workspace.scanning = false;
        }, function (err) {
            state.workspace.scanning = false;
            state.workspace.lastError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
        });
    }

    // ── Knowledge index (embedding RAG) ─────────────────────────────────

    function loadEmbeddingsEngines() {
        return $http.get('/api/ai/workspace/embeddings-engines').then(function (r) {
            var d = r.data || {};
            state.workspace.embeddingsEngines = d.loaded || [];
            state.workspace.pendingRestartEmbeddingsEngines = d.pendingRestart || [];
            state.workspace.defaultEmbeddingsEngineId = d.defaultEngineId || '';
            return d;
        }, function () { /* swallow */ });
    }

    function addEmbeddingsEngine(body) {
        return $http.post('/api/ai/workspace/embeddings-engines', body || {}).then(function (r) {
            return loadEmbeddingsEngines().then(function () { return r.data; });
        });
    }

    function removeEmbeddingsEngine(id) {
        return $http({ method: 'DELETE', url: '/api/ai/workspace/embeddings-engines/' + encodeURIComponent(id) })
            .then(function () { return loadEmbeddingsEngines(); });
    }

    function loadIndexStatus() {
        return $http.get('/api/ai/workspace/index').then(function (r) {
            var d = r.data || {};
            state.workspace.indexStatusByRoot = {};
            (d.roots || []).forEach(function (s) { state.workspace.indexStatusByRoot[s.root] = s; });
            return d;
        }, function () { /* swallow */ });
    }

    function buildIndex(root, engineId) {
        state.workspace.indexBuildingByRoot = state.workspace.indexBuildingByRoot || {};
        state.workspace.indexBuildingByRoot[root] = true;
        return $http.post('/api/ai/workspace/index/build', { Root: root, EmbeddingsEngineId: engineId || null })
            .then(function (r) {
                state.workspace.indexBuildingByRoot[root] = false;
                return loadIndexStatus().then(function () { return r.data; });
            }, function (err) {
                state.workspace.indexBuildingByRoot[root] = false;
                throw err;
            });
    }

    function clearIndex(root) {
        return $http({ method: 'DELETE', url: '/api/ai/workspace/index', data: { Root: root }, headers: { 'Content-Type': 'application/json' } })
            .then(function () { return loadIndexStatus(); });
    }

    function toggleKnowledgeFile(absolutePath, on) {
        var idx = state.workspace.selectedKnowledgeFiles.indexOf(absolutePath);
        if (on && idx === -1) state.workspace.selectedKnowledgeFiles.push(absolutePath);
        if (!on && idx >= 0) state.workspace.selectedKnowledgeFiles.splice(idx, 1);
        return saveWorkspaceConfig({ selectedKnowledgeFiles: state.workspace.selectedKnowledgeFiles });
    }

    function loadProjectContext() {
        return $http.get('/api/ai/project-context').then(function (r) {
            state.projectContext = (r.data && r.data.text) || '';
            return state.projectContext;
        }, function () { state.projectContext = ''; return ''; });
    }

    function saveProjectContext(text) {
        return $http.put('/api/ai/project-context', { Text: text || '' }).then(function (r) {
            state.projectContext = text || '';
            return r.data;
        });
    }

    function openInChatWithSuggestion(s) {
        var title = pf(s, 'Title') || 'this finding';
        var rationale = pf(s, 'Rationale') || '';
        state.activeTab = 'chat';
        state.open = true;
        persist();
        // Seed the input via a transient field consumed by the bubble controller.
        state.pendingPrompt = 'Investigate: ' + title + '\n\n' + rationale;
    }

    // ── UI ───────────────────────────────────────────────────────────────

    function toggleOpen() {
        state.open = !state.open;
        if (state.open) {
            state.unread = 0;
            ensureLoaded();
        }
        persist();
    }

    function setActiveTab(t) {
        state.activeTab = t;
        persist();
    }

    // Phase-aware apply that doesn't rely on $applyAsync (which we hit a "not
    // a function" error on this Angular build for — see aiBubble.js for the
    // matching applySafely). Uses $rootScope.safeApply when present (defined
    // in app.js) and falls back to a manual digest check otherwise.
    function applySafely(fn) {
        try {
            if (typeof $rootScope.safeApply === 'function') { $rootScope.safeApply(fn); return; }
            var phase = $rootScope.$$phase;
            if (phase === '$apply' || phase === '$digest') { fn(); }
            else { $rootScope.$apply(fn); }
        } catch (err) {
            if (window && window.console && window.console.warn) console.warn('aiBubbleService applySafely:', err);
        }
    }

    // ── live suggestions via SignalR ─────────────────────────────────────

    if (eventHubService && eventHubService.monitorEvent && eventHubService.monitorEvent.subscribe) {
        eventHubService.monitorEvent.subscribe(function (env) {
            if (!env) return;
            if ((env.kind || env.Kind) !== 'suggestionRaised') return;
            var s = env.suggestion || env.Suggestion;
            if (!s) return;
            applySafely(function () {
                state.suggestions.unshift(s);
                sortSuggestions();
                if (!state.open) state.unread++;
            });
        });
    }

    return {
        state: state,
        ensureLoaded: ensureLoaded,
        loadInfo: loadInfo,
        loadModels: loadModels,
        loadMonitorState: loadMonitorState,
        setSelectedEngine: setSelectedEngine,
        setSelectedModel: setSelectedModel,
        setAllowTools: setAllowTools,
        getAllowTools: getAllowTools,
        sendMessage: sendMessage,
        clearConversation: clearConversation,
        ackSuggestion: ackSuggestion,
        snoozeSuggestion: snoozeSuggestion,
        setMode: setMode,
        toggleSource: toggleSource,
        analyseNow: analyseNow,
        openInChatWithSuggestion: openInChatWithSuggestion,
        toggleOpen: toggleOpen,
        setActiveTab: setActiveTab,
        addEngine: addEngine,
        removeEngine: removeEngine,
        scanEngines: scanEngines,
        discoverEngines: discoverEngines,
        loadProjectContext: loadProjectContext,
        saveProjectContext: saveProjectContext,
        loadWorkspaceConfig: loadWorkspaceConfig,
        saveWorkspaceConfig: saveWorkspaceConfig,
        scanWorkspace: scanWorkspace,
        toggleKnowledgeFile: toggleKnowledgeFile,
        loadEmbeddingsEngines: loadEmbeddingsEngines,
        addEmbeddingsEngine: addEmbeddingsEngine,
        removeEmbeddingsEngine: removeEmbeddingsEngine,
        loadIndexStatus: loadIndexStatus,
        buildIndex: buildIndex,
        clearIndex: clearIndex,

        // helpers exposed for templates
        engineLabel: function (e) {
            if (!e) return '';
            var avail = e.available ? '' : ' (unavailable)';
            var cost = e.cost === 'free' ? ' · free' : (e.cost === 'paid' ? ' · paid' : '');
            return (e.label || (e.kind + ' · ' + e.model)) + cost + avail;
        },
        severityClass: function (s) { return 'sev-' + ((pf(s, 'Severity') || 'info').toLowerCase()); },
        pickField: pf
    };
});

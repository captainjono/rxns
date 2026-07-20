/// <reference path="../../../app.js" />

// Always-on AI bubble — mounted in index.html outside <ui-view> so it
// survives every route change. All state lives in aiBubbleService so the
// /#/claude and /#/monitor pages can read the same conversation +
// suggestions stack (they share the singleton).
angular.module('portal').directive('aiBubble', function (aiBubbleService, $timeout, $window, $http, $q) {
    return {
        restrict: 'E',
        templateUrl: 'support/partial/aiBubble/aiBubble.html',
        scope: {},
        link: function (scope) {

            scope.s = aiBubbleService;
            // AngularJS "dot rule": ng-if creates a child scope, and bare
            // primitives (`scope.input = ''`) get shadowed by the child scope
            // on first write — so what the user types lives on the child and
            // scope.send() reads the (still-empty) parent. Wrapping in an
            // object means the child scope reads+writes the same reference
            // via prototypal inheritance. Same trick for any other ng-model
            // we add inside ng-if'd sub-panels.
            scope.ui = { chatInput: '', projectContext: '', newRoot: '', patternsText: '' };
            scope.busy = false;
            scope.ctxBusy = false;
            scope.ctxStatus = '';

            // Mirror discovery patterns into a flat textarea — operator edits
            // free-form, we split on lines on blur and persist.
            scope.$watchCollection(function () { return aiBubbleService.state.workspace.discoveryPatterns; }, function (v) {
                scope.ui.patternsText = (v || []).join('\n');
            });

            scope.addRoot = function () {
                var v = (scope.ui.newRoot || '').trim();
                if (!v) return;
                var roots = (aiBubbleService.state.workspace.roots || []).slice();
                if (roots.indexOf(v) === -1) roots.push(v);
                aiBubbleService.saveWorkspaceConfig({ roots: roots });
                scope.ui.newRoot = '';
            };
            scope.removeRoot = function (idx) {
                var roots = (aiBubbleService.state.workspace.roots || []).slice();
                roots.splice(idx, 1);
                aiBubbleService.saveWorkspaceConfig({ roots: roots });
            };
            scope.onRootsChanged = function () {
                // Debounce-on-blur would be nicer; for now persist on every change.
                aiBubbleService.saveWorkspaceConfig({ roots: aiBubbleService.state.workspace.roots });
            };
            scope.savePatterns = function () {
                var lines = (scope.ui.patternsText || '').split(/\r?\n/).map(function (s) { return s.trim(); }).filter(function (s) { return s.length > 0; });
                aiBubbleService.saveWorkspaceConfig({ discoveryPatterns: lines });
            };
            scope.scanFiles = function () {
                aiBubbleService.scanWorkspace();
            };
            scope.isSelected = function (abs) {
                return (aiBubbleService.state.workspace.selectedKnowledgeFiles || []).indexOf(abs) >= 0;
            };
            scope.toggleFile = function (abs) {
                var on = !scope.isSelected(abs);
                aiBubbleService.toggleKnowledgeFile(abs, on);
            };
            // ── knowledge index / embeddings ────────────────────────────
            scope.filesSelectedUnder = function (root) {
                var selected = aiBubbleService.state.workspace.selectedKnowledgeFiles || [];
                var rootLower = (root || '').toLowerCase();
                for (var i = 0; i < selected.length; i++) {
                    if (selected[i].toLowerCase().indexOf(rootLower) === 0) return true;
                }
                return false;
            };
            scope.buildIndex = function (root) {
                aiBubbleService.state.workspace.lastIndexError = null;
                aiBubbleService.buildIndex(root, aiBubbleService.state.workspace.defaultEmbeddingsEngineId).then(function () {
                    /* refreshed by service */
                }, function (err) {
                    aiBubbleService.state.workspace.lastIndexError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
                });
            };
            scope.clearIndex = function (root) {
                if (!$window.confirm('Delete the knowledge index for ' + root + '?')) return;
                aiBubbleService.clearIndex(root);
            };
            scope.quickAddOllamaEmbed = function () {
                aiBubbleService.addEmbeddingsEngine({
                    Kind: 'ollama',
                    Model: 'nomic-embed-text:latest',
                    Label: 'Ollama · nomic-embed-text',
                    MakeDefault: true
                }).then(function (r) {
                    if (r && r.pendingRestart) {
                        aiBubbleService.state.workspace.lastIndexError =
                            'Engine persisted. Restart the portal to activate it before building an index.';
                    }
                }, function (err) {
                    aiBubbleService.state.workspace.lastIndexError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
                });
            };

            scope.selectAllInRoot = function (root, on) {
                var files = (aiBubbleService.state.workspace.scanGroupedByRoot[root] || []).map(function (f) { return f.absolutePath; });
                var selected = (aiBubbleService.state.workspace.selectedKnowledgeFiles || []).slice();
                if (on) {
                    files.forEach(function (p) { if (selected.indexOf(p) === -1) selected.push(p); });
                } else {
                    selected = selected.filter(function (p) { return files.indexOf(p) === -1; });
                }
                aiBubbleService.saveWorkspaceConfig({ selectedKnowledgeFiles: selected });
            };

            // Mirror projectContext from the service whenever it changes
            // server-side (e.g. after ensureLoaded fetches it).
            scope.$watch(function () { return aiBubbleService.state.projectContext; }, function (v) {
                if (typeof v === 'string') scope.ui.projectContext = v;
            });
            scope.saveProjectContext = function () {
                scope.ctxBusy = true;
                scope.ctxStatus = '';
                aiBubbleService.saveProjectContext(scope.ui.projectContext || '').then(function (r) {
                    scope.ctxStatus = 'saved · ' + ((r && r.length) || 0) + ' chars';
                }, function (err) {
                    scope.ctxStatus = 'save failed: ' + ((err && err.data && err.data.error) || ('HTTP ' + (err && err.status)));
                })['finally'](function () { scope.ctxBusy = false; });
            };
            scope.reloadProjectContext = function () {
                scope.ctxBusy = true;
                aiBubbleService.loadProjectContext().then(function (text) {
                    scope.ui.projectContext = text;
                    scope.ctxStatus = 'reloaded · ' + (text || '').length + ' chars';
                })['finally'](function () { scope.ctxBusy = false; });
            };

            // Defensive: reset `sending` on every mount. If a previous chat
            // request blew up in a way that skipped the `.finally()` block
            // (broken digest, hard refresh mid-flight, ...) the flag could
            // stay true and silently disable the Send button — symptom is
            // "typing + clicking Send does nothing, no error". Clearing
            // here means a refresh always re-enables Send.
            aiBubbleService.state.sending = false;

            // Lazy-load engines + monitor state on first open AND on first mount
            // (so suggestions show their count badge from the start).
            aiBubbleService.ensureLoaded();

            scope.toggle = function () { aiBubbleService.toggleOpen(); };
            scope.setTab = function (t) { aiBubbleService.setActiveTab(t); };
            scope.clearConversation = function () { aiBubbleService.clearConversation(); };
            scope.currentEngine = function () {
                var id = aiBubbleService.state.selectedEngineId;
                return aiBubbleService.state.engines.find(function (e) { return e.id === id; }) || {};
            };
            scope.currentEngineModels = function () {
                var id = aiBubbleService.state.selectedEngineId;
                return aiBubbleService.state.modelsByEngine[id] || { list: [], loading: false };
            };
            scope.onEngineChange = function () {
                aiBubbleService.setSelectedEngine(aiBubbleService.state.selectedEngineId);
            };
            scope.onModelChange = function () {
                var id = aiBubbleService.state.selectedEngineId;
                aiBubbleService.setSelectedModel(aiBubbleService.state.selectedModelByEngine[id]);
            };
            scope.toggleAllowTools = function () {
                aiBubbleService.setAllowTools(!aiBubbleService.getAllowTools());
            };

            // ── chat ────────────────────────────────────────────────────
            scope.send = function () {
                var text = (scope.ui.chatInput || '').trim();
                if (!text) return;
                if (aiBubbleService.state.sending) {
                    // Defensive: if the flag is stuck on, force-unstick before
                    // returning so the next click can proceed.
                    aiBubbleService.state.sending = false;
                    return;
                }
                scope.ui.chatInput = '';
                aiBubbleService.sendMessage(text).then(scrollChatBottom);
                scrollChatBottom();
            };
            scope.onKey = function (ev) {
                if (!ev) return;
                if (ev.keyCode === 13 && !ev.shiftKey) { ev.preventDefault(); scope.send(); }
            };
            function scrollChatBottom() {
                $timeout(function () {
                    var el = document.getElementById('aiBubbleChatLog');
                    if (el) el.scrollTop = el.scrollHeight;
                }, 0);
            }

            // ── monitor / suggestions ───────────────────────────────────
            scope.setMode = function () { aiBubbleService.setMode(aiBubbleService.state.mode); };
            scope.toggleSource = function (src) {
                if (!src || !src.available) return;
                aiBubbleService.toggleSource(src.id, !src.enabled);
            };
            scope.analyseNow = function () {
                scope.busy = true;
                aiBubbleService.analyseNow()['finally'](function () {
                    scope.busy = false;
                    aiBubbleService.loadMonitorState();
                });
            };
            scope.ack = function (sg) { aiBubbleService.ackSuggestion(aiBubbleService.pickField(sg, 'Id')); };
            scope.snooze = function (sg) { aiBubbleService.snoozeSuggestion(aiBubbleService.pickField(sg, 'Id'), 30); };
            scope.openInChat = function (sg) { aiBubbleService.openInChatWithSuggestion(sg); scrollChatBottom(); };

            scope.evidenceSources = function (sg) {
                var ev = aiBubbleService.pickField(sg, 'Evidence') || [];
                var ids = {};
                ev.forEach(function (e) {
                    var id = aiBubbleService.pickField(e, 'SourceId') || 'unknown';
                    ids[id] = true;
                });
                var arr = Object.keys(ids);
                return arr.length ? arr.join(', ') : 'manual';
            };

            scope.runAction = function (sg, action) {
                var tool = aiBubbleService.pickField(action, 'Tool');
                var argsJson = aiBubbleService.pickField(action, 'ArgumentsJson');
                var label = aiBubbleService.pickField(action, 'Label') || tool || '(no-op)';
                if (!tool) { scope.openInChat(sg); return; }

                var mode = aiBubbleService.state.mode;
                var trusted = (aiBubbleService.state.trustedActions || []).some(function (t) {
                    return (t.Tool || t.tool) === tool && (t.ArgSchemaHash || t.argSchemaHash) === aiBubbleService.pickField(action, 'ArgSchemaHash');
                });

                if (mode === 'auto' && trusted) {
                    invokeTool(tool, argsJson);
                    return;
                }
                if (!$window.confirm('Run ' + tool + ' with these args?\n\n' + (argsJson || '{}'))) return;
                invokeTool(tool, argsJson).then(function () {
                    if (mode === 'semi' && !trusted) {
                        if ($window.confirm('Trust this action so it can run silently in Auto mode? (tool=' + tool + ')')) {
                            $http.post('/api/monitor/trust', { Tool: tool, ArgumentsJson: argsJson, Label: label }).then(function () {
                                aiBubbleService.loadMonitorState();
                            });
                        }
                    }
                });
            };

            function invokeTool(tool, argsJson) {
                scope.busy = true;
                var prompt = 'Call the tool `' + tool + '` with these arguments and summarise: ' + (argsJson || '{}');
                aiBubbleService.state.activeTab = 'chat';
                aiBubbleService.state.open = true;
                return aiBubbleService.sendMessage(prompt)['finally'](function () { scope.busy = false; scrollChatBottom(); });
            }

            // ── engine management (settings tab) ────────────────────────
            scope.add = { kind: 'ollama', endpoint: '', apiKey: '', model: '', label: '', lastError: null, lastSuccess: null };
            scope.scan = { cidr: '', portsText: '11434,5273', results: [], lastError: null, lastSummary: null };
            scope.discover = { results: [], lastError: null, lastSummary: null };
            scope.addBusy = false;
            scope.scanBusy = false;
            scope.discoverBusy = false;

            scope.onAddKindChange = function () {
                scope.add.endpoint = '';
                scope.add.apiKey = '';
                scope.add.lastError = null;
            };

            scope.canAddHost = function () {
                if (!scope.add.kind) return false;
                if (scope.add.kind === 'claude') return !!(scope.add.apiKey && scope.add.apiKey.length > 5);
                return !!(scope.add.endpoint && /^https?:\/\//i.test(scope.add.endpoint));
            };

            scope.doAddHost = function () {
                if (!scope.canAddHost()) return;
                scope.addBusy = true;
                scope.add.lastError = null;
                scope.add.lastSuccess = null;
                aiBubbleService.addEngine({
                    Kind: scope.add.kind,
                    Endpoint: scope.add.endpoint || null,
                    ApiKey: scope.add.apiKey || null,
                    Model: scope.add.model || null,
                    Label: scope.add.label || null
                }).then(function (added) {
                    scope.add.lastSuccess = (added && added.id) || 'engine';
                    scope.add.endpoint = '';
                    scope.add.apiKey = '';
                    scope.add.model = '';
                    scope.add.label = '';
                }, function (err) {
                    scope.add.lastError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
                })['finally'](function () { scope.addBusy = false; });
            };

            scope.removeEngine = function (e) {
                if (!e || !e.id) return;
                if (!$window.confirm('Remove engine "' + e.id + '" from local config?\n\nThis does NOT touch the base appstatus.config or env-var registrations.')) return;
                aiBubbleService.removeEngine(e.id);
            };

            scope.doScan = function () {
                if (!scope.scan.cidr) return;
                var portsText = (scope.scan.portsText || '11434,5273').trim();
                var ports = portsText.split(',').map(function (p) { return parseInt(p.trim(), 10); }).filter(function (p) { return p > 0 && p < 65536; });

                // Warn + confirm before any LAN probing.
                var msg = 'About to probe ' + scope.scan.cidr + ' on ports [' + ports.join(', ') + '].\n\n' +
                          'This is safe on dev / home networks. On corporate networks it may trigger IDS alerts.\n\n' +
                          'Continue?';
                if (!$window.confirm(msg)) return;

                scope.scanBusy = true;
                scope.scan.lastError = null;
                scope.scan.lastSummary = null;
                scope.scan.results = [];

                aiBubbleService.scanEngines(scope.scan.cidr, ports).then(function (data) {
                    var results = (data && data.discovered) || [];
                    // Pre-pick the first model on each result so the Add button
                    // is immediately enabled — the dropdown lets the operator
                    // change it before clicking.
                    results.forEach(function (r) { r.pickedModel = (r.models && r.models[0]) || ''; });
                    scope.scan.results = results;
                    scope.scan.lastSummary = 'probed ' + (data && data.scanned) + ' host(s) on [' + ((data && data.ports) || []).join(',') + '] · found ' + results.length;
                }, function (err) {
                    scope.scan.lastError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
                })['finally'](function () { scope.scanBusy = false; });
            };

            scope.doDiscover = function () {
                scope.discoverBusy = true;
                scope.discover.lastError = null;
                scope.discover.lastSummary = null;
                scope.discover.results = [];

                aiBubbleService.discoverEngines().then(function (data) {
                    var raw = (data && data.discovered) || [];
                    // Filter out adapter-error rows (they have an `error` field
                    // and no `url`) but record them as a hint at the top of
                    // the section.
                    var ok = raw.filter(function (r) { return r.url; });
                    var errs = raw.filter(function (r) { return !r.url && r.error; });
                    ok.forEach(function (r) { r.pickedModel = (r.models && r.models[0]) || ''; });
                    scope.discover.results = ok;
                    var summary = 'asked ' + ((data && data.adapters) || []).length + ' adapter(s) · found ' + ok.length;
                    if (errs.length) summary += ' · ' + errs.length + ' adapter(s) errored';
                    scope.discover.lastSummary = summary;
                }, function (err) {
                    scope.discover.lastError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
                })['finally'](function () { scope.discoverBusy = false; });
            };

            scope.adoptScanResult = function (r) {
                if (!r || r.adopted) return;
                var model = r.pickedModel || (r.models && r.models[0]) || '';
                if (!model) return;
                aiBubbleService.addEngine({
                    Kind: r.kind,
                    Endpoint: r.url,
                    Model: model,
                    Label: r.kind + ' · ' + r.url + ' · ' + model
                }).then(function () { r.adopted = true; }, function (err) {
                    var target = r.adapter ? scope.discover : scope.scan;
                    target.lastError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
                });
            };

            // Register one picker entry per model returned by the host so the
            // operator doesn't have to "Add → pick model → Add → pick model"
            // N times. Each entry is a separate AiEngineCfg, so the picker
            // shows every model as a switchable option in the dropdown.
            scope.adoptAllScanModels = function (r) {
                if (!r || r.adoptedAll) return;
                var models = (r.models || []).filter(function (m) { return !!m; });
                if (!models.length) return;
                r.adoptingAll = true;
                var target = r.adapter ? scope.discover : scope.scan;
                target.lastError = null;
                var chain = models.reduce(function (p, m) {
                    return p.then(function () {
                        return aiBubbleService.addEngine({
                            Kind: r.kind,
                            Endpoint: r.url,
                            Model: m,
                            Label: r.kind + ' · ' + r.url + ' · ' + m
                        });
                    });
                }, $q.when());
                chain.then(function () {
                    r.adopted = true;
                    r.adoptedAll = true;
                }, function (err) {
                    target.lastError = (err && err.data && err.data.error) || ('HTTP ' + (err && err.status));
                })['finally'](function () { r.adoptingAll = false; });
            };

            // ── pending prompt from "Open in chat" ──────────────────────
            scope.$watch(function () { return aiBubbleService.state.pendingPrompt; }, function (v) {
                if (!v) return;
                scope.ui.chatInput = v;
                aiBubbleService.state.pendingPrompt = null;
                $timeout(function () {
                    var ta = document.querySelector('.ai-panel .chat-input textarea');
                    if (ta) ta.focus();
                }, 50);
            });

            // ── resize handle (top-left grip) ───────────────────────────
            // Panel is anchored BR (right:18, bottom:70), so the user grips
            // the TL corner and drags up-and-left to grow the panel. We
            // translate the mouse delta into width/height deltas and clamp
            // to min/max. Final size is persisted to localStorage so the
            // panel reopens at the operator's preferred dimensions.
            var SIZE_STORAGE_KEY = 'ai.bubble.size.v1';
            scope.panelStyle = {};

            try {
                var savedSize = JSON.parse(localStorage.getItem(SIZE_STORAGE_KEY) || 'null');
                if (savedSize && savedSize.w && savedSize.h) {
                    scope.panelStyle = { width: savedSize.w + 'px', height: savedSize.h + 'px' };
                }
            } catch (e) { /* ignore */ }

            // app.js attaches $rootScope.safeApply — phase-aware $apply wrapper.
            // Inherited on every child scope. Falls back to a manual digest
            // check if for some reason safeApply isn't defined yet.
            function applySafely(fn) {
                try {
                    if (typeof scope.safeApply === 'function') { scope.safeApply(fn); return; }
                    var phase = scope.$root && scope.$root.$$phase;
                    if (phase === '$apply' || phase === '$digest') { fn(); }
                    else { scope.$apply(fn); }
                } catch (err) {
                    // SwAllow — a thrown error in a mousemove handler would
                    // otherwise break Angular's digest and disable every
                    // ng-click on the page (Send, Add, etc.).
                    if (window && window.console && window.console.warn) console.warn('aiBubble applySafely:', err);
                }
            }

            scope.onResizeStart = function (ev) {
                if (!ev) return;
                ev.preventDefault();

                var panel = ev.currentTarget && ev.currentTarget.parentElement;
                if (!panel) return;
                var rect = panel.getBoundingClientRect();
                var startW = rect.width, startH = rect.height;
                var startX = ev.clientX, startY = ev.clientY;

                function onMove(e) {
                    try {
                        // Drag up-left grows panel; right/down shrinks. Mouse
                        // delta is inverted because the panel is anchored BR.
                        var w = startW + (startX - e.clientX);
                        var h = startH + (startY - e.clientY);
                        var maxW = Math.max(360, window.innerWidth - 36);
                        var maxH = Math.max(320, window.innerHeight - 100);
                        w = Math.max(360, Math.min(maxW, w));
                        h = Math.max(320, Math.min(maxH, h));
                        applySafely(function () {
                            scope.panelStyle = { width: w + 'px', height: h + 'px' };
                        });
                    } catch (err) {
                        // Defensive: a throw here would otherwise propagate up
                        // and break Angular's digest — disabling every ng-click
                        // on the page (Send included). Bug we hit on first
                        // ship: scope.$applyAsync wasn't a function on this
                        // Angular build, and the cascade silently broke chat.
                        if (window && window.console && window.console.warn) console.warn('aiBubble resize onMove:', err);
                    }
                }

                function onUp() {
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup', onUp);
                    try {
                        var w = parseInt(scope.panelStyle.width, 10);
                        var h = parseInt(scope.panelStyle.height, 10);
                        if (w && h) localStorage.setItem(SIZE_STORAGE_KEY, JSON.stringify({ w: w, h: h }));
                    } catch (e) { /* ignore */ }
                }

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup', onUp);
            };

            // ── keyboard shortcut Ctrl+/ to toggle ─────────────────────
            function onGlobalKey(ev) {
                if ((ev.ctrlKey || ev.metaKey) && ev.key === '/') {
                    ev.preventDefault();
                    scope.$applyAsync(function () { aiBubbleService.toggleOpen(); });
                }
            }
            document.addEventListener('keydown', onGlobalKey);
            scope.$on('$destroy', function () { document.removeEventListener('keydown', onGlobalKey); });
        }
    };
});

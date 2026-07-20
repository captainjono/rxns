/// <reference path="../../support.js" />

// AI diagnostic chat pane. Talks to the multi-engine /api/ai surface — Claude,
// Ollama, Foundry, or any IAiChatEngine an augmentation has registered. The
// engine for THIS pane is persisted per-tab in localStorage so the operator
// can keep a local-llm pane next to a Claude pane on a second monitor.
angular.module('support').controller('ClaudeChatCtrl', function ($scope, $http, $timeout) {

    var ENGINE_STORAGE_KEY = 'ai.selectedEngine.support';
    var MODEL_STORAGE_KEY  = 'ai.selectedModel.support';   // keyed by engineId in a JSON map

    $scope.messages = [];          // AiChatMessage[] sent on each request
    $scope.transcript = [];        // [{type, role, body, html?, name?}]
    $scope.engineInfo = null;
    $scope.meta = { state: 'loading', engines: [], defaultEngine: '', toolCount: 0, toolNames: '' };
    $scope.busy = false;
    $scope.composerHint = 'Enter to send · Shift+Enter for newline';
    $scope.composerHintError = false;
    $scope.canSend = false;
    $scope.prompt = '';
    $scope.selectedEngineId = null;
    $scope.selectedEngineLabel = '';
    $scope.selectedModel = null;
    $scope.models = { list: [], loading: false, defaultModel: null };

    function scrollToBottom() {
        $timeout(function () {
            var el = document.getElementById('claudeChatArea');
            if (el) { el.scrollTop = el.scrollHeight; }
        }, 0);
    }

    function addUser(text) {
        $scope.transcript.push({ type: 'message', role: 'user', avatar: 'U', body: text });
        scrollToBottom();
    }
    function addAssistant(text) {
        $scope.transcript.push({ type: 'message', role: 'assistant', avatar: 'A', body: text });
        scrollToBottom();
    }
    function addTool(name, content) {
        var preview = content || '';
        if (preview.length > 600) { preview = preview.substring(0, 600) + '...'; }
        $scope.transcript.push({ type: 'tool', role: 'tool', avatar: '*', name: name, body: preview });
        scrollToBottom();
    }
    function addToolCalls(calls, secondTurnPending) {
        $scope.transcript.push({
            type: 'toolcalls',
            role: 'assistant',
            avatar: 'A',
            note: secondTurnPending
                ? 'The model wants to call more tools — restart the conversation to continue, or send a follow-up to confirm:'
                : 'Calling tools...',
            calls: calls.map(function (c) { return { name: c.Name || c.name, args: c.ArgumentsJson || c.argumentsJson }; })
        });
        scrollToBottom();
    }
    function addThinking() {
        var id = 'thinking-' + Date.now();
        $scope.transcript.push({ type: 'thinking', id: id, role: 'assistant', avatar: 'A' });
        scrollToBottom();
        return id;
    }
    function removeThinking(id) {
        for (var i = $scope.transcript.length - 1; i >= 0; i--) {
            if ($scope.transcript[i].id === id) { $scope.transcript.splice(i, 1); return; }
        }
    }
    function addError(msg, replaceId) {
        if (replaceId) { removeThinking(replaceId); }
        $scope.transcript.push({ type: 'error', role: 'assistant', avatar: 'A', body: msg });
        scrollToBottom();
    }

    $scope.engineLabel = function (e) {
        if (!e) return '';
        var avail = e.available ? '' : ' (unavailable)';
        var cost = e.cost === 'free' ? ' · free' : (e.cost === 'paid' ? ' · paid' : '');
        return (e.label || (e.kind + ' · ' + e.model)) + cost + avail;
    };

    $scope.reloadInfo = function () {
        $scope.meta.state = 'loading';
        $http.get('/api/ai/info').then(function (r) {
            $scope.engineInfo = r.data || {};
            $scope.meta.engines = $scope.engineInfo.engines || [];
            $scope.meta.defaultEngine = $scope.engineInfo.defaultEngine || '';
            $scope.meta.state = 'ok';

            var tools = ($scope.engineInfo.tools || []).map(function (t) { return t.name; });
            $scope.meta.toolCount = tools.length;
            $scope.meta.toolNames = tools.join(', ');
            $scope.readOnly = !!$scope.engineInfo.readOnly;

            // Per-pane preference wins over the server default. Fall back to the
            // server's default; if that engine is unavailable, drop to the first
            // available one so the picker never lands on a dead engine.
            var stored = null;
            try { stored = localStorage.getItem(ENGINE_STORAGE_KEY); } catch (e) { stored = null; }
            var pick = stored || $scope.meta.defaultEngine;
            var resolved = resolveEngineId(pick) || resolveFirstAvailable();
            $scope.selectedEngineId = resolved;
            $scope.selectedEngineLabel = labelFor(resolved);
            loadModelsForEngine(resolved);

            updateComposerState();
        }, function (err) {
            $scope.meta = { state: 'error', engines: [], defaultEngine: '', toolCount: 0, toolNames: '', error: 'HTTP ' + (err && err.status) };
            $scope.canSend = false;
            $scope.composerHint = 'No AI engine configured. Set CLAUDE_API_KEY, OLLAMA_URL, or FOUNDRY_URL to enable.';
            $scope.composerHintError = true;
        });
    };

    function resolveEngineId(id) {
        if (!id) return null;
        for (var i = 0; i < $scope.meta.engines.length; i++) {
            if ($scope.meta.engines[i].id === id) return id;
        }
        return null;
    }
    function resolveFirstAvailable() {
        for (var i = 0; i < $scope.meta.engines.length; i++) {
            if ($scope.meta.engines[i].available) return $scope.meta.engines[i].id;
        }
        return $scope.meta.engines.length ? $scope.meta.engines[0].id : null;
    }
    function labelFor(id) {
        for (var i = 0; i < $scope.meta.engines.length; i++) {
            if ($scope.meta.engines[i].id === id) return $scope.engineLabel($scope.meta.engines[i]);
        }
        return id || '(none)';
    }
    function selectedEngine() {
        for (var i = 0; i < $scope.meta.engines.length; i++) {
            if ($scope.meta.engines[i].id === $scope.selectedEngineId) return $scope.meta.engines[i];
        }
        return null;
    }

    $scope.onEngineChange = function () {
        try { localStorage.setItem(ENGINE_STORAGE_KEY, $scope.selectedEngineId || ''); } catch (e) { /* ignore */ }
        $scope.selectedEngineLabel = labelFor($scope.selectedEngineId);
        loadModelsForEngine($scope.selectedEngineId);
        updateComposerState();
    };

    $scope.onModelChange = function () {
        try {
            var raw = localStorage.getItem(MODEL_STORAGE_KEY);
            var map = raw ? JSON.parse(raw) : {};
            map[$scope.selectedEngineId] = $scope.selectedModel || '';
            localStorage.setItem(MODEL_STORAGE_KEY, JSON.stringify(map));
        } catch (e) { /* ignore */ }
    };

    function loadModelsForEngine(engineId) {
        $scope.models = { list: [], loading: !!engineId, defaultModel: null };
        $scope.selectedModel = null;
        if (!engineId) return;

        $http.get('/api/ai/engines/' + encodeURIComponent(engineId) + '/models').then(function (r) {
            var d = r.data || {};
            var list = d.models || [];
            var fallback = d.defaultModel || null;
            // Ensure the engine's default is always selectable even if the server
            // didn't include it in models[] (Foundry sometimes lists only loaded).
            if (fallback && list.indexOf(fallback) === -1) list = [fallback].concat(list);

            $scope.models = { list: list, loading: false, defaultModel: fallback };

            // Restore stored selection if it's still in the list; otherwise pick
            // the engine's default; otherwise the first list entry.
            var stored = null;
            try {
                var raw = localStorage.getItem(MODEL_STORAGE_KEY);
                if (raw) stored = (JSON.parse(raw) || {})[engineId];
            } catch (e) { stored = null; }
            $scope.selectedModel =
                (stored && list.indexOf(stored) >= 0) ? stored :
                (fallback && list.indexOf(fallback) >= 0) ? fallback :
                (list.length ? list[0] : null);
        }, function () {
            $scope.models = { list: [], loading: false, defaultModel: null };
            $scope.selectedModel = null;
        });
    }

    function updateComposerState() {
        var eng = selectedEngine();
        if (!eng) {
            $scope.canSend = false;
            $scope.composerHint = 'No engine available — configure one (CLAUDE_API_KEY / OLLAMA_URL / FOUNDRY_URL).';
            $scope.composerHintError = true;
        } else if (!eng.available) {
            $scope.canSend = false;
            $scope.composerHint = eng.label + ' is unavailable right now. Pick a different engine or start the backend.';
            $scope.composerHintError = true;
        } else {
            $scope.canSend = true;
            $scope.composerHintError = false;
            $scope.composerHint = 'Enter to send · Shift+Enter for newline · routing through ' + $scope.engineLabel(eng);
        }
    }

    $scope.send = function () {
        if ($scope.busy || !$scope.canSend) { return; }
        var text = ($scope.prompt || '').trim();
        if (!text) { return; }
        $scope.prompt = '';

        $scope.busy = true;
        $scope.messages.push({ Role: 'user', Content: text });
        addUser(text);
        var thinkingId = addThinking();

        var payload = {
            Messages: $scope.messages,
            ResolveTools: true,
            Engine: $scope.selectedEngineId,
            Model: $scope.selectedModel || null
        };

        $http.post('/api/ai/chat', payload).then(function (r) {
            removeThinking(thinkingId);
            var data = r.data || {};
            var first = data.firstTurn || data.FirstTurn;
            if (first && (first.ToolCalls || first.toolCalls)) {
                var tc = first.ToolCalls || first.toolCalls || [];
                if (tc.length) { addToolCalls(tc, false); }
            }
            var firstText = first && (first.AssistantText || first.assistantText);
            if (firstText) {
                $scope.messages.push({ Role: 'assistant', Content: firstText });
                addAssistant(firstText);
            }

            var resolved = data.toolsResolved || data.ToolsResolved;
            var msgsAfter = data.messagesAfter || data.MessagesAfter;
            if (resolved && msgsAfter) {
                for (var i = 0; i < msgsAfter.length; i++) {
                    var m = msgsAfter[i];
                    var role = m.Role || m.role;
                    if (role === 'tool') {
                        addTool(m.ToolName || m.toolName, m.Content || m.content);
                    }
                }
                $scope.messages.length = 0;
                for (var j = 0; j < msgsAfter.length; j++) { $scope.messages.push(msgsAfter[j]); }

                var second = data.secondTurn || data.SecondTurn;
                var secondText = second && (second.AssistantText || second.assistantText);
                if (secondText) {
                    $scope.messages.push({ Role: 'assistant', Content: secondText });
                    addAssistant(secondText);
                }
                var secondCalls = second && (second.ToolCalls || second.toolCalls);
                if (secondCalls && secondCalls.length) {
                    addToolCalls(secondCalls, true);
                }
            }
        }, function (err) {
            addError('HTTP ' + (err && err.status) + ': ' + ((err && err.data) || ''), thinkingId);
        })['finally'](function () {
            $scope.busy = false;
        });
    };

    $scope.onKey = function (ev) {
        if (!ev) { return; }
        if (ev.keyCode === 13 && !ev.shiftKey) {
            ev.preventDefault();
            $scope.send();
        }
    };

    // If the operator clicked "Open in chat" on a monitor suggestion, the
    // monitor pane stashes a seeded prompt in sessionStorage. Pick it up
    // here, drop it into the composer, and clear the seed so a manual
    // navigation later doesn't replay the same prompt.
    try {
        var seeded = sessionStorage.getItem('monitor.openInChat');
        if (seeded) {
            $scope.prompt = seeded;
            sessionStorage.removeItem('monitor.openInChat');
        }
    } catch (e) { /* ignore — sessionStorage can throw in private mode */ }

    $scope.reloadInfo();
});

/// <reference path="../../support.js" />

// Monitor pane — ambient AI suggestions for the live system.
// Reads /api/monitor/state, lets the operator toggle source streams + mode,
// runs heal/fix actions, and subscribes to live suggestion pushes over the
// existing eventHubService SignalR channel.
angular.module('support').controller('MonitorCtrl', function ($scope, $http, $window, $state, eventHubService) {

    $scope.loading = true;
    $scope.error = null;
    $scope.busy = false;
    $scope.mode = 'manual';
    $scope.sources = [];
    $scope.suggestions = [];
    $scope.trusted = [];

    function pickField(obj, key) {
        if (!obj) return undefined;
        // server has both PascalCase (some serialisers) and camelCase; tolerate either.
        if (obj[key] !== undefined) return obj[key];
        var lc = key.charAt(0).toLowerCase() + key.slice(1);
        return obj[lc];
    }

    $scope.severityClass = function (s) {
        var sev = (pickField(s, 'Severity') || 'info').toLowerCase();
        return 'sev-' + sev;
    };

    $scope.reload = function () {
        $scope.loading = true;
        $scope.error = null;
        $http.get('/api/monitor/state').then(function (r) {
            $scope.loading = false;
            var d = r.data || {};
            $scope.mode = d.mode || 'manual';
            $scope.sources = d.sources || [];
            $scope.suggestions = d.suggestions || [];
            $scope.trusted = d.trustedActions || [];
        }, function (err) {
            $scope.loading = false;
            $scope.error = 'GET /api/monitor/state failed: HTTP ' + (err && err.status);
        });
    };

    $scope.onModeChange = function () {
        $scope.busy = true;
        $http.post('/api/monitor/mode', { Mode: $scope.mode }).then(function () {
            $scope.busy = false;
        }, function () { $scope.busy = false; $scope.reload(); });
    };

    $scope.toggleSource = function (src) {
        if (!src || !src.available) return;
        var next = !src.enabled;
        src.enabled = next;
        $http.post('/api/monitor/sources/' + encodeURIComponent(src.id), { Enabled: next })
            .catch(function () { src.enabled = !next; });
    };

    $scope.ack = function (s) {
        var id = pickField(s, 'Id');
        $http.post('/api/monitor/suggestions/' + encodeURIComponent(id) + '/ack').then(function () {
            removeSuggestion(id);
        });
    };

    $scope.snooze = function (s) {
        var id = pickField(s, 'Id');
        $http.post('/api/monitor/suggestions/' + encodeURIComponent(id) + '/snooze', { Minutes: 30 }).then(function () {
            removeSuggestion(id);
        });
    };

    function removeSuggestion(id) {
        for (var i = $scope.suggestions.length - 1; i >= 0; i--) {
            if (pickField($scope.suggestions[i], 'Id') === id) {
                $scope.suggestions.splice(i, 1);
            }
        }
    }

    $scope.isTrusted = function (a) {
        var tool = pickField(a, 'Tool');
        var hash = pickField(a, 'ArgSchemaHash');
        if (!tool || !hash) return false;
        for (var i = 0; i < $scope.trusted.length; i++) {
            var t = $scope.trusted[i];
            if (pickField(t, 'Tool') === tool && pickField(t, 'ArgSchemaHash') === hash) return true;
        }
        return false;
    };

    $scope.runAction = function (suggestion, action) {
        var tool = pickField(action, 'Tool');
        var argsJson = pickField(action, 'ArgumentsJson');
        var label = pickField(action, 'Label') || tool || '(no-op)';

        if (!tool) {
            // Informational action — nothing to invoke. Open chat with the suggestion.
            $scope.openInChat(suggestion);
            return;
        }

        var trusted = $scope.isTrusted(action);
        var mode = $scope.mode;

        // Mode gate:
        //   manual: always confirm
        //   semi:   confirm first, then offer to trust after a successful run
        //   auto:   if trusted, run silently; otherwise prompt
        if (mode === 'auto' && trusted) {
            invokeTool(tool, argsJson);
            return;
        }

        var msg = 'Run ' + tool + ' with these args?\n\n' + (argsJson || '{}');
        if (!$window.confirm(msg)) return;

        invokeTool(tool, argsJson).then(function () {
            if (mode === 'semi' && !trusted) {
                if ($window.confirm('Trust this action so it can run silently in Auto mode? (tool=' + tool + ')')) {
                    $http.post('/api/monitor/trust', { Tool: tool, ArgumentsJson: argsJson, Label: label }).then(function () {
                        $scope.reload();
                    });
                }
            }
        });
    };

    function invokeTool(tool, argsJson) {
        // Tools are invoked via the AI chat surface so the model can reason about
        // results. This sends a one-shot prompt: "Call <tool>(<args>) and summarise".
        // The chat controller auto-resolves the tool round-trip server-side.
        $scope.busy = true;
        var prompt = 'Call the tool `' + tool + '` with these arguments and summarise the result for the operator: ' + (argsJson || '{}');
        return $http.post('/api/ai/chat', {
            Messages: [{ Role: 'user', Content: prompt }],
            ResolveTools: true
        }).then(function () {
            $scope.busy = false;
        }, function () {
            $scope.busy = false;
        });
    }

    $scope.revoke = function (t) {
        var tool = pickField(t, 'Tool');
        var hash = pickField(t, 'ArgSchemaHash');
        $http({
            method: 'DELETE',
            url: '/api/monitor/trust',
            data: { Tool: tool, ArgSchemaHash: hash },
            headers: { 'Content-Type': 'application/json' }
        }).then($scope.reload);
    };

    $scope.openInChat = function (s) {
        // For now, route to the chat tab. Pre-populating the prompt is best-effort
        // via sessionStorage; the chat controller picks it up on load.
        try {
            var title = pickField(s, 'Title') || 'this finding';
            var rationale = pickField(s, 'Rationale') || '';
            sessionStorage.setItem('monitor.openInChat',
                'Investigate: ' + title + '\n\n' + rationale);
        } catch (e) { /* ignore */ }
        $state.go('claudeChat');
    };

    $scope.analyseNow = function () {
        $scope.busy = true;
        $http.post('/api/monitor/analyse-now').then(function () {
            $scope.busy = false;
            $scope.reload();
        }, function () { $scope.busy = false; });
    };

    $scope.popOut = function () {
        // Same URL, separate window — lets the operator park monitor mode on a
        // second display while doing the day job in the main window.
        var url = window.location.origin + window.location.pathname + '#/monitor';
        $window.open(url, 'rxns-monitor', 'width=900,height=700,menubar=no,toolbar=no');
    };

    // Live updates via SignalR — pushed by MonitorService whenever a new
    // suggestion is raised. The envelope is { kind, suggestion }.
    var sub = eventHubService.monitorEvent.subscribe(function (env) {
        if (!env) return;
        if ((env.kind || env.Kind) !== 'suggestionRaised') return;
        var s = env.suggestion || env.Suggestion;
        if (!s) return;
        // Prepend so newest is first
        $scope.$applyAsync(function () { $scope.suggestions.unshift(s); });
    });
    $scope.$on('$destroy', function () { try { sub.dispose(); } catch (e) { /* ignore */ } });

    $scope.reload();
});

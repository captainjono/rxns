angular.module('systemstatus').controller('RemoteShellCtrl', function ($scope, $timeout, $stateParams, testArenaApi, eventHubService) {

    var MAX_LINES = 500;

    // Persisted command history. Survives page refresh via localStorage so the
    // user can recall recent commands across sessions, like a real terminal.
    var HISTORY_KEY = 'bfg.remoteShell.history';
    var HISTORY_MAX = 200;

    function loadHistory() {
        try {
            var raw = window.localStorage && window.localStorage.getItem(HISTORY_KEY);
            if (!raw) { return []; }
            var parsed = JSON.parse(raw);
            return Array.isArray(parsed) ? parsed : [];
        } catch (e) { return []; }
    }

    function saveHistory(arr) {
        try {
            if (window.localStorage) {
                window.localStorage.setItem(HISTORY_KEY, JSON.stringify(arr));
            }
        } catch (e) { /* quota / private mode — silently degrade to in-memory */ }
    }

    $scope.history = loadHistory();
    $scope.historyIdx = $scope.history.length; // pointing past the end = "fresh input"
    var historyDraft = '';

    function recordHistory(cmd) {
        if (!cmd) { return; }
        var last = $scope.history[$scope.history.length - 1];
        if (cmd === last) {
            $scope.historyIdx = $scope.history.length;
            historyDraft = '';
            return;
        }
        $scope.history.push(cmd);
        while ($scope.history.length > HISTORY_MAX) { $scope.history.shift(); }
        saveHistory($scope.history);
        $scope.historyIdx = $scope.history.length;
        historyDraft = '';
    }

    $scope.workers = [];           // flattened [{route, display}]
    // Deep-link: accept ?route= from testArena shortcut icon. If present it is
    // applied up-front so the <select> shows the pre-chosen worker; subsequent
    // status-hub events won't overwrite it (guarded in statusInitial / Updates).
    $scope.selectedRoute = ($stateParams && $stateParams.route) ? $stateParams.route : null;
    $scope.command = '';
    $scope.lines = [];             // [{text, cls}]
    // Per-route working directory, populated from RemoteShellResult.cwd. The
    // worker's PersistentShell carries shell state across commands (cd, env,
    // aliases) and reports its post-cmd cwd back; we render that as the
    // input prompt so `{cwd}> cmd` reads like a real terminal.
    $scope.cwdByRoute = {};

    function appendLine(text, cls) {
        // split multi-line output into individual lines for wrapping/auto-scroll
        var parts = (text == null ? '' : ('' + text)).split(/\r?\n/);
        for (var i = 0; i < parts.length; i++) {
            $scope.lines.push({ text: parts[i], cls: cls || 'out' });
        }
        while ($scope.lines.length > MAX_LINES) {
            $scope.lines.shift();
        }
        $timeout(function () {
            var el = document.getElementById('remoteShellTerminal');
            if (el) { el.scrollTop = el.scrollHeight; }
        }, 0);
    }

    function flattenMachines(machines) {
        var out = [];
        if (!machines) return out;
        for (var i = 0; i < machines.length; i++) {
            var m = machines[i];
            var tenant = m.tenant || m.Tenant || 'NoTenant';
            var systems = m.systems || m.Systems || [];
            for (var j = 0; j < systems.length; j++) {
                var s = systems[j];
                var sys = s.system || s.System || {};
                var name = sys.systemName || sys.SystemName;
                if (!name) continue;
                out.push({
                    route: tenant + '\\' + name,
                    display: tenant + '\\' + name
                });
            }
        }
        return out;
    }

    function mergeWorker(entry) {
        if (!entry || !entry.route) return;
        for (var i = 0; i < $scope.workers.length; i++) {
            if ($scope.workers[i].route === entry.route) { return; }
        }
        $scope.workers.push(entry);
    }

    // Seed the list with the deep-linked route so the <select> renders its label
    // immediately, even before statusInitial arrives from the event hub.
    if ($scope.selectedRoute) {
        mergeWorker({ route: $scope.selectedRoute, display: $scope.selectedRoute });
    }

    // Track subscriptions so we can dispose them on $destroy. Without this the
    // RemoteShellResult handler leaks across route changes — on the appstatus
    // page (post-navigation) it fires with a stale $scope and
    // `$scope.lines.push` throws "cannot read properties of undefined".
    var subscriptions = [];

    subscriptions.push(eventHubService.statusInitial.subscribe(function (appStatusByTenant) {
        $scope.$apply(function () {
            var list = flattenMachines(appStatusByTenant);
            for (var i = 0; i < list.length; i++) { mergeWorker(list[i]); }
            if (!$scope.selectedRoute && $scope.workers.length > 0) {
                $scope.selectedRoute = $scope.workers[0].route;
            }
        });
    }));

    subscriptions.push(eventHubService.statusUpdates.subscribe(function (appStatusByTenant) {
        $scope.$apply(function () {
            var list = flattenMachines(appStatusByTenant);
            for (var i = 0; i < list.length; i++) { mergeWorker(list[i]); }
            if (!$scope.selectedRoute && $scope.workers.length > 0) {
                $scope.selectedRoute = $scope.workers[0].route;
            }
        });
    }));

    function b64utf8(str) {
        try {
            return btoa(unescape(encodeURIComponent(str)));
        } catch (e) {
            return btoa(str);
        }
    }

    $scope.promptFor = function (route) {
        var cwd = $scope.cwdByRoute[route];
        return cwd ? cwd : (route || '');
    };

    $scope.runCommand = function () {
        var route = $scope.selectedRoute;
        var cmd = ($scope.command || '').trim();
        if (!route || !cmd) { return; }

        // Local UI command — wipe terminal output. Mirrors `cls` in cmd.exe / `clear`
        // on bash but only clears the on-screen buffer; no shell side effect.
        // Still recorded to history — user typed it and may want to recall it.
        if (cmd === 'cls' || cmd === 'clear') {
            $scope.lines = [];
            $scope.command = '';
            recordHistory(cmd);
            return;
        }

        appendLine($scope.promptFor(route) + '> ' + cmd, 'prompt');

        var encoded = b64utf8(cmd);
        testArenaApi.sendCommand(route, 'RemoteShellCmd ' + encoded);

        $scope.command = '';
        recordHistory(cmd);
    };

    $scope.onKey = function (ev) {
        if (!ev) { return; }
        // Up arrow — walk backwards through history. Stash the in-progress
        // text on first press so down-arrow can restore it.
        if (ev.keyCode === 38) {
            ev.preventDefault();
            if ($scope.history.length === 0) { return; }
            if ($scope.historyIdx === $scope.history.length) {
                historyDraft = $scope.command || '';
            }
            if ($scope.historyIdx > 0) { $scope.historyIdx--; }
            $scope.command = $scope.history[$scope.historyIdx] || '';
            return;
        }
        // Down arrow — walk forwards. Past the newest entry, restore the draft.
        if (ev.keyCode === 40) {
            ev.preventDefault();
            if ($scope.historyIdx < $scope.history.length) { $scope.historyIdx++; }
            $scope.command = ($scope.historyIdx === $scope.history.length)
                ? historyDraft
                : ($scope.history[$scope.historyIdx] || '');
            return;
        }
        if (ev.keyCode === 13) {
            $scope.runCommand();
        }
    };

    $scope.clearOutput = function () {
        $scope.lines = [];
    };

    function typeName(e) {
        return (e && (e.$type || e.T || e.t || e.type)) || '';
    }

    subscriptions.push(testArenaApi.updates.subscribe(function (ev) {
        try {
            var e = (typeof ev === 'string') ? angular.fromJson(ev) : ev;
            var tn = typeName(e);
            if (!tn) { return; }

            // Streaming partials arrive as RemoteShellPartialResult during a
            // long-running cmd; the terminal RemoteShellResult carries exit +
            // cwd at the end. Append both stdout/stderr exactly as they arrive
            // so the UI feels live; only the terminal frame updates the prompt.
            var isPartial = tn.indexOf('RemoteShellPartialResult') !== -1;
            var isFinal = !isPartial && tn.indexOf('RemoteShellResult') !== -1;
            if (!isPartial && !isFinal) { return; }

            $scope.$apply(function () {
                var stdout = e.stdout || e.Stdout || '';
                var stderr = e.stderr || e.Stderr || '';

                if (stdout) { appendLine(stdout, 'out'); }
                if (stderr) { appendLine(stderr, 'err'); }

                if (isFinal) {
                    var exitCode = (e.exitCode != null) ? e.exitCode : e.ExitCode;
                    var cwd = e.cwd || e.Cwd;
                    if (cwd && $scope.selectedRoute) {
                        $scope.cwdByRoute[$scope.selectedRoute] = cwd;
                    }
                    if (exitCode != null && exitCode !== 0) {
                        appendLine('[exit ' + exitCode + ']', 'hdr');
                    }
                }
            });
        } catch (e) { /* ignore malformed */ }
    }));

    // Dispose all subscriptions when the controller is torn down (route change,
    // page reload). Otherwise handlers keep firing against a destroyed $scope
    // and crash the next controller that lands on `testArenaApi.updates`.
    $scope.$on('$destroy', function () {
        for (var i = 0; i < subscriptions.length; i++) {
            try { subscriptions[i].dispose(); } catch (e) { /* best-effort */ }
        }
        subscriptions = [];
    });
});

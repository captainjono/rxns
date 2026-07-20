/// <reference path="../../systemstatus.js" />

// Logs dashboard for the support portal. Consumes the AppStatus HTTP API
// (cards + drilldown) and the /appStatusLogHub SignalR hub for live updates.
//
// Wire-compatible with the vanilla Rxns.Delivery support-insights page —
// this is just the Angular 1.x rehoming so the partial bundles into the
// portal $templateCache and lives under the existing top nav.
angular.module('systemstatus').controller('SupportInsightsCtrl', function ($scope, $http, $timeout, $stateParams) {

    var MAX_ENTRIES = 500;

    $scope.systemName = ($stateParams && $stateParams.system) || 'insights';
    $scope.entries = [];          // newest-first
    $scope.stats = { ErrorsLast1h: '-', ErrorsLast24h: '-', WarningsLast1h: '-', InfoLast1h: '-' };
    $scope.levels = { Error: true, Warning: true, Information: true, Verbose: false };
    $scope.reporterFilter = '';
    $scope.streamStatus = { cls: 'badge', text: 'disconnected' };
    $scope.expanded = {};
    $scope.notConnected = false;
    $scope.errMessage = null;

    var connection = null;
    var pollTimer = null;

    // --- REST loads ---
    $scope.loadStats = function () {
        $http.get('/api/appstatus/stats', { params: { systemName: $scope.systemName } }).then(function (r) {
            var s = r.data || {};
            $scope.stats = {
                ErrorsLast1h: s.ErrorsLast1h || 0,
                ErrorsLast24h: s.ErrorsLast24h || 0,
                WarningsLast1h: s.WarningsLast1h || 0,
                InfoLast1h: s.InfoLast1h || 0
            };
        }, function () { /* fail-soft; the log loader will surface the user-visible error */ });
    };

    $scope.loadLog = function () {
        $http.get('/api/appstatus/log', { params: { systemName: $scope.systemName, take: 200 } }).then(function (r) {
            var page = r.data || {};
            $scope.entries = (page.Entries || []).slice();
            $scope.notConnected = false;
            $scope.errMessage = null;
        }, function (err) {
            $scope.notConnected = true;
            $scope.errMessage = 'HTTP ' + (err && err.status) + ' from /api/appstatus/log';
        });
    };

    $scope.reloadAll = function () {
        $scope.loadStats();
        $scope.loadLog();
        reconnectStream();
    };

    $scope.onSystemChange = function () {
        var s = ($scope.systemName || '').trim();
        if (!s) { s = 'insights'; }
        $scope.systemName = s;
        $scope.reloadAll();
    };

    // --- SignalR stream ---
    function setStreamBadge(cls, text) {
        $scope.$applyAsync(function () {
            $scope.streamStatus = { cls: 'badge ' + cls, text: text };
        });
    }

    function reconnectStream() {
        if (typeof window.signalR === 'undefined' || !window.signalR.HubConnectionBuilder) {
            startPollingFallback();
            return;
        }
        try {
            if (connection) {
                try { connection.stop(); } catch (e) { /* noop */ }
            }
            connection = new window.signalR.HubConnectionBuilder()
                .withUrl('/appStatusLogHub')
                .withAutomaticReconnect()
                .build();

            connection.on('LogEntry', function (entry) {
                $scope.$applyAsync(function () {
                    $scope.entries.unshift(entry);
                    if ($scope.entries.length > MAX_ENTRIES) { $scope.entries.length = MAX_ENTRIES; }
                });
            });

            connection.onreconnecting(function () { setStreamBadge('warn', 'reconnecting'); });
            connection.onreconnected(function () {
                setStreamBadge('live', 'live');
                connection.invoke('Subscribe', $scope.systemName).catch(function () { });
            });
            connection.onclose(function () {
                setStreamBadge('err', 'closed');
                startPollingFallback();
            });

            connection.start().then(function () {
                setStreamBadge('live', 'live');
                return connection.invoke('Subscribe', $scope.systemName);
            }).catch(function () {
                setStreamBadge('err', 'no signalr');
                startPollingFallback();
            });
        } catch (e) {
            startPollingFallback();
        }
    }

    function startPollingFallback() {
        setStreamBadge('warn', 'polling');
        if (pollTimer) { clearInterval(pollTimer); }
        pollTimer = setInterval(function () {
            $scope.loadStats();
            $scope.loadLog();
        }, 5000);
    }

    // --- View helpers ---
    $scope.visibleEntries = function () {
        var levels = $scope.levels || {};
        var filter = ($scope.reporterFilter || '').toLowerCase();
        var out = [];
        for (var i = 0; i < $scope.entries.length; i++) {
            var e = $scope.entries[i];
            var lvl = (e.Level || '').toLowerCase();
            // bucket fatal/warn aliases into the parent checkbox
            if (lvl === 'fatal') { lvl = 'error'; }
            if (lvl === 'warn') { lvl = 'warning'; }
            if (lvl === 'info') { lvl = 'information'; }
            var key = lvl.charAt(0).toUpperCase() + lvl.slice(1);
            if (!levels[key]) { continue; }
            if (filter && (e.Reporter || '').toLowerCase().indexOf(filter) < 0) { continue; }
            out.push(e);
        }
        return out;
    };

    $scope.levelClass = function (level) {
        var l = (level || 'Info').toLowerCase();
        if (l === 'error' || l === 'fatal') { return 'level-pill level-Error'; }
        if (l === 'warn' || l === 'warning') { return 'level-pill level-Warning'; }
        if (l === 'info' || l === 'information') { return 'level-pill level-Information'; }
        return 'level-pill level-Verbose';
    };

    $scope.toggleStack = function (idx) {
        $scope.expanded[idx] = !$scope.expanded[idx];
    };

    $scope.$on('$destroy', function () {
        if (connection) { try { connection.stop(); } catch (e) { /* noop */ } }
        if (pollTimer) { clearInterval(pollTimer); }
    });

    // --- Boot ---
    $scope.reloadAll();
});

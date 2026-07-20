/// <reference path="../../support.js" />

// App Insights KQL browser. Reads /api/appinsights/info for target list + presets,
// posts /api/appinsights/query with Targets[] (multi) + (PresetName | Kql) + Offset + MaxRows.
// Each target carries a per-session enabled flag (persisted in localStorage) so operators
// can flatten queries across multiple AppInsights instances by enabling several at once.
angular.module('support').controller('AppInsightsCtrl', function ($scope, $http) {

    var STORAGE_KEY = 'rxns-appinsights-enabled';

    $scope.targets = [];        // [{ Name, SubscriptionId, ResourceGroup, AppName, AppId, DefaultEnabled, _enabled }]
    $scope.presets = [];
    $scope.preset = '';
    $scope.offset = '24h';
    $scope.maxRows = 100;
    $scope.kql = '';
    $scope.status = { cls: 'badge', text: 'no query run yet' };
    $scope.banner = null;
    $scope.results = null;       // { columns: [...], rows: [...] }
    $scope.empty = true;
    $scope.emptyMessage = 'Enable targets above, pick a preset, then Run.';

    function loadEnabledFromStorage() {
        try {
            var raw = localStorage.getItem(STORAGE_KEY);
            return raw ? JSON.parse(raw) : null;  // { name1: true, name2: false, ... }
        } catch (e) { return null; }
    }

    function persistEnabled() {
        try {
            var map = {};
            $scope.targets.forEach(function (t) { map[t.Name || t.AppName] = !!t._enabled; });
            localStorage.setItem(STORAGE_KEY, JSON.stringify(map));
        } catch (e) { /* swallow — localStorage may be disabled */ }
    }

    $scope.loadInfo = function () {
        $scope.banner = null;
        $http.get('/api/appinsights/info').then(function (r) {
            var info = r.data || {};
            var raw = info.targets || info.Targets || [];
            var stored = loadEnabledFromStorage() || {};

            $scope.targets = raw.map(function (t) {
                var key = t.Name || t.AppName;
                // stored wins → cfg DefaultEnabled → true (sensible default for single-target setups)
                var enabled = stored.hasOwnProperty(key)
                    ? !!stored[key]
                    : (t.DefaultEnabled !== false);
                return angular.extend({}, t, { _enabled: enabled });
            });

            $scope.presets = info.presets || info.Presets || [];

            if (!info.available && !info.Available && !$scope.targets.length) {
                $scope.banner = 'No AppInsights targets configured. Drop an `appstatus.config` next to the host with a Targets[] array.';
            }
        }, function (err) {
            $scope.banner = 'Cannot load /api/appinsights/info: HTTP ' + (err && err.status);
        });
    };

    $scope.toggleTarget = function (t) {
        t._enabled = !t._enabled;
        persistEnabled();
    };

    $scope.enableAll = function () {
        $scope.targets.forEach(function (t) { t._enabled = true; });
        persistEnabled();
    };

    $scope.disableAll = function () {
        $scope.targets.forEach(function (t) { t._enabled = false; });
        persistEnabled();
    };

    $scope.enabledCount = function () {
        return $scope.targets.filter(function (t) { return t._enabled; }).length;
    };

    $scope.run = function () {
        var enabled = $scope.targets.filter(function (t) { return t._enabled; });
        if (!enabled.length) {
            alert('Enable at least one target.');
            return;
        }
        var kql = ($scope.kql || '').trim();
        var preset = $scope.preset || '';
        if (!kql && !preset) {
            alert('Enter KQL or pick a preset.');
            return;
        }

        $scope.status = { cls: 'badge', text: 'running ' + enabled.length + ' target' + (enabled.length === 1 ? '' : 's') + '...' };
        $scope.banner = null;

        // Strip the UI-only _enabled flag before posting.
        var targets = enabled.map(function (t) {
            return {
                Name: t.Name, SubscriptionId: t.SubscriptionId,
                ResourceGroup: t.ResourceGroup, AppName: t.AppName, AppId: t.AppId
            };
        });

        $http.post('/api/appinsights/query', {
            Targets: targets,
            Kql: kql || null,
            PresetName: preset || null,
            Offset: ($scope.offset || '').trim() || '24h',
            MaxRows: parseInt($scope.maxRows, 10) || 100
        }).then(function (r) {
            var data = r.data || {};
            if (data.IsError || data.isError) {
                $scope.status = { cls: 'badge err', text: 'error' };
                $scope.banner = data.ErrorMessage || data.errorMessage || 'Query failed';
                $scope.results = null;
                $scope.empty = true;
                return;
            }
            renderTable(data);
        }, function (err) {
            $scope.status = { cls: 'badge err', text: 'failed' };
            $scope.banner = 'HTTP ' + (err && err.status) + ': ' + ((err && err.data && (err.data.ErrorMessage || err.data.errorMessage)) || 'query failed');
            $scope.results = null;
            $scope.empty = true;
        });
    };

    function renderTable(data) {
        var rows = data.Rows || data.rows || [];
        if (!rows.length) {
            $scope.results = null;
            $scope.empty = true;
            $scope.emptyMessage = 'No rows.';
            $scope.status = { cls: 'badge', text: '0 rows' };
            return;
        }
        var cols = data.Columns || data.columns || Object.keys(rows[0]);
        // Move _target column to the front for at-a-glance attribution.
        var idx = cols.indexOf('_target');
        if (idx > 0) { cols.splice(idx, 1); cols.unshift('_target'); }

        var rendered = rows.map(function (row) {
            return cols.map(function (c) {
                var v = row[c];
                var str = (v == null) ? '' : String(v);
                var json = typeof v === 'string' && (v.charAt(0) === '{' || v.charAt(0) === '[');
                return { value: str, json: json, isTarget: c === '_target' };
            });
        });
        $scope.results = { columns: cols, rows: rendered };
        $scope.empty = false;
        $scope.status = { cls: 'badge', text: (data.RowCount || data.rowCount || rows.length) + ' rows from ' + $scope.enabledCount() + ' target' + ($scope.enabledCount() === 1 ? '' : 's') };
    }

    $scope.loadInfo();
});

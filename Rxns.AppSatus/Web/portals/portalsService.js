angular.module('portals').factory('portalsService', function ($http, $interval, rxnPortalConfiguration) {
    var baseUrl = rxnPortalConfiguration.baseWebServicesUrl;
    var state = { peers: [], available: true };
    var subs = [];

    function applyResponse(r) {
        state.available = true;
        state.peers = (r && r.data && r.data.peers) ? r.data.peers : [];
        notify();
    }

    function fetch() {
        return $http.get(baseUrl + '/api/portals/peers', { timeout: 4000 })
            .then(applyResponse, function (err) {
                if (err && err.status === 404) {
                    state.available = false;
                    state.peers = [];
                    notify();
                }
            });
    }

    function add(name, url) {
        return $http.post(baseUrl + '/api/portals/peers/custom', { name: name, url: url })
            .then(function (r) { applyResponse(r); return r; });
    }

    function remove(url) {
        return $http.delete(baseUrl + '/api/portals/peers/custom', { params: { url: url } })
            .then(function (r) { applyResponse(r); return r; });
    }

    function notify() {
        for (var i = 0; i < subs.length; i++) {
            try { subs[i](state); } catch (e) { }
        }
    }

    fetch();
    $interval(fetch, 10000);

    return {
        get: function () { return state; },
        subscribe: function (fn) {
            subs.push(fn);
            try { fn(state); } catch (e) { }
            return function () {
                var idx = subs.indexOf(fn);
                if (idx >= 0) subs.splice(idx, 1);
            };
        },
        add: add,
        remove: remove,
        refresh: fetch
    };
});

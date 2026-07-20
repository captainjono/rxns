/// <reference path="bower_components/angular/angular.js" />
/// <reference path="bower_components/angular-mocks/angular-mocks.js" />
/// <reference path="bower_components/angular-ui-utils/ui-utils.js" />
/// <reference path="bower_components/angular-ui-router/release/angular-ui-router.js" />
/// <reference path="bower_components/angular-resource/angular-resource.js" />
/// <reference path="bower_components/angular-bootstrap/ui-bootstrap-tpls.js" />
/// <reference path="bower_components/angular-animate/angular-animate.js" />

angular.module('portal', ['ui.bootstrap', 'ui.utils', 'ui.router', 'errors', 'metrics', 'systemstatus', 'support', 'authentication', 'SignalR', 'rx', 'filter.duration', 'updates', 'ngFileUpload', 'ngVis', 'portals']);

angular.module('portal').config(function ($stateProvider, $urlRouterProvider) {

    $stateProvider.state('allModules', {
        url: '/',
        templateUrl: 'partials/allModules.html'
    });
    /* Add New States Above */
    $urlRouterProvider.otherwise('/');

    // Augment slot: each entry in window.RxnsPortalAugments is a descriptor
    // pushed by an augment's /augment/init.js. Registered here at config time
    // so ui-router routes resolve from the moment the SPA bootstraps. nav
    // buttons are surfaced separately via the augmentsService for the
    // <aug-nav> directive in allModules.html.
    var augs = (window.RxnsPortalAugments || []);
    for (var i = 0; i < augs.length; i++) {
        var a = augs[i];
        if (!a || !a.state || !a.url || !a.templateUrl) continue;
        $stateProvider.state(a.state, {
            url: a.url,
            templateUrl: a.templateUrl,
            controller: a.controller || undefined
        });
    }
});

// Service that exposes the registered augments to nav directives + any other
// consumer that needs to enumerate them. Reads from the same global the config
// block uses so the order matches the order of <script src="augment/init.js">
// execution (= the order augments push, deterministic per host).
angular.module('portal').factory('augmentsService', function () {
    return {
        list: function () { return (window.RxnsPortalAugments || []).slice(); },
        navButtons: function () {
            return (window.RxnsPortalAugments || [])
                .filter(function (a) { return a && a.navButton && a.state; })
                .map(function (a) { return { state: a.state, navButton: a.navButton, name: a.name }; });
        }
    };
});

angular.module('portal').run(function ($rootScope) {

    $rootScope.safeApply = function (fn) {
        var phase = $rootScope.$$phase;
        if (phase === '$apply' || phase === '$digest') {
            if (fn && (typeof (fn) === 'function')) {
                fn();
            }
        } else {
            this.$apply(fn);
        }
    };
});

angular.module('portal').constant('angularMomentConfig', {
    preprocess: 'utc' // optional
    //timezone: 'Australia/Sydney' // optional
});

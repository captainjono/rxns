/// <reference path="bower_components/angular/angular.js" />
/// <reference path="bower_components/angular-mocks/angular-mocks.js" />
/// <reference path="bower_components/angular-ui-utils/ui-utils.js" />
/// <reference path="bower_components/angular-ui-router/release/angular-ui-router.js" />
/// <reference path="bower_components/angular-resource/angular-resource.js" />
/// <reference path="bower_components/angular-bootstrap/ui-bootstrap-tpls.js" />
/// <reference path="bower_components/angular-animate/angular-animate.js" />

angular.module('portal', ['ui.bootstrap', 'ui.utils', 'ui.router', 'errors', 'metrics', 'systemstatus', 'authentication', 'SignalR', 'rx', 'filter.duration', 'updates', 'ngFileUpload', 'ngVis']);

angular.module('portal').config(function ($stateProvider, $urlRouterProvider) {

    $stateProvider.state('allModules', {
        url: '/',
        templateUrl: 'partials/allModules.html'
    });
    /* Add New States Above */
    $urlRouterProvider.otherwise('/');

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

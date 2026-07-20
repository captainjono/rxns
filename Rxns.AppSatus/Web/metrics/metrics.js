/// <reference path="../app.js" />
/// <reference path="../bower_components/angular-moment/angular-moment.js" />

angular.module('metrics', ['ui.bootstrap', 'ui.utils', 'ui.router', 'ngAnimate', 'ngResource', 'angularMoment']);

angular.module('metrics').config(function($stateProvider) {

    $stateProvider.state('metrics', {
        url: '/metrics',
        templateUrl: 'metrics/partial/metrics/metrics.html'
    });
    /* Add New States Above */

});


/// <reference path="../app.js" />

// Support module — hosts the Claude diagnostic pane and the App Insights
// KQL browser. Both states consume HTTP APIs already exposed by the
// AppStatus host; this module just provides the ui-router wiring and
// controllers that bind the responses into the portal.
angular.module('support', ['ui.bootstrap', 'ui.utils', 'ui.router', 'ngAnimate', 'ngResource']);

angular.module('support').config(function ($stateProvider) {

    $stateProvider.state('support', {
        url: '/support',
        templateUrl: 'support/partial/allModules/allModules.html'
    });
    $stateProvider.state('claudeChat', {
        url: '/claude',
        templateUrl: 'support/partial/claudeChat/claudeChat.html'
    });
    $stateProvider.state('appInsights', {
        url: '/appinsights',
        templateUrl: 'support/partial/appInsights/appInsights.html'
    });
    $stateProvider.state('monitor', {
        url: '/monitor',
        templateUrl: 'support/partial/monitor/monitor.html'
    });
    /* Add New States Above */

});

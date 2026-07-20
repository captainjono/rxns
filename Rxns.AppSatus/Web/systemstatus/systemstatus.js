

angular.module('systemstatus', ['ui.bootstrap', 'ui.utils', 'ui.router', 'ngAnimate', 'ngResource', 'angularMoment']);

angular.module('systemstatus').config(function($stateProvider) {

    $stateProvider.state('applicationStatus', {
        url: '/appstatus',
        templateUrl: 'systemstatus/partial/applicationStatus/applicationStatus.html'
    });
    $stateProvider.state('systemLog', {
        url: '/systemLog',
        templateUrl: 'systemstatus/partial/systemLog/systemLog.html'
    });
    $stateProvider.state('remoteCommand', {
        url: '/cmd',
        templateUrl: 'systemstatus/partial/remoteCommand/remoteCommand.html'
    });
    $stateProvider.state('appStatus', {
        url: '/appStatusV2',
        templateUrl: 'systemstatus/partial/appStatus/appStatus.html'
    });
    $stateProvider.state('supportInsights', {
        url: '/supportInsights?system',
        templateUrl: 'systemstatus/partial/supportInsights/supportInsights.html'
    });
    $stateProvider.state('remoteShell', {
        url: '/remoteShell?route',
        templateUrl: 'systemstatus/partial/remoteShell/remoteShell.html'
    });
    /* Add New States Above */

});


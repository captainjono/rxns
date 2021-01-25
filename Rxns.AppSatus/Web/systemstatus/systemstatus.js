

angular.module('systemstatus', ['ui.bootstrap', 'ui.utils', 'ui.router', 'ngAnimate', 'ngResource']);

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
    /* Add New States Above */

});


/// <reference path="../app.js" />
/// <reference path="../shared_components/angular-ux-datagrid.js" />
/// <reference path="../bower_components/angular-moment/angular-moment.js" />
/// <reference path="../shared_components/infinite-scroll.js" />

angular.module('errors', ['ui.bootstrap', 'ui.utils', 'ui.router', 'ngAnimate', 'ngResource', 'ux', 'infinite-scroll', 'ui.scroll', 'angularMoment']);

angular.module('errors').config(function ($stateProvider) {

    $stateProvider.state('errors', {
        url: '/errors',
        templateUrl: 'errors/partial/errors/errors.html'
        //,
        //resolve: {
        //    params: function ($stateParams) {
        //        return $stateParams;
        //    }
        //},
        //controller: 'ErrorsCtrl'
    });
    /* Add New States Above */

});


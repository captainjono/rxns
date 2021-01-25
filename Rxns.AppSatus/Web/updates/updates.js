angular.module('updates', ['ui.bootstrap','ui.utils','ui.router','ngAnimate']);

angular.module('updates').config(function($stateProvider) {

    $stateProvider.state('updates', {
        url: '/updates',
        templateUrl: 'updates/partial/list/list.html'
    });
    /* Add New States Above */

});


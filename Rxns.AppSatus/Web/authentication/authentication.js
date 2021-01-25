angular.module('authentication', ['ui.bootstrap','ui.utils','ui.router','ngAnimate', 'ngStorage']);

angular.module('authentication').config(function($stateProvider, $httpProvider) {

    $stateProvider.state('login', {
        url: '/login',
        templateUrl: 'authentication/partial/login/login.html'
    });
    $stateProvider.state('refresh', {
        url: '/refresh',
        templateUrl: 'authentication/partial/refresh/refresh.html'
    });
    /* Add New States Above */

    $httpProvider.interceptors.push('authHttpInterceptorService');
});


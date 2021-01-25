angular.module('authentication').factory('authHttpInterceptorService', function ($q, $location, $localStorage, $injector) {

    var authHttpInterceptorService = {};

    var request = function(config) {

        config.headers = config.headers || {};

        // var authData = $localStorage.authorizationData;
        // if (authData) {
        //     config.headers.Authorization = 'Bearer ' + authData.token;
        // }

        return config;
    };

    var responseError = function(rejection) {
        if (rejection.status === 401) {
            var authService = $injector.get('rxnAuthService');
            // var authData = $localStorage.authorizationData;

            // if (authData) {
            //     if (authData.useRefreshTokens) {
            //         $location.path('/refresh');
            //         return $q.reject(rejection);
            //     }
            // }

            authService.logout();
            $location.path('/login');
        }
        return $q.reject(rejection);
    };

    authHttpInterceptorService.request = request;
    authHttpInterceptorService.responseError = responseError;

	return authHttpInterceptorService;
});
angular.module('authentication').factory('rxnAuthService', function ($http, $q, $location, $localStorage, rxnPortalConfiguration) {

    var serviceBase = rxnPortalConfiguration.baseWebServicesUrl;
    var authentication = {
        isAuth: false,
        userName: "",
        useRefreshTokens: false
    };

    var login = function (loginData) {

        var data = "grant_type=password&username=" + loginData.userName + "&password=" + loginData.password;

        //for refresh tokens
        //here there was a login checkbox 'use refresh tokens'
        if (loginData.useRefreshTokens) {
            data = data + "&client_id=" + rxnPortalConfiguration.clientId;
        }

        var deferred = $q.defer();

        $http.post(serviceBase + '/token', data, { headers: { 'Content-Type': 'application/x-www-form-urlencoded' } }).success(function (response) {

            $localStorage.authorizationData = {
                token: response.access_token,
                userName: loginData.userName,
                refreshToken: "", useRefreshTokens: false
                //refreshToken: response.refresh_token, useRefreshTokens: true 
            };

            authentication.isAuth = true;
            authentication.userName = loginData.userName;
            authentication.useRefreshTokens = false;

            deferred.resolve(response);

        }).error(function (err, status) {
            logOut();
            deferred.reject(err);
        });

        return deferred.promise;

    };

    var logOut = function () {

        $localStorage.authorizationData = null;

        authentication.isAuth = false;
        authentication.userName = "";
        authentication.useRefreshTokens = false;
    };

    var fillAuthData = function() {

        var authData = $localStorage.authorizationData;
        if (authData) {
            authentication.isAuth = true;
            authentication.userName = authData.userName;
            authentication.useRefreshTokens = authData.useRefreshTokens;
        }
    };

    var refreshToken = function () {
        var deferred = $q.defer();

        var authData = $localStorage.authorizationData;

        if (authData) {

            if (authData.useRefreshTokens) {

                var data = "grant_type=refresh_token&refresh_token=" + authData.refreshToken + "&client_id=" + rxnPortalConfiguration.clientId;

                $localStorage.authorizationData = null;

                $http.post(serviceBase + 'token', data, { headers: { 'Content-Type': 'application/x-www-form-urlencoded' } }).success(function (response) {

                    $localStorage.authorizationData = {
                        token: response.access_token,
                        userName: response.userName,
                        refreshToken: response.refresh_token,
                        useRefreshTokens: true
                    };

                    deferred.resolve(response);

                }).error(function (err, status) {
                    logOut();
                    deferred.reject(err);
                });
            }
        }

        return deferred.promise;
    };

    rxnAuthService.login = login;
    rxnAuthService.logout = logOut;
    rxnAuthService.fillAuthData = fillAuthData;
    rxnAuthService.authentication = authentication;
    rxnAuthService.refreshToken = refreshToken;

	return rxnAuthService;
});

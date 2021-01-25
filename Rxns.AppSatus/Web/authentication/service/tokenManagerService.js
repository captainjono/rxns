angular.module('authentication').factory('tokenManagerService', function ($http, rxnPortalConfiguration) {

    var tokenManagerService = {};
    var serviceBase = rxnPortalConfiguration.apiServiceBaseUri;
    
    var getRefreshTokens = function () {

        return $http.get(serviceBase + '/refreshtokens').then(function (results) {
            return results;
        });
    };

    var deleteRefreshTokens = function (tokenid) {

        return $http.delete(serviceBase + '/refreshtokens/?tokenid=' + tokenid).then(function (results) {
            return results;
        });
    };

    tokenManagerService.deleteRefreshTokens = deleteRefreshTokens;
    tokenManagerService.getRefreshTokens = getRefreshTokens;

    return tokenManagerService;
});

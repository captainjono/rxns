angular.module('authentication').controller('RefreshCtrl', function ($scope, $location, rxnAuthService) {

    $scope.authentication = rxnAuthService.authentication;
    $scope.tokenRefreshed = false;
    $scope.tokenResponse = null;

    $scope.refreshToken = function () {

        rxnAuthService.refreshToken().then(function (response) {
            $scope.tokenRefreshed = true;
            $scope.tokenResponse = response;
        },
         function (error) {
             $location.path('/login');
         });
    };

});
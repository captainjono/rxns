angular.module('authentication').controller('LoginCtrl', function ($scope, $location, rxnAuthService) {
    $scope.loginData = {
        userName: "",
        password: "",
        useRefreshTokens: false
    };

    $scope.message = "";

    $scope.login = function () {

        rxnAuthService.login($scope.loginData).then(function (response) {
                history.go(-1);
            },
         function (err) {
             $scope.message = "please try again";
         });
    };

});
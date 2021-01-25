/// <reference path="../../errors.js" />
/// <reference path="../../service/errorsService.js" />

angular.module('errors').controller('ErrorsCtrl', function ($scope, errorsService) {

    $scope.loading = false;
    $scope.logLoading = false;
    $scope.recordsPerPage = 10;
    $scope.page = 0;
    $scope.errorsPageEnd = false;

    $scope.getMoreErrors = function() {
        $scope.loading = true;

        if (!$scope.errorsPageEnd) {
            errorsService.getUnresolvedErrors({ pageId: $scope.page++, rows: $scope.recordsPerPage/*, tenant: $stateParams.tenant, systemName: $stateParams.systemName */ }).$promise.then(function (errors) {

                if (errors.length !== $scope.recordsPerPage) {
                    $scope.errorsPageEnd = true;
                }

                errors.forEach(function(e) { $scope.errors.push(e); });

                $scope.loading = false;
            });
        }
    };

    $scope.errors = [];
    $scope.log = [];

    $scope.getLog = function(errorId) {
        $scope.logLoading = true;

        $scope.log = errorsService.getLog({ id: errorId });

        $scope.log.$promise.then(function(log) {
            $scope.logLoading = false;
        });
    };
});
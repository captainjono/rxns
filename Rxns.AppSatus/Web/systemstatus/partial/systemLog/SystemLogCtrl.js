angular.module('systemstatus').controller('SystemLogCtrl',function($scope, statusService)
{
    $scope.log = statusService.getSystemLog();
});
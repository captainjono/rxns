/// <reference path="../../service/statusService.js" />
/// <reference path="../../systemstatus.js" />

angular.module('systemstatus').controller('ApplicationStatusCtrl', function ($scope, statusService, $interval) {
    var refreshService;

    $scope.machines = {};
    $scope.hasMachines = false;

    //gets the latest status from the service
    var fetchStatus = function() {
        statusService.getApplicationStatus().$promise.then(function(result) {
            $scope.hasMachines = true;
            $scope.machines = result;
        });
    };

    //setup a timer that refreshes the status in the background
    //while the user is still on the page
    refreshService = $interval(function () {
      fetchStatus();
    }, 5*60*1000);

    //destroys the refresh timer
    $scope.stopRefresh = function () {
       
            refreshService = undefined;
    };

    //listens to the destroy event and to kill the
    //refresh timer
    $scope.$on('destroy', function() {
        $scope.stopRefresh();
    });

    $scope.onlyQueues = function(meta) {
        return meta.QueueName !== null;
    };

    //fetches the inital status because the timer
    //only executes after the first interval has passed
    fetchStatus();
    
});
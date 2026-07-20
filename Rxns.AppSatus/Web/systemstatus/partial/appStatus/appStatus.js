angular.module('systemstatus').controller('AppstatusCtrl', function ($scope, eventHubService, rx) {
    $scope.machines = [];
    $scope.hasMachines = false;

    var status = eventHubService.statusInitial.subscribe(function (appStatusByTenant) {
        $scope.hasMachines = true;
        $scope.$apply(function () {
                $scope.machines = appStatusByTenant;
        });
    });

    var statusUpdates = eventHubService.statusUpdates.subscribe(function (appStatusByTenant) {

        $scope.$apply(function () {
          
            var tenant = appStatusByTenant[0].tenant;

            for (var i = 0; i < $scope.machines.length; i++) {
                var t = tenant;
                if ($scope.machines[i].tenant === t) {
                    for (var y = 0; y < $scope.machines[i].systems.length; y++) {
                        if ($scope.machines[i].systems[y].system.systemName === appStatusByTenant[0].systems[0].system.systemName) {
                            $scope.machines[i].systems[y] = appStatusByTenant[0].systems[0];
                            return;
                        }
                    }

                    $scope.machines[i].systems.push(appStatusByTenant[0]);
                    return;
                }
            }
            //notfound in list
            $scope.machines.push(appStatusByTenant[0]);
        });
    });

});

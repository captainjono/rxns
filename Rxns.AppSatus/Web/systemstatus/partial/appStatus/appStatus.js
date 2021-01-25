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
          
            var tenant = appStatusByTenant[0].Tenant;

            for (var i = 0; i < $scope.machines.length; i++) {
                var t = tenant;
                if ($scope.machines[i].Tenant === t) {
                    for (var y = 0; y < $scope.machines[i].Systems.length; y++) {
                        if ($scope.machines[i].Systems[y].System.SystemName === appStatusByTenant[0].Systems[0].System.SystemName) {
                            $scope.machines[i].Systems[y] = appStatusByTenant[0].Systems[0];
                            return;
                        }
                    }

                    $scope.machines[i].Systems.push(appStatusByTenant[0]);
                    return;
                }
            }
            //notfound in list
            $scope.machines.push(appStatusByTenant[0]);
        });
    });

});

angular.module('systemstatus').controller('AppstatusCtrl', function ($scope, eventHubService, rx) {
    $scope.machines = [];
    $scope.hasMachines = false;

    // Page 1 replaces, later pages add to it. This used to assign unconditionally, which was
    // right while the status was always one message and would show only the last page once it
    // stopped being - a fleet appearing a fraction of its real size, with nothing to suggest the
    // rest had arrived and been overwritten.
    var status = eventHubService.statusInitial.subscribe(function (initial) {
        $scope.hasMachines = true;
        $scope.$apply(function () {
            if (initial.page <= 1) {
                $scope.machines = initial.statuses;
                return;
            }

            for (var i = 0; i < initial.statuses.length; i++) {
                var incoming = initial.statuses[i];
                var existing = null;

                for (var j = 0; j < $scope.machines.length; j++) {
                    if ($scope.machines[j].tenant === incoming.tenant) {
                        existing = $scope.machines[j];
                        break;
                    }
                }

                // A tenant can straddle a page boundary, so the same tenant arrives twice
                // carrying different systems. Concatenating is safe because the server never
                // sends a system twice across the pages of one push.
                if (existing) {
                    existing.systems = existing.systems.concat(incoming.systems);
                } else {
                    $scope.machines.push(incoming);
                }
            }
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

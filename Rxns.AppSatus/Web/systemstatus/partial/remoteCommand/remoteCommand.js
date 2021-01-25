angular.module('systemstatus').controller('remoteCommandCtrl', function ($scope, eventHubService, rx, moment) {

    $scope.log = [];
    
    printToConsole = function(msg, maxLogs) {
        if($scope.log.length > maxLogs) {
            $scope.log.pop();
        }

        $scope.log.unshift(msg);
    }

    printToTailSummaryOnUI = function(msg) {
        var asConsoleMsg = `[${moment(msg.Timestamp).format("YYYY-MM-DD HH:mm:ss.SS")}][${msg.Level}][${msg.Tenant}][${msg.Reporter}] ${msg.Message}`;
        switch(msg.Level)
        {
            case 'Info':
                console.info(asConsoleMsg);
                break;
                case 'Error':
                console.error(asConsoleMsg);
                break;
                case 'Verbose':
                console.debug(asConsoleMsg);
                break;                    
        }
    }

    var sub = eventHubService.logEntry.subscribe(function (message) {

        $scope.$apply(function () {
            var msg = angular.fromJson(message.Message);
            msg.Tenant = message.Tenant;

            printToTailSummaryOnUI(msg, 100);
            printToConsole(msg)
        });
    });
    
    $scope.publish = function(destination, cmd) {
        eventHubService.sendCommand(destination, cmd);
    };
});
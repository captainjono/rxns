angular.module('updates').controller('ListCtrl', function($scope, updatesService) {


    $scope.appUpdates = updatesService.getUpdates({ systemName: 'app' });
});

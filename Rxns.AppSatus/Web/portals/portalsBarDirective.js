angular.module('portals').directive('rxnsPortalsBar', function (portalsService) {
    return {
        restrict: 'E',
        templateUrl: 'portals/portalsBar.html',
        scope: {},
        link: function (scope) {
            scope.peers = [];
            scope.addOpen = false;
            scope.newPeer = { name: '', url: '' };
            scope.addError = null;

            var off = portalsService.subscribe(function (s) {
                scope.peers = (s && s.peers) ? s.peers : [];
            });
            scope.$on('$destroy', off);

            scope.goto = function (url) {
                if (url) window.location.assign(url);
            };

            scope.openAdd = function () {
                scope.addOpen = true;
                scope.addError = null;
                scope.newPeer = { name: '', url: '' };
            };

            scope.cancelAdd = function () {
                scope.addOpen = false;
                scope.addError = null;
            };

            scope.submitAdd = function () {
                var name = (scope.newPeer.name || '').trim();
                var url = (scope.newPeer.url || '').trim();
                if (!name || !url) {
                    scope.addError = 'name and url required';
                    return;
                }
                scope.addError = null;
                portalsService.add(name, url).then(function () {
                    scope.addOpen = false;
                    scope.newPeer = { name: '', url: '' };
                }, function (err) {
                    var msg = (err && err.data && err.data.error) || ('add failed (' + (err && err.status) + ')');
                    scope.addError = msg;
                });
            };

            scope.remove = function (url) {
                portalsService.remove(url);
            };
        }
    };
});

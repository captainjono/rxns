angular.module('portal').controller('AllmodulesCtrl', function ($scope, augmentsService) {
    // Augment-supplied nav buttons. The augmentsService reads from
    // window.RxnsPortalAugments (populated by /augment/init.js before angular
    // bootstrap) so this is deterministic and host-specific.
    $scope.augmentNavButtons = augmentsService.navButtons();
});
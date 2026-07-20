angular.module('portal').factory('rxnPortalConfiguration', function ($location) {
    var cfg = {
        baseWebServicesUrl: (window.RxnsPortalConfig && window.RxnsPortalConfig.baseWebServicesUrl) || window.location.origin,
        clientId: 'rxnPortalClient'
    }

    return cfg;
});

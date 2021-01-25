angular.module('portal').factory('rxnPortalConfiguration', function ($location) {
    var cfg = {
        baseWebServicesUrl: $location.protocol() + "://" + $location.host() + ":888",
        clientId: 'rxnPortalClient' 
    }

    return cfg;
});

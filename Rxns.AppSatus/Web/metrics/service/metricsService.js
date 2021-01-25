/// <reference path="../metrics.js" />

angular.module('metrics').factory('metricsService', function ($resource, rxnPortalConfiguration) {

    var baseUrl = rxnPortalConfiguration.baseWebServicesUrl;

    return $resource(baseUrl + '/metrics', {}, {
        getMetrics: { method: 'GET', isArray: true },
    });
});
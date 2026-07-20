/// <reference path="../systemstatus.js" />

angular.module('systemstatus').factory('statusService', function ($resource, rxnPortalConfiguration) {

    var baseUrl = rxnPortalConfiguration.baseWebServicesUrl;

    return $resource(baseUrl + '/systemstatus/:part', {}, {
        getApplicationStatus: { method: 'GET', params: { part: "heartbeats" }, isArray: true },
        getSystemLog: { method: 'GET', params: { part: "log" }, isArray: true },
    });
});

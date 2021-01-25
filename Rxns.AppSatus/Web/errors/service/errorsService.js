/// <reference path="../errors.js" />

angular.module('errors').factory('errorsService', function ($resource, rxnPortalConfiguration) {

    var baseUrl = rxnPortalConfiguration.baseWebServicesUrl;

    return $resource(baseUrl + '/errors?page=:pageId&rows=:rows', {}, {
        getUnresolvedErrors: { method: 'GET', param: {pageId: '@pageId', rows: '@rows'}, isArray: true },
        getLog: { method: 'GET', url: baseUrl + '/errors/:id/meta', param: { id: '@id' }, isArray: true }
    });
});

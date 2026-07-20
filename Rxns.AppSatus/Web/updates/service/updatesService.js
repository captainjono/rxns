angular.module('updates').factory('updatesService', function ($resource, rxnPortalConfiguration) {
    
	var baseUrl = rxnPortalConfiguration.baseWebServicesUrl;

	return $resource(baseUrl + '/updates/:systemName/:part', {}, {
	    getUpdates: { method: 'GET', params: { systemName: '@systemName', part: "list" }, isArray: true },
	    getUpdate: { method: 'GET', params: { systemName: '@systemName', part: '@version' }, isArray: false },
	});
});
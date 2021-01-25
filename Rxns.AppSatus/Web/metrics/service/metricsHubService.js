angular.module('metrics').factory('metricsHubService', function (Hub, $localStorage, rxnPortalConfiguration, rx) {


    var log = new rx.Subject();
    var onLoad = new rx.Subject();
    var onUpdate = new rx.Subject();

    var hub = new Hub('systemMetricsHub', {
        rootPath: rxnPortalConfiguration.baseWebServicesUrl,

        listeners: {
            'HistoricalData': function (allMetrics) {
                // remoteEvents.forEach(function (remoteEvent) {
                onLoad.onNext(allMetrics);

            },
            'OnUpdate': function (metric) {
                // remoteEvents.forEach(function (remoteEvent) {
                onUpdate.onNext(metric);
            }
        },
        methods: ['sendCommand'],

        queryParams: {
            'bearer_token': ''// $localStorage.authorizationData.token
        }
    });

    hub.on('disconnected', function() {
        setTimeout(function() {
            $.connection.hub.start();
        }, 5000); // Restart connection after 5 seconds.
     });

    var metricsHubService = {
        onLoad: onLoad,
        onUpdate: onUpdate
    };

    return metricsHubService;
});
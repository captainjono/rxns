angular.module('metrics').factory('metricsHubService', function ($localStorage, rxnPortalConfiguration, rx) {


    var log = new rx.Subject();
    var onLoad = new rx.Subject();
    var onUpdate = new rx.Subject();


    const hub = new signalR.HubConnectionBuilder()
        .withUrl("/reportHub")
        //.rootPath(rxnPortalConfiguration.baseWebServicesUrl)
        //  .configureLogging(signalR.LogLevel.Debug)

        //    .withAutomaticReconnect([0, 0, 10000])
        //'bearer_token': ''//$localStorage.authorizationData.token
        .build();

    hub.start().then(function () {
        console.log("RxnMetrics now streaming")
    }).catch(function (err) {
        return console.error("RxnMetrics connected failed: " + err.toString());
    });

    hub.on("HistoricalData", function (allMetrics) {
        onLoad.onNext(allMetrics);
    });

    hub.on("OnUpdate", function (metric) {
        onUpdate.onNext(metric);
    });

    var metricsHubService = {
        onLoad: onLoad,
        onUpdate: onUpdate
    };

    return metricsHubService;
});

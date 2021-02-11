angular.module('portal').factory('eventHubService', function ($localStorage, rxnPortalConfiguration, rx) {


    var log = new rx.Subject();
    var statusInitial = new rx.Subject();
    var statusUpdates = new rx.Subject();

    const hub = new signalR.HubConnectionBuilder()
        .withUrl("/eventsHub")
        //.rootPath(rxnPortalConfiguration.baseWebServicesUrl)
        //   .configureLogging(signalR.LogLevel.Information)
        //    .withAutomaticReconnection()
        //'bearer_token': ''//$localStorage.authorizationData.token
        .build();

    hub.start().then(function () {
        console.log("RxnHubs connected")
    }).catch(function (err) {
        return console.error("RxnHubs connected failed: " + err.toString());
    });

    hub.on("EventReceived", function (remoteEvents) {
        log.onNext(remoteEvents);
    });

    hub.on("StatusInitialSubscribe", function (remoteEvents) {
        statusInitial.onNext(remoteEvents);
    });

    hub.on("StatusUpdatesSubscribe", function (remoteEvents) {
        statusUpdates.onNext(remoteEvents);
    });

    var eventHubService = {
        sendCommand: function(destination, cmd) {

            hub.sendCommand(destination, cmd);

            console.log(destination + ": " + cmd);
        },

        logEntry: log,
        statusInitial: statusInitial,
        statusUpdates: statusUpdates
    };

    return eventHubService;
});

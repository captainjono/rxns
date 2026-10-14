angular.module('portal').factory('eventHubService', function ($localStorage, rxnPortalConfiguration, rx) {


    var log = new rx.Subject();
    var statusInitial = new rx.Subject();
    var statusUpdates = new rx.Subject();
    var monitorEvent = new rx.Subject();

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

    // The initial status arrives in pages when it is too large for one message - a fleet of
    // workers all reporting under one tenant gets there long before a portal watching a handful
    // of machines does. Each tenant object in the page carries page/of; a server that predates
    // paging sends neither, so treat their absence as "one page of one".
    //
    // The marker rides on the payload rather than arriving as a second argument because the
    // .NET SignalR client - which every worker uses - drops an invocation carrying more
    // arguments than its handler declares. Only the browser would have tolerated that.
    //
    // Emitted as an envelope rather than as the bare array because a subscriber has to know
    // whether it is looking at a fresh status or the continuation of one, and only page 1 means
    // "replace what you have".
    hub.on("StatusInitialSubscribe", function (remoteEvents) {
        var statuses = remoteEvents || [];
        var marker = statuses[0] || {};
        statusInitial.onNext({
            statuses: statuses,
            page: marker.page || 1,
            of: marker.of || 1
        });
    });

    hub.on("StatusUpdatesSubscribe", function (remoteEvents) {
        statusUpdates.onNext(remoteEvents);
    });

    // Pushed by MonitorService whenever an AI-generated suggestion is raised.
    // Shape: { kind: 'suggestionRaised', suggestion: { id, title, ... } }.
    hub.on("MonitorEvent", function (envelope) {
        monitorEvent.onNext(envelope);
    });

    var eventHubService = {
        sendCommand: function(destination, cmd) {

            hub.invoke("sendCommand", destination, cmd);

            console.log(destination + ": " + cmd);
        },

        logEntry: log,
        statusInitial: statusInitial,
        statusUpdates: statusUpdates,
        monitorEvent: monitorEvent
    };

    return eventHubService;
});

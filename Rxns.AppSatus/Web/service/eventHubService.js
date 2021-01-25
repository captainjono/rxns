angular.module('portal').factory('eventHubService', function (Hub, $localStorage, rxnPortalConfiguration, rx) {


    var log = new rx.Subject();
    var statusInitial = new rx.Subject();
    var statusUpdates = new rx.Subject();

    var hub = new Hub('eventsHub', {
        rootPath: rxnPortalConfiguration.baseWebServicesUrl,

        listeners: {
            'EventReceived': function (remoteEvents) {
               // remoteEvents.forEach(function (remoteEvent) {
                    log.onNext(remoteEvents);
                
            },
            'StatusInitialSubscribe': function (remoteEvents) {
                // remoteEvents.forEach(function (remoteEvent) {
                statusInitial.onNext(remoteEvents);

            },
            'StatusUpdatesSubscribe': function (remoteEvents) {
                // remoteEvents.forEach(function (remoteEvent) {
                statusUpdates.onNext(remoteEvents);

            }
        },
        methods: ['sendCommand'],

        queryParams: {
            'bearer_token': ''//$localStorage.authorizationData.token
        }
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
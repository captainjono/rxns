angular.module('portal').directive('fileDownload', function (rxnPortalConfiguration, $localStorage) {
    return {
        restrict: 'A',
        replace: false,
        template: "{{link}}<iframe style='position:fixed;display:none;top:-1px;left:-1px;' />",
        scope: {
            url: "@",
            link: "@"
        },
        link: function(scope, element, attrs) {
            element.click(function() {
                var iframe = element.find('iframe'),
                    iframeBody = $(iframe[0].contentWindow.document.body), //" + $localStorage.authorizationData.token + "
                    form = angular.element("<form method='POST' action='" + rxnPortalConfiguration.baseWebServicesUrl + "/" + scope.url + "'><input type='hidden' name='access_token' value='' /></form>");

                iframeBody.append(form);
                form.submit();
            });
        }
    };
});

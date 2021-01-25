angular.module('portal').run(['$templateCache', function($templateCache) {
  'use strict';

  $templateCache.put('authentication/partial/login/login.html',
    "<div class=col-md-12 ng-controller=LoginCtrl><input type=text ng-model=loginData.userName id=\"username\"> <input type=password ng-model=loginData.password id=\"password\"> <input type=Submit id=login value=Login ng-click=\"login()\"> <input type=button id=login value=Logout ng-click=\"logout()\"><div>{{message}}</div></div>"
  );


  $templateCache.put('authentication/partial/refresh/refresh.html',
    "<div class=col-md-12 ng-controller=RefreshCtrl></div>"
  );


  $templateCache.put('directive/fileDownload/fileDownload.html',
    "<div></div>"
  );


  $templateCache.put('errors/partial/errors/errors.html',
    "<div ng-controller=ErrorsCtrl><div infinite-scroll=getMoreErrors() infinite-scroll-distance=1 infinite-scroll-immediate-check=true><accordion class=bg-danger close-others=true><accordion-group heading={{error.error}} ng-repeat=\"error in errors\"><accordion-heading><div class=media><div class=pull-left><i style=\"vertical-align: central\" class=\"fa fa-chain-broken fa-3x fa-border\"></i></div><div class=media-body><div class=\"media-heading h4\"><i>{{error.tenant}}</i> - {{error.system}}</div></div><div class=\"media-body h5 no-overflow\">{{error.error}}</div><div class=\"pull-right h5 media-body\"><div am-time-ago=error.timestamp></div></div></div></accordion-heading><tabset><tab heading=Stacktrace><div class=\"well well-large\"><code class=bold>{{error.error}}</code><p></p><code>{{error.stackTrace}}</code></div></tab><tab heading=Log ng-click=getLog(error.errorId)><table class=\"table table-hover table-striped table-condensed h6\"><thead><th>Timestamp</th><th>Reporter</th><th>Level</th><th>Message</th></thead><tbody><tr ng-class=\"{error: entry.level === 'Error', infor: entry.level === 'Info', verbose: entry.level === 'Verbose'}\" ng-repeat=\"entry in log\"><td>{{entry.timestamp | amDateFormat:'hh:mm:ss.SSS'}}</td><td>{{entry.reporter}}</td><td>{{entry.level}}</td><td>{{entry.message}}</td></tr></tbody></table></tab><li class=dropdown><a class=dropdown-toggle data-toggle=dropdown href=#>Action <span class=caret></span></a><ul class=dropdown-menu role=menu><li><a href=\"\">Resolve...</a></li><li><a href=\"\">Dismiss...</a></li><li class=divider></li><li><a class=dropdown-menu-item href=\"\">File Bug...</a></li></ul></li></tabset></accordion-group></accordion></div></div>"
  );


  $templateCache.put('metrics/partial/metrics/metrics.html',
    "<div ng-controller=MetricsCtrl style=\"font-family: monospace\"><h2>Events Publishing Overview</h2><vis-graph2d data=graphData groups=graphGroups options=graphOptions events=graphEvents></vis-graph2d><vis-network data=networkData groups=networkGroups options=networkOptions events=networkEvents></vis-network></div>"
  );


  $templateCache.put('partials/allModules.html',
    "<div class=col-md-12 ng-controller=AllmodulesCtrl><ul class=\"nav nav-pills nav-stacked\"><li class=\"btn btn-group fa-4x\"><a ui-sref=applicationStatus><span class=\"fa fa-cloud text-muted\"></span></a></li><li class=\"btn btn-group fa-4x\"><a ui-sref=appStatus><span class=\"fa fa-cloud\"></span></a></li><li class=\"btn btn-group\"><a ui-sref=errors><span class=\"fa fa-chain-broken fa-4x\"></span></a></li><li class=\"btn btn-group\"><a ui-sref=metrics><span class=\"fa fa-bar-chart-o fa-4x\"></span></a></li><li class=\"btn btn-group\"><a ui-sref=systemLog><span class=\"fa fa-list-alt fa-4x\"></span></a></li><li class=\"btn btn-group\"><a ui-sref=remoteCommand><span class=\"fa fa-caret-square-o-right fa-4x\"></span></a></li><li class=\"btn btn-group\"><a ui-sref=updates><span class=\"fa fa-cloud-download fa-4x\"></span></a></li></ul></div>"
  );


  $templateCache.put('systemstatus/partial/appStatus/appStatus.html',
    "<div class=col-md-12 ng-controller=AppstatusCtrl><span class=\"fa fa-question-circle fa-4x\" ng-hide=hasMachines></span><ul id=machine-status ng-repeat=\"machine in machines\"><li id=machine-status class=\"fa fa-laptop\">{{machine.Tenant}}</li><ul><li class=list-unstyled ng-repeat=\"status in machine.Systems\"><span class=\"fa fa-cloud\" ng-class=\"{error: status.System.Status == '1', infor: status.System.Status == '0'}\"></span>[{{status.System.Version}}] <span id=machine-status-application>{{status.System.SystemName}}</span><div id=machine-status-last-update am-time-ago=status.System.Timestamp></div><div class=\"fa fa-gears\" style=\"padding-left: 20px\">{{status.System.IpAddress}}</div><div style=\"padding-left: 20px\" ng-repeat=\"meta in status.Meta\"><div class=\"fa fa-bar-chart\" ng-class=\"{error: meta.CpuAverage > '70' || meta.MemAverage > '89', infor: meta.CpuAverage < '70' || meta.MemAverage < '89'}\" ng-if=\"meta.CpuAverage != null\">CPU: [{{meta.CpuAverage | number:1}}%] RAM: [{{meta.MemAverage | number:1}}%] Threads: [{{meta.Threads}}] TaskPool: [{{meta.AppThreadsSize}} / {{meta.AppThreadsMax}}]</div><div class=\"fa fa-gears\" ng-if=\"meta.ToProcess != null\">Synced: {{meta.Total - meta.ToProcess}} / {{meta.Total}} [{{meta.SourceTotal}}] ({{((meta.Total - meta.ToProcess) / meta.Total) * 100 | number: 2}}%)</div><div class=\"fa fa-gears\" ng-if=\"meta.LastSync != null && meta.Duration == null\">Last Sync: {{meta.Count}} records <span am-time-ago=meta.LastSync></span></div><div class=\"fa fa-gears\" ng-if=\"meta.Duration != null\">Last Sync: {{meta.Count}} records ({{meta.Duration | duration: 'seconds':'1'}}) <span am-time-ago=meta.LastSync></span></div><div class=\"fa fa-gears\" ng-if=\"meta.InFlight != null\">InFlght/Discarded: {{meta.InFlight}} / <span ng-class=\"{error: meta.Discarded !== '0', infor: meta.Discarded == '0'}\">{{meta.Discarded | number:0}}</span><span id=machine-status-last-update am-time-ago=meta.LastUpload></span></div></div><div style=\"padding-left: 20px\" ng-repeat=\"meta in status.Meta | filter: onlyQueues\"><div class=\"fa fa-inbox\" ng-if=\"meta.QueueName != null\">{{meta.QueueName}} : {{meta.QueueCurrent}} / {{meta.QueueSize}}</div></div></li></ul></ul></div>"
  );


  $templateCache.put('systemstatus/partial/applicationStatus/applicationStatus.html',
    "<div class=col-md-12 ng-controller=ApplicationStatusCtrl><span class=\"fa fa-question-circle fa-4x\" ng-hide=hasMachines></span><ul id=machine-status ng-repeat=\"machine in machines\"><li id=machine-status class=\"fa fa-laptop\">{{machine.tenant}}</li><ul><li class=list-unstyled ng-repeat=\"status in machine.systems.$values\"><span class=\"fa fa-cloud\" ng-class=\"{error: status.system.status == '1', infor: status.system.status == '0'}\"></span> [{{status.system.version}}] <span id=machine-status-application>{{status.system.systemName}}</span><div id=machine-status-last-update am-time-ago=status.system.timestamp></div><div class=\"fa fa-gears\" style=\"padding-left: 20px\">{{status.system.ipAddress}}</div><div style=\"padding-left: 20px\" ng-repeat=\"meta in status.meta\"><div class=\"fa fa-bar-chart\" ng-class=\"{error: meta.CpuAverage > '70' || meta.MemAverage > '89', infor: meta.CpuAverage < '70' || meta.MemAverage < '89'}\" ng-if=\"meta.CpuAverage != null\">CPU: [{{meta.CpuAverage | number:1}}%] RAM: [{{meta.MemAverage | number:1}}%] Threads: [{{meta.Threads}}] TaskPool: [{{meta.AppThreadsSize}} / {{meta.AppThreadsMax}}]</div><div style=\"padding-left: 20px\" ng-repeat=\"(key,value) in meta\"><div class=\"fa fa-task\">{{key}} : <i>{{value}}</i></div></div><div class=\"fa fa-gears\" ng-if=\"meta.OS != null\">OS: {{meta.OS}}</div></div></li></ul></ul></div>"
  );


  $templateCache.put('systemstatus/partial/remoteCommand/remoteCommand.html',
    "<div class=col-md-12 ng-controller=remoteCommandCtrl><form name=remote><div class=input-group><input type=text class=form-control ng-init=\"destination = 'Not Set\\\\App'\" ng-model=destination> <input type=text class=form-control ng-init=\"cmd = 'Update'\" ng-model=cmd> <span class=input-group-btn><button class=\"btn btn-default\" type=button ng-click=\"publish(destination, cmd)\">Send</button></span></div><div class=span6><table class=\"table table-hover table-striped table-condensed h6\"><thead><th>Timestamp</th><th>System</th><th>Reporter</th><th>Level</th><th>Message</th></thead><tbody><tr ng-class=\"{error: entry.Level === 'Error', infor: entry.Level === 'Info', verbose: entry.Level === 'Verbose'}\" ng-repeat=\"entry in log\"><td>{{entry.Timestamp | amDateFormat:'hh:mm:ss.SSS'}}</td><td>{{entry.Tenant}}</td><td>{{entry.Reporter}}</td><td>{{entry.Level}}</td><td>{{entry.Message}}</td></tr></tbody></table></div></form></div>"
  );


  $templateCache.put('systemstatus/partial/systemLog/systemLog.html',
    "<div class=col-md-12 ng-controller=SystemLogCtrl><table class=\"table table-hover table-striped table-condensed h6\"><thead><th>Timestamp</th><th>Reporter</th><th>Level</th><th>Message</th></thead><tbody><tr ng-class=\"{error: entry.level === 'Error', infor: entry.level === 'Info', verbose: entry.level === 'Verbose'}\" ng-repeat=\"entry in log\"><td>{{entry.timestamp | amDateFormat:'hh:mm:ss.SSS'}}</td><td>{{entry.reporter}}</td><td>{{entry.level}}</td><td>{{entry.message}}</td></tr></tbody></table></div>"
  );


  $templateCache.put('updates/partial/list/list.html',
    "<div class=col-md-12 ng-controller=ListCtrl><li>App<div ng-repeat=\"update in getAppUpdates\"><a link={{update}} ng-attr-url=updates/app/{{update}}/get file-download></a></div></li></div>"
  );

}]);

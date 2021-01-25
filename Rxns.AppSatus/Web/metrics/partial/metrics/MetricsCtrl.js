angular.module('metrics').controller('MetricsCtrl', function ($scope, $timeout, metricsHubService, VisDataSet, moment) {
    var DELAY = 1000; // delay in ms to add new data points
    var dataGroups = new VisDataSet();

    dataGroups.add({
        id: 1,
        content: 'Queue 1'
    });

    var dataSet = new VisDataSet();

    var options = {
        width: '100%',
        height: '700px',
        defaultGroup: '_',
        start: moment().add(-100, 'seconds'), // changed so its faster
        end: moment(),
        dataAxis: {
            left: {
                title: {
                    text: 'Total Event(s)'
                }
            },
            showMajorLabels: true
        },
        drawPoints: {
            size: 2,
            style: 'circle'
        },
        legend:
            {
                enabled: false,
                left:
                {
                    visible: true,
                    position: 'bottom-left'
                }
            }
    };

    $scope.graphOptions = options;
    $scope.graphData = dataSet;
    $scope.graphGroups = dataGroups;
    $scope.graphEvents = {
        onload: function (graph) {
            addDataAndRender(graph, 'discrete');
        }
    };

    function renderStep(graph, strategy) {
        // move the window (you can think of different strategies).
        var now = moment();
        var range = graph.getWindow();
        var interval = range.end - range.start;
        switch (strategy) {
            case 'continuous':
                // continuously move the window
                graph.setWindow(now - interval, now, { animation: false });
                requestAnimationFrame(function () { renderStep(graph, strategy); });
                break;

            case 'discrete':
                graph.setWindow(now - interval, now, { animation: false });
                $timeout(function () {
                    renderStep(graph, strategy);

                }, DELAY);
                return;

            default: // 'static'
                // move the window 90% to the left when now is larger than the end of the window
                if (now > range.end) {
                    graph.setWindow(now - 0.1 * interval, now + 0.9 * interval);
                }
                $timeout(function () {
                    renderStep(graph, strategy);

                }, DELAY);
                break;
        }
    }

    /**
     * Add a new datapoint to the graph
     */
    function addDataPoint(graph, point) {
        // add a new data point to the dataset
        //var now = moment();
        dataSet.add(point);
        //    ({
        //    x: now,
        //    y: y(now / 1000)
        //});

        // remove all data points which are no longer visible
        var range = graph.getWindow();
        var interval = range.end - range.start;
        var oldIds = dataSet.getIds({
            filter: function (item) {
                return item.x < range.start - interval;
            }
        });
        dataSet.remove(oldIds);
    }
    
    function merge(firstArray, secondArray, keyProperty, objectProperties) {

        function mergeObjectProperties(object, otherObject, objectPropertiesToMerge) {
            _.forEach(objectPropertiesToMerge, function (eachProperty) {
                if (otherObject.hasOwnProperty(eachProperty)) {
                    object.set(eachProperty, otherObject[eachProperty]);
                }
            });
        }

        if (firstArray.length === 0) {
            _.forEach(secondArray, function (each) {
                firstArray.push(each);
            });
        } else {
            _.forEach(secondArray, function (itemFromSecond) {
                var itemFromFirst = _.find(firstArray, function (item) {
                    return item[keyProperty] === itemFromSecond[keyProperty];
                });

                if (itemFromFirst) {

                    mergeObjectProperties(itemFromFirst, itemFromSecond, objectProperties);
                } else {
                    firstArray.push(itemFromSecond);
                }
            });
        }

        return firstArray;
    }

    //the network graph
    var nOptions = {
        physics: {
            enabled: true
        },
        
        width: '1400px',
        height: '1400px',
        edges:
        {
            scaling: {
                label: {
                    enabled: true
                },
                min: 10,
                max: 500
            }
        }
    };

    var nodes = new VisDataSet([
      //{ id: 1, label: 'matter notification generator' },
      //{ id: 2, label: 'notifications' },
      //{ id: 3, label: 'Node 3' },
      //{ id: 4, label: 'default' },
      //{ id: 5, label: 'badges' }
    ]);
    
    // create an array with edges
    var edges = new VisDataSet([
      //{ from: 1, to: 2 },
      //{ from: 2, to: 4 },
      //{ from: 5, to: 4 },
      //{ from: 3, to: 5 }
    ]);

    // create a network
    var data = {
        nodes: nodes,
        edges: edges
    };
        
    $scope.networkOptions = nOptions;
    $scope.networkData = data;
    $scope.networkGroups = dataGroups;
    $scope.networkEvents = {
        onload: function(graph) {
        }
    };

    var addDataAndRender = function (graph, renderType) {
        renderStep(graph, renderType);

        if($scope.sub) {
            return;
        }
        $scope.sub = metricsHubService.onUpdate.subscribe(function (event) {
            addDataPoint(graph, {
                x: moment(),
                y: event.Value,
                group: event.Name,
                label: {
                    className: 'chartTitle',
                    content: event.Value > 10 ? event.Name : ' ' 
                    
                }
            });


            //console.log('value:' + event.Value);

            if(event) {
                console.log('WARN:' + JSON.stringify(event));
            }
            addToNetwork(nodes, event.Name, event.Value);
        });
    };
    
    var addToNetwork = function(network, node, value) {
        var parts = getParts(node);
        var current = network.get(parts.currentId.replace('Spd', ''));

        if (!current) //we have not seen the node before ? should we asscoiate the path instead of just the name? 
        {
            var reactor = createReactors(network, node);

            var nId = getNodeId(network, parts.neighbour, parts.neighbour, 'square', 'white', '30px arial grey');
            var cId = getNodeId(network, parts.current, node, 'square', 'lightgrey', '14px arial red');
            associate(edges, cId, nId);

            //if(!parts.neighbour) {
              //  parts.neighbour = ""
            //}

            console.log("Found service: " + parts.neighbour + " -> " + parts.current);

            if (parts.neighbour !== reactor) {
                associate(edges, nId, reactor);
                console.log("Found reactor connection: " + parts.neighbour);
            }

            if (parts.currentId.includes('Spd')) {
            nodes.update({ id: parts.currentId.replace('Spd', ''), color: { background: colorFade('00CC00', 'CC0000', value * 5) } }); //set node width?
            } else {
                nodes.update({ id: parts.currentId, value: value, label: parts.current + "- " + value }); //set node width?
            }

        } else {
            if (parts.currentId.includes('Spd')) {
                nodes.update({ id: parts.currentId.replace('Spd', ''), color: { background: colorFade('00CC00', 'CC0000', value * 5) } }); //set node width?
            } else {
                nodes.update({ id: current.id, value: value, label: parts.current + "- " + value }); //set node width?
            }

        }
    };

    var createReactors = function(network, nodeName) {
        var reactors = nodeName.match(/<([^>]+)/gmi);

        var lastReactor;
        if (!reactors) {
            return null;
        }
        for (var i = 0; i < reactors.length; i++) {
            var current = reactors[i].replace('<', '').replace('>', '');

            if (lastReactor) {
                var r1Id = getNodeId(network, current, current, 'circle', 'white', '30px arial black');
                associate(edges, lastReactor, r1Id);
            }
            lastReactor = getNodeId(network, current, current, 'circle', 'white', '30px arial black');
            //console.log("Found reactor: " + lastReactor);
        }

        return lastReactor;
    };

    var associate = function (edges, idA, idB) {
        var exists1 = edges.get({
            filter: function(item) {
                return (item.from === idA && item.to === idB);
            }
        });
        if (exists1.length > 0) {
            return;
        }

        edges.add({ from: idA, to: idB });
    };


    var getParts = function (nodeName) {
        var parts = nodeName.split('.');
        var current = parts[parts.length - 1];
        var neighbour = parts[parts.length - 2];

        if (neighbour) {
             neighbour = neighbour.replace('<', '').replace('>', '');
        }
        if (current) {
             current = current.replace('<', '').replace('>', '');
        }
        
        return { neighbour: neighbour, current: current, currentId: nodeName };
    };

    var getNodeId = function (network, node, nodeId, shape, color, size) {
        if(!nodeId) {
            nodeId = "main"
        }
        if(!node) {
            node = "main"
        }
        nodeId = nodeId.replace('Spd', '');
        node = node.replace('Spd', '');

        if (network.get(nodeId)) {
            return nodeId;
        } else {

            //need to keep a list of nodes seperately and there ids because it seems i cant find these out in revserve from the the name
            network.add({ id: nodeId, label: node, shape: shape, font: size });
            return nodeId;
        }
    };

    ///////////////////////////// Colour Functions /////////////////////////////////////
    //Convert a hex value to its decimal value - the inputed hex must be in the
    // format of a hex triplet - the kind we use for HTML colours. The function
    // will return an array with three values.
    function hex2num(hex) {
        if(hex.charAt(0) === "#") { 
            hex = hex.slice(1);
        }
        hex = hex.toUpperCase();
        var hex_alphabets = "0123456789ABCDEF";
        var value = new Array(3);
        var k = 0;
        var int1,int2;
        for(var i=0;i<6;i+=2) {
            int1 = hex_alphabets.indexOf(hex.charAt(i));
            int2 = hex_alphabets.indexOf(hex.charAt(i+1));
            value[k] = (int1 * 16) + int2;
            k++;
        }
        return(value);
    }

    //Function that fades the color.
    //Arguments...
    //id  - ID of the element whose colour must be faded.
    //start_hex - The initial color of the element.
    //stop_hex - The final color. The element will fade from the initial color to the final color.
    //difference- The colour values will be incremented by this number
    //delay  - The speed of the the effect - higher delay means slower effect.
    //color_background- The fade must be for the color of the element or for its background.
    //      Allowed values are 'c'(Color of element) and 'b'(Background)
    function colorFade(start_hex,stop_hex,difference) {
        //Default values...
        if (!difference) {
            difference = 20;
        }
        if (!start_hex) {
            start_hex = "#FFFFFF";
        }
        if (!stop_hex) {
            stop_hex = "#000000";
        }

        var start= hex2num(start_hex);
        var stop = hex2num(stop_hex);
 
        //Make it numbers rather than strings.
        for(var i=0;i<3;i++) {
            start[i] = Number(start[i]);
            stop[i] = Number(stop[i]);
        }

        //Morph one colour to the other. If the start color is greater than the stop colour, start color will
        // be decremented till it reaches the stop color. If it is lower, it will incremented.
        for(i=0;i<3;i++) {
            if (start[i] < stop[i]) {
                start[i] += difference;
                if (start[i] > stop[i]) {
                    start[i] = stop[i];
                }//If we have overshot our target, make it equal - or it won't stop.
            }
            else if(start[i] > stop[i]) {
                start[i] -= difference;
                if (start[i] < stop[i]) {
                    start[i] = stop[i];
                }
            }
        }

        //Change the color(or the background color).
        var color = "rgb("+start[0]+","+start[1]+","+start[2]+")";
        return color;
    }
});
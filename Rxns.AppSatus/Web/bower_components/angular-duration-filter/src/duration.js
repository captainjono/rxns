'use strict';

// https://github.com/angular/angular.js/issues/1414
angular.module('filter.duration', ['ng'])

    .factory('$localeDurations', [function () {
        return {
            'one': {
                year: '{} year',
                month: '{} month',
                week: '{} week',
                day: '{} day',
                hour: '{} hour',
                minute: '{} minute',
                second: '{} second'
            },
            'other': {
                year: '{} years',
                month: '{} months',
                week: '{} weeks',
                day: '{} days',
                hour: '{} hours',
                minute: '{} minutes',
                second: '{} seconds'
            }
        };
    }])

    .filter('duration', ['$locale', '$localeDurations', function ($locale, $localeDurations) {
        return function duration(value, unit, precision) {

            var unitNames = ['year', 'month', 'week', 'day', 'hour', 'minute', 'second'],
                units = {
                    year: 86400*365.25,
                    month: 86400*31,
                    week: 86400*7,
                    day: 86400,
                    hour: 3600,
                    minute: 60,
                    second: 1
                },
                words = [],
                maxUnits = unitNames.length;


            precision = parseInt(precision, 10) || units[precision || 'second'] || 1;
            value = (parseInt(value, 10) || 0) * (units[unit || 'second'] || 1);

            if (value >= precision) {
                value = Math.round(value / precision) * precision;
            } else {
                maxUnits = 1;
            }

            var i, n;
            for (i = 0, n = unitNames.length; i < n && value !== 0; i++) {

                var unitName = unitNames[i],
                    unitValue = Math.floor(value / units[unitName]);

                if (unitValue !== 0) {
                    words.push(($localeDurations[unitValue] || $localeDurations[$locale.pluralCat(unitValue)] || {unitName: ('{} ' + unitName)})[unitName].replace('{}', unitValue));
                    if (--maxUnits === 0) {
                        break;
                    }
                }

                value = value % units[unitName];
            }

            return words.join(' ');
        };
    }]);

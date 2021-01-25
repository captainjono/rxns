/// <reference path="ErrorsCtrl.js" />

describe('the errors controller', function () {

	beforeEach(module('errors'));

	var scope,ctrl;

    beforeEach(inject(function($rootScope, $controller) {
      scope = $rootScope.$new();
      ctrl = $controller('ErrorsCtrl', {$scope: scope});
    }));	

    it('should act be an angular controller', inject(function () {

	    expect(ctrl).toBeDefined();

	}));

});
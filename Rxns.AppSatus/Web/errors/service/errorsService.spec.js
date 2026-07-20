/// <reference path="errorsService.js" />

describe('The errors service provider', function () {
    
    beforeEach(module('errors'));

    it('should be injected into function definitions', inject(function (errorsService) {

        expect(errorsService).toBeDefined();
        expect(errorsService.getUnresolvedErrors()).toBeDefined();
    }));
    


});
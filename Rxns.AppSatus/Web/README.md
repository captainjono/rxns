BowerPower
=========

BowerPower is a Powershell interface to use Bower.js in Visual Studio

  - It is very minimalistic
  - Extends Visual Studio's Package Manager Console's commands
  - It's a work in progress for now :)

Requirement
---
Please install node and npm bower. 

* [node.js] - Click the "Install" here
* [npm] - Read about Node package manager

Then, "npm install bower"

The only two commands
---

```sh
bowerinit
```
This tries to create a "Scripts" folder and then puts the .bowerrc and bower.json files in place.

You update the bower.json as stated in the [bower] documentation


```sh
bowerinstall
```
This gets the specified modules in the "Scripts\vendor"


[node.js]:http://nodejs.org
[npm]:https://npmjs.org/
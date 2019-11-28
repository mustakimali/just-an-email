# Need to send something quick?

> I will just send an email to myself then go to another device, login and download it! 🥳

or you could use just-an-email.com

### Running live here [justA.ML](https://justa.ml/)

#### Staging site [justaml.azurewebsites.net](http://justaml.azurewebsites.net)

[![Build status](https://ci.appveyor.com/api/projects/status/3jxpwg2tcwoiaq9b/branch/master?svg=true)](https://ci.appveyor.com/project/mustakimali/justa-ml/branch/master) Production
[![Build status](https://ci.appveyor.com/api/projects/status/notmi7mr5jd27k8d?svg=true)](https://ci.appveyor.com/project/mustakimali/justa-ml-a82xl) Development (Unstable)

[![BuitlWithDot.Net shield](https://builtwithdot.net/project/64/justa.ml/badge)](https://builtwithdot.net/project/64/justa.ml)

## Run from docker hub

```
docker run -p 61452:80 mustakimali/justaml
```
Should be running in http://localhost:61452

## Building the code
* Make sure you have [.NET Core SDK](https://www.microsoft.com/net/download/core) installed
* Clone
* `cd src\JustSending`
* `dotnet run` or `dotnet watch run`
* Should be running in http://localhost:61452

## A huge thanks to the following projects

* [Stanford Javascript Crypto Library ](https://github.com/bitwiseshiftleft/sjcl)
* [bignumber.js](https://github.com/MikeMcl/bignumber.js)

... and all other third party libraries used in this project.

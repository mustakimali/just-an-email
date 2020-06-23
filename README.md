# Need to send something quick?

> I will just send an email to myself then go to another device, login and download it! 🥳

or you could use [justa.ml](https://justa.ml)

### Running live: [https://justa.ml](https://justa.ml/)  🚀

## Run from docker hub

```
docker run -p 61452:80 mustakimali/justaml
```
Should be running in http://localhost:61452

## Building the code
* Make sure you have latest preview of [.NET Core SDK 5](https://www.microsoft.com/net/download/core) installed
* Clone
* `cd src\JustSending`
* `dotnet run` or `dotnet watch run`
* Should be running in http://localhost:61452

## A huge thanks to the following projects

* [Stanford Javascript Crypto Library ](https://github.com/bitwiseshiftleft/sjcl)
* [bignumber.js](https://github.com/MikeMcl/bignumber.js)
* [Sentry](https://sentry.io) for sponsoring license

... and all other third party libraries used in this project.

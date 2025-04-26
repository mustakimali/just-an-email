# Need to send something quick?

> I will just send an email to myself then go to another device, login and download it! 🥳

or you could use [tnxfr.com](https://tnxfr.com)

### Running live: [https://tnxfr.com](https://tnxfr.com)  🚀

(previously [https://justa.ml](https://justa.ml))

## Run from docker hub

```
docker run -p 5050:80 mustakimali/justaml
```
Should be running in http://localhost:5050

## Run locally

### Using Docker
* Build the docker image: `make docker-build`
* Run `make docker-run`
* Access the app using `localhost:5050`

### Dependencies

* Sqlite file storage for statistics data.

#### When running locally
* InProc storage for session data
* InProc storage for background jobs
* InProc SignalR (websocket) session information

#### In Production
When configured in `appsettions.json`
* Redis for session data
* Redis storage for background jobs 
* Azure SignalR service as backplane
## Building the code
* Make sure you have latest preview of [.NET Core SDK 6](https://www.microsoft.com/net/download/core) installed (you can use [this script](https://gist.github.com/mustakimali/1dd2a1f4ea22907760eb112a7cdd9c68) to install latest nightly build and use `dotnet-pre` instead of `dotnet` below)
* Clone
* `cd src\JustSending`
* `dotnet run` or `dotnet watch run`
* Should be running in http://localhost:5000

## A huge thanks to the following projects

* [Stanford Javascript Crypto Library ](https://github.com/bitwiseshiftleft/sjcl)
* [bignumber.js](https://github.com/MikeMcl/bignumber.js)
* [Sentry](https://sentry.io) for sponsoring license

... and all other third party libraries used in this project.

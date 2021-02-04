FROM mcr.microsoft.com/dotnet/sdk:5.0.102-ca-patch-buster-slim-amd64 AS build-env
# install chrome for testing
RUN \
   apt-get update && \
   apt-get install -y wget gnupg

RUN \
   wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add - && \
   echo "deb http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google.list && \
   apt-get update && \
   apt-get install -y google-chrome-stable && \
   rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY src/JustSending/JustSending.csproj ./src/JustSending/JustSending.csproj
COPY test/JustSending.Test/JustSending.Test.csproj ./test/JustSending.Test/JustSending.Test.csproj
COPY JustSending.sln .
RUN dotnet restore -r linux-x64

COPY . /app

WORKDIR /app/test/JustSending.Test

RUN rm Drivers/chromedriver
RUN cp Drivers/Linux/chromedriver Drivers/


RUN dotnet build JustSending.Test.csproj
RUN dotnet test JustSending.Test.csproj -c Debug -v q --no-build

WORKDIR /app/src/JustSending
RUN cd /app/src/JustSending
RUN dotnet publish -c Release -r linux-x64 -o out -p:PublishReadyToRun=true
RUN ls -lsah out/

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime-deps:5.0
WORKDIR /app
COPY --from=build-env /app/src/JustSending/out .
ENTRYPOINT ["./JustSending"]
VOLUME ["App_Data/"]
VOLUME ["wwwroot/uploads"]
FROM mustakimali/dotnet-sdk:2.2-chrome AS build-env

COPY . /app

WORKDIR /app/test/JustSending.Test

RUN rm Drivers/chromedriver
RUN cp Drivers/Linux/chromedriver Drivers/

RUN dotnet build JustSending.Test.csproj
RUN dotnet test JustSending.Test.csproj -c Debug -v q --no-build

WORKDIR /app/src/JustSending
RUN cd /app/src/JustSending
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:2.2-alpine
WORKDIR /app
COPY --from=build-env /app/src/JustSending/out .
ENTRYPOINT ["dotnet", "JustSending.dll"]
VOLUME ["App_Data/"]
VOLUME ["wwwroot/uploads"]

## mustakimali/dotnet-sdk:latest-chrome-stable

# FROM mcr.microsoft.com/dotnet/core/sdk:2.2 AS build-env
# RUN \
#   apt-get update && \
#   apt-get install -y wget

# RUN \
#   wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add - && \
#   echo "deb http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google.list && \
#   apt-get update && \
#   apt-get install -y google-chrome-stable && \
#   rm -rf /var/lib/apt/lists/*
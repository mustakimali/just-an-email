FROM mcr.microsoft.com/dotnet/nightly/sdk:5.0.100-preview.5 AS build-env
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
RUN dotnet restore

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
FROM mcr.microsoft.com/dotnet/nightly/aspnet:5.0-alpine
WORKDIR /app
COPY --from=build-env /app/src/JustSending/out .
ENTRYPOINT ["dotnet", "JustSending.dll"]
VOLUME ["App_Data/"]
VOLUME ["wwwroot/uploads"]
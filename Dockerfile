FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-env
# install chrome for testing
RUN \
   apt-get update && \
   apt-get install -y wget

RUN \
   wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add - && \
   echo "deb http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google.list && \
   apt-get update && \
   apt-get install -y google-chrome-stable && \
   rm -rf /var/lib/apt/lists/*

# redis
RUN cd /tmp \
    && wget http://download.redis.io/releases/redis-5.0.7.tar.gz \
    && tar -xvf redis-5.0.7.tar.gz \
    && cd redis-5.0.7 \
    && apt-get update && apt-get install -y make gcc \
    && make install 

COPY src/JustSending/JustSending.csproj src/JustSending/
COPY test/JustSending.Test/JustSending.Test.csproj test/JustSending.Test/
COPY JustSending.sln .
RUN dotnet restore

COPY . /app

WORKDIR /app/test/JustSending.Test

RUN rm Drivers/chromedriver
RUN cp Drivers/Linux/chromedriver Drivers/

RUN dotnet run --project ../../src/JustSending/JustSending.csproj &
RUN redis-server | sleep 5s && dotnet test JustSending.Test.csproj

WORKDIR /app/src/JustSending
RUN cd /app/src/JustSending
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine
WORKDIR /app
COPY --from=build-env /app/src/JustSending/out .
ENTRYPOINT ["dotnet", "JustSending.dll"]
VOLUME ["App_Data/"]
VOLUME ["wwwroot/uploads"]
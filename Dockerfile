# FROM microsoft/dotnet:2.1.401-sdk AS build-env
# 
# RUN \
#   apt-get update && \
#   apt-get install -y wget
# 
# RUN \
#   wget -q -O - https://dl-ssl.google.com/linux/linux_signing_key.pub | apt-key add - && \
#   echo "deb http://dl.google.com/linux/chrome/deb/ stable main" > /etc/apt/sources.list.d/google.list && \
#   apt-get update && \
#   apt-get install -y google-chrome-stable && \
#   rm -rf /var/lib/apt/lists/*

  
# FROM microsoft/dotnet:2.1.401-sdk AS build-env

FROM mustakimali/dotnet-2.1.401-sdk-chrome-stable AS build-env

COPY . /app

# ENV ASPNETCORE_ENVIRONMENT Development
# ENV Logging:LogLevel:Default Error
# ENV Logging:LogLevel:Microsoft Error

WORKDIR /app/test/JustSending.Test

RUN rm Drivers/chromedriver
RUN cp Drivers/Linux/chromedriver Drivers/

RUN dotnet build JustSending.Test.csproj
RUN dotnet test JustSending.Test.csproj -c Debug -v q --no-build

WORKDIR /app/src/JustSending
RUN cd /app/src/JustSending
RUN dotnet publish -c Release -o out

# Build runtime image
FROM microsoft/dotnet:2.1.3-aspnetcore-runtime-alpine3.7
WORKDIR /app
COPY --from=build-env /app/src/JustSending/out .
ENTRYPOINT ["dotnet", "JustSending.dll"]
VOLUME ["App_Data/"]
VOLUME ["wwwroot/uploads"]
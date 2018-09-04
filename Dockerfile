FROM microsoft/dotnet:2.1.401-sdk AS build-env

COPY . /app

# WORKDIR /app/test/JustSending.Test
# RUN ls
# RUN dotnet build JustSending.Test.csproj
# RUN dotnet test JustSending.Test.csproj --no-build


WORKDIR /app/src/JustSending
RUN cd /app/src/JustSending
RUN dotnet publish -c Release -o out

# Build runtime image
FROM microsoft/dotnet:2.1-aspnetcore-runtime
WORKDIR /app
COPY --from=build-env /app/src/JustSending/out .
ENTRYPOINT ["dotnet", "JustSending.dll"]
VOLUME ["App_Data/"]
VOLUME ["wwwroot/uploads"]
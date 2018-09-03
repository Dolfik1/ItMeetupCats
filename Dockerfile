FROM microsoft/dotnet:2.0-sdk AS build
COPY . ./app
WORKDIR /app/
RUN dotnet publish -c Release -o output

FROM microsoft/dotnet:2.0-runtime AS runtime
COPY --from=build /app/output .
ENTRYPOINT dotnet ItMeetupCats.dll $BOT_TOKEN

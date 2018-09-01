FROM microsoft/dotnet:2.1-sdk
WORKDIR /app

# copy fsproj and restore as distinct layers
COPY *.fsproj ./
RUN dotnet restore
# copy and build everything else
COPY . ./
RUN dotnet publish -c Release -o out
ENTRYPOINT dotnet out/ItMeetupCats.dll $BOT_TOKEN
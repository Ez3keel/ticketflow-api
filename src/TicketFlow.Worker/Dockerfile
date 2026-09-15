FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/TicketFlow.Worker/TicketFlow.Worker.csproj
RUN dotnet publish src/TicketFlow.Worker/TicketFlow.Worker.csproj -c Release -o /app/publish --no-restore


# Not mcr.microsoft.com/dotnet/runtime:8.0: even though the Worker itself hosts no
# web server, TicketFlow.Infrastructure pulls in SignalR types (TicketFlowHub :
# Hub) for the shared observability/notifier wiring, which need the ASP.NET Core
# shared framework at runtime, not just plain .NET.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 9464
ENTRYPOINT ["dotnet", "TicketFlow.Worker.dll"]

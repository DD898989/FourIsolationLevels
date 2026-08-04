# 使用 .NET 10.0 SDK 作為建置環境
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 複製專案檔並進行 NuGet 還原
COPY ["MyApiApp.csproj", "./"]
RUN dotnet restore "MyApiApp.csproj"

# 複製其餘所有原始碼並進行發佈建置
COPY . .
RUN dotnet publish "MyApiApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 使用 ASP.NET Core 10.0 執行階段映像檔
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# 從建置階段複製發佈產物
COPY --from=build /app/publish .

# 設定啟動進入點
ENTRYPOINT ["dotnet", "MyApiApp.dll"]

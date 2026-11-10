# --- Etapa de Compilación (Build) ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar el archivo de proyecto y restaurar dependencias
COPY ["FerreteriaAPI.csproj", "./"]
RUN dotnet restore "FerreteriaAPI.csproj"

# Copiar todo el código restante y compilar
COPY . .
RUN dotnet publish -c Release -o /app/publish

# --- Etapa de Ejecución (Runtime) ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Configurar el puerto dinámico para Render
ENV ASPNETCORE_URLS=http://*:$PORT

FROM mono:6.12.0.182 AS build

WORKDIR /src
COPY . .

RUN curl -fsSL https://dist.nuget.org/win-x86-commandline/v6.11.1/nuget.exe -o /tmp/nuget.exe \
    && mono /tmp/nuget.exe restore EcoServerEmulator.sln -PackagesDirectory packages -NonInteractive \
    && xbuild EcoServerEmulator.sln /t:Build /p:Configuration=Release /verbosity:minimal

FROM mono:6.12.0.182

WORKDIR /app
COPY --from=build /src/WorldServer/bin/Release/ /app/WorldServer/
COPY --from=build /src/LoginServer/bin/Release/ /app/LoginServer/
COPY --from=build /src/MapServer/bin/Release/ /app/MapServer/
COPY --from=build /src/ResearchClient/bin/Release/ /app/ResearchClient/
COPY docker/entrypoint.sh docker/run-research-client.sh /app/

RUN sed -i 's/\r$//' /app/entrypoint.sh /app/run-research-client.sh \
    && chmod +x /app/entrypoint.sh /app/run-research-client.sh

EXPOSE 17831 17832 17833

ENTRYPOINT ["/app/entrypoint.sh"]

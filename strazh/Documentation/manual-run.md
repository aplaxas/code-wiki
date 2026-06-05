
# Manual run

##### Prerequisite

Run Neo4j on your local (for example, using docker)

```
docker run --name strazh_neo4j `
    -p7474:7474 -p7687:7687 -d `
    --env NEO4J_AUTH=neo4j/strazhpass neo4j:latest
```

##### Build Strazh

```
dotnet build ./Strazh/Strazh.csproj -c Release -o ./app
```

##### Run Strazh

```
dotnet .\Strazh\bin\Debug\net9.0\Strazh.dll -c neo4j:neo4j:strazhpass -s C:\develop\baw\phase2\baw-phase2-platform\Vanuatu\Vanuatu.sln

```
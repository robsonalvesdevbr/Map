using Map.Models;
using Neo4j.Driver;
using Scalar.AspNetCore;
using Path = Map.Models.Path;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<MapService>();

builder.Services.AddSingleton<IDriver>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return GraphDatabase.Driver(config["GraphDatabase:Url"], 
        AuthTokens.Basic(config["GraphDatabase:Username"], config["GraphDatabase:Password"]));
});

builder.Services.AddOpenApi();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
 
app.MapPost("/city", async (MapService service, City request) => {
    // validation
    // mapping

    await service.AddCityAsync(request);
});

app.MapGet("/city", async (MapService service) =>
    Results.Ok((object?)await service.GetAllCities()));

app.MapGet("/city/{filter}", async (MapService service, string filter) =>
    Results.Ok((object?)await service.GetAllCities(filter)));

app.MapPost("/path", async (MapService service, string source, string destination, int distance) => {
    await service.AddPathAsync(source ,destination,distance);
});
 
app.MapGet("/path/", async (MapService service) => 
    Results.Ok(await service.GetAllPathsAsyuc()));
 
app.MapGet("/path/{source}/{destination}", async (MapService service, string source, string destination ) => 
    Results.Ok(await service.GetPathsAsync(source,destination)));

app.MapGet("/path/shortest", async (MapService service, string source, string destination ) => 
    Results.Ok(await service.GetShortestPathAsync(source,destination)));

app.MapGet("/path/shortest/info", async (MapService service, string source, string destination ) => 
    Results.Ok(await service.GetShortestPathDistanceAsync(source,destination)));


app.MapGet("/path/shortest/distance", async (MapService service, string source, string destination ) => 
    Results.Ok(await service.GetShortestPathByDistanceAsync(source,destination)));

app.UseHttpsRedirection();
app.Run();

 
public class MapService(IDriver driver)
{
    private readonly IDriver _driver = driver;
 
    public async Task AddCityAsync(City request)
    {
        var insertQuery = @"
            CREATE (:City {name: $name, population: $population})
        ";
        
        await using var session = _driver.AsyncSession();
        await session.RunAsync(insertQuery, new
        {
            name = request.Name,
            population = request.Population
        });
    }

    public async Task<List<City>> GetAllCities()
    {
        var getAllQuery = @"
            MATCH (city:City)
            RETURN city.name as name, city.population as population
        ";
        
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync(getAllQuery);
 
        var cities = new List<City>();
        await result.ForEachAsync(record =>
        {
            cities.Add(new City(
                record["name"].As<string>(),
                record["population"].As<int>()));
            
        });

        return cities;
    }
    
    public async Task<List<City>> GetAllCities(string filter)
    {
        var getAllQuery = @"
            MATCH (city:City)
            WHERE city.name CONTAINS $filter
            RETURN city.name as name, city.population as population
        ";
        
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync(getAllQuery, new {
            filter
        });
 
        var cities = new List<City>();
        await result.ForEachAsync(record =>
        {
            cities.Add(new City(
                record["name"].As<string>(),
                record["population"].As<int>()));
            
        });

        return cities;
    }

    public async Task AddPathAsync(string source, string destination, int distance)
    {
        var addPathQuery = @"
            MATCH (src:City {name:$source}), (dest:City {name:$destination})
            MERGE (src)-[r:ROAD {distance: $distance}]->(dest)      
        ";
         
        await using var session = _driver.AsyncSession();
        await session.RunAsync(addPathQuery, new {
            source,destination, distance
        });
    }

    public async Task<List<Path>> GetAllPathsAsyuc()
    {
        var getAllQuery = @"
            MATCH (src:City)-[road:ROAD]->(dest:City)
            RETURN  src.name as srcName,dest.name as destName, road.distance as roadDistance
        ";
        
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync(getAllQuery);
 
        var paths = new List<Path>();
        await result.ForEachAsync(record =>
        {
            paths.Add(new Path(
                record["srcName"].As<string>(),
                record["destName"].As<string>(),
                record["roadDistance"].As<int>()));
            
        });

        return paths;
    }
 
    public async Task<List<string>> GetPathsAsync(string source, string destination)
    {
        var routeQuery = @"
            MATCH path = (src:City {name: $source})-[r:ROAD*]->(dest:City {name: $destination})
            RETURN path
            LIMIT 3
        ";
        
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync(routeQuery, new {source, destination});
 
        var paths = new List<string>();
        await result.ForEachAsync(record =>
        {
            var path = record["path"].As<IPath>();
            var route = string.Join(" -> ", path.Nodes.Select(node => node["name"].As<string>()));
            paths.Add(route);
        });

        return paths;
    }
    
    public async Task<List<string>> GetShortestPathAsync(string source, string destination)
    {
        var routeQuery = @"
            MATCH path = shortestPath((src:City {name: $source})-[*]-(dest:City {name: $destination}))
            RETURN path
        ";
        
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync(routeQuery, new {source, destination});
 
        var paths = new List<string>();
        await result.ForEachAsync(record =>
        {
            var path = record["path"].As<IPath>();
            var route = string.Join(" -> ", path.Nodes.Select(node => node["name"].As<string>()));
            paths.Add(route);
        });

        return paths;
    }
     
    public async Task<List<string>> GetShortestPathDistanceAsync(string source, string destination)
    {
        var routeQuery = @"
            MATCH path = shortestPath((src:City {name: $source})-[*]-(dest:City {name: $destination}))
            WITH path, reduce(cost = 0, rel IN relationships(path) | cost + rel.distance) as totalDistance
            RETURN path, totalDistance
        ";
        
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync(routeQuery, new {source, destination});
 
        var paths = new List<string>();
        await result.ForEachAsync(record =>
        {
            var path = record["path"].As<IPath>();
            var pathDistance = record["totalDistance"].As<int>();
            var route = string.Join(" -> ", path.Nodes.Select(node => node["name"].As<string>()));
            paths.Add($"{route} == {pathDistance}");
        });

        return paths;
    }
    
    public async Task<ShortestPathResult> GetShortestPathByDistanceAsync(string source, string destination)
    {
        var routeQuery = @"
            MATCH (src:City {name: $source}), (dest:City {name: $destination})
            CALL algo.shortestPath.dijkstra({
                relationshipProjection: {
                    ROAD: {
                        type: 'ROAD',
                        properties: 'distance',
                        direction: 'both'
                    }
                },
                sourceNode: src,
                targetNode: dest,
                costProperty: 'distance'
            })
            YIELD totalCost, path
            RETURN path,totalCost
        ";
        
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync(routeQuery, new {source, destination});

        
        var funcResult = new ShortestPathResult();
        await result.ForEachAsync(record =>
        {
            funcResult.Path = record["path"]?.ToString() ?? string.Empty;
            funcResult.Cost = record["totalCost"].As<int>();
        });

        return funcResult;
    }
}
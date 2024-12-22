# Navigation mesh construction and query webservice

This project is a HTTP webservice that can build navigation meshes for constructs, and answer path queries in it.



https://github.com/user-attachments/assets/945a5988-86bf-4616-a950-508c0fa88011


## Running

    ./RecastServer

or:

    ./RecastServer VOXEL_BASE_URL ORLEANS_BASE_URL LISTEN_URL

## Routes

### POST /navigation/build/{constructId}

Expects a JSON body (don't forget to set content-type) with build options matching:

    public class DtoBuildOptions
    {
        public List<ulong> ignoredTypes;
        public List<ulong> ignoredLocalIds;
        public List<ulong> ignoredIds;
    }

Will construct and store a nav mesh from voxels and elements for given construct id.


### POST /navigation/path/{constructId}

Expects a JSON body object with fields "origin" and "destination", each an array of 3 floats x,y,z in construct local coordinates.

Returns an array of 3-float arrays: the straight line segments path to follow to go from origin to destination.

If the nav mesh was not previously built, build it, ignoring all elements.


# Sample Mod code

The following code shows how a NPC can follow a path to go from waypoint to waypoint without going through walls or elements.

Be sure to adjust the RecastServer URL before running.

```csharp

public class DtoPath
{
    public List<double> origin;
    public List<double> destination;
}

public class Building
{
    public readonly ulong constructId;
    public readonly IServiceProvider isp;
    public List<NQ.Vec3> waypoints = new();
    public HttpClient httpClient;
    
    public Building(ulong cid, IServiceProvider isp)
    {
        constructId = cid;
        this.isp = isp;
        httpClient = isp.GetRequiredService<IHttpClientFactory>().CreateClient();
    }
    public async Task Initialize()
    {
        var orleans = isp.GetRequiredService<IClusterClient>();
        var elems = await orleans.GetConstructElementsGrain(constructId).GetVisibleAt(0);
        foreach (var el in elems.elements)
        {
            if (el.elementType == 2012928469) // Pressure plates are our waypoints
                waypoints.Add(el.position);
        }
    }
    public async Task<List<NQ.Vec3>> QueryPath(DtoPath query)
    {
        // If this mods runs in docker, but recast server doesn't, 127.0.0.1 will not work, use your host's LAN IP instead.
        var path = await httpClient.Post<List<List<double>>>($"http://192.168.1.42:8879/navigation/path/{constructId}", query, binary: false);
        var res = new List<NQ.Vec3>();
        foreach(var p in path)
        {
            res.Add(new NQ.Vec3 { x = p[0], y = p[1], z = p[2]});
        }
        return res;
    }
}

public class Character
{
    public Building building;
    public readonly ulong playerId;
    public List<NQ.Vec3> waypoints = new();
    public int waypointIndex;
    public NQ.Vec3 position;
    public Random rnd;
    public Character(ulong pid, Building b)
    {
        building = b;
        playerId = pid;
        rnd = new();
        position = building.waypoints[rnd.Next(0, building.waypoints.Count)];
        _ = Task.Factory.StartNew(async () => {
                while (true)
                {
                    try
                    {
                        await PositionLoop();
                    }
                    catch (Exception e)
                    {
                        building.isp.GetRequiredService<ILogger<Character>>()
                           .LogError(e, "bronking in position loop");
                    }
                }
        });
    }
    protected async Task PositionLoop()
    {
        while (true)
        {
            if (waypointIndex >= waypoints.Count)
            {
                var target = building.waypoints[rnd.Next(0, building.waypoints.Count)];
                waypoints = await building.QueryPath(new DtoPath
                    {
                        origin = new List<double>{ position.x, position.y, position.z},
                        destination = new List<double>{target.x, target.y, target.z},
                    });
                waypointIndex = 1;
            }
            var direction = waypoints[waypointIndex] - position;
            if (direction.Norm() < 0.4)
            {
                waypointIndex += 1;
                continue;
            }
            direction = direction * (0.25 / direction.Norm()); // a 10th of 2.5m/s
            var next = position + direction;
            position = next;
            var loc = new SimpleLocation
            {
                ConstructId = building.constructId,
                Position = position,
            };
            var rot = NQ.Quat.Identity, // Left as an exercise to the reader.
            var pu = new PlayerUpdate
            {
                playerId = playerId,
                constructId = building.constructId,
                position = position,
                rotation = rot,
                velocity = direction * 10.0,
                time = TimePoint.Now(),
            };
            var op = new NQutils.Messages.PlayerUpdate(pu);
            await building.isp.GetRequiredService<NQ.Visibility.Internal.InternalClient>()
                .PublishGenericEventAsync(new EventLocation
                    {
                        Event = NQutils.Serialization.Grpc.MakeEvent(op),
                        Location = loc,
                        VisibilityDistance = 1000,
                    });
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}

```

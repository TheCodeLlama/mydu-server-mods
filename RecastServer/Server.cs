
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Numerics;
using System.Text;
using System.Xml.Schema;
using DotRecast.Core;
using DotRecast.Core.Collections;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using DotRecast.Recast.Toolset.Builder;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Npgsql.Internal.TypeHandlers.GeometricHandlers;
using SharpGLTF.Schema2;

public class FixInputGeomProvider: IInputGeomProvider
{
    public static FixInputGeomProvider LoadFile(string objFilePath)
    {
        byte[] chunk = RcIO.ReadFileIfFound(objFilePath);
        var context = RcObjImporter.LoadContext(chunk);
        return new FixInputGeomProvider(context.vertexPositions, context.meshFaces);
    }
    public FixInputGeomProvider(List<float> vertexPositions, List<int> meshFaces)
            : this(MapVertices(vertexPositions), MapFaces(meshFaces))
        {
        }
        public float[] vertices;
        public int[] faces;
        public float[] normals;
        private RcVec3f bmin;
        private RcVec3f bmax;

        private readonly List<RcConvexVolume> volumes = new List<RcConvexVolume>();
        private RcTriMesh _mesh;

        private static int[] MapFaces(List<int> meshFaces)
        {
            int[] faces = new int[meshFaces.Count];
            for (int i = 0; i < faces.Length; i++)
            {
                faces[i] = meshFaces[i];
            }

            return faces;
        }

        private static float[] MapVertices(List<float> vertexPositions)
        {
            float[] vertices = new float[vertexPositions.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = vertexPositions[i];
            }

            return vertices;
        }

        public FixInputGeomProvider(float[] vertices, int[] faces)
        {
            Replace(vertices, faces);
        }
        public void Replace(float[] vertices, int[] faces)
        {
            Console.WriteLine($"{vertices.Length}");
            this.vertices = vertices;
            this.faces = faces;
            normals = new float[faces.Length];
            CalculateNormals();
            bmin = new RcVec3f(vertices);
            bmax = new RcVec3f(vertices);
            for (int i = 1; i < vertices.Length / 3; i++)
            {
                bmin = RcVec3f.Min(bmin, RcVec.Create(vertices, i * 3));
                bmax = RcVec3f.Max(bmax, RcVec.Create(vertices, i * 3));
            }

            _mesh = new RcTriMesh(vertices, faces);
        }
        public void SwapYZ()
        {
            for (int i = 0; i < vertices.Length / 3; i++)
            {
                float z = vertices[i*3+2];
                vertices[i*3+2] = vertices[i*3+1];
                vertices[i*3+1] = z;
            }
            CalculateNormals();
            bmin = new RcVec3f(vertices);
            bmax = new RcVec3f(vertices);
            for (int i = 1; i < vertices.Length / 3; i++)
            {
                bmin = RcVec3f.Min(bmin, RcVec.Create(vertices, i * 3));
                bmax = RcVec3f.Max(bmax, RcVec.Create(vertices, i * 3));
            }

            _mesh = new RcTriMesh(vertices, faces);
            Console.WriteLine($"after swap: {bmin} {bmax}");
        }
        public void Save(string filename)
        {
            SaveObj(filename, _mesh);
        }
        private void SaveObj(string filename, RcTriMesh mesh)
        {
            try
            {
                string path = Path.Combine("test-output", filename);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using StreamWriter fw = new StreamWriter(path);
                var verts = mesh.GetVerts();
                for (int v = 0; v < verts.Length/3; v++)
                {
                    fw.Write($"v {verts[v*3]} {verts[v*3+1]} {verts[v*3+2]}\n");
                }
                fw.Write("\ng default\n");
                var tris = mesh.GetTris();
                for (int i = 0; i < tris.Length/3; i++)
                {
                    fw.Write($"f {tris[i*3]+1} {tris[i*3+1]+1} {tris[i*3+2]+1}\n");
                }

                fw.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public RcTriMesh GetMesh()
        {
            return _mesh;
        }

        public RcVec3f GetMeshBoundsMin()
        {
            return bmin;
        }

        public RcVec3f GetMeshBoundsMax()
        {
            return bmax;
        }

        public IList<RcConvexVolume> ConvexVolumes()
        {
            return volumes;
        }

        public void AddConvexVolume(float[] verts, float minh, float maxh, RcAreaModification areaMod)
        {
            RcConvexVolume vol = new RcConvexVolume();
            vol.hmin = minh;
            vol.hmax = maxh;
            vol.verts = verts;
            vol.areaMod = areaMod;
        }

        public void AddConvexVolume(RcConvexVolume convexVolume)
        {
            volumes.Add(convexVolume);
        }

        public IEnumerable<RcTriMesh> Meshes()
        {
            return RcImmutableArray.Create(_mesh);
        }

        public List<RcOffMeshConnection> GetOffMeshConnections()
        {
            return new();
        }

        public void AddOffMeshConnection(RcVec3f start, RcVec3f end, float radius, bool bidir, int area, int flags)
        {
            throw new NotImplementedException();
        }

        public void RemoveOffMeshConnections(Predicate<RcOffMeshConnection> filter)
        {
            throw new NotImplementedException();
        }

        public void CalculateNormals()
        {
            for (int i = 0; i < faces.Length; i += 3)
            {
                int v0 = faces[i] * 3;
                int v1 = faces[i + 1] * 3;
                int v2 = faces[i + 2] * 3;

                var e0 = new RcVec3f();
                var e1 = new RcVec3f();
                e0.X = vertices[v1 + 0] - vertices[v0 + 0];
                e0.Y = vertices[v1 + 1] - vertices[v0 + 1];
                e0.Z = vertices[v1 + 2] - vertices[v0 + 2];

                e1.X = vertices[v2 + 0] - vertices[v0 + 0];
                e1.Y = vertices[v2 + 1] - vertices[v0 + 1];
                e1.Z = vertices[v2 + 2] - vertices[v0 + 2];

                normals[i] = e0.Y * e1.Z - e0.Z * e1.Y;
                normals[i + 1] = e0.Z * e1.X - e0.X * e1.Z;
                normals[i + 2] = e0.X * e1.Y - e0.Y * e1.X;
                float d = MathF.Sqrt(normals[i] * normals[i] + normals[i + 1] * normals[i + 1] + normals[i + 2] * normals[i + 2]);
                if (d > 0)
                {
                    d = 1.0f / d;
                    normals[i] *= d;
                    normals[i + 1] *= d;
                    normals[i + 2] *= d;
                }
            }
        }
}
public class RecastBuilder
{
    private const float m_cellSize = 0.3f;
    private const float m_cellHeight = 0.2f;
    private const float m_agentHeight = 2.0f;
    private const float m_agentRadius = 0.6f;
    private const float m_agentMaxClimb = 0.9f;
    private const float m_agentMaxSlope = 45.0f;
    private const int m_regionMinSize = 8;
    private const int m_regionMergeSize = 20;
    private const float m_edgeMaxLen = 12.0f;
    private const float m_edgeMaxError = 1.3f;
    private const int m_vertsPerPoly = 6;
    private const float m_detailSampleDist = 6.0f;
    private const float m_detailSampleMaxError = 1.0f;
    private RcPartition m_partitionType = RcPartition.WATERSHED;

    public DtNavMesh  Build(string filename)
    {
        IInputGeomProvider geomProvider = null;
        if (filename.EndsWith("glb"))
            geomProvider = LoadGLTF(filename);
        else
            geomProvider = FixInputGeomProvider.LoadFile(filename);
        
        return Build(geomProvider);
    }
    public IInputGeomProvider LoadGLTF(string filename)
    {
        var model = SharpGLTF.Schema2.ModelRoot.Load(filename);
        model.SaveAsWavefront("/tmp/temp-obj.obj");
        var geomProvider = FixInputGeomProvider.LoadFile("/tmp/temp-obj.obj");
        geomProvider.SwapYZ();
        return geomProvider;
    }
    public DtNavMesh Build(IInputGeomProvider geomProvider)
    {
        long time = RcFrequency.Ticks;
        RcVec3f bmin = geomProvider.GetMeshBoundsMin();
        RcVec3f bmax = geomProvider.GetMeshBoundsMax();
        RcContext m_ctx = new RcContext();
        //
        // Step 1. Initialize build config.
        //

        // Init build configuration from GUI
        RcConfig cfg = new RcConfig(
            m_partitionType,
            m_cellSize, m_cellHeight,
            m_agentMaxSlope, m_agentHeight, m_agentRadius, m_agentMaxClimb,
            m_regionMinSize, m_regionMergeSize,
            m_edgeMaxLen, m_edgeMaxError,
            m_vertsPerPoly,
            m_detailSampleDist, m_detailSampleMaxError,
            true, true, true,
            SampleAreaModifications.SAMPLE_AREAMOD_WALKABLE, true);
        RcBuilderConfig bcfg = new RcBuilderConfig(cfg, bmin, bmax);

        //
        // Step 2. Rasterize input polygon soup.
        //

        // Allocate voxel heightfield where we rasterize our input data to.
        RcHeightfield m_solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);

        foreach (RcTriMesh geom in geomProvider.Meshes())
        {
            float[] verts = geom.GetVerts();
            int[] tris = geom.GetTris();
            int ntris = tris.Length / 3;

            // Allocate array that can hold triangle area types.
            // If you have multiple meshes you need to process, allocate
            // and array which can hold the max number of triangles you need to process.

            // Find triangles which are walkable based on their slope and rasterize them.
            // If your input data is multiple meshes, you can transform them here, calculate
            // the are type for each of the meshes and rasterize them.
            int[] m_triareas = RcRecast.MarkWalkableTriangles(m_ctx, cfg.WalkableSlopeAngle, verts, tris, ntris, cfg.WalkableAreaMod);
            RcRasterizations.RasterizeTriangles(m_ctx, verts, tris, m_triareas, ntris, m_solid, cfg.WalkableClimb);
        }

        //
        // Step 3. Filter walkable surfaces.
        //

        // Once all geometry is rasterized, we do initial pass of filtering to
        // remove unwanted overhangs caused by the conservative rasterization
        // as well as filter spans where the character cannot possibly stand.
        RcFilters.FilterLowHangingWalkableObstacles(m_ctx, cfg.WalkableClimb, m_solid);
        RcFilters.FilterLedgeSpans(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);
        RcFilters.FilterWalkableLowHeightSpans(m_ctx, cfg.WalkableHeight, m_solid);

        //
        // Step 4. Partition walkable surface to simple regions.
        //

        // Compact the heightfield so that it is faster to handle from now on.
        // This will result more cache coherent data as well as the neighbours
        // between walkable cells will be calculated.
        RcCompactHeightfield m_chf = RcCompacts.BuildCompactHeightfield(m_ctx, cfg.WalkableHeight, cfg.WalkableClimb, m_solid);

        // Erode the walkable area by agent radius.
        RcAreas.ErodeWalkableArea(m_ctx, cfg.WalkableRadius, m_chf);

        // (Optional) Mark areas.
        //
        // ConvexVolume vols = m_geom->GetConvexVolumes(); for (int i = 0; i < m_geom->GetConvexVolumeCount(); ++i)
         // RcMarkConvexPolyArea(m_ctx, vols[i].verts, vols[i].nverts, vols[i].hmin, vols[i].hmax, (unsigned
         //char)vols[i].area, *m_chf);
         //

        // Partition the heightfield so that we can use simple algorithm later
        // to triangulate the walkable areas.
        // There are 3 partitioning methods, each with some pros and cons:
        // 1) Watershed partitioning
        // - the classic Recast partitioning
        // - creates the nicest tessellation
        // - usually slowest
        // - partitions the heightfield into nice regions without holes or
        // overlaps
        // - the are some corner cases where this method creates produces holes
        // and overlaps
        // - holes may appear when a small obstacles is close to large open area
        // (triangulation can handle this)
        // - overlaps may occur if you have narrow spiral corridors (i.e
        // stairs), this make triangulation to fail
        // * generally the best choice if you precompute the navmesh, use this
        // if you have large open areas
        // 2) Monotone partioning
        // - fastest
        // - partitions the heightfield into regions without holes and overlaps
        // (guaranteed)
        // - creates long thin polygons, which sometimes causes paths with
        // detours
        // * use this if you want fast navmesh generation
        // 3) Layer partitoining
        // - quite fast
        // - partitions the heighfield into non-overlapping regions
        // - relies on the triangulation code to cope with holes (thus slower
        // than monotone partitioning)
        // - produces better triangles than monotone partitioning
        // - does not have the corner cases of watershed partitioning
        // - can be slow and create a bit ugly tessellation (still better than
        // monotone)
        // if you have large open areas with small obstacles (not a problem if
        // you use tiles)
        // * good choice to use for tiled navmesh with medium and small sized
        // tiles
        long time3 = RcFrequency.Ticks;

        if (m_partitionType == RcPartition.WATERSHED)
        {
            // Prepare for region partitioning, by calculating distance field
            // along the walkable surface.
            RcRegions.BuildDistanceField(m_ctx, m_chf);
            // Partition the walkable surface into simple regions without holes.
            RcRegions.BuildRegions(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
        }
        else if (m_partitionType == RcPartition.MONOTONE)
        {
            // Partition the walkable surface into simple regions without holes.
            // Monotone partitioning does not need distancefield.
            RcRegions.BuildRegionsMonotone(m_ctx, m_chf, cfg.MinRegionArea, cfg.MergeRegionArea);
        }
        else
        {
            // Partition the walkable surface into simple regions without holes.
            RcRegions.BuildLayerRegions(m_ctx, m_chf, cfg.MinRegionArea);
        }

        //
        // Step 5. Trace and simplify region contours.
        //

        // Create contours.
        RcContourSet m_cset = RcContours.BuildContours(m_ctx, m_chf, cfg.MaxSimplificationError, cfg.MaxEdgeLen, RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);

        //
        // Step 6. Build polygons mesh from contours.
        //

        // Build polygon navmesh from the contours.
        RcPolyMesh m_pmesh = RcMeshs.BuildPolyMesh(m_ctx, m_cset, cfg.MaxVertsPerPoly);
        //
        // Step 7. Create detail mesh which allows to access approximate height
        // on each polygon.
        //

        RcPolyMeshDetail m_dmesh = RcMeshDetails.BuildPolyMeshDetail(m_ctx, m_pmesh, m_chf, cfg.DetailSampleDist,
            cfg.DetailSampleMaxError);
        long time2 = RcFrequency.Ticks;

        var rcResult = new RcBuilderResult(0, 0, m_solid, m_chf, m_cset, m_pmesh, m_dmesh, m_ctx);
        DtNavMeshCreateParams options = DemoNavMeshBuilder.GetNavMeshCreateParams(geomProvider, m_cellSize, m_cellHeight, m_agentHeight, m_agentRadius, m_agentMaxClimb, rcResult);

        var meshData = DtNavMeshBuilder.CreateNavMeshData(options);
        Console.WriteLine($"{options.vertCount} {options.polyCount}");
        var nm = new DtNavMesh();
        nm.Init(meshData, 10000, 0);
        return nm;
        //SaveObj(filename.Substring(0, filename.LastIndexOf('.')) + "_" + partitionType + "_detail.obj", m_dmesh);
        //SaveObj(filename.Substring(0, filename.LastIndexOf('.')) + "_" + partitionType + ".obj", m_pmesh);
        //foreach (var rtt in m_ctx.ToList())
        //{
        //    Console.WriteLine($"{rtt.Key} : {rtt.Millis} ms");
       // }
    }
    protected void AddFace(List<float> verts, List<int> faces, Vec3 p, Quaternion q, double xmin, double ymin, double zmin, double xmax, double ymax, double zmax)
    {

    }
    public void Augment(FixInputGeomProvider geom, DtoElements elems, Dictionary<ulong, DtoBox> boxes, DtoBuildOptions options)
    {
        var verts = new List<float>();
        var faces = new List<int>();
        foreach(var el in elems.elements)
        {
            if (options.ignoredIds != null && options.ignoredIds.Contains(el.elementId))
                continue;
            if (options.ignoredLocalIds != null && options.ignoredLocalIds.Contains(el.localId))
                continue;
            if (options.ignoredTypes != null && options.ignoredTypes.Contains(el.elementType))
                continue;
            if (!boxes.ContainsKey(el.elementType))
            {
                Console.WriteLine($"Dropping element of type {el.elementType}");
                continue;
            }
            
            var box = boxes[el.elementType];
            var q = new System.Numerics.Quaternion();
            q.X = (float)el.rotation.x;
            q.Y = (float)el.rotation.y;
            q.Z = (float)el.rotation.z;
            q.W = (float)el.rotation.w;
            // Note: lying to match chatgpt face output order
            var p000 = new Vector3(box.min[0], box.min[1], box.min[2]);
            var p001 = new Vector3(box.max[0], box.min[1], box.min[2]);
            var p010 = new Vector3(box.max[0], box.max[1], box.min[2]);
            var p011 = new Vector3(box.min[0], box.max[1], box.min[2]);
            var p100 = new Vector3(box.min[0], box.min[1], box.max[2]);
            var p101 = new Vector3(box.max[0], box.min[1], box.max[2]);
            var p110 = new Vector3(box.max[0], box.max[1], box.max[2]);
            var p111 = new Vector3(box.min[0], box.max[1], box.max[2]);
            var plist = new List<Vector3>{ p000, p001, p010, p011, p100, p101, p110, p111};
            var vShift = verts.Count / 3;
            foreach (var p in plist)
            {
                var a = new Vector3(el.position.x, el.position.y, el.position.z) + Vector3.Transform(p, q);
                verts.Add(a.X);
                verts.Add(a.Y);
                verts.Add(a.Z);
            }
            var newFaces = new int[]{ // Finally chatgpt proving itself useful :)
                // Back face
                0, 1, 2,  0, 2, 3,
                // Front face
                4, 5, 6,  4, 6, 7,
                // Left face
                0, 4, 7,  0, 7, 3,
                // Right face
                1, 5, 6,  1, 6, 2,
                // Bottom face
                0, 1, 5,  0, 5, 4,
                // Top face
                3, 2, 6,  3, 6, 7
            };
            foreach (var f in newFaces)
            {
                faces.Add(f+vShift);
            }
        }
        var fShift = geom.vertices.Length / 3;
        for (int i = 0; i< faces.Count; i++)
            faces[i] = faces[i] + fShift;
        var averts = new float[geom.vertices.Length + verts.Count];
        Array.Copy(geom.vertices, averts, geom.vertices.Length);
        for (int i=0; i<verts.Count; i++)
            averts[i+geom.vertices.Length] = verts[i];
        var afaces = new int[geom.faces.Length + faces.Count];
        Array.Copy(geom.faces, afaces, geom.faces.Length);
        for (int i=0; i<faces.Count; i++)
            afaces[i+geom.faces.Length] = faces[i];
        var maxf = 0;
        foreach(var f in geom.faces)
        {
            maxf = Math.Max(f, maxf);
        }
        Console.WriteLine($"MAX {maxf} COUNT {geom.vertices.Length/3}");
        geom.Replace(averts, afaces);
    }
     public DtNavMesh Build(string gltf, DtoElements elems, Dictionary<ulong, DtoBox> boxes, DtoBuildOptions options)
    {
         var model = SharpGLTF.Schema2.ModelRoot.Load(gltf);
        model.SaveAsWavefront("/tmp/temp-obj.obj");
        var geomProvider = FixInputGeomProvider.LoadFile("/tmp/temp-obj.obj");
        Augment(geomProvider, elems, boxes, options);
        geomProvider.SwapYZ();
        geomProvider.Save("/tmp/augmented.obj"); // Debug
        return Build(geomProvider);
    }
}

public class DtoPath
{
    public List<float> origin;
    public List<float> destination;
}
public class DtoBuildOptions
{
    public List<ulong> ignoredTypes;
    public List<ulong> ignoredLocalIds;
    public List<ulong> ignoredIds;
}

public class Vec3
{
    public float x;
    public float y;
    public float z;
}

public class Quat
{
    public double x;
    public double y;
    public double z;
    public double w;
}
public class DtoElement
{
        public ulong elementId { get; set; }
        public ulong localId { get; set; }
        public ulong constructId { get; set; }
        public ulong elementType { get; set; }
        public Vec3 position { get; set; }
        public Quat rotation { get; set; }
}
public class DtoBox
{
    public List<float> min;
    public List<float> max;
}
public class DtoItem
{
    public DtoBox box; 
}
public class DtoElements
{
    public List<DtoElement> elements;
}
public class Storage
{
    public static ConcurrentDictionary<ulong, DtNavMesh> ByConstruct = new();
    public static string VoxelBase = "http://localhost:8081";
    public static string OrleansBase = "http://localhost:10111";

    public static async Task<DtNavMesh> Fetch(HttpClient client, ulong cid)
    {
        if (ByConstruct.ContainsKey(cid))
        {
            while (ByConstruct[cid] == null)
                await Task.Delay(50);
            return ByConstruct[cid];
        }
        ByConstruct[cid] = null;
        var payload = await client.GetRaw($"{VoxelBase}/public/voxels/constructs/{cid}/mesh.glb");
        var name = $"/tmp/mesh-{cid}.glb";
        System.IO.File.WriteAllBytes(name, payload.ToArray());
        ByConstruct[cid] = (new RecastBuilder()).Build(name);
        return ByConstruct[cid];
    }
    public static ulong IdFor(string name)
    {
        uint id = 0;
        if (name != "InvalidItem")
        {
            foreach (char c in name)
            {
                IdHash(ref id, c);
            }
        }
        return id;
    }
    private static void IdHash(ref uint id, char c)
    {
        // IMPORTANT: must be consistent with item_definition.py:make_id and NQ::GameplayDefinition::itemIdHash
        if (c >= 'A' && c <= 'Z')
        {
            c += (char)32; // To lowercase
        }
        if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z'))
        {
            id ^= c + 0x9e3779b9 + (id << 6) + (id >> 2);
        }

    }
   
    public static async Task Build(HttpClient client, ulong cid, DtoBuildOptions options)
    {
        var payload = await client.GetRaw($"{VoxelBase}/public/voxels/constructs/{cid}/mesh.glb");
        var name = $"/tmp/mesh-{cid}.glb";
        System.IO.File.WriteAllBytes(name, payload.ToArray());
        var elems = await client.Get<DtoElements>($"{OrleansBase}/public/elements/{cid}/lod/0", new Dictionary<string, string>{{"Accept", "application/json"}});
        var boxs = System.IO.File.ReadAllText("items-boxes.json");
        var boxes = JsonConvert.DeserializeObject<Dictionary<string, DtoItem>>(boxs);
        var boxt = new Dictionary<ulong, DtoBox>();
        foreach(var kv in boxes)
        {
            var id = IdFor(kv.Key);
            if (!boxt.ContainsKey(id))
                boxt.Add(id, kv.Value.box);
        }
        ByConstruct[cid] = (new RecastBuilder()).Build(name, elems, boxt, options);
    }
}
 public static class HTTPClientExtensions
    {
        private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, HttpContent content = null,
            Dictionary<string, string> headers = null)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, uri)
            {
                Content = content
            };
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }
            return request;
        }

        private static async Task<HttpResponseMessage> MakeRequest(HttpClient client, HttpRequestMessage request, string callerName)
        {
            return await client.SendAsync(request);
        }
         public static async Task<Memory<byte>> GetRaw(this HttpClient client, string requestUri, Dictionary<string, string> headers = null, string callerName = "")
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, headers: headers);
            using HttpResponseMessage response = await MakeRequest(client, request, callerName);
            return await response.Content.ReadAsByteArrayAsync();
        }
        public static async Task<T> Get<T>(this HttpClient client, string requestUri, Dictionary<string, string> headers = null, string callerName = "")
        {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, requestUri, headers: headers);
            using HttpResponseMessage response = await MakeRequest(client, request, callerName);
            return await DeserializeResponse<T>(response);
        }
        private static async Task<T> DeserializeResponse<T>(HttpResponseMessage response)
        {
            byte[] buffer = await response.Content.ReadAsByteArrayAsync();
            string contentType = response.Content.Headers.ContentType?.MediaType;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                T result = JsonConvert.DeserializeObject<T>(System.Text.Encoding.UTF8.GetString(buffer));
                return result;
            }
            throw new Exception("bad status code");
        }
    }

[Route("[controller]")]
public class NavigationController : ControllerBase
{
    private readonly IHttpClientFactory ClientFactory;
    private readonly HttpClient Client;
    public NavigationController(IHttpClientFactory cf)
    {
        this.ClientFactory = cf;
        this.Client = cf.CreateClient();
    }
    [HttpPost]
    [Route("build/{id}")]
    public async Task<IActionResult> Build(ulong id, [FromBody]DtoBuildOptions options)
    {
        await Storage.Build(Client, id, options);
        return Ok(true);
    }

    [HttpPost]
    [Route("path/{id}")]
    public async Task<IActionResult> GetPath(ulong id, [FromBody]DtoPath path)
    {
        var nav = await Storage.Fetch(Client, id);
        DtNavMeshQuery q = new DtNavMeshQuery(nav);
        var vStart = new RcVec3f(path.origin[0], path.origin[2], path.origin[1]);
        var vEnd = new RcVec3f(path.destination[0], path.destination[2], path.destination[1]);
        Console.WriteLine($"origin: {vStart} dest: {vEnd}");
        var status = q.FindNearestPoly(vStart,
            new RcVec3f(0.1f, 0.5f, 0.1f),
            new DtQueryDefaultFilter() , out var pOrigin, out var cOrigin, out var hOrigin);
        if (!status.Succeeded())
        {
            Console.WriteLine($"{status.Value}");
            throw new Exception("origin not in poly");
        }
        status = q.FindNearestPoly(vEnd,
            new RcVec3f(0.1f, 0.5f, 0.1f),
            new DtQueryDefaultFilter(), out var pDestination, out var cDestination, out var hDestination);
        if (!status.Succeeded())
            throw new Exception("origin not in poly");
        var resultPath = new List<long>();
        status = q.FindPath(pOrigin, pDestination, vStart,vEnd, new DtQueryDefaultFilter(),ref resultPath, DtFindPathOption.NoOption);
        if (!status.Succeeded())
            throw new Exception("no path");
        var lines = new DtStraightPath[10];
        status = q.FindStraightPath(vStart, vEnd, resultPath, resultPath.Count, lines, out var lCount, 50, 0);
        if (!status.Succeeded())
            throw new Exception("no lines");
        var res = new List<List<float>>();
        for (var i=0; i<lCount; i++)
            res.Add(new List<float>{lines[i].pos.X, lines[i].pos.Z, lines[i].pos.Y});
        return Ok(res);
    }
}
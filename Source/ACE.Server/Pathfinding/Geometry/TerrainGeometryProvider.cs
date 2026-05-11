using System;
using System.Collections.Generic;

using ACE.Entity;
using ACE.Server.Entity;

using DotRecast.Core.Collections;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

namespace ACE.Server.Pathfinding.Geometry
{
    /// <summary>
    /// Recast <see cref="IInputGeomProvider"/> for an outdoor landblock.
    /// Builds a triangle mesh directly from the AC <see cref="LandblockMesh"/>
    /// (9x9 vertices, 8x8 cells, 2 triangles per cell) using landblock-local
    /// coordinates: (x in [0,192], z = height, y in [0,192]).
    ///
    /// Coordinate system matches what <see cref="Pathfinder"/> already uses for queries:
    /// AC X -> Recast X, AC Z (up) -> Recast Y, AC Y -> Recast Z.
    /// </summary>
    public class TerrainGeometryProvider : IInputGeomProvider
    {
        public readonly float[] vertices;
        public readonly int[] faces;
        public readonly float[] normals;

        private readonly RcVec3f bmin;
        private readonly RcVec3f bmax;
        private readonly List<RcConvexVolume> volumes = new List<RcConvexVolume>();
        private readonly RcTriMesh _mesh;

        public static TerrainGeometryProvider LoadGeometry(uint landblockId)
        {
            var lbId = new LandblockId(landblockId | 0xFFFF);
            var landblockMesh = new LandblockMesh(lbId);

            if (landblockMesh.Vertices == null || landblockMesh.Vertices.Count == 0
                || landblockMesh.Triangles == null || landblockMesh.Triangles.Count == 0)
            {
                return null;
            }

            var vertList = new List<float>(landblockMesh.Vertices.Count * 3 + 1024);
            var triList = new List<int>(landblockMesh.Triangles.Count * 3 + 1024);

            // Terrain: pack into Recast (X, Y_up, Z) layout.
            for (int i = 0; i < landblockMesh.Vertices.Count; i++)
            {
                var v = landblockMesh.Vertices[i];
                vertList.Add(v.X);
                vertList.Add(v.Z);
                vertList.Add(v.Y);
            }
            for (int i = 0; i < landblockMesh.Triangles.Count; i++)
            {
                var t = landblockMesh.Triangles[i];
                triList.Add(t.Indices[0]);
                triList.Add(t.Indices[1]);
                triList.Add(t.Indices[2]);
            }

            // Buildings + outdoor static objects: append their physics polygons.
            // LandblockGeometryExporter already produces vertices in (X, Y_up, Z) layout
            // and with the winding Recast expects. We just need to triangulate n-gons.
            try
            {
                var lbGeom = new LandblockGeometry(landblockId);
                if (lbGeom.LandblockInfo != null)
                {
                    var exporter = new LandblockGeometryExporter(lbGeom);
                    exporter.LoadOutdoorStaticObjects();

                    var baseIndex = vertList.Count / 3;
                    foreach (var v in exporter.Vertices)
                    {
                        vertList.Add(v.X);
                        vertList.Add(v.Y);
                        vertList.Add(v.Z);
                    }

                    foreach (var poly in exporter.Polygons)
                    {
                        if (poly == null || poly.Count < 3)
                            continue;

                        // Exporter polygon indices are 1-based; rebase against our combined buffer.
                        // Fan-triangulate n-gons.
                        var i0 = baseIndex + (poly[0] - 1);
                        for (int i = 1; i < poly.Count - 1; i++)
                        {
                            var i1 = baseIndex + (poly[i] - 1);
                            var i2 = baseIndex + (poly[i + 1] - 1);
                            triList.Add(i0);
                            triList.Add(i1);
                            triList.Add(i2);
                        }
                    }
                }
            }
            catch
            {
                // Static-object loading is a best-effort enhancement; terrain alone is still usable.
            }

            return new TerrainGeometryProvider(vertList.ToArray(), triList.ToArray());
        }

        private TerrainGeometryProvider(float[] vertices, int[] faces)
        {
            this.vertices = vertices;
            this.faces = faces;
            this.normals = new float[faces.Length];
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

        public RcTriMesh GetMesh() => _mesh;
        public RcVec3f GetMeshBoundsMin() => bmin;
        public RcVec3f GetMeshBoundsMax() => bmax;
        public IList<RcConvexVolume> ConvexVolumes() => volumes;

        public void AddConvexVolume(float[] verts, float minh, float maxh, RcAreaModification areaMod)
        {
            var vol = new RcConvexVolume
            {
                hmin = minh,
                hmax = maxh,
                verts = verts,
                areaMod = areaMod
            };
            volumes.Add(vol);
        }

        public void AddConvexVolume(RcConvexVolume convexVolume) => volumes.Add(convexVolume);

        public IEnumerable<RcTriMesh> Meshes() => RcImmutableArray.Create(_mesh);

        public List<RcOffMeshConnection> GetOffMeshConnections() => new List<RcOffMeshConnection>();

        public void AddOffMeshConnection(RcVec3f start, RcVec3f end, float radius, bool bidir, int area, int flags) { }

        public void RemoveOffMeshConnections(Predicate<RcOffMeshConnection> filter) { }

        private void CalculateNormals()
        {
            for (int i = 0; i < faces.Length; i += 3)
            {
                int v0 = faces[i] * 3;
                int v1 = faces[i + 1] * 3;
                int v2 = faces[i + 2] * 3;

                var e0 = new RcVec3f
                {
                    X = vertices[v1 + 0] - vertices[v0 + 0],
                    Y = vertices[v1 + 1] - vertices[v0 + 1],
                    Z = vertices[v1 + 2] - vertices[v0 + 2]
                };
                var e1 = new RcVec3f
                {
                    X = vertices[v2 + 0] - vertices[v0 + 0],
                    Y = vertices[v2 + 1] - vertices[v0 + 1],
                    Z = vertices[v2 + 2] - vertices[v0 + 2]
                };

                normals[i + 0] = e0.Y * e1.Z - e0.Z * e1.Y;
                normals[i + 1] = e0.Z * e1.X - e0.X * e1.Z;
                normals[i + 2] = e0.X * e1.Y - e0.Y * e1.X;

                float d = (float)Math.Sqrt(normals[i + 0] * normals[i + 0]
                                         + normals[i + 1] * normals[i + 1]
                                         + normals[i + 2] * normals[i + 2]);
                if (d > 0)
                {
                    d = 1.0f / d;
                    normals[i + 0] *= d;
                    normals[i + 1] *= d;
                    normals[i + 2] *= d;
                }
            }
        }
    }
}

using System.Numerics;
using System.Text.Json;
using BIS.Core;
using BIS.Core.Math;
using BIS.P3D.ODOL;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Ops;

public sealed class AddLabelQuadsOp : OdolOp
{
    private static readonly Vector2[] CornerUv = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    protected override bool RewritesFaces => true;

    protected override void Edit(ODOL odol, JsonElement op, OpContext context)
    {
        var labels = Labels(op, context);
        var texture = OpJson.String(op, "texture");
        foreach (var name in OpJson.Strings(op, "lods"))
        {
            var lod = FindLod(odol, name, context);
            var template = OpJson.Int(op, "templateSection", -1);
            if (template < 0 || template >= lod.Sections.Length)
            {
                throw new PatchException($"{context.AssetPath}: LOD {name} has no template section {template}");
            }

            foreach (var label in labels)
            {
                if (lod.NamedSelections.Any(s => s.Name.Equals(label.Selection, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new PatchException($"{context.AssetPath}: LOD {name} already has a selection '{label.Selection}'");
                }
            }

            var anchorSection = OpJson.Int(op, "anchorSection", template);
            if (anchorSection < 0 || anchorSection >= lod.Sections.Length)
            {
                throw new PatchException($"{context.AssetPath}: LOD {name} has no anchor section {anchorSection}");
            }

            var convention = Convention(lod, template);
            var special = op.TryGetProperty("special", out var s) ? s.GetInt32() : (int)lod.Sections[template].Special;
            foreach (var label in labels)
            {
                AddLabel(lod, label, template, anchorSection, texture, special, convention, context);
            }

            OdolBounds.Grow(odol, lod);
            OdolLodInfo.Sync(lod);
        }
    }

    private sealed record Quad(Vector3[] Corners, Vector3 Normal);

    private sealed record Label(string Selection, Quad[] Quads);

    private static List<Label> Labels(JsonElement op, OpContext context)
    {
        if (!op.TryGetProperty("labels", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
        {
            throw new PatchException($"{context.AssetPath}: add-label-quads needs a non-empty \"labels\" array");
        }

        return array.EnumerateArray().Select(l =>
        {
            var quads = l.GetProperty("quads").EnumerateArray().Select(q =>
            {
                var corners = q.GetProperty("corners").EnumerateArray().Select(Point).ToArray();
                if (corners.Length != 4 || corners.Any(c => !float.IsFinite(c.X + c.Y + c.Z)))
                {
                    throw new PatchException($"{context.AssetPath}: a label quad needs 4 corners (top-left, top-right, bottom-right, bottom-left as seen from outside)");
                }

                var n = OpJson.Floats(q, "normal", 3);
                var normal = Vector3.Normalize(new Vector3(n[0], n[1], n[2]));
                var facing = Vector3.Dot(Vector3.Cross(corners[1] - corners[0], corners[3] - corners[0]), normal);
                if (Math.Abs(facing) < 1e-6f)
                {
                    throw new PatchException($"{context.AssetPath}: label quad corners are degenerate or parallel to its normal");
                }

                return new Quad(corners, normal);
            }).ToArray();
            return new Label(OpJson.String(l, "selection"), quads);
        }).ToList();
    }

    private static Vector3 Point(JsonElement c)
    {
        var f = c.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        return f.Length == 3 ? new Vector3(f[0], f[1], f[2]) : new Vector3(float.NaN);
    }

    private sealed record Conventions(bool CrossRightDownIsOut, bool StoredNormalsPointOut);

    private static Conventions Convention(LOD lod, int template)
    {
        var centre = (lod.BMin.Vector3 + lod.BMax.Vector3) / 2;
        int winding = 0, stored = 0;
        foreach (var f in OdolGraph.FacesInSection(lod, lod.Sections[template]))
        {
            var v = lod.Polygons.Faces[f].VertexIndices;
            var p = v.Select(i => lod.Vertices[i].Vector3).ToArray();
            var outward = p.Aggregate(Vector3.Zero, (a, b) => a + b) / p.Length - centre;
            var geometric = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            var n = lod.NormalsCompressed[v[0]];
            winding += Math.Sign(Vector3.Dot(geometric, outward));
            stored += Math.Sign(Vector3.Dot(new Vector3(n.X, n.Y, n.Z), outward));
        }

        return new Conventions(winding > 0, stored > 0);
    }

    private static void AddLabel(LOD lod, Label label, int template, int anchorSection, string texture, int special, Conventions convention, OpContext context)
    {
        var n0 = lod.Vertices.Count;
        var positions = lod.Vertices.ToList();
        var normals = lod.NormalsCompressed.ToList();
        var st = lod.STCoordsCompressed.ToList();
        var hasTangents = st.Count == n0;
        var clip = lod.Clip.ToList();
        var bones = OdolGraph.BoneRefs(lod);
        var hasBones = bones.Count == n0;
        var uvs = lod.UvSets.Select(s => s.GetUV().ToList()).ToArray();
        var anchors = OdolGraph.FacesInSection(lod, lod.Sections[anchorSection]).SelectMany(f => lod.Polygons.Faces[f].VertexIndices).Distinct().ToList();
        var newFaces = new List<Polygon>();
        foreach (var quad in label.Quads)
        {
            var stored = convention.StoredNormalsPointOut ? quad.Normal : -quad.Normal;
            var right = Vector3.Normalize(quad.Corners[1] - quad.Corners[0]);
            var down = Vector3.Normalize(quad.Corners[3] - quad.Corners[0]);
            var first = positions.Count;
            for (var k = 0; k < 4; k++)
            {
                var anchor = anchors.MinBy(a => Vector3.Distance(lod.Vertices[a].Vector3, quad.Corners[k]));
                positions.Add(new Vector3P(quad.Corners[k]));
                normals.Add(UvData.Pack([stored.X, stored.Y, stored.Z], 0));
                if (hasTangents)
                {
                    st.Add(Tuple.Create(UvData.Pack([right.X, right.Y, right.Z], 0), UvData.Pack([down.X, down.Y, down.Z], 0)));
                }

                clip.Add(lod.Clip[anchor]);
                if (hasBones)
                {
                    bones.Add(bones[anchor]);
                }

                foreach (var set in uvs)
                {
                    set.Add(CornerUv[k]);
                }
            }

            var outFacing = Vector3.Dot(Vector3.Cross(quad.Corners[1] - quad.Corners[0], quad.Corners[3] - quad.Corners[0]), quad.Normal) > 0;
            int[] order = outFacing == convention.CrossRightDownIsOut ? [0, 1, 2, 3] : [0, 3, 2, 1];
            newFaces.Add(OdolFaces.NewFace(order.Select(i => first + i).ToArray()));
        }

        LibraryAccess.Set(lod, nameof(LOD.Vertices), new TrackedArray<Vector3P>(positions));
        LibraryAccess.Set(lod, nameof(LOD.NormalsCompressed), new TrackedArray<Vector3PCompressed>(normals));
        LibraryAccess.Set(lod, nameof(LOD.STCoordsCompressed), new TrackedArray<Tuple<Vector3PCompressed, Vector3PCompressed>>(st));
        LibraryAccess.Set(lod, nameof(LOD.Clip), new TrackedArray<int>(clip));
        if (hasBones)
        {
            OdolGraph.SetBoneRefs(lod, bones);
        }

        for (var set = 0; set < uvs.Length; set++)
        {
            if (!lod.UvSets[set].DefaultFill)
            {
                UvData.Write(lod.UvSets[set], uvs[set].ToArray(), context);
            }

            LibraryAccess.Set(lod.UvSets[set], nameof(UVSet.NVertices), (uint)positions.Count);
        }

        LibraryAccess.Set(lod, nameof(LOD.VertexCount), (uint)positions.Count);
        AppendSection(lod, label.Selection, newFaces, template, texture, special, context);
    }

    private static void AppendSection(LOD lod, string selection, List<Polygon> newFaces, int template, string texture, int special, OpContext context)
    {
        var ranges = OdolFaces.SectionFaceRanges(lod, context.AssetPath);
        var end = lod.Polygons.Faces.Length;
        var textureIndex = Array.FindIndex(lod.Textures, t => t.Equals(texture, StringComparison.OrdinalIgnoreCase));
        if (textureIndex < 0)
        {
            textureIndex = lod.Textures.Length;
            LibraryAccess.Set(lod, nameof(LOD.Textures), lod.Textures.Append(texture).ToArray());
        }

        var section = (Section)OdolGraph.Clone(lod.Sections[template]);
        LibraryAccess.Set(section, nameof(Section.TextureIndex), (short)textureIndex);
        LibraryAccess.Set(section, nameof(Section.Special), (uint)special);
        LibraryAccess.Set(lod, nameof(LOD.Sections), lod.Sections.Append(section).ToArray());
        var sectionIndex = lod.Sections.Length - 1;
        lod.NamedSelections = lod.NamedSelections.Append(new NamedSelection(selection, true, [sectionIndex])).ToArray();
        OdolFaces.Replace(lod, lod.Polygons.Faces.Concat(newFaces).ToArray(), ranges.Append((end, end + newFaces.Count)).ToArray());
    }
}

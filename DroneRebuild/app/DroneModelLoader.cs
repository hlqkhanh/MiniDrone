using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using SharpGLTF.Schema2;

namespace DroneRebuild;

/// <summary>Loads the static, textured glTF scene into WPF's Y-up model space.</summary>
public static class DroneModelLoader
{
    public static Model3DGroup Load(string path)
    {
        var source = ModelRoot.Load(path);
        var result = new Model3DGroup();
        var scene = source.DefaultScene ?? throw new InvalidDataException("The drone model has no scene.");
        foreach (var node in scene.VisualChildren)
            result.Children.Add(LoadNode(node));

        var bounds = result.Bounds;
        var longestSide = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        if (bounds.IsEmpty || !double.IsFinite(longestSide) || longestSide <= 0)
            throw new InvalidDataException("The drone model contains no visible geometry.");

        // Center the drone at its rotation pivot and fit the existing viewport.
        var normalization = new Transform3DGroup();
        normalization.Children.Add(new TranslateTransform3D(
            -bounds.X - bounds.SizeX / 2, -bounds.Y - bounds.SizeY / 2, -bounds.Z - bounds.SizeZ / 2));
        var scale = 6.0 / longestSide;
        normalization.Children.Add(new ScaleTransform3D(scale, scale, scale));
        result.Transform = normalization;
        result.Freeze();
        return result;
    }

    private static Model3DGroup LoadNode(Node node)
    {
        var m = node.LocalMatrix;
        var group = new Model3DGroup
        {
            Transform = new MatrixTransform3D(new Matrix3D(
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44))
        };
        if (node.Mesh is { } mesh)
        {
            foreach (var primitive in mesh.Primitives)
            {
                if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES)
                    throw new InvalidDataException("The drone model must use triangle meshes.");
                var geometry = new MeshGeometry3D
                {
                    Positions = new Point3DCollection(primitive.GetVertexAccessor("POSITION").AsVector3Array()
                        .Select(v => new Point3D(v.X, v.Y, v.Z))),
                    TriangleIndices = new Int32Collection(primitive.GetIndices().Select(i => checked((int)i)))
                };
                if (primitive.GetVertexAccessor("NORMAL") is { } normals)
                    geometry.Normals = new Vector3DCollection(normals.AsVector3Array()
                        .Select(v => new Vector3D(v.X, v.Y, v.Z)));
                var channel = primitive.Material?.FindChannel("BaseColor");
                var textureCoordinate = channel?.TextureCoordinate ?? 0;
                if (primitive.GetVertexAccessor($"TEXCOORD_{textureCoordinate}") is { } uv)
                    geometry.TextureCoordinates = new PointCollection(uv.AsVector2Array()
                        .Select(v => new Point(v.X, v.Y)));

                Brush brush = Brushes.LightGray;
                if (channel?.Texture?.PrimaryImage is { } image)
                {
                    using var stream = new MemoryStream(image.Content.Content.ToArray());
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 2048;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    brush = new ImageBrush(bitmap) { ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile };
                }
                else if (channel is { } baseColor)
                {
                    var color = baseColor.Color;
                    brush = new SolidColorBrush(Color.FromScRgb(color.W, color.X, color.Y, color.Z));
                }

                // Retain the photographed base color and add a restrained highlight.
                // WPF has no glTF PBR renderer; broad, low-energy specular light
                // reveals the curved motors/propellers without bleaching textures.
                var material = new MaterialGroup();
                material.Children.Add(new DiffuseMaterial(brush));
                material.Children.Add(new SpecularMaterial(
                    new SolidColorBrush(Color.FromRgb(48, 48, 48)), 28));
                group.Children.Add(new GeometryModel3D(geometry, material)
                {
                    BackMaterial = primitive.Material?.DoubleSided == true ? material : null
                });
            }
        }
        foreach (var child in node.VisualChildren)
            group.Children.Add(LoadNode(child));
        return group;
    }
}

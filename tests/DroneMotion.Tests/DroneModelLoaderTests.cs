using System.Windows.Media;
using System.Windows.Media.Media3D;
using DroneMotion.Wpf.Services;

namespace DroneMotion.Tests;

public sealed class DroneModelLoaderTests
{
    [Fact]
    public void DroneAssetLoadsWithTextureAndCenteredRotationPivot()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = DroneModelLoader.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "drone.glb"));
                Assert.True(model.IsFrozen);
                var bounds = model.Bounds;
                Assert.InRange(Math.Abs(bounds.X + bounds.SizeX / 2), 0, 0.00001);
                Assert.InRange(Math.Abs(bounds.Y + bounds.SizeY / 2), 0, 0.00001);
                Assert.InRange(Math.Abs(bounds.Z + bounds.SizeZ / 2), 0, 0.00001);
                Assert.InRange(Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)), 5.999, 6.001);
                var mesh = Assert.Single(Geometry(model));
                var geometry = Assert.IsType<MeshGeometry3D>(mesh.Geometry);
                Assert.InRange(geometry.TriangleIndices.Count / 3, 1, 150_000);
                Assert.Equal(geometry.Positions.Count, geometry.Normals.Count);
                Assert.Equal(geometry.Positions.Count, geometry.TextureCoordinates.Count);
                Assert.All(geometry.TriangleIndices, index => Assert.InRange(index, 0, geometry.Positions.Count - 1));
                var material = Assert.IsType<DiffuseMaterial>(mesh.Material);
                var texture = Assert.IsType<ImageBrush>(material.Brush);
                Assert.NotNull(texture.ImageSource);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Drone model load timed out.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static IEnumerable<GeometryModel3D> Geometry(Model3D model) => model switch
    {
        GeometryModel3D geometry => [geometry],
        Model3DGroup group => group.Children.SelectMany(Geometry),
        _ => []
    };
}

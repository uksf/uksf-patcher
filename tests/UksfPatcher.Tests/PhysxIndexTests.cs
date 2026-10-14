using UksfPatcher.Ops;
using UksfPatcher.P3d;
using UksfPatcher.Patching;

namespace UksfPatcher.Tests;

public sealed class PhysxIndexTests
{
    [Fact]
    public void VanillaJetStoresItsPhysxLodInTheFieldAddPhysxWrites()
    {
        var odol = OdolFile.Parse(Models.Load("air_f_jets.pbo", @"Plane_Fighter_01\Plane_Fighter_01_F.p3d"), "fa181");

        var index = LibraryAccess.Get<byte>(odol.ModelInfo, AddPhysxOp.PhysxIndexField);

        Assert.Equal("geometryPhys", LodNames.Name(odol.Lods[index].Resolution));
    }
}

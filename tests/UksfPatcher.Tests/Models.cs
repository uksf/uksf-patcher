using System.Collections.Concurrent;
using UksfPatcher.Pbo;

namespace UksfPatcher.Tests;

public static class Models
{
    public const string RhiccPbo = "RHICC.pbo";
    public const string Rhicc = @"rhicc\addons\rhicc.p3d";
    public const string PumaPbo = "rksla3-pumahc.pbo";
    public const string PumaTroop = @"rksla3\rksla3-pumahc\rksla3_puma_hc1_troop.p3d";
    public const string RncPbo = "rnc_apartments.pbo";
    public const string RncApartment = @"variant1\15x15l\rnc_apt_v1_15x15l_c1_f3.p3d";
    public const string L110Pbo = "uk3cb_baf_weapons_L110.pbo";
    public const string L110A2 = @"UK3CB_BAF_Weapons\addons\UK3CB_BAF_Weapons_L110\UK3CB_L110a2.p3d";
    public const string MissilesPbo = "Ballistic_missiles.pbo";
    public const string EwdjPbo = "ew_modular.pbo";
    public const string EwdjGenerator = @"z\ewdj\addons\ew_modular\data\ewdj_generator.p3d";

    public const string M119Pbo = "tbd_m119_m119.pbo";
    public const string M119 = @"tbd_m119\TBD_M119.p3d";

    public const string EawsPbo = "EAWS_EF2000.pbo";
    public const string EawsFuselage = "EAWS_EF2000.p3d";

    private static readonly ConcurrentDictionary<(string, string), byte[]> Cache = new();

    public static byte[] Load(string pbo, string asset) =>
        (byte[])Cache.GetOrAdd((pbo, asset), key =>
        {
            var file = RawPbo.Read(TestData.Specimen(key.Item1));
            var entry = file.Find(key.Item2) ?? throw new FileNotFoundException(key.Item2);
            return file.ReadData(entry);
        }).Clone();
}

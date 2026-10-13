namespace PoolCar.Reservations.Data;

/// <summary>Plates of the cars that receive seeded reservations.</summary>
public static class SeedCars
{
    public const string TomorrowMorning = "KSP73B";

    public const string TomorrowAndDayAfter = "V94FFF";

    public const string PickedUpToday = "V62PLT";

    public const string NotReturned = "GNK21N";

    /// <summary>Plates of the cars that the seeder never books.</summary>
    public static readonly IReadOnlyList<string> NeverBooked = ["HPN72V", "GNB11N", "V15PGL"];

    /// <summary>The cars with random reservations.</summary>
    public static readonly IReadOnlyList<string> Background =
    [
        // Utrecht
        "87BZV2",
        "GGB17S",
        "GJS21Z",
        "GKR56B",
        "GLN66F",
        "GRN31T",
        "GZV09V",
        "V02DNJ",
        "V35PTP",
        "V41PVJ",

        // Amsterdam
        "GBX45H",
        "GKR66B",
        "GKS66V",
        "GLB54F",
        "GSX93K",
        "GTD41X",
        "GZS34T",
        "V04NPG",
        "V10PJP",
        "V17FPP",

        // Eindhoven
        "GHX15J",
        "GLG79S",
        "GTB87R",
        "GVV82N",
        "HPJ83J",
        "HPR99Z",
        "JFP99J",
        "V18FTF",
        "V31DTV",
        "V38RDZ",
    ];
}

using System.Runtime.Serialization;
#if COI_BRIDGE
namespace CoIBridge
#else
namespace CoIStateReporter
#endif
{
    [DataContract]
    public sealed class TilePosition
    {
        [DataMember(Name = "x", Order = 0)] public int X;
        [DataMember(Name = "y", Order = 1)] public int Y;
        [DataMember(Name = "z", Order = 2)] public int Z;
    }
}

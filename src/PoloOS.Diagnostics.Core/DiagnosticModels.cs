namespace PoloOS.Diagnostics.Core;
public enum VagProtocol { Unknown, Kwp1281, Kwp2000 }
public sealed record DtcEntry(int Code, byte Status) { public string Description => VagDtcCatalog.GetDescription(Code); public string Category => VagDtcCatalog.GetCategory(Code); public override string ToString() => $"{Code:D5} • {Description} • status={Status:X2}"; }
public sealed record ModuleSpec(byte Address,string Name,int Baud,bool PreferEvenParity=false);
public sealed record ModuleScanResult(ModuleSpec Module,bool Connected,VagProtocol Protocol,int KeywordVersion,string Identification,IReadOnlyList<DtcEntry> Dtcs,string Status,string TimingProfile);
public sealed record WakeResult(int KeywordVersion,byte KeywordLsb,byte KeywordMsb,bool EvenParity,int BitMilliseconds);

namespace PoloOS.PC.Models;

public sealed record VehicleState(
    DateTime Timestamp,
    double Rpm,
    double SpeedKmh,
    double BoostBar,
    double PedalPercent,
    double CoolantC,
    double Voltage,
    double MafGps,
    double FuelTempC,
    double IntakeTempC,
    double InjectionMgStroke)
{
    public bool BrakePressed { get; init; }
    public bool BrakeValid { get; init; }
    public bool ClutchPressed { get; init; }
    public bool ClutchValid { get; init; }
    public bool AcRequested { get; init; }
    public bool AcRequestedValid { get; init; }
    public byte? PedalSwitchMask { get; init; }
    public bool RpmValid { get; init; } = true;
    public bool SpeedValid { get; init; } = true;
    public bool BoostValid { get; init; } = true;
    public bool PedalValid { get; init; } = true;
    public bool CoolantValid { get; init; } = true;
    public bool VoltageValid { get; init; } = true;
    public bool MafValid { get; init; } = true;
    public bool FuelTempValid { get; init; } = true;
    public bool IntakeTempValid { get; init; } = true;
    public bool InjectionValid { get; init; } = true;
}

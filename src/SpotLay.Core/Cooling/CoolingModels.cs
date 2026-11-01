using SpotLay.Core.Localization;

namespace SpotLay.Core.Cooling;

public sealed record CoolingChannelInfo(
    string Id,
    string Hardware,
    string Name,
    string DefaultName,
    string Category,
    float HardwareMinimum,
    float HardwareMaximum,
    float? CurrentDuty,
    float? CurrentRpm,
    bool HasRpm,
    bool Controlled,
    double? Temperature,
    string Status);

public sealed record CoolingCalibrationInfo(
    bool Running,
    string ChannelId,
    int CurrentDuty,
    int? RecommendedMinimumDuty,
    int? MaximumRpm,
    string Message)
{
    public static CoolingCalibrationInfo Empty { get; } = new(false, "", 0, null, null, Tr.T("Sin calibración activa"));
}

public sealed record CoolingTestInfo(
    bool Running,
    string ChannelId,
    int Duty,
    int RemainingSeconds,
    float? BaselineRpm,
    float? CurrentRpm,
    float? PeakRpm,
    string Message)
{
    public static CoolingTestInfo Empty { get; } = new(false, "", 0, 0, null, null, null,
        Tr.T("Sin prueba de identificación activa"));
}

public sealed record CoolingSnapshot(
    bool Enabled,
    bool Conflict,
    string Message,
    IReadOnlyList<CoolingChannelInfo> Channels,
    CoolingCalibrationInfo Calibration,
    CoolingTestInfo Test)
{
    public static CoolingSnapshot Empty { get; } = new(false, false, Tr.T("Buscando controles de ventilador…"), [],
        CoolingCalibrationInfo.Empty, CoolingTestInfo.Empty);
}

public sealed record CoolingActionResult(bool Applied, string Message);

using System.Globalization;
using SpotLay.Core.Metrics;
using SpotLay.Core.Localization;

namespace SpotLay.App.Overlay;

public static class MetricFormat
{
    private static readonly CultureInfo Spanish = Tr.Culture;

    public static string Value(MetricValue? metric)
    {
        if (metric is null) return "—";
        if (metric.Text is not null) return metric.Text;
        if (metric.Value is not { } v || !double.IsFinite(v)) return "—";
        return v.ToString(Decimals(metric.Id, metric.Unit), Spanish);
    }

    public static string Decimals(string id, string unit) =>
        id is KnownMetrics.Fps or KnownMetrics.FpsReal or KnownMetrics.FpsGenerated or KnownMetrics.Low1 or KnownMetrics.Low01
            or KnownMetrics.Low1Real or KnownMetrics.Low01Real || unit is "MHz" or "RPM" ? "F0" : "F1";

    public static string Number(double value, string format = "F0") => value.ToString(format, Spanish);
}

using System.Windows.Media;
using PrintLink.Core.Enums;
using PrintLink.Core.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace PrintLink.Desktop.ViewModels;

public sealed class PrintJobItemViewModel : ViewModelBase
{
    public Guid JobId { get; }
    public string PrinterName { get; }
    public string ClientId { get; }
    public string DocumentName { get; }
    public long TotalBytes { get; }
    public JobState State { get; }
    public int? SpoolerJobId { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public DateTimeOffset CreatedAt { get; }

    public string StateName => State.ToString();

    public string FullJobId => JobId.ToString();

    public string FormattedSize
    {
        get
        {
            if (TotalBytes < 1024) return $"{TotalBytes} B";
            if (TotalBytes < 1024 * 1024) return $"{TotalBytes / 1024.0:F1} KB";
            return $"{TotalBytes / (1024.0 * 1024.0):F2} MB";
        }
    }

    public string FormattedTime => CreatedAt.ToLocalTime().ToString("HH:mm:ss");
    public string FullFormattedTime => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public MediaBrush StateBrush => State switch
    {
        JobState.Completed => new MediaSolidColorBrush(MediaColor.FromRgb(16, 185, 129)), // Green
        JobState.Spooling => new MediaSolidColorBrush(MediaColor.FromRgb(59, 130, 246)),  // Blue
        JobState.Accepted or JobState.Sending or JobState.Queued => new MediaSolidColorBrush(MediaColor.FromRgb(245, 158, 11)), // Amber
        JobState.Failed => new MediaSolidColorBrush(MediaColor.FromRgb(239, 68, 68)),    // Red
        JobState.Cancelled => new MediaSolidColorBrush(MediaColor.FromRgb(107, 114, 128)), // Gray
        _ => new MediaSolidColorBrush(MediaColor.FromRgb(139, 92, 246))                  // Purple (Unknown)
    };

    public MediaBrush StateBadgeBackgroundBrush => State switch
    {
        JobState.Completed => new MediaSolidColorBrush(MediaColor.FromRgb(220, 252, 231)), // #DCFCE7 Light Green
        JobState.Spooling => new MediaSolidColorBrush(MediaColor.FromRgb(219, 234, 254)),  // #DBEAFE Light Blue
        JobState.Accepted or JobState.Sending or JobState.Queued => new MediaSolidColorBrush(MediaColor.FromRgb(254, 243, 199)), // #FEF3C7 Light Amber
        JobState.Failed => new MediaSolidColorBrush(MediaColor.FromRgb(254, 226, 226)),    // #FEE2E2 Light Red
        JobState.Cancelled => new MediaSolidColorBrush(MediaColor.FromRgb(241, 245, 249)), // #F1F5F9 Light Slate
        _ => new MediaSolidColorBrush(MediaColor.FromRgb(243, 232, 255))                  // #F3E8FF Light Purple
    };

    public MediaBrush StateBadgeForegroundBrush => State switch
    {
        JobState.Completed => new MediaSolidColorBrush(MediaColor.FromRgb(21, 128, 61)),  // #15803D Dark Green
        JobState.Spooling => new MediaSolidColorBrush(MediaColor.FromRgb(29, 78, 216)),   // #1D4ED8 Dark Blue
        JobState.Accepted or JobState.Sending or JobState.Queued => new MediaSolidColorBrush(MediaColor.FromRgb(180, 83, 9)),  // #B45309 Dark Amber
        JobState.Failed => new MediaSolidColorBrush(MediaColor.FromRgb(185, 28, 28)),    // #B91C1C Dark Red
        JobState.Cancelled => new MediaSolidColorBrush(MediaColor.FromRgb(71, 85, 105)),  // #475569 Slate
        _ => new MediaSolidColorBrush(MediaColor.FromRgb(107, 33, 168))                  // #6B21A8 Dark Purple
    };

    public PrintJobItemViewModel(PrintJob job)
    {
        JobId = job.JobId;
        PrinterName = job.PrinterName;
        ClientId = job.ClientId;
        DocumentName = job.DocumentName;
        TotalBytes = job.TotalBytes;
        State = job.State;
        SpoolerJobId = job.SpoolerJobId;
        ErrorCode = job.ErrorCode;
        ErrorMessage = job.ErrorMessage;
        CreatedAt = job.CreatedAt;
    }
}

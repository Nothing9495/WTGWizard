using System.Collections.Generic;

namespace WTGWizard.Shared.Services.WimService;

/// <summary>
/// WIM 映像元数据快照。
/// </summary>
public sealed record ImageInfo(
    int Index,
    string Name,
    string Description,
    string DisplayDescription,
    int MajorVersion,
    string FeatureVersion,
    string Sku,
    string Architecture,
    string BuildNumber,
    double ExpandedSizeGB,
    string DateCreated,
    IReadOnlyList<string> AnsFilePaths
);

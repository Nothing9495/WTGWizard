using static WTGWizard.Models.Constants;

namespace WTGWizard.Helpers;

/// <summary>
/// Windows 构建号判断工具方法。
/// </summary>
internal static class WindowsBuildHelper
{
    /// <summary>
    /// 判断单个构建号是否满足 BootEx 最低要求。
    /// 条件：Build > 26200，或 Build = 26100/26200 且 Revision ≥ 8037。
    /// </summary>
    public static bool MeetsBootExThreshold(string? buildNumber)
    {
        var build = TryGetBuildRevision(buildNumber);
        if (build is null) return false;

        return build.Value.major > BuildMajor26200 ||
            (build.Value.major == BuildMajor26100 && build.Value.revision >= BuildRevisionThreshold) ||
            (build.Value.major == BuildMajor26200 && build.Value.revision >= BuildRevisionThreshold);
    }

    /// <summary>
    /// 从构建号字符串提取 (major, revision)。
    /// 支持 3 段（"10.0.28000"）和 4 段（"10.0.28000.1836"）格式。
    /// 3 段格式无 revision 时默认为 0。
    /// </summary>
    public static (int major, int revision)? TryGetBuildRevision(string? buildStr)
    {
        if (string.IsNullOrEmpty(buildStr)) return null;
        var parts = buildStr.Split('.');

        if (parts.Length >= 3 && int.TryParse(parts[2], out var major))
        {
            var revision = parts.Length >= 4 && int.TryParse(parts[3], out var r) ? r : 0;
            return (major, revision);
        }

        return null;
    }
}

namespace WTGWizard.Main.DeploymentCore.Models;

public readonly record struct DeployTaskId
{
    public string Value { get; }

    private DeployTaskId(string value) => Value = value;

    public static readonly DeployTaskId CreateDiskLayout   = new("create-disk-layout");
    public static readonly DeployTaskId ExtractImage       = new("extract-image");
    public static readonly DeployTaskId IntegrateDrivers   = new("integrate-drivers");
    public static readonly DeployTaskId ImportAnswerFile   = new("import-answer-file");
    public static readonly DeployTaskId ApplySysSettings   = new("apply-sys-settings");
    public static readonly DeployTaskId CreateBootFiles    = new("create-boot-files");
    public static readonly DeployTaskId RemoveDriveLetters = new("remove-drive-letters");

    public override string ToString() => Value;
    public static implicit operator string(DeployTaskId id) => id.Value;
}

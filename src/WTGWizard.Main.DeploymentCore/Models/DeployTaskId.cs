namespace WTGWizard.Main.DeploymentCore.Models;

public readonly record struct DeployTaskId
{
    public string Value { get; }

    private DeployTaskId(string value) => Value = value;

    public static readonly DeployTaskId Partition    = new("partition");
    public static readonly DeployTaskId Extract      = new("extract");
    public static readonly DeployTaskId Drivers      = new("drivers");
    public static readonly DeployTaskId ImportAns    = new("import-ansfile");
    public static readonly DeployTaskId ApplyWtg     = new("apply-settings");
    public static readonly DeployTaskId CreateBoot   = new("create-boot");
    public static readonly DeployTaskId RemoveLetter = new("remove-letter");

    public override string ToString() => Value;
    public static implicit operator string(DeployTaskId id) => id.Value;
}

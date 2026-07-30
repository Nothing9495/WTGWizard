using System.Collections.Generic;
using System.Xml.Linq;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Builders;

/// <summary>
/// 应答文件配置 provider 接口。
/// </summary>
public interface IAnswerFileProvider
{
    string Name { get; }
    bool ShouldGenerate(DeploymentConfig config);
    IEnumerable<XElement> GenerateElements();
}

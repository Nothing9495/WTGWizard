using System.Collections.Generic;
using System.Xml.Linq;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Contracts;

public interface IAnswerFileProvider
{
    string Name { get; }
    bool ShouldGenerate(DeploymentConfig config);
    IEnumerable<XElement> GenerateElements();
}

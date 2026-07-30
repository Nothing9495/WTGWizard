using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using WTGWizard.Main.DeploymentCore.Models;

namespace WTGWizard.Main.DeploymentCore.Builders;

/// <summary>
/// 应答文件生成器 — 根据配置开关合并多个 provider 生成单个 XML 应答文件。
/// </summary>
public static class AnswerFileGenerator
{
    private static readonly XNamespace Ns = "urn:schemas-microsoft-com:unattend";

    private static readonly IAnswerFileProvider[] Providers =
    [
        new SanPolicyProvider(),
        new PreventDeviceEncryptionProvider(),
    ];

    public static string? GenerateAndSave(DeploymentConfig config)
    {
        var settings = new XElement(Ns + "settings",
            new XAttribute("pass", "offlineServicing"));

        var hasContent = false;

        foreach (var provider in Providers)
        {
            if (provider.ShouldGenerate(config))
            {
                foreach (var element in provider.GenerateElements())
                    settings.Add(element);
                hasContent = true;
            }
        }

        if (!hasContent)
            return null;

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(Ns + "unattend", settings));

        var timestamp = DateTime.Now.ToString("yyMMddHHmmss");
        var fileName = $"WinSettings-{timestamp}.xml";

        string dir = Path.Combine(Path.GetTempPath(), "WTGWizard", "WorkerCache", "AnswerFiles");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        doc.Save(path);

        return path;
    }

    private static readonly string[] Architectures = ["x86", "amd64", "arm64"];

    private sealed class SanPolicyProvider : IAnswerFileProvider
    {
        private static readonly XNamespace WcmNs = "http://schemas.microsoft.com/WMIConfig/2002/State";
        private static readonly XNamespace XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

        public string Name => "SanPolicy";

        public bool ShouldGenerate(DeploymentConfig config)
            => config.HideLocalDisks;

        public IEnumerable<XElement> GenerateElements()
        {
            foreach (var arch in Architectures)
            {
                yield return new XElement(Ns + "component",
                    new XAttribute(XNamespace.Xmlns + "wcm", WcmNs),
                    new XAttribute(XNamespace.Xmlns + "xsi", XsiNs),
                    new XAttribute("language", "neutral"),
                    new XAttribute("name", "Microsoft-Windows-PartitionManager"),
                    new XAttribute("processorArchitecture", arch),
                    new XAttribute("publicKeyToken", "31bf3856ad364e35"),
                    new XAttribute("versionScope", "nonSxS"),
                    new XElement(Ns + "SanPolicy", 4));
            }
        }
    }

    private sealed class PreventDeviceEncryptionProvider : IAnswerFileProvider
    {
        private static readonly XNamespace WcmNs = "http://schemas.microsoft.com/WMIConfig/2002/State";
        private static readonly XNamespace XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

        public string Name => "PreventDeviceEncryption";

        public bool ShouldGenerate(DeploymentConfig config)
            => config.PreventDeviceEncryption;

        public IEnumerable<XElement> GenerateElements()
        {
            foreach (var arch in Architectures)
            {
                yield return new XElement(Ns + "component",
                    new XAttribute(XNamespace.Xmlns + "wcm", WcmNs),
                    new XAttribute(XNamespace.Xmlns + "xsi", XsiNs),
                    new XAttribute("language", "neutral"),
                    new XAttribute("name", "microsoft-windows-securestartup-filterdriver-"),
                    new XAttribute("processorArchitecture", arch),
                    new XAttribute("publicKeyToken", "31bf3856ad364e35"),
                    new XAttribute("versionScope", "nonSxS"),
                    new XElement(Ns + "PreventDeviceEncryption", true));
            }
        }
    }
}

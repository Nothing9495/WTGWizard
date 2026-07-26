# WTGWizard

基于 C# + .NET 10 + Windows App SDK 2.3 开发的图形化工具。

## 项目结构

```
WTGWizard.AOT/
├── WTGWizard.slnx              # 解决方案文件
├── global.json                 # .NET SDK版本配置
├── .editorconfig               # 代码风格规范
├── .gitignore                  # Git忽略规则
├── docs/                       # 文档目录
└── src/                        # 源代码目录
    ├── WTGWizard.Main/         # 主WinUI 3应用
    └── WTGWizard.Worker/       # 业务逻辑类库
```

## 技术栈

- **框架**: .NET 10 + Windows App SDK 2.3
- **UI框架**: WinUI 3 (XAML)
- **架构模式**: MVVM
- **平台**: x64

## 开发环境

- Visual Studio 2022
- .NET 10 SDK
- Windows App SDK

## 构建

```powershell
dotnet build WTGWizard.slnx
```

## 发布

```powershell
dotnet publish src/WTGWizard.Main -c Release -r win-x64
```

﻿using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using System.Reflection;
using BenchmarkDotNet.Extensions;
using BenchmarkDotNet.Tasks;
using Microsoft.CSharp;

namespace BenchmarkDotNet
{
    internal class BenchmarkProjectGenerator
    {
        private const string MainNamespaceName = "BenchmarkDotNet.Autogenerated";
        private const string MainClassName = "Program";
        private const string RunMethodName = "Run";
        private const string ClassInstanceName = "instance";

        private readonly CodeDomProvider provider = new CSharpCodeProvider();

        public string GenerateProject(Benchmark benchmark)
        {
            var compileUnit = BuildCompileUnit(benchmark);
            var projectDir = SaveProject(provider, benchmark, compileUnit);
            return projectDir;
        }

        public void CompileCode(string directoryPath)
        {
            var executor = new BenchmarkExecutor();
            executor.Exec("MSBuild", Path.Combine(directoryPath, MainClassName + ".csproj"));
            Console.WriteLine(File.Exists(Path.Combine(directoryPath, MainClassName + ".exe")) ? "Success" : "Fail");
        }

        #region Build

        private static CodeCompileUnit BuildCompileUnit(Benchmark benchmark)
        {
            var systemConsoleType = new CodeTypeReferenceExpression("System.Console");
            var environmentHelperType = new CodeTypeReferenceExpression("BenchmarkDotNet.EnvironmentHelper");

            var compileUnit = new CodeCompileUnit();
            var mainNamespace = new CodeNamespace(MainNamespaceName);
            compileUnit.Namespaces.Add(mainNamespace);

            mainNamespace.Imports.Add(new CodeNamespaceImport("System"));
            mainNamespace.Imports.Add(new CodeNamespaceImport("System.Threading"));
            mainNamespace.Imports.Add(new CodeNamespaceImport("BenchmarkDotNet"));
            mainNamespace.Imports.Add(new CodeNamespaceImport(benchmark.Target.Type.Namespace));

            var mainClass = new CodeTypeDeclaration(MainClassName);
            mainClass.BaseTypes.Add(benchmark.Target.Type);
            mainNamespace.Types.Add(mainClass);

            var runMethod = new CodeMemberMethod
            {
                Attributes = MemberAttributes.Public,
                Name = RunMethodName
            };

            var getEnvInfoStatement = new CodeMethodInvokeExpression(environmentHelperType, "GetFullEnvironmentInfo");
            var consoleWriteEnvInfo = new CodeMethodInvokeExpression(systemConsoleType, "WriteLine", getEnvInfoStatement);
            var consoleWriteFinish = new CodeMethodInvokeExpression(systemConsoleType, "WriteLine", new CodePrimitiveExpression("// Work finished"));
            runMethod.Statements.Add(consoleWriteEnvInfo);
            runMethod.Statements.Add(new CodeSnippetStatement("            Thread.CurrentThread.Priority = ThreadPriority.Highest;"));
            if (benchmark.Task.Configuration.Mode == BenchmarkMode.SingleRun)
            {
                var runMethodName = benchmark.Target.Method.ReturnType == typeof (void) ? "SingleRunVoid" : "SingleRun";
                var snippet = new CodeSnippetStatement { Value = $"            BenchmarkUtils.{runMethodName}({benchmark.Target.Method.Name}, args);" };
                runMethod.Statements.Add(snippet);
            }
            runMethod.Statements.Add(consoleWriteFinish);
            var argsParam = new CodeParameterDeclarationExpression(typeof(string[]), "args");
            runMethod.Parameters.Add(argsParam);

            var start = new CodeMemberMethod { Attributes = MemberAttributes.Static | MemberAttributes.Public, Name = "Main" };
            start.Parameters.Add(argsParam);
            var createInstanceExpression = new CodeObjectCreateExpression(new CodeTypeReference(MainClassName));
            var createInstanceStatament = new CodeVariableDeclarationStatement(new CodeTypeReference(MainClassName), ClassInstanceName, createInstanceExpression);
            var runInvokeStatement = new CodeMethodInvokeExpression(new CodeVariableReferenceExpression(ClassInstanceName), RunMethodName, new CodeSnippetExpression("args"));
            start.Statements.Add(createInstanceStatament);
            start.Statements.Add(runInvokeStatement);

            mainClass.Members.Add(start);
            mainClass.Members.Add(runMethod);

            return compileUnit;
        }

        #endregion

        #region Save

        private static string SaveProject(CodeDomProvider provider, Benchmark benchmark, CodeCompileUnit compileUnit)
        {
            var projectDir = CreateProjectDirectory(benchmark);

            SaveSourceFile(provider, compileUnit, projectDir);
            SaveProjectFile(projectDir, benchmark.Task.Configuration);
            SaveAppConfigFile(projectDir, benchmark.Task.Configuration);

            return projectDir;
        }

        private static void SaveSourceFile(CodeDomProvider provider, CodeCompileUnit compileUnit, string projectDir)
        {
            string fileName = Path.Combine(projectDir, MainClassName + ".cs");
            var textWriter = new IndentedTextWriter(new StreamWriter(fileName, false), "    ");
            provider.GenerateCodeFromCompileUnit(compileUnit, textWriter, new CodeGeneratorOptions());
            textWriter.Close();
        }

        private static void SaveProjectFile(string projectDir, BenchmarkConfiguration configuration)
        {
            string fileName = Path.Combine(projectDir, MainClassName + ".csproj");
            File.WriteAllText(fileName, GetCsProj(configuration.Platform.ToConfig(), configuration.Framework.ToConfig()));
        }

        private static void SaveAppConfigFile(string projectDir, BenchmarkConfiguration configuration)
        {
            string appConfigPath = Path.Combine(projectDir, "app.config");
            File.WriteAllText(appConfigPath, GetAppConfig(configuration.JitVersion.ToConfig()));
        }

        private static string CreateProjectDirectory(Benchmark benchmark)
        {
            var directoryPath = Path.Combine(Directory.GetCurrentDirectory(), benchmark.Caption);
            try
            {
                if (Directory.Exists(directoryPath))
                    Directory.Delete(directoryPath, true);
            }
            catch (Exception)
            {
                // Nevermind
            }
            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }

        #endregion

        #region Templates

        private static string GetCsProj(string platform, string framework)
        {
            const string csProjTemplate = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Project ToolsVersion=""4.0"" DefaultTargets=""Build"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <Import Project=""$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props"" Condition=""Exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props')"" />
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <Platform>$PLATFORM$</Platform>
    <PlatformTarget>$PLATFORM$</PlatformTarget>
    <OutputType>Exe</OutputType>
    <AppDesignerFolder>Properties</AppDesignerFolder>
    <RootNamespace>BenchmarkDotNet.Autogenerated</RootNamespace>
    <AssemblyName>Program</AssemblyName>
    <TargetFrameworkVersion>$TARGETFRAMEWORKVERSION$</TargetFrameworkVersion>
    <TargetFrameworkProfile></TargetFrameworkProfile>
    <DebugType>pdbonly</DebugType>
    <Optimize>true</Optimize>
    <OutputPath>.\</OutputPath>
    <DefineConstants>TRACE</DefineConstants>
    <ErrorReport>prompt</ErrorReport>
    <WarningLevel>4</WarningLevel>
    <Prefer32Bit>false</Prefer32Bit>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include=""BenchmarkDotNet"">
      <HintPath>..\BenchmarkDotNet.dll</HintPath>
    </Reference>
    <Reference Include=""Benchmarks"">
      <HintPath>..\$ENTRYASSEMBLY$.exe</HintPath>
    </Reference>
    <Reference Include=""System"" />
    <Reference Include=""System.Core"" />
    <Reference Include=""System.Xml.Linq"" />
    <Reference Include=""System.Xml"" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include=""Program.cs"" />
  </ItemGroup>
  <ItemGroup>
    <None Include=""app.config"">
      <SubType>Designer</SubType>
    </None>
  </ItemGroup>
  <Import Project=""$(MSBuildToolsPath)\Microsoft.CSharp.targets"" />
</Project>";
            return csProjTemplate.
                Replace("$PLATFORM$", platform).
                Replace("$TARGETFRAMEWORKVERSION$", framework).
                Replace("$ENTRYASSEMBLY$", Assembly.GetEntryAssembly().GetName().Name);
        }

        private static string GetAppConfig(string useLegacyJit)
        {
            const string appConfigTemplate = @"<?xml version=""1.0""?>
<configuration>
  <runtime>
    <useLegacyJit enabled=""$USELEGACYJIT$"" />
  </runtime>
</configuration>
";
            return appConfigTemplate.Replace("$USELEGACYJIT$", useLegacyJit);
        }

        #endregion
    }
}
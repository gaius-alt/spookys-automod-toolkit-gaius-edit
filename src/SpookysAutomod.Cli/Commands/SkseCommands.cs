using System.CommandLine;
using SpookysAutomod.Core.Logging;
using SpookysAutomod.Skse.Models;
using SpookysAutomod.Skse.Services;

namespace SpookysAutomod.Cli.Commands;

public static class SkseCommands
{
    private static IModLogger CreateLogger(bool json, bool verbose) =>
        json ? new SilentLogger() : new ConsoleLogger(verbose);

    public static Command Create(Option<bool> jsonOption, Option<bool> verboseOption)
    {
        var skseCommand = new Command("skse", "SKSE C++ plugin project management");

        skseCommand.AddCommand(CreateCreateCommand(jsonOption, verboseOption));
        skseCommand.AddCommand(CreateBuildCommand(jsonOption, verboseOption));
        skseCommand.AddCommand(CreateInfoCommand(jsonOption, verboseOption));
        skseCommand.AddCommand(CreateListTemplatesCommand(jsonOption));
        skseCommand.AddCommand(CreateAddFunctionCommand(jsonOption, verboseOption));

        return skseCommand;
    }

    private static Command CreateCreateCommand(Option<bool> jsonOption, Option<bool> verboseOption)
    {
        var command = new Command("create", "Create a new SKSE plugin project");

        var nameArg = new Argument<string>("name", "Project name");
        var templateOpt = new Option<string>("--template", () => "basic", "Template to use (basic, papyrus-native)");
        var outputOpt = new Option<string>("--output", () => ".", "Output directory");
        var authorOpt = new Option<string>("--author", () => "Unknown", "Author name");
        var descriptionOpt = new Option<string>("--description", () => "", "Project description");

        command.AddArgument(nameArg);
        command.AddOption(templateOpt);
        command.AddOption(outputOpt);
        command.AddOption(authorOpt);
        command.AddOption(descriptionOpt);

        command.SetHandler((name, template, output, author, description, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new SkseProjectService(logger);
            var config = new SkseProjectConfig
            {
                Name = name,
                Template = template,
                Author = author,
                Description = string.IsNullOrEmpty(description) ? $"{name} SKSE Plugin" : description
            };

            var result = service.CreateProject(config, output);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else
            {
                if (result.Success)
                {
                    Console.WriteLine($"Created SKSE project: {result.Value}");
                    Console.WriteLine();
                    Console.WriteLine("Next steps:");
                    Console.WriteLine($"  cd {result.Value}");
                    Console.WriteLine("  Run 'skse build .' to build the plugin");
                    Console.WriteLine("  Or manually: xmake (first run downloads commonlibsse-ng + deps via xmake-repo)");
                }
                else
                {
                    Console.Error.WriteLine($"Error: {result.Error}");
                    if (result.Suggestions != null && result.Suggestions.Count > 0)
                    {
                        Console.Error.WriteLine("Suggestions:");
                        foreach (var suggestion in result.Suggestions)
                        {
                            Console.Error.WriteLine($"  - {suggestion}");
                        }
                    }
                    Environment.ExitCode = 1;
                }
            }
        }, nameArg, templateOpt, outputOpt, authorOpt, descriptionOpt, jsonOption, verboseOption);

        return command;
    }

    private static Command CreateBuildCommand(Option<bool> jsonOption, Option<bool> verboseOption)
    {
        var command = new Command("build", "Build an SKSE plugin project using xmake");

        var projectArg = new Argument<string>("project", () => ".", "Project directory");
        var configOpt = new Option<string>("--config", () => "Release", "Build configuration (Release or Debug)");
        var cleanOpt = new Option<bool>("--clean", () => false, "Clean build directory before building");

        command.AddArgument(projectArg);
        command.AddOption(configOpt);
        command.AddOption(cleanOpt);

        command.SetHandler(async (project, config, clean, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new SkseProjectService(logger);

            if (!json)
            {
                Console.WriteLine($"Building SKSE plugin in: {Path.GetFullPath(project)}");
                Console.WriteLine($"Configuration: {config}");
                if (clean) Console.WriteLine("Clean build requested");
                Console.WriteLine();
            }

            var result = await service.BuildProjectAsync(project, config, clean);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else
            {
                if (result.Success && result.Value != null)
                {
                    Console.WriteLine("Build succeeded!");
                    if (result.Value.OutputDll != null)
                    {
                        Console.WriteLine($"Output: {result.Value.OutputDll}");
                        Console.WriteLine();
                        Console.WriteLine("To install:");
                        Console.WriteLine($"  Copy the DLL to: <Skyrim>/Data/SKSE/Plugins/");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Error: {result.Error}");
                    if (result.Suggestions != null && result.Suggestions.Count > 0)
                    {
                        Console.Error.WriteLine();
                        Console.Error.WriteLine("Suggestions:");
                        foreach (var suggestion in result.Suggestions)
                        {
                            Console.Error.WriteLine($"  - {suggestion}");
                        }
                    }
                    Environment.ExitCode = 1;
                }
            }
        }, projectArg, configOpt, cleanOpt, jsonOption, verboseOption);

        return command;
    }

    private static Command CreateInfoCommand(Option<bool> jsonOption, Option<bool> verboseOption)
    {
        var command = new Command("info", "Get information about an SKSE project");

        var pathArg = new Argument<string>("path", () => ".", "Project directory");

        command.AddArgument(pathArg);

        command.SetHandler((path, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new SkseProjectService(logger);
            var result = service.GetProjectInfo(path);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else
            {
                if (result.Success && result.Value != null)
                {
                    var config = result.Value;
                    Console.WriteLine($"Project: {config.Name}");
                    Console.WriteLine($"Author: {config.Author}");
                    Console.WriteLine($"Version: {config.Version}");
                    Console.WriteLine($"Template: {config.Template}");
                    Console.WriteLine($"Description: {config.Description}");
                    Console.WriteLine($"Target Versions: {string.Join(", ", config.TargetVersions)}");

                    if (config.PapyrusFunctions.Count > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("Papyrus Functions:");
                        foreach (var func in config.PapyrusFunctions)
                        {
                            var paramStr = string.Join(", ", func.Parameters.Select(p => $"{p.Type} {p.Name}"));
                            Console.WriteLine($"  {func.ReturnType} {func.Name}({paramStr})");
                        }
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Error: {result.Error}");
                    Environment.ExitCode = 1;
                }
            }
        }, pathArg, jsonOption, verboseOption);

        return command;
    }

    private static Command CreateListTemplatesCommand(Option<bool> jsonOption)
    {
        var command = new Command("templates", "List available SKSE templates");

        command.SetHandler((json) =>
        {
            var logger = CreateLogger(json, false);
            var service = new SkseProjectService(logger);
            var result = service.ListTemplates();

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else
            {
                if (result.Success)
                {
                    Console.WriteLine("Available SKSE Templates:");
                    Console.WriteLine();
                    foreach (var template in result.Value ?? Array.Empty<string>())
                    {
                        Console.WriteLine($"  {template}");
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Error: {result.Error}");
                    Environment.ExitCode = 1;
                }
            }
        }, jsonOption);

        return command;
    }

    private static Command CreateAddFunctionCommand(Option<bool> jsonOption, Option<bool> verboseOption)
    {
        var command = new Command("add-function", "Add a Papyrus native function to a project");

        var projectArg = new Argument<string>("project", () => ".", "Project directory");
        var nameOpt = new Option<string>("--name", "Function name") { IsRequired = true };
        var returnOpt = new Option<string>("--return", () => "void", "Return type");
        var paramsOpt = new Option<string[]>("--param", "Parameters (format: type:name)") { AllowMultipleArgumentsPerToken = true };

        command.AddArgument(projectArg);
        command.AddOption(nameOpt);
        command.AddOption(returnOpt);
        command.AddOption(paramsOpt);

        command.SetHandler((project, name, returnType, paramStrings, json, verbose) =>
        {
            var logger = CreateLogger(json, verbose);
            var service = new SkseProjectService(logger);

            var function = new PapyrusNativeFunction
            {
                Name = name,
                ReturnType = returnType
            };

            if (paramStrings != null)
            {
                foreach (var paramStr in paramStrings)
                {
                    var parts = paramStr.Split(':');
                    if (parts.Length == 2)
                    {
                        function.Parameters.Add(new PapyrusParameter
                        {
                            Type = parts[0],
                            Name = parts[1]
                        });
                    }
                }
            }

            var result = service.AddPapyrusFunction(project, function);

            if (json)
            {
                Console.WriteLine(result.ToJson(true));
            }
            else
            {
                if (result.Success)
                {
                    Console.WriteLine($"Added function: {name}");
                    Console.WriteLine("  Rebuild the project to include the new function");
                }
                else
                {
                    Console.Error.WriteLine($"Error: {result.Error}");
                    if (result.Suggestions != null && result.Suggestions.Count > 0)
                    {
                        foreach (var suggestion in result.Suggestions)
                        {
                            Console.Error.WriteLine($"  - {suggestion}");
                        }
                    }
                    Environment.ExitCode = 1;
                }
            }
        }, projectArg, nameOpt, returnOpt, paramsOpt, jsonOption, verboseOption);

        return command;
    }
}

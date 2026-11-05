using System.Diagnostics;
using Launcher.Core;
using Waypoint;

namespace Waypoint.Tests;

// A checkout offers tools as folders with a waypoint-tool.json beside the script; Waypoint only reads them and launches
// them, so nothing here names a tool.
public class CheckoutToolsTests
{
    static string NewCheckout()
    {
        string root = Path.Combine(Path.GetTempPath(), "waypoint-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static CheckoutTool? Tool(string root, string folder, string descriptor, string script = "tool.ps1")
    {
        string directory = Path.Combine(root, folder);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, script), "Write-Output 'tool'");
        string path = Path.Combine(directory, CheckoutTools.DescriptorName);
        File.WriteAllText(path, descriptor);
        return CheckoutTools.Read(path, root);
    }

    const string Descriptor = """
        {
          "name": "Item forge",
          "description": "Rolls items.",
          "script": "tool.ps1",
          "needsGame": true,
          "icon": "Anvil"
        }
        """;

    [Fact]
    public void Tools_beside_their_scripts_are_found_in_Tools_and_Mods_Binaries()
    {
        string root = NewCheckout();
        try
        {
            Assert.NotNull(Tool(root, Path.Combine("Tools", "ItemForge"), Descriptor));
            Assert.NotNull(Tool(root, Path.Combine("Mods", "Binaries", "console"), Descriptor));
            var tools = CheckoutTools.Find(root);
            Assert.Equal(2, tools.Count);
            var forge = tools[0];
            Assert.Equal("Item forge", forge.Name);
            Assert.Equal("Rolls items.", forge.Description);
            Assert.EndsWith("tool.ps1", forge.Script);
            Assert.Equal(Path.Combine(root, "Tools", "ItemForge"), forge.Folder);
            // The tool runs where its own .cmd would: beside the script, not in Waypoint's checkout root.
            Assert.Equal(forge.Folder, forge.WorkingDirectory);
            Assert.True(forge.NeedsGame);
            Assert.Equal("Anvil", forge.Icon);
            Assert.Empty(forge.Arguments);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Arguments_and_a_working_directory_beside_the_script_are_read()
    {
        string root = NewCheckout();
        try
        {
            var tool = Tool(root, Path.Combine("Tools", "console"), """
                { "name": "Console", "script": "tool.ps1", "arguments": ["-SelfTest"], "workingDirectory": "." }
                """);
            Assert.NotNull(tool);
            Assert.Equal(new[] { "-SelfTest" }, tool!.Arguments);
            Assert.Equal(tool.Folder, tool.WorkingDirectory);
            Assert.False(tool.NeedsGame);
            Assert.Null(tool.Icon);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_broken_descriptor_never_hides_the_others()
    {
        string root = NewCheckout();
        try
        {
            Assert.NotNull(Tool(root, Path.Combine("Tools", "good"), Descriptor));
            // No such script.
            Assert.Null(Tool(root, Path.Combine("Tools", "missing"), """
                { "name": "Missing", "script": "nope.ps1" }
                """));
            // Not JSON.
            Assert.Null(Tool(root, Path.Combine("Tools", "bad"), "not json"));
            // Escapes the checkout.
            Assert.Null(Tool(root, Path.Combine("Tools", "escape"), """
                { "name": "Escape", "script": "../../../outside.ps1" }
                """));
            // A tool outside the two tool folders is not picked up.
            Assert.NotNull(Tool(root, Path.Combine("Misc", "elsewhere"), Descriptor));
            var tools = CheckoutTools.Find(root);
            Assert.Single(tools);
            Assert.Equal("Item forge", tools[0].Name);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_tool_is_started_like_its_cmd_runs_it()
    {
        string root = NewCheckout();
        try
        {
            var tool = Tool(root, Path.Combine("Tools", "console"), """
                { "name": "Console", "script": "tool.ps1", "arguments": ["-SelfTest"] }
                """);
            Assert.NotNull(tool);
            var start = LocalToolRun.StartInfo(tool!);
            Assert.False(start.UseShellExecute);
            Assert.True(start.CreateNoWindow);
            // Not WindowStyle.Hidden: STARTUPINFO's SW_HIDE hides the tool's own WinForms window, the first a process shows.
            Assert.Equal(ProcessWindowStyle.Normal, start.WindowStyle);
            Assert.Equal(tool.WorkingDirectory, start.WorkingDirectory);
            Assert.Equal(new[] { "-NoProfile", "-Sta", "-ExecutionPolicy", "Bypass", "-File", tool.Script, "-SelfTest" },
                start.ArgumentList);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_tool_runs_in_its_folder_with_the_checkouts_powershell()
    {
        string root = NewCheckout();
        try
        {
            string folder = Path.Combine(root, "Tools", "echo");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "tool.ps1"),
                "Set-Content -LiteralPath (Join-Path $PSScriptRoot ran.txt) -Value ok");
            string descriptor = Path.Combine(folder, CheckoutTools.DescriptorName);
            File.WriteAllText(descriptor, """{ "name": "Echo", "script": "tool.ps1" }""");
            var tool = CheckoutTools.Read(descriptor, root);
            Assert.NotNull(tool);
            using var process = LocalToolRun.Start(tool!);
            Assert.True(process.WaitForExit(30_000), "the tool did not finish");
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("ok", File.ReadAllText(Path.Combine(folder, "ran.txt")).Trim());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_checkout_without_descriptors_offers_nothing()
    {
        string root = NewCheckout();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Tools", "ItemForge"));
            Assert.Empty(CheckoutTools.Find(root));
            Assert.Empty(CheckoutTools.Find(""));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

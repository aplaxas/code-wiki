using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeWiki.Cli;
using Spectre.Console;

namespace CodeWiki.Semantic;

public sealed class EnrichPicker
{
    private readonly EnrichRunner _runner;
    private readonly IGraphReader _reader;
    private readonly string _root;

    public EnrichPicker(EnrichRunner runner, IGraphReader reader, string vanuatuRoot)
    {
        _runner = runner; _reader = reader; _root = vanuatuRoot;
    }

    public async Task RunAsync()
    {
        var top = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("enrich 대상 종류")
            .AddChoices("화면 ViewModel", "서버 인터페이스", "종료"));
        if (top == "화면 ViewModel") await RunVmFlow();
        else if (top == "서버 인터페이스") await RunIfaceFlow();
    }

    private async Task RunVmFlow()
    {
        var projects = VanuatuLayout.ListClientModuleProjects(_root);
        if (projects.Count == 0) { Warn("Client/Module 프로젝트가 없습니다."); return; }
        var project = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("프로젝트").PageSize(20).AddChoices(projects));
        var projectDir = System.IO.Path.Combine(_root, "Client", "Module", project);
        var vms = VanuatuLayout.ListViewModels(projectDir);
        if (vms.Count == 0) { Warn("ViewModel이 없습니다."); return; }
        var picked = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title($"{project} — ViewModel 선택 (space 토글, enter 확정)")
            .PageSize(20).Required(false).AddChoices(vms));
        if (picked.Count == 0) { Warn("선택 없음."); return; }
        await RunEach(picked, _runner.RunVmAsync);
    }

    private async Task RunIfaceFlow()
    {
        var ifaces = VanuatuLayout.ListServiceInterfaces(_root);
        if (ifaces.Count == 0) { Warn("인터페이스가 없습니다."); return; }
        var prompt = new SelectionPrompt<string>().Title("인터페이스 (폴더별)").PageSize(20);
        foreach (var g in ifaces.GroupBy(x => x.Folder))
            prompt.AddChoiceGroup(g.Key, g.Select(x => x.Name));
        var iface = AnsiConsole.Prompt(prompt);
        var methods = _reader.ListIfaceMethods(iface);
        if (methods.Count == 0) { Warn($"{iface}: enrich 가능한 메서드가 없습니다(Torba 구현 없음)."); return; }
        var picked = AnsiConsole.Prompt(new MultiSelectionPrompt<string>()
            .Title($"{iface} — 메서드 선택").PageSize(20).Required(false).AddChoices(methods));
        if (picked.Count == 0) { Warn("선택 없음."); return; }
        await RunEach(picked, _runner.RunIfaceAsync);
    }

    private static async Task RunEach(IReadOnlyList<string> items, Func<string, Task<int>> run)
    {
        int enriched = 0, skipped = 0, failed = 0;
        foreach (var item in items)
        {
            try
            {
                var n = await run(item);
                if (n > 0) { enriched += n; AnsiConsole.MarkupLine($"[green]✓[/] {Markup.Escape(item)}: {n} records"); }
                else { skipped++; AnsiConsole.MarkupLine($"[grey]•[/] {Markup.Escape(item)}: skipped"); }
            }
            catch (Exception e)
            {
                failed++; AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(item)}: {Markup.Escape(e.Message)}");
            }
        }
        AnsiConsole.MarkupLine($"[bold]done — enriched {enriched} / skipped {skipped} / failed {failed}[/]");
    }

    private static void Warn(string msg) => AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(msg)}[/]");
}

using System.Diagnostics;
using System.IO;
using FTO_App.Services;

namespace FTO_App.Tests;

/// <summary>
/// O script de atualização roda escondido, com o app já fechado — um erro nele não aparece em
/// lugar nenhum. A v3.0.1 foi para o cliente com um erro de sintaxe (<c>@('.env' 'FTO.db')</c>, sem
/// vírgula): o PowerShell recusava o arquivo inteiro, nada era copiado e o app não reabria.
/// Estes testes geram o script como o app gera e o EXECUTAM contra pastas temporárias.
/// </summary>
public class ScriptAtualizacaoTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "fto_update_teste_" + Guid.NewGuid().ToString("N"));
    private readonly string _origem;
    private readonly string _destino;
    private readonly string _script;
    private readonly string _log;

    public ScriptAtualizacaoTests()
    {
        _origem = Path.Combine(_raiz, "pacote");
        _destino = Path.Combine(_raiz, "instalado");
        _script = Path.Combine(_raiz, "ApplyUpdate.ps1");
        _log = Path.Combine(_raiz, "update.log");
        Directory.CreateDirectory(_origem);
        Directory.CreateDirectory(_destino);
        UpdateService.GravarScriptAtualizacao(_script);
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch { }
    }

    private static (int Codigo, string Saida) PowerShell(params string[] argumentos)
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string a in argumentos) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var erro = p.StandardError.ReadToEndAsync();
        string saida = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(120_000), "PowerShell não terminou em 2 minutos");
        return (p.ExitCode, saida + erro.Result);
    }

    private (int Codigo, string Saida) RodarScript(string origem) => PowerShell(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", _script,
        "-Source", origem,
        "-Target", _destino,
        "-ExePath", Path.Combine(_destino, "FTO_App.exe"),
        "-WaitPid", "0",
        "-Log", _log,
        "-NaoReabrir");

    private string Resultado() =>
        File.ReadAllText(Path.Combine(_destino, UpdateService.ArquivoResultadoAtualizacao)).Trim();

    [Fact]
    public void Script_NaoTemErroDeSintaxe()
    {
        var (codigo, saida) = PowerShell("-NoProfile", "-Command",
            "$e = $null; " +
            $"[void][System.Management.Automation.Language.Parser]::ParseFile('{_script}', [ref]$null, [ref]$e); " +
            "if ($e.Count -gt 0) { $e | ForEach-Object { \"linha $($_.Extent.StartLineNumber): $($_.Message)\" }; exit 1 }");

        Assert.True(codigo == 0, "Erro de sintaxe no script de atualização:\n" + saida);
    }

    /// <summary>
    /// Sem BOM o Windows PowerShell 5.1 lê o .ps1 como ANSI: além de estragar acentos, bytes do
    /// UTF-8 podem virar aspas tipográficas — que o PowerShell aceita como delimitador de string.
    /// </summary>
    [Fact]
    public void Script_EGravadoEmUtf8ComBom()
    {
        byte[] bytes = File.ReadAllBytes(_script);

        Assert.True(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "O script precisa ser gravado em UTF-8 com BOM");
    }

    [Fact]
    public void Script_CopiaOPacote_PreservaEnvEBanco_EMarcaSucesso()
    {
        File.WriteAllText(Path.Combine(_origem, "FTO_App.exe"), "NOVO");
        Directory.CreateDirectory(Path.Combine(_origem, "pt-BR"));
        File.WriteAllText(Path.Combine(_origem, "pt-BR", "recursos.dll"), "NOVA");
        File.WriteAllText(Path.Combine(_origem, ".env"), "ENV_DO_PACOTE");
        File.WriteAllText(Path.Combine(_origem, "FTO.db"), "DB_DO_PACOTE");

        File.WriteAllText(Path.Combine(_destino, "FTO_App.exe"), "VELHO");
        File.WriteAllText(Path.Combine(_destino, ".env"), "SEGREDO_DO_CLIENTE");
        File.WriteAllText(Path.Combine(_destino, "FTO.db"), "DADOS_DO_CLIENTE");

        var (codigo, saida) = RodarScript(_origem);

        Assert.True(codigo == 0, saida);
        Assert.Equal("NOVO", File.ReadAllText(Path.Combine(_destino, "FTO_App.exe")));
        Assert.Equal("NOVA", File.ReadAllText(Path.Combine(_destino, "pt-BR", "recursos.dll")));
        // Configuração e banco do cliente nunca podem ser sobrescritos pelo pacote.
        Assert.Equal("SEGREDO_DO_CLIENTE", File.ReadAllText(Path.Combine(_destino, ".env")));
        Assert.Equal("DADOS_DO_CLIENTE", File.ReadAllText(Path.Combine(_destino, "FTO.db")));
        Assert.Equal("OK", Resultado());
        Assert.Contains("robocopy terminou com codigo", File.ReadAllText(_log));
    }

    [Fact]
    public void Script_FalhaNaCopia_MarcaFalhaComMotivo()
    {
        var (codigo, saida) = RodarScript(Path.Combine(_raiz, "pasta-que-nao-existe"));

        Assert.True(codigo == 0, saida); // a falha é tratada no script, não derruba o PowerShell
        string resultado = Resultado();
        Assert.StartsWith("FALHA|", resultado);
        Assert.Contains("robocopy", resultado);
        Assert.Contains("FALHA:", File.ReadAllText(_log));
    }

    [Fact]
    public void ResultadoDaAtualizacao_ELidoUmaVezSo()
    {
        string marcador = Path.Combine(AppContext.BaseDirectory, UpdateService.ArquivoResultadoAtualizacao);
        File.WriteAllText(marcador, "FALHA|arquivo em uso");

        var primeira = UpdateService.ConsumirResultadoUltimaAtualizacao();
        var segunda = UpdateService.ConsumirResultadoUltimaAtualizacao();

        Assert.NotNull(primeira);
        Assert.False(primeira!.Value.Sucesso);
        Assert.Equal("arquivo em uso", primeira.Value.Mensagem);
        Assert.Null(segunda);
        Assert.False(File.Exists(marcador));
    }
}

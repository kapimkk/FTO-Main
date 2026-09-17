using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FTO_App.Services
{
    /// <summary>
    /// Verifica e aplica atualizações a partir das Releases do GitHub.
    /// Preserva .env e banco SQLite durante a troca de arquivos.
    /// </summary>
    public static class UpdateService
    {
        public const string GitHubOwner = "kapimkk";
        public const string GitHubRepo = "FTO-Main";
        public const string PreferredAssetName = "FTO_App-win-x64.zip";

        /// <summary>1 MB por leitura: o padrão do CopyToAsync é 80 KB, e o FileStream padrão
        /// grava síncrono em blocos de 4 KB — muita ida ao disco para um pacote de dezenas de MB.</summary>
        private const int BufferBytes = 1024 * 1024;

        /// <summary>Ritmo do texto de andamento. Mais rápido que isso só pisca no botão.</summary>
        private static readonly TimeSpan IntervaloProgresso = TimeSpan.FromSeconds(1);

        private static readonly string[] PreserveFileNames =
        {
            ".env",
            "FTO.db",
            "FTO.db-shm",
            "FTO.db-wal"
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public static Version GetLocalVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            string? info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                string clean = info.Split('+')[0].Trim().TrimStart('v', 'V');
                if (Version.TryParse(NormalizeVersion(clean), out var fromInfo))
                    return fromInfo;
            }

            return asm.GetName().Version ?? new Version(1, 0, 0, 0);
        }

        public static string GetLocalVersionDisplay()
        {
            Version v = GetLocalVersion();
            return $"v{v.Major}.{v.Minor}.{v.Build}";
        }

        public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
        {
            using var client = CreateHttpClient();
            string url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.Fail(
                    "Nenhuma release encontrada no GitHub.\n\n" +
                    "Crie uma Release com o asset FTO_App-win-x64.zip.");
            }

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions)
                ?? throw new InvalidOperationException("Resposta inválida da API do GitHub.");

            Version remote = ParseTagVersion(release.TagName);
            Version local = GetLocalVersion();
            bool available = remote > TruncateToThreeParts(local);

            GitHubAsset? asset = FindAsset(release);
            return new UpdateCheckResult
            {
                Success = true,
                UpdateAvailable = available && asset != null,
                LocalVersion = local,
                RemoteVersion = remote,
                ReleaseName = release.Name ?? release.TagName,
                ReleaseNotes = release.Body ?? string.Empty,
                ReleaseUrl = release.HtmlUrl ?? string.Empty,
                DownloadUrl = asset?.BrowserDownloadUrl,
                AssetName = asset?.Name,
                AssetSizeBytes = asset?.Size ?? 0,
                ErrorMessage = asset == null && available
                    ? $"Release {release.TagName} sem o arquivo {PreferredAssetName}."
                    : null
            };
        }

        public static async Task DownloadAndPrepareUpdateAsync(
            UpdateCheckResult check,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (check is null || !check.UpdateAvailable || string.IsNullOrWhiteSpace(check.DownloadUrl))
                throw new InvalidOperationException("Não há atualização válida para baixar.");

            LimparTentativasAnteriores();

            string tempRoot = Path.Combine(Path.GetTempPath(), "FTO_Update", Guid.NewGuid().ToString("N"));
            string zipPath = Path.Combine(tempRoot, check.AssetName ?? PreferredAssetName);
            string extractDir = Path.Combine(tempRoot, "extract");
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(extractDir);

            progress?.Report("Baixando pacote...");
            long baixados;
            long esperados;
            using (var client = CreateHttpClient(paraDownloadBinario: true))
            using (var response = await client.GetAsync(check.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                esperados = response.Content.Headers.ContentLength ?? check.AssetSizeBytes;

                await using var origem = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var destino = new FileStream(
                    zipPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true);

                baixados = await CopiarComProgressoAsync(origem, destino, esperados, progress, ct).ConfigureAwait(false);
            }

            // Sem esta checagem, um download cortado no meio só aparece depois, como um erro de
            // ZIP corrompido ("End of Central Directory record could not be found") — que não diz
            // ao usuário que o problema foi a conexão.
            if (esperados > 0 && baixados != esperados)
            {
                throw new IOException(
                    $"Download incompleto: {baixados / 1048576.0:0.#} MB de {esperados / 1048576.0:0.#} MB. " +
                    "Verifique a conexão e tente novamente.");
            }

            progress?.Report("Extraindo arquivos...");
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            string sourceDir = ResolvePublishRoot(extractDir);
            string targetDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string exePath = Path.Combine(targetDir, "FTO_App.exe");
            string scriptPath = Path.Combine(tempRoot, "ApplyUpdate.ps1");
            GravarScriptAtualizacao(scriptPath);

            string logPath = CaminhoLogAtualizacao;
            try { if (File.Exists(logPath)) File.Delete(logPath); } catch { /* log anterior preso não impede */ }

            progress?.Report("Aplicando atualização...");
            int pid = Environment.ProcessId;
            string args =
                $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                $"-Source \"{sourceDir}\" -Target \"{targetDir}\" -ExePath \"{exePath}\" -WaitPid {pid} " +
                $"-Log \"{logPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempRoot
            };

            Process.Start(psi);
        }

        /// <summary>
        /// Copia o pacote reportando andamento. Sem isso o botão fica parado em
        /// "Baixando pacote..." por minutos e o usuário conclui que travou.
        /// </summary>
        private static async Task<long> CopiarComProgressoAsync(
            Stream origem, Stream destino, long total, IProgress<string>? progress, CancellationToken ct)
        {
            byte[] buffer = new byte[BufferBytes];
            long lidos = 0;
            var relogio = Stopwatch.StartNew();
            TimeSpan ultimoAviso = TimeSpan.Zero;

            int n;
            while ((n = await origem.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await destino.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                lidos += n;

                if (relogio.Elapsed - ultimoAviso < IntervaloProgresso) continue;
                ultimoAviso = relogio.Elapsed;
                progress?.Report(DescreverProgresso(lidos, total, relogio.Elapsed));
            }

            return lidos;
        }

        private static string DescreverProgresso(long lidos, long total, TimeSpan decorrido)
        {
            double mb = lidos / 1048576.0;
            double mbPorSegundo = decorrido.TotalSeconds > 0 ? mb / decorrido.TotalSeconds : 0;

            if (total <= 0)
                return $"Baixando {mb:0.#} MB ({mbPorSegundo:0.#} MB/s)";

            return $"Baixando {lidos * 100 / total}% " +
                   $"({mb:0.#}/{total / 1048576.0:0.#} MB · {mbPorSegundo:0.#} MB/s)";
        }

        /// <param name="paraDownloadBinario">
        /// O download do asset sai por redirecionamento para o CDN do GitHub, que não é a API:
        /// mandar Accept de JSON da API e o token para lá não ajuda em nada e só atrapalha a
        /// negociação. Este cliente vai limpo, só com o User-Agent.
        /// </param>
        private static HttpClient CreateHttpClient(bool paraDownloadBinario = false)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FTO-App-Updater");

            if (paraDownloadBinario)
            {
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                return client;
            }

            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            string? token = TryReadUpdateToken();
            if (!string.IsNullOrWhiteSpace(token))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            return client;
        }

        private static string? TryReadUpdateToken()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("FTO_UPDATE_TOKEN");
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return fromEnv.Trim();

            string envPath = Path.Combine(AppContext.BaseDirectory, ".env");
            if (!File.Exists(envPath))
                return null;

            foreach (string raw in File.ReadAllLines(envPath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                if (!line.StartsWith("FTO_UPDATE_TOKEN=", StringComparison.OrdinalIgnoreCase))
                    continue;

                return line[(line.IndexOf('=') + 1)..].Trim().Trim('"');
            }

            return null;
        }

        private static GitHubAsset? FindAsset(GitHubRelease release)
        {
            if (release.Assets == null || release.Assets.Count == 0)
                return null;

            return release.Assets.FirstOrDefault(a =>
                       a.Name.Equals(PreferredAssetName, StringComparison.OrdinalIgnoreCase))
                   ?? release.Assets.FirstOrDefault(a =>
                       a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                       a.Name.Contains("FTO", StringComparison.OrdinalIgnoreCase))
                   ?? release.Assets.FirstOrDefault(a =>
                       a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolvePublishRoot(string extractDir)
        {
            // Zip pode ter arquivos na raiz ou dentro de FTO_App-win-x64/
            string directExe = Path.Combine(extractDir, "FTO_App.exe");
            if (File.Exists(directExe))
                return extractDir;

            string? nested = Directory.GetDirectories(extractDir)
                .Select(d => new { Dir = d, Exe = Path.Combine(d, "FTO_App.exe") })
                .FirstOrDefault(x => File.Exists(x.Exe))
                ?.Dir;

            if (!string.IsNullOrEmpty(nested))
                return nested;

            throw new DirectoryNotFoundException(
                "Pacote inválido: FTO_App.exe não encontrado no ZIP da release.");
        }

        private static Version ParseTagVersion(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return new Version(0, 0, 0);

            // Aceita v1.2.3 e o formato comum v.1.2.3
            string clean = System.Text.RegularExpressions.Regex.Replace(
                tag.Trim(),
                @"^[vV]\.?",
                "");

            if (Version.TryParse(NormalizeVersion(clean), out var v))
                return TruncateToThreeParts(v);

            return new Version(0, 0, 0);
        }

        private static string NormalizeVersion(string value)
        {
            string[] parts = value.Split('.');
            if (parts.Length >= 3)
                return $"{parts[0]}.{parts[1]}.{parts[2]}";
            if (parts.Length == 2)
                return $"{parts[0]}.{parts[1]}.0";
            if (parts.Length == 1)
                return $"{parts[0]}.0.0";
            return "0.0.0";
        }

        private static Version TruncateToThreeParts(Version v) =>
            new(v.Major, v.Minor, Math.Max(v.Build, 0));

        /// <summary>
        /// Apaga o que sobrou de atualizações anteriores. Cada tentativa deixa o ZIP (~80 MB) e a
        /// pasta extraída (~190 MB) no TEMP; com a atualização falhando em silêncio isso ia
        /// acumulando gigabytes na máquina do cliente sem ninguém perceber.
        /// </summary>
        private static void LimparTentativasAnteriores()
        {
            string raiz = Path.Combine(Path.GetTempPath(), "FTO_Update");
            if (!Directory.Exists(raiz)) return;

            foreach (string pasta in Directory.GetDirectories(raiz))
            {
                try { Directory.Delete(pasta, recursive: true); }
                catch { /* pasta em uso por outra instância: fica para a próxima */ }
            }
        }

        /// <summary>Resultado deixado pelo script na pasta do app: "OK" ou "FALHA|motivo".</summary>
        public const string ArquivoResultadoAtualizacao = "atualizacao-resultado.txt";

        /// <summary>Log fixo do último script de atualização — o endereço que se passa ao suporte.</summary>
        public static string CaminhoLogAtualizacao =>
            Path.Combine(Path.GetTempPath(), "FTO_Update", "ultima-atualizacao.log");

        /// <summary>
        /// Lê e apaga o resultado da última atualização. Nulo quando não houve atualização desde a
        /// última abertura. O script roda escondido depois que o app fecha — sem isso uma falha ali
        /// não aparece em lugar nenhum (foi assim que a v3.0.1 "atualizava" sem mudar nada).
        /// </summary>
        public static (bool Sucesso, string Mensagem)? ConsumirResultadoUltimaAtualizacao()
        {
            string caminho = Path.Combine(AppContext.BaseDirectory, ArquivoResultadoAtualizacao);
            if (!File.Exists(caminho)) return null;

            string conteudo;
            try
            {
                conteudo = File.ReadAllText(caminho).Trim();
                File.Delete(caminho);
            }
            catch
            {
                return null;
            }

            if (conteudo.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                return (true, "");

            string motivo = conteudo.StartsWith("FALHA|", StringComparison.OrdinalIgnoreCase) ? conteudo[6..] : conteudo;
            return (false, motivo);
        }

        /// <summary>
        /// Grava o script em UTF-8 COM BOM. O Windows PowerShell 5.1 lê .ps1 sem BOM como ANSI:
        /// acento vira lixo e certos bytes do UTF-8 viram aspas tipográficas, que o PowerShell
        /// aceita como delimitador de string — dá para quebrar a sintaxe só com um comentário.
        /// </summary>
        internal static void GravarScriptAtualizacao(string caminho) =>
            File.WriteAllText(caminho, BuildApplyUpdateScript(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        /// <summary>
        /// Script que troca os arquivos depois que o app fecha.
        ///
        /// Regras que ele segue porque roda sem janela e sem ninguém olhando:
        /// * QUALQUER falha vai para o log e para <see cref="ArquivoResultadoAtualizacao"/>;
        /// * o app é reaberto no finally, dando certo ou não — nunca deixa o usuário sem sistema;
        /// * a sintaxe é conferida por teste: um erro de parse faz o PowerShell recusar o arquivo
        ///   inteiro antes da primeira linha, e nenhuma dessas proteções chega a rodar.
        /// </summary>
        internal static string BuildApplyUpdateScript()
        {
            // Vírgula é obrigatória: @('a' 'b') é erro de sintaxe no PowerShell.
            string preserveList = string.Join(", ", PreserveFileNames.Select(n => $"'{n}'"));
            return $$"""
param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][int]$WaitPid,
    [string]$Log = '',
    [switch]$NaoReabrir
)

$ErrorActionPreference = 'Stop'
$resultado = Join-Path $Target '{{ArquivoResultadoAtualizacao}}'

function Registrar([string]$texto) {
    if ([string]::IsNullOrWhiteSpace($Log)) { return }
    try { Add-Content -LiteralPath $Log -Value ('{0:dd/MM/yyyy HH:mm:ss}  {1}' -f (Get-Date), $texto) -Encoding UTF8 } catch {}
}

try {
    Registrar "Inicio. Origem: $Source | Destino: $Target"

    if ($WaitPid -gt 0) {
        try { Wait-Process -Id $WaitPid -Timeout 90 -ErrorAction SilentlyContinue } catch {}
    }
    Start-Sleep -Seconds 2

    # robocopy num processo só: copiar arquivo a arquivo com Copy-Item faz o antivírus abrir e
    # varrer cada um separadamente, o que dominava o tempo da atualização.
    $excluir = @({{preserveList}})
    $argumentos = @($Source, $Target, '/E', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:5', '/W:2', '/XF') + $excluir
    $saida = & robocopy.exe @argumentos 2>&1
    $codigo = $LASTEXITCODE
    Registrar "robocopy terminou com codigo $codigo"

    # robocopy usa 0-7 para sucesso (8+ é falha real).
    if ($codigo -ge 8) {
        throw "robocopy falhou (codigo $codigo). $(($saida | Out-String).Trim())"
    }

    Set-Content -LiteralPath $resultado -Value 'OK' -Encoding UTF8
    Registrar 'Arquivos copiados.'
}
catch {
    $motivo = $_.Exception.Message
    Registrar "FALHA: $motivo"
    try { Set-Content -LiteralPath $resultado -Value "FALHA|$motivo" -Encoding UTF8 } catch {}
}
finally {
    if (-not $NaoReabrir) {
        Start-Sleep -Seconds 1
        try {
            Start-Process -FilePath $ExePath
            Registrar 'App reaberto.'
        }
        catch {
            Registrar "Nao foi possivel reabrir o app: $($_.Exception.Message)"
        }
    }
}
""";
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = "";

            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("body")]
            public string? Body { get; set; }

            [JsonPropertyName("html_url")]
            public string? HtmlUrl { get; set; }

            [JsonPropertyName("assets")]
            public System.Collections.Generic.List<GitHubAsset>? Assets { get; set; }
        }

        private sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = "";

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = "";

            [JsonPropertyName("size")]
            public long Size { get; set; }
        }
    }

    public sealed class UpdateCheckResult
    {
        public bool Success { get; init; }
        public bool UpdateAvailable { get; init; }
        public Version LocalVersion { get; init; } = new(0, 0, 0);
        public Version RemoteVersion { get; init; } = new(0, 0, 0);
        public string ReleaseName { get; init; } = "";
        public string ReleaseNotes { get; init; } = "";
        public string ReleaseUrl { get; init; } = "";
        public string? DownloadUrl { get; init; }
        public string? AssetName { get; init; }
        public long AssetSizeBytes { get; init; }
        public string? ErrorMessage { get; init; }

        public static UpdateCheckResult Fail(string message) => new()
        {
            Success = false,
            ErrorMessage = message
        };

        public string RemoteVersionDisplay =>
            $"v{RemoteVersion.Major}.{RemoteVersion.Minor}.{RemoteVersion.Build}";

        public string LocalVersionDisplay =>
            $"v{LocalVersion.Major}.{LocalVersion.Minor}.{Math.Max(LocalVersion.Build, 0)}";
    }
}

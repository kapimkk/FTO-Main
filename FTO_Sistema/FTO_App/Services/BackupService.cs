using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace FTO_App.Services
{
    /// <summary>
    /// Backup completo do sistema para o servidor da rede local.
    ///
    /// O pacote é montado primeiro numa pasta temporária local e só depois copiado para o
    /// servidor: pg_dump escrevendo direto num compartilhamento é lento e, se a rede cair no
    /// meio, deixa um .dump truncado no destino com cara de backup válido. O RESUMO.txt é
    /// gravado por último, de propósito — pasta sem RESUMO.txt é backup que não terminou.
    /// </summary>
    public static class BackupService
    {
        public const string ChaveDestino = "BACKUP_DESTINO";
        public const string ChaveUsuario = "BACKUP_USUARIO";
        public const string ChaveSenha = "BACKUP_SENHA";
        public const string ChavePgDump = "BACKUP_PG_DUMP";

        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

        public static BackupConfig LerConfig()
        {
            string envPath = Path.Combine(AppContext.BaseDirectory, ".env");
            var map = EnvFileHelper.ReadMap(envPath);

            // A senha do servidor é digitada em claro no .env; na primeira leitura ela é
            // criptografada com DPAPI, igual ao PGPASSWORD.
            string senhaBruta = map.GetValueOrDefault(ChaveSenha, "");
            if (!string.IsNullOrEmpty(senhaBruta) && !SecretProtector.IsProtected(senhaBruta))
            {
                try { EnvFileHelper.ProtectPasswordInPlace(envPath); } catch { /* não bloqueia o backup */ }
            }

            return new BackupConfig
            {
                Destino = map.GetValueOrDefault(ChaveDestino, "").Trim(),
                Usuario = map.GetValueOrDefault(ChaveUsuario, "").Trim(),
                Senha = SecretProtector.Unprotect(map.GetValueOrDefault(ChaveSenha, "")),
                PgDump = map.GetValueOrDefault(ChavePgDump, "").Trim()
            };
        }

        /// <summary>
        /// Monta e envia o backup. Nunca lança por item faltando — o que não deu certo vai em
        /// <see cref="BackupResultado.Avisos"/> e também para o RESUMO.txt, para o backup não
        /// passar por completo quando não está.
        /// </summary>
        public static async Task<BackupResultado> ExecutarAsync(
            IProgress<string>? progresso = null, CancellationToken ct = default)
        {
            var resultado = new BackupResultado();
            BackupConfig config = LerConfig();

            if (string.IsNullOrWhiteSpace(config.Destino))
            {
                resultado.Erro =
                    $"Destino do backup não configurado.\n\nNo arquivo .env, preencha:\n\n" +
                    $"{ChaveDestino}=\\\\192.168.0.10\\backup-sistema-fto\n" +
                    $"{ChaveUsuario}=usuario_do_servidor   (opcional)\n" +
                    $"{ChaveSenha}=senha_do_servidor       (opcional)\n\n" +
                    $"Arquivo: {Path.Combine(AppContext.BaseDirectory, ".env")}";
                return resultado;
            }

            DateTime agora = DateTime.Now;
            string temp = Path.Combine(Path.GetTempPath(), "FTO_Backup", Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(temp);

                await Task.Run(() => MontarPacote(temp, config, agora, resultado, progresso, ct), ct)
                          .ConfigureAwait(false);

                progresso?.Report("Conectando no servidor...");
                using var conexao = ConexaoRedeWindows.Abrir(config.Destino, config.Usuario, config.Senha);

                string destinoFinal = MontarCaminhoDestino(config.Destino, agora);
                progresso?.Report($"Enviando para {destinoFinal}...");

                await Task.Run(() =>
                {
                    Directory.CreateDirectory(destinoFinal);
                    CopiarPasta(temp, destinoFinal, ct);

                    // Por último: é o carimbo de "terminou".
                    // Com BOM: o RESUMO é lido no Bloco de Notas do servidor, e sem BOM os
                    // acentos saem como "Ã§" em quem ainda abre como ANSI.
                    File.WriteAllText(
                        Path.Combine(destinoFinal, "RESUMO.txt"),
                        MontarResumo(resultado, agora, destinoFinal),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }, ct).ConfigureAwait(false);

                resultado.PastaDestino = destinoFinal;
                resultado.Sucesso = true;
                progresso?.Report("Backup concluído.");
            }
            catch (OperationCanceledException)
            {
                resultado.Erro = "Backup cancelado.";
            }
            catch (Exception ex)
            {
                resultado.Erro = ex.Message;
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch { }
            }

            return resultado;
        }

        /// <summary>&lt;destino&gt;\Setembro-2026\09-09-2026\143025</summary>
        public static string MontarCaminhoDestino(string raiz, DateTime quando)
        {
            string mes = PtBr.TextInfo.ToTitleCase(quando.ToString("MMMM", PtBr));
            return Path.Combine(
                raiz.TrimEnd('\\', '/'),
                $"{mes}-{quando:yyyy}",
                quando.ToString("dd-MM-yyyy"),
                quando.ToString("HHmmss"));
        }

        private static void MontarPacote(
            string temp, BackupConfig config, DateTime agora,
            BackupResultado resultado, IProgress<string>? progresso, CancellationToken ct)
        {
            progresso?.Report("Gerando dump do PostgreSQL...");
            GerarDumpBanco(Path.Combine(temp, "banco"), config, agora, resultado);

            ct.ThrowIfCancellationRequested();
            progresso?.Report("Copiando configurações e arquivos...");

            string appDir = AppContext.BaseDirectory;
            CopiarArquivo(Path.Combine(appDir, ".env"), Path.Combine(temp, "config", ".env"), ".env", resultado);
            CopiarArquivo(DeviceSettingsStore.CaminhoArquivo,
                Path.Combine(temp, "config", "devices.json"), "devices.json (impressora/scanner)", resultado);
            CopiarArquivo(Database.SqliteLegacyPath,
                Path.Combine(temp, "arquivos", "FTO.db"), "FTO.db (SQLite legado)", resultado, opcional: true);

            CopiarPastaSeExistir(Path.Combine(appDir, "xml_nfe"),
                Path.Combine(temp, "arquivos", "xml_nfe"), "XMLs de NF-e", resultado, ct);

            var empresa = EmpresaConfigStore.Current;
            CopiarArquivo(empresa.CertificadoPath,
                Path.Combine(temp, "arquivos", "certificado", Path.GetFileName(empresa.CertificadoPath ?? "")),
                "certificado digital", resultado, opcional: true);
            CopiarArquivo(empresa.LogoPath,
                Path.Combine(temp, "arquivos", "logo", Path.GetFileName(empresa.LogoPath ?? "")),
                "logo do emitente", resultado, opcional: true);
        }

        private static void GerarDumpBanco(
            string pastaBanco, BackupConfig config, DateTime agora, BackupResultado resultado)
        {
            string? pgDump = LocalizarPgDump(config.PgDump);
            if (pgDump == null)
            {
                resultado.Avisos.Add(
                    "BANCO DE DADOS NÃO INCLUÍDO: pg_dump.exe não encontrado nesta máquina. " +
                    $"Instale as ferramentas de linha de comando do PostgreSQL ou informe o caminho em {ChavePgDump} no .env " +
                    @"(ex.: C:\Program Files\PostgreSQL\16\bin\pg_dump.exe).");
                return;
            }

            NpgsqlConnectionStringBuilder csb;
            try
            {
                csb = new NpgsqlConnectionStringBuilder(Database.ConnectionString);
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"BANCO DE DADOS NÃO INCLUÍDO: conexão do PostgreSQL inválida — {ex.Message}");
                return;
            }

            Directory.CreateDirectory(pastaBanco);
            string arquivo = Path.Combine(pastaBanco, $"{csb.Database}-{agora:yyyyMMdd-HHmmss}.dump");

            var psi = new ProcessStartInfo
            {
                FileName = pgDump,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            psi.ArgumentList.Add($"--host={csb.Host}");
            psi.ArgumentList.Add($"--port={csb.Port}");
            psi.ArgumentList.Add($"--username={csb.Username}");
            psi.ArgumentList.Add($"--dbname={csb.Database}");
            // custom: comprimido e restaurável seletivamente com pg_restore.
            psi.ArgumentList.Add("--format=custom");
            // Sem isto o pg_dump PARA esperando a senha no console quando ela está errada,
            // e o backup fica pendurado para sempre.
            psi.ArgumentList.Add("--no-password");
            psi.ArgumentList.Add($"--file={arquivo}");

            // Senha por variável de ambiente: na linha de comando ela apareceria na lista de
            // processos para qualquer usuário da máquina.
            psi.Environment["PGPASSWORD"] = csb.Password ?? "";

            using var processo = Process.Start(psi)
                ?? throw new InvalidOperationException("Não foi possível iniciar o pg_dump.");

            string erro = processo.StandardError.ReadToEnd();
            processo.WaitForExit();

            if (processo.ExitCode != 0)
            {
                try { if (File.Exists(arquivo)) File.Delete(arquivo); } catch { }
                resultado.Avisos.Add(
                    $"BANCO DE DADOS NÃO INCLUÍDO: pg_dump terminou com código {processo.ExitCode}. " +
                    (string.IsNullOrWhiteSpace(erro) ? "" : erro.Trim()));
                return;
            }

            var info = new FileInfo(arquivo);
            resultado.Itens.Add($"banco de dados '{csb.Database}' ({Tamanho(info.Length)}, formato custom do pg_dump)");
            resultado.PgDumpUsado = pgDump;
        }

        /// <summary>
        /// pg_dump precisa ser da mesma versão do servidor ou mais novo; por isso a busca começa
        /// pela instalação mais recente encontrada.
        /// </summary>
        public static string? LocalizarPgDump(string? configurado)
        {
            if (!string.IsNullOrWhiteSpace(configurado) && File.Exists(configurado))
                return configurado;

            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string caminho = Path.Combine(dir.Trim(), "pg_dump.exe");
                    if (File.Exists(caminho)) return caminho;
                }
                catch { /* PATH com entrada inválida */ }
            }

            foreach (string raiz in new[] { @"C:\Program Files\PostgreSQL", @"C:\Program Files (x86)\PostgreSQL" })
            {
                if (!Directory.Exists(raiz)) continue;

                string? achado = Directory.GetDirectories(raiz)
                    .OrderByDescending(d => int.TryParse(Path.GetFileName(d), out int v) ? v : 0)
                    .Select(d => Path.Combine(d, "bin", "pg_dump.exe"))
                    .FirstOrDefault(File.Exists);

                if (achado != null) return achado;
            }

            return null;
        }

        private static void CopiarArquivo(
            string? origem, string destino, string descricao, BackupResultado resultado, bool opcional = false)
        {
            if (string.IsNullOrWhiteSpace(origem) || !File.Exists(origem))
            {
                if (!opcional)
                    resultado.Avisos.Add($"{descricao}: não encontrado em '{origem}'.");
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                File.Copy(origem, destino, overwrite: true);
                resultado.Itens.Add($"{descricao} ({Tamanho(new FileInfo(origem).Length)})");
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"{descricao}: falha ao copiar — {ex.Message}");
            }
        }

        private static void CopiarPastaSeExistir(
            string origem, string destino, string descricao, BackupResultado resultado, CancellationToken ct)
        {
            if (!Directory.Exists(origem)) return;

            try
            {
                var arquivos = Directory.GetFiles(origem, "*", SearchOption.AllDirectories);
                if (arquivos.Length == 0) return;

                CopiarPasta(origem, destino, ct);
                long bytes = arquivos.Sum(f => new FileInfo(f).Length);
                resultado.Itens.Add($"{descricao} — {arquivos.Length} arquivo(s) ({Tamanho(bytes)})");
            }
            catch (Exception ex)
            {
                resultado.Avisos.Add($"{descricao}: falha ao copiar — {ex.Message}");
            }
        }

        private static void CopiarPasta(string origem, string destino, CancellationToken ct)
        {
            Directory.CreateDirectory(destino);

            foreach (string dir in Directory.GetDirectories(origem, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(origem, destino));

            foreach (string arquivo in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                File.Copy(arquivo, arquivo.Replace(origem, destino), overwrite: true);
            }
        }

        private static string MontarResumo(BackupResultado resultado, DateTime quando, string destino)
        {
            var sb = new StringBuilder();
            sb.AppendLine("BACKUP DO SISTEMA FTO");
            sb.AppendLine("=====================");
            sb.AppendLine($"Data/hora:  {quando:dd/MM/yyyy HH:mm:ss}");
            sb.AppendLine($"Máquina:    {Environment.MachineName}  (usuário {Environment.UserName})");
            sb.AppendLine($"Versão app: {UpdateService.GetLocalVersionDisplay()}");
            sb.AppendLine($"Destino:    {destino}");
            if (!string.IsNullOrWhiteSpace(resultado.PgDumpUsado))
                sb.AppendLine($"pg_dump:    {resultado.PgDumpUsado}");
            sb.AppendLine();

            sb.AppendLine("INCLUÍDO NESTE BACKUP");
            foreach (string item in resultado.Itens)
                sb.AppendLine($"  - {item}");
            if (resultado.Itens.Count == 0)
                sb.AppendLine("  (nada)");
            sb.AppendLine();

            if (resultado.Avisos.Count > 0)
            {
                sb.AppendLine("ATENÇÃO — NÃO ENTROU NESTE BACKUP");
                foreach (string aviso in resultado.Avisos)
                    sb.AppendLine($"  - {aviso}");
                sb.AppendLine();
            }

            sb.AppendLine("COMO RESTAURAR");
            sb.AppendLine("  Banco:  pg_restore --host=SERVIDOR --port=5432 --username=postgres \\");
            sb.AppendLine("                     --dbname=fto --clean --if-exists banco\\<arquivo>.dump");
            sb.AppendLine("  Config: copie config\\.env e arquivos\\ de volta para a pasta do FTO_App.");
            sb.AppendLine();
            sb.AppendLine("  OBS.: o PGPASSWORD dentro do .env está criptografado com DPAPI e só abre na");
            sb.AppendLine("  MESMA máquina e MESMO usuário do Windows que o gerou. Restaurando em outra");
            sb.AppendLine("  máquina, apague o valor de PGPASSWORD e digite a senha de novo — o app volta");
            sb.AppendLine("  a criptografar sozinho na primeira abertura.");

            return sb.ToString();
        }

        private static string Tamanho(long bytes) => bytes switch
        {
            >= 1048576 => $"{bytes / 1048576.0:0.#} MB",
            >= 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes} bytes"
        };
    }

    public sealed class BackupConfig
    {
        public string Destino { get; init; } = "";
        public string Usuario { get; init; } = "";
        public string Senha { get; init; } = "";
        public string PgDump { get; init; } = "";
    }

    public sealed class BackupResultado
    {
        public bool Sucesso { get; set; }
        public string? PastaDestino { get; set; }
        public string? Erro { get; set; }
        public string? PgDumpUsado { get; set; }
        public List<string> Itens { get; } = new();
        public List<string> Avisos { get; } = new();
    }
}

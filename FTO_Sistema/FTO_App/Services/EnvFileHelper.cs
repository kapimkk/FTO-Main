using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FTO_App.Services
{
    /// <summary>
    /// .env contém apenas conexão PostgreSQL e token de update — nunca dados de empresa/fiscal.
    /// </summary>
    public static class EnvFileHelper
    {
        // Chave fora desta lista é apagada pelo WriteClean — ao acrescentar configuração nova no
        // .env, incluir aqui E no WriteClean, senão ela some na primeira limpeza.
        private static readonly HashSet<string> AllowedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "PGHOST", "PGPORT", "PGDATABASE", "PGUSER", "PGPASSWORD", "DATABASE_URL", "FTO_UPDATE_TOKEN",
            "BACKUP_DESTINO", "BACKUP_USUARIO", "BACKUP_SENHA", "BACKUP_PG_DUMP"
        };

        private static readonly string[] BusinessPrefixes =
        {
            "EMPRESA_", "NFE_", "CUPOM_"
        };

        public static Dictionary<string, string> ReadMap(string path)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return map;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;
                map[line[..idx].Trim()] = line[(idx + 1)..].Trim().Trim('"');
            }
            return map;
        }

        public static void StripBusinessKeys(string path)
        {
            if (!File.Exists(path)) return;
            var map = ReadMap(path);
            WriteClean(path, map);
        }

        /// <summary>Senhas do .env que são gravadas em claro pelo usuário e criptografadas depois.</summary>
        private static readonly string[] PasswordKeys = { "PGPASSWORD", "BACKUP_SENHA" };

        /// <summary>
        /// Criptografa as senhas do .env no lugar, preservando demais chaves
        /// (ex.: EMPRESA_* ainda não migradas para o banco).
        /// </summary>
        public static void ProtectPasswordInPlace(string path)
        {
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path).ToList();
            bool changed = false;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                int idx = line.IndexOf('=');
                if (idx <= 0) continue;

                string chave = line[..idx].Trim();
                if (!PasswordKeys.Contains(chave, StringComparer.OrdinalIgnoreCase))
                    continue;

                string raw = line[(idx + 1)..].Trim().Trim('"');
                if (string.IsNullOrEmpty(raw) || SecretProtector.IsProtected(raw))
                    continue;

                lines[i] = $"{chave.ToUpperInvariant()}=" + SecretProtector.Protect(raw);
                changed = true;
            }

            if (changed)
                File.WriteAllLines(path, lines, Encoding.UTF8);
        }

        /// <summary>Regrava .env só com chaves permitidas e senha criptografada (DPAPI).</summary>
        public static void WriteClean(string path, Dictionary<string, string> map)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# FTO — apenas conexão PostgreSQL e update (dados da empresa ficam no banco).");
            sb.AppendLine("# Não coloque EMPRESA_*, NFE_* ou CUPOM_* neste arquivo.");
            sb.AppendLine();

            string host = map.GetValueOrDefault("PGHOST", "localhost");
            string port = map.GetValueOrDefault("PGPORT", "5432");
            string db = map.GetValueOrDefault("PGDATABASE", "fto");
            string user = map.GetValueOrDefault("PGUSER", "postgres");
            string pass = map.GetValueOrDefault("PGPASSWORD", "");
            string url = map.GetValueOrDefault("DATABASE_URL", "");
            string token = map.GetValueOrDefault("FTO_UPDATE_TOKEN", "");

            if (!string.IsNullOrWhiteSpace(url))
            {
                sb.AppendLine($"DATABASE_URL={url}");
            }
            else
            {
                sb.AppendLine($"PGHOST={host}");
                sb.AppendLine($"PGPORT={port}");
                sb.AppendLine($"PGDATABASE={db}");
                sb.AppendLine($"PGUSER={user}");
                if (!string.IsNullOrEmpty(pass))
                    sb.AppendLine($"PGPASSWORD={SecretProtector.Protect(SecretProtector.Unprotect(pass))}");
                else
                    sb.AppendLine("PGPASSWORD=");
            }

            if (!string.IsNullOrWhiteSpace(token))
                sb.AppendLine($"FTO_UPDATE_TOKEN={token}");

            string destino = map.GetValueOrDefault("BACKUP_DESTINO", "");
            if (!string.IsNullOrWhiteSpace(destino))
            {
                sb.AppendLine();
                sb.AppendLine("# Servidor de backup (rede local).");
                sb.AppendLine($"BACKUP_DESTINO={destino}");

                string backupUser = map.GetValueOrDefault("BACKUP_USUARIO", "");
                if (!string.IsNullOrWhiteSpace(backupUser))
                    sb.AppendLine($"BACKUP_USUARIO={backupUser}");

                string backupPass = map.GetValueOrDefault("BACKUP_SENHA", "");
                if (!string.IsNullOrEmpty(backupPass))
                    sb.AppendLine($"BACKUP_SENHA={SecretProtector.Protect(SecretProtector.Unprotect(backupPass))}");

                string pgDump = map.GetValueOrDefault("BACKUP_PG_DUMP", "");
                if (!string.IsNullOrWhiteSpace(pgDump))
                    sb.AppendLine($"BACKUP_PG_DUMP={pgDump}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static bool IsBusinessKey(string key) =>
            BusinessPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase)) ||
            !AllowedKeys.Contains(key);
    }
}

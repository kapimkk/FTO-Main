using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FTO_App.Services
{
    /// <summary>
    /// Autentica num compartilhamento SMB (\\servidor\pasta) pelo tempo de uma operação.
    ///
    /// Existe porque o servidor de backup fica na rede local: se ele exige usuário e senha e o
    /// Windows não tem essa credencial guardada, qualquer <c>File.Copy</c> para lá falha com
    /// "acesso negado" — sem chance de informar a senha. WNetAddConnection2 é a mesma coisa que o
    /// "net use" faz, só que sem deixar o mapeamento (e a credencial) permanentes na máquina.
    ///
    /// Sem usuário configurado não conecta nada: usa a credencial do próprio Windows, que é o
    /// caso comum quando servidor e estação estão no mesmo domínio ou grupo de trabalho.
    /// </summary>
    public sealed class ConexaoRedeWindows : IDisposable
    {
        private const int RESOURCETYPE_DISK = 0x00000001;
        private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;
        private const int ERROR_ALREADY_ASSIGNED = 85;

        private readonly string? _compartilhamento;
        private bool _liberado;

        private ConexaoRedeWindows(string? compartilhamento) => _compartilhamento = compartilhamento;

        /// <summary>
        /// Conecta ao compartilhamento que contém <paramref name="caminho"/>, se ele for UNC e
        /// houver usuário configurado. Caso contrário devolve um objeto que não faz nada.
        /// </summary>
        public static ConexaoRedeWindows Abrir(string caminho, string? usuario, string? senha)
        {
            string? compartilhamento = RaizDoCompartilhamento(caminho);
            if (compartilhamento == null || string.IsNullOrWhiteSpace(usuario))
                return new ConexaoRedeWindows(null);

            var recurso = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpRemoteName = compartilhamento
            };

            int resultado = WNetAddConnection2(ref recurso, senha, usuario, 0);

            // Já existe sessão para este servidor (com outra credencial ou a mesma): o Windows não
            // permite duas ao mesmo tempo. A que existe é a que vale — segue com ela.
            if (resultado is ERROR_SESSION_CREDENTIAL_CONFLICT or ERROR_ALREADY_ASSIGNED)
                return new ConexaoRedeWindows(null);

            if (resultado != 0)
            {
                throw new InvalidOperationException(
                    $"Não foi possível conectar em {compartilhamento} como '{usuario}': " +
                    new Win32Exception(resultado).Message);
            }

            return new ConexaoRedeWindows(compartilhamento);
        }

        /// <summary>\\servidor\pasta\sub\sub → \\servidor\pasta. Nulo se não for UNC.</summary>
        public static string? RaizDoCompartilhamento(string caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho)) return null;

            string limpo = caminho.Replace('/', '\\').TrimEnd('\\');
            if (!limpo.StartsWith(@"\\", StringComparison.Ordinal)) return null;

            string[] partes = limpo[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return partes.Length >= 2 ? $@"\\{partes[0]}\{partes[1]}" : null;
        }

        public void Dispose()
        {
            if (_liberado || _compartilhamento == null) return;
            _liberado = true;
            try { WNetCancelConnection2(_compartilhamento, 0, false); } catch { }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NETRESOURCE
        {
            public int dwScope;
            public int dwType;
            public int dwDisplayType;
            public int dwUsage;
            public string? lpLocalName;
            public string? lpRemoteName;
            public string? lpComment;
            public string? lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int WNetAddConnection2(
            ref NETRESOURCE netResource, string? password, string? username, int flags);

        [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int WNetCancelConnection2(string name, int flags, bool force);
    }
}

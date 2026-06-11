# FTO - Sistema de Gestão e Controle de Vendas

Sistema desktop desenvolvido para facilitar o controle financeiro, gestão de clientes e vendas da empresa FTO. O software oferece uma interface intuitiva, suporte a temas (claro/escuro) e atualizações automáticas.

# 🚀 Funcionalidades

Controle de Acesso: Login seguro com usuário e senha.

# Gestão de Vendas:

Lançamento de vendas com cálculo automático de lucro.

Classificação por data, cliente e status de pagamento.

Filtros avançados por mês, ano e busca textual.

# Gestão de Clientes:

Cadastro rápido durante a venda ou via gerenciador.

Histórico de contato e documentos (CPF/CNPJ).

# Relatórios:

Dashboard financeiro com totais de Ganhos, Gastos e Lucro Líquido.

Exportação de relatórios para Excel (.xlsx).

# Impressão térmica (cupom não fiscal):

Seleção de impressora e scanner na tela de módulos (após login).

Impressão via layout WPF (`PrintVisual`), mesmo padrão do projeto Imperial Colors — recomendado para **MP-2500 HT** e outras impressoras instaladas no Windows.

Cupom com logo, dados da empresa (FTO Informática), número da venda, data, serviço, total, forma de pagamento e mensagem de agradecimento (sem dados pessoais do cliente, gastos, lucro ou status).

Botão **Imprimir** no painel de vendas (habilitado ao selecionar uma linha na tabela).

Botão **WhatsApp** ao lado de Excluir: abre a conversa no WhatsApp Desktop usando o contato da venda selecionada.

Janela de confirmação com pré-visualização do cupom antes de enviar à impressora.

# Sistema Inteligente:

Banco de dados local (SQLite) otimizado para alto desempenho.

Atualizador automático integrado via GitHub.

Modo Escuro (Dark Mode) para conforto visual.

🛠 Tecnologias Utilizadas

# Linguagem: C# (.NET / WPF)

Banco de Dados: SQLite (com WAL mode ativado)

Exportação: ClosedXML (Excel)

Design: XAML customizado

# 📦 Gerar executável (build para distribuição)

Na pasta do projeto, execute no PowerShell (recomendado — pasta completa, mais estável com SQLite e impressão):

```powershell
dotnet publish "FTO_Sistema\FTO_App\FTO_App.csproj" -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=pt-BR -p:PublishReadyToRun=false -p:DebugType=none -p:DebugSymbols=false -p:IncludeNativeLibrariesForSelfExtract=true -o "FTO_Sistema\publish\FTO_App-win-x64"
```

Alternativa com perfil de publicação (mesmas opções):

```powershell
dotnet publish "FTO_Sistema\FTO_App\FTO_App.csproj" -p:PublishProfile=Win64-SelfContained
```

`SatelliteResourceLanguages=pt-BR` evita copiar dezenas de pastas de idiomas (`cs`, `de`, `en`, etc.). O pacote ainda inclui o runtime .NET (~150–170 MB) porque é self-contained.

**Entrega ao cliente:** compacte a pasta `FTO_Sistema\publish\FTO_App-win-x64` inteira em um `.zip`. O usuário deve extrair e executar `FTO_App.exe` (não copiar só o `.exe` — as DLLs e `SQLite.Interop.dll` precisam estar na mesma pasta).

| Item validado no publish | Status |
|--------------------------|--------|
| `FTO_App.exe` | Incluído |
| Runtime .NET 8 (self-contained) | Incluído (~200 MB) |
| `SQLite.Interop.dll` | Incluído |
| `icons/fto.png` (cupom térmico) | Incluído |
| Inicialização do app | Testada |

**Não use** `PublishSingleFile` neste projeto: WPF + SQLite nativo costuma falhar ao extrair bibliotecas de um único arquivo.

# 📦 Requisitos de Sistema (build self-contained)

Com o publish acima, o cliente **não precisa** instalar .NET separadamente.

Windows 10 ou 11 (64-bits).

Para build *framework-dependent* (menor, exige .NET 8 Desktop Runtime instalado), omita `--self-contained true`.

# 📥 Como Instalar

Baixe o arquivo .zip da versão mais recente.

Extraia a pasta em um local seguro (ex: C:\FTO Sistema).

Execute o arquivo FTO_App.exe.
(O banco de dados será criado automaticamente na primeira execução).

# 🔄 Como Atualizar

O sistema possui um verificador de atualizações integrado.

Na tela de login, clique em "Verificar Atualizações".

Se houver uma nova versão, o sistema baixará e instalará automaticamente.
